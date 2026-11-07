using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Graphics;
using Client.Main.Objects.Player;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects
{
    // Merged player body meshes. A player draws its body parts (helm, armor, pants, gloves, boots) as separate
    // objects and meshes, about a dozen ItemMaterial draws per player, each costing 10+ us in the integrated
    // GPU driver. The ItemMaterial meshes of all eligible parts are merged into one skinned mesh per player
    // (texture atlas + per-vertex item parameters) and drawn in one or two calls; the parts then skip their
    // ItemMaterial groups. Shadows, highlights, overlay passes, transparent meshes and the other shaders stay
    // on the regular per-part path.
    public partial class ModelObject
    {
        private const int MaxCompositeParts = 16;

        private sealed class PlayerCompositeMesh : IDisposable
        {
            public VertexBuffer VertexBuffer;
            public IndexBuffer IndexBuffer;
            public int OneSidedPrimitives;
            public int TwoSidedPrimitives;
            public int BoneCount;
            public long Signature;

            public void Dispose()
            {
                VertexBuffer?.Dispose();
                VertexBuffer = null;
                IndexBuffer?.Dispose();
                IndexBuffer = null;
            }
        }

        private readonly struct CompositeEntry
        {
            public CompositeEntry(ModelObject part, int partIndex, int mesh, Texture2D texture, Vector4 rect, bool twoSided)
            {
                Part = part;
                PartIndex = partIndex;
                Mesh = mesh;
                Texture = texture;
                Rect = rect;
                TwoSided = twoSided;
            }

            public ModelObject Part { get; }
            public int PartIndex { get; }
            public int Mesh { get; }
            public Texture2D Texture { get; }
            public Vector4 Rect { get; }
            public bool TwoSided { get; }
        }

        private PlayerCompositeMesh _playerComposite;
        private int _compositeCoveredFrame = -1;

        [ThreadStatic] private static List<CompositeEntry> s_compositeEntries;
        [ThreadStatic] private static List<ModelObject> s_compositeParts;
        [ThreadStatic] private static List<int> s_compositePartMeshes;
        private static readonly Vector4[] s_partChromeColors = new Vector4[MaxCompositeParts];
        private static readonly Vector4[] s_partChrome2Tints = new Vector4[MaxCompositeParts];
        private static readonly Vector4[] s_partAncientColors = new Vector4[MaxCompositeParts];

        /// <summary>Diagnostics: merged player meshes drawn last frame and the part meshes they replaced.</summary>
        public static int LastFramePlayerComposites { get; private set; }
        public static int LastFramePlayerCompositeMeshes { get; private set; }
        private static int s_compositesThisFrame, s_compositeMeshesThisFrame, s_compositeMetricsFrame = -1;

        /// <summary>The ItemMaterial meshes of this part were drawn by its player's merged mesh this frame.</summary>
        internal bool IsCompositeCoveredThisFrame => _compositeCoveredFrame == MuGame.FrameIndex;

        private static bool IsCompositeBodyPart(ModelObject part) =>
            part is PlayerHelmObject or PlayerMaskHelmObject or PlayerArmorObject or PlayerPantObject or
                PlayerGloveObject or PlayerBootObject;

        private bool CanPartUseComposite(Matrix rootWorld)
        {
            return Visible && Model?.Meshes != null && _meshes != null &&
                   LinkParentAnimation && ParentBoneLink < 0 &&
                   SpecialItemSpec == null && !PcRoomEffect && _activeOverlayMode == ItemOverlayMode.None &&
                   TotalAlpha >= 0.999f && !UsesMutableMeshData && GetDepthBias() == 0f &&
                   TextureCoordinateOffset == Vector2.Zero && CanUseGpuSkinGeometry() &&
                   WorldPosition == rootWorld;
        }

        /// <summary>
        /// Collects the ItemMaterial meshes of this part; false when any of them cannot be merged (then the
        /// whole part keeps its regular draws).
        /// </summary>
        private bool TryCollectCompositeMeshes(List<int> meshes)
        {
            meshes.Clear();
            foreach (var group in GetMeshRenderPlan(false))
            {
                if (group.Key.ShaderKind != MeshShaderKind.ItemMaterial)
                    continue;

                BlendState blend = group.Key.BlendState;
                if (!ReferenceEquals(blend, BlendState.Opaque) &&
                    !ReferenceEquals(blend, BlendState.AlphaBlend) &&
                    !ReferenceEquals(blend, BlendState.NonPremultiplied))
                    return false;

                List<int> groupMeshes = group.Value;
                for (int n = 0; n < groupMeshes.Count; n++)
                {
                    int mesh = groupMeshes[n];
                    if ((uint)mesh >= (uint)_meshes.Length || IsHiddenMesh(mesh) || IsBlendMesh(mesh) ||
                        _meshes[mesh].IsRgba || _meshes[mesh].Texture == null || !CanUseGpuSkinningForMesh(mesh))
                        return false;
                    meshes.Add(mesh);
                }
            }

            return meshes.Count > 0;
        }

        /// <summary>Draws the merged ItemMaterial body meshes of this player; parts drawn here are marked covered.</summary>
        private bool TryDrawPlayerComposite()
        {
            if (!Constants.ENABLE_PLAYER_COMPOSITE || this is not PlayerObject || Children.Count == 0)
                return false;

            var effect = GraphicsManager.Instance.ItemMaterialEffect;
            if (!Constants.ENABLE_ITEM_MATERIAL_SHADER || effect == null)
                return false;

            EffectTechnique technique = TryGetTechnique(effect, "BasicColorDrawing_CompositeSkinned");
            if (technique == null)
                return false;

            var entries = s_compositeEntries ??= new List<CompositeEntry>(32);
            var parts = s_compositeParts ??= new List<ModelObject>(8);
            var partMeshes = s_compositePartMeshes ??= new List<int>(8);
            entries.Clear();
            parts.Clear();

            Matrix rootWorld = default;
            bool haveWorld = false;
            int atlasPage = -1;
            bool atlasReady = true;
            var children = Children.GetSnapshotArray();
            for (int i = 0; i < children.Length && parts.Count < MaxCompositeParts; i++)
            {
                if (children[i] is not ModelObject part || !IsCompositeBodyPart(part) ||
                    !ReferenceEquals(part.Parent, this) || !part.Visible)
                    continue;

                if (!haveWorld)
                {
                    rootWorld = part.WorldPosition;
                    haveWorld = true;
                }

                if (!part.CanPartUseComposite(rootWorld) || !part.TryCollectCompositeMeshes(partMeshes))
                    continue;

                int partIndex = parts.Count;
                for (int n = 0; n < partMeshes.Count; n++)
                {
                    int mesh = partMeshes[n];
                    Texture2D texture = part._meshes[mesh].Texture;
                    if (!PlayerPartAtlas.TryGet(texture, out int page, out Vector4 rect))
                    {
                        atlasReady = false;
                        continue;
                    }

                    if (atlasPage < 0)
                        atlasPage = page;
                    else if (atlasPage != page)
                        atlasReady = false;

                    entries.Add(new CompositeEntry(part, partIndex, mesh, texture, rect, part.IsMeshTwoSided(mesh, false)));
                }

                parts.Add(part);
            }

            if (!atlasReady || entries.Count < 2)
                return false;

            long signature = ComputeCompositeSignature(entries, parts, atlasPage);
            if (_playerComposite == null || _playerComposite.Signature != signature ||
                _playerComposite.VertexBuffer is not { IsDisposed: false })
            {
                _playerComposite?.Dispose();
                _playerComposite = BuildPlayerComposite(entries, parts, signature);
                if (_playerComposite == null)
                    return false;
            }

            if (!TryQueueDeferredComposite(entries, parts, atlasPage))
            {
                var drawPathToken = Client.Main.Diagnostics.DrawPathCounter.Start();
                bool drawn = DrawPlayerComposite(effect, technique, parts, PlayerPartAtlas.GetPageTexture(atlasPage));
                Client.Main.Diagnostics.DrawPathCounter.StopExtra(drawPathToken, Client.Main.Diagnostics.DrawPathCounter.Extra.CompositeDraw);
                if (!drawn)
                    return false;
            }

            int frame = MuGame.FrameIndex;
            for (int i = 0; i < parts.Count; i++)
                parts[i]._compositeCoveredFrame = frame;

            if (s_compositeMetricsFrame != frame)
            {
                LastFramePlayerComposites = s_compositesThisFrame;
                LastFramePlayerCompositeMeshes = s_compositeMeshesThisFrame;
                s_compositesThisFrame = s_compositeMeshesThisFrame = 0;
                s_compositeMetricsFrame = frame;
            }
            s_compositesThisFrame++;
            s_compositeMeshesThisFrame += entries.Count;
            return true;
        }

        private static long ComputeCompositeSignature(List<CompositeEntry> entries, List<ModelObject> parts, int atlasPage)
        {
            unchecked
            {
                long hash = 1469598103934665603L ^ atlasPage;
                for (int i = 0; i < entries.Count; i++)
                {
                    var e = entries[i];
                    hash = (hash ^ RuntimeHelpers.GetHashCode(e.Part.Model)) * 1099511628211L;
                    hash = (hash ^ e.Mesh) * 1099511628211L;
                    hash = (hash ^ RuntimeHelpers.GetHashCode(e.Texture)) * 1099511628211L;
                    hash = (hash ^ e.Rect.GetHashCode()) * 1099511628211L;
                    hash = (hash ^ (e.TwoSided ? 1 : 0)) * 1099511628211L;
                    hash = (hash ^ e.PartIndex) * 1099511628211L;
                }

                for (int i = 0; i < parts.Count; i++)
                {
                    var p = parts[i];
                    hash = (hash ^ p.GetCompositeItemOptions()) * 1099511628211L;
                    hash = (hash ^ (p.HasExcellentGlow ? 1 : 0) ^ (p.IsAncientItem ? 2 : 0)) * 1099511628211L;
                }
                return hash;
            }
        }

        private int GetCompositeItemOptions()
        {
            int options = EffectiveItemLevel & 0x0F;
            if (HasExcellentGlow)
                options |= 0x10;
            options |= Core.Utilities.ItemVisualRules.GetLowLevelChromeMode(MaterialItemGroup, MaterialItemIndex) << 5;
            return options;
        }

        private PlayerCompositeMesh BuildPlayerComposite(List<CompositeEntry> entries, List<ModelObject> parts, long signature)
        {
            try
            {
                var vertices = new List<PlayerCompositeVertex>(4096);
                var oneSided = new List<int>(8192);
                var twoSided = new List<int>(1024);
                int boneCount = 0;

                for (int i = 0; i < entries.Count; i++)
                {
                    var e = entries[i];
                    if (!BMDLoader.Instance.TryGetSkinnedMeshGeometry(e.Part.Model, e.Mesh,
                            out var source, out int[] indices, out int meshBones))
                        return null;

                    boneCount = Math.Max(boneCount, meshBones);
                    var part = e.Part;
                    var itemData = new Vector4(part.GetCompositeItemOptions(), part.HasExcellentGlow ? 1f : 0f,
                        part.IsAncientItem ? 1f : 0f, e.PartIndex);
                    int baseVertex = vertices.Count;
                    for (int v = 0; v < source.Length; v++)
                    {
                        var s = source[v];
                        vertices.Add(new PlayerCompositeVertex
                        {
                            Position = s.Position,
                            Color = s.Color,
                            Normal = s.Normal,
                            TextureCoordinate = s.TextureCoordinate,
                            BoneIndices = s.BoneIndices,
                            AtlasRect = e.Rect,
                            ItemData = itemData,
                        });
                    }

                    var target = e.TwoSided ? twoSided : oneSided;
                    for (int k = 0; k < indices.Length; k++)
                        target.Add(baseVertex + indices[k]);
                }

                if (vertices.Count == 0 || boneCount > MaxGpuSkinBones)
                    return null;

                var gd = GraphicsDevice;
                var composite = new PlayerCompositeMesh
                {
                    Signature = signature,
                    BoneCount = boneCount,
                    OneSidedPrimitives = oneSided.Count / 3,
                    TwoSidedPrimitives = twoSided.Count / 3,
                    VertexBuffer = new VertexBuffer(gd, PlayerCompositeVertex.VertexDeclaration, vertices.Count, BufferUsage.WriteOnly),
                };
                composite.VertexBuffer.SetData(vertices.ToArray());

                oneSided.AddRange(twoSided);
                if (vertices.Count <= ushort.MaxValue)
                {
                    var shortIndices = new ushort[oneSided.Count];
                    for (int i = 0; i < shortIndices.Length; i++)
                        shortIndices[i] = (ushort)oneSided[i];
                    composite.IndexBuffer = new IndexBuffer(gd, IndexElementSize.SixteenBits, shortIndices.Length, BufferUsage.WriteOnly);
                    composite.IndexBuffer.SetData(shortIndices);
                }
                else
                {
                    composite.IndexBuffer = new IndexBuffer(gd, IndexElementSize.ThirtyTwoBits, oneSided.Count, BufferUsage.WriteOnly);
                    composite.IndexBuffer.SetData(oneSided.ToArray());
                }

                return composite;
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Building the merged player mesh failed.");
                return null;
            }
        }

        // Composite queued in the opaque pass (see ModelObject.DeferredMeshes): its parts and their merged meshes,
        // kept to draw it later and, if that fails, to draw the covered meshes through the regular path.
        private List<ModelObject> _deferredCompositeParts;
        private List<(ModelObject Part, int Mesh)> _deferredCompositeMeshes;
        private int _deferredCompositeAtlasPage;

        private bool TryQueueDeferredComposite(List<CompositeEntry> entries, List<ModelObject> parts, int atlasPage)
        {
            if (!CanDeferActorDraw())
                return false;

            (_deferredCompositeParts ??= new List<ModelObject>(8)).Clear();
            _deferredCompositeParts.AddRange(parts);
            (_deferredCompositeMeshes ??= new List<(ModelObject, int)>(16)).Clear();
            for (int i = 0; i < entries.Count; i++)
                _deferredCompositeMeshes.Add((entries[i].Part, entries[i].Mesh));
            _deferredCompositeAtlasPage = atlasPage;
            QueueDeferredComposite();
            return true;
        }

        private void DrawDeferredComposite()
        {
            var effect = GraphicsManager.Instance.ItemMaterialEffect;
            EffectTechnique technique = effect != null ? TryGetTechnique(effect, "BasicColorDrawing_CompositeSkinned") : null;
            if (technique != null && _deferredCompositeParts is { Count: > 0 } &&
                DrawPlayerComposite(effect, technique, _deferredCompositeParts, PlayerPartAtlas.GetPageTexture(_deferredCompositeAtlasPage)))
                return;

            // The parts already skipped these meshes; draw them one by one instead.
            if (_deferredCompositeMeshes == null)
                return;
            for (int i = 0; i < _deferredCompositeMeshes.Count; i++)
            {
                var (part, mesh) = _deferredCompositeMeshes[i];
                if (part.Status != Models.GameControlStatus.Disposed && part.Visible)
                    part.DrawMesh(mesh);
            }
        }

        private bool DrawPlayerComposite(Effect effect, EffectTechnique technique, List<ModelObject> parts, Texture2D atlas)
        {
            var composite = _playerComposite;
            if (atlas == null || atlas.IsDisposed || composite?.VertexBuffer == null || composite.IndexBuffer == null)
                return false;

            ModelObject first = parts[0];
            var gd = GraphicsDevice;
            ModelEffectBindings bindings = GetModelEffectBindings(effect);
            var prevBlend = gd.BlendState;
            var prevDepth = gd.DepthStencilState;
            var prevRaster = gd.RasterizerState;
            var prevTechnique = effect.CurrentTechnique;

            try
            {
                effect.CurrentTechnique = technique;
                if (!first.TryUploadGpuSkinBoneMatrices(effect, composite.BoneCount))
                    return false;

                // Same per-draw inputs as DrawMeshWithItemMaterial for the parts (they share the player's
                // world matrix, bones and lighting); the per-item values come from the vertices and arrays.
                GraphicsManager.Instance.ShadowMapRenderer?.ApplyShadowParameters(effect);
                Vector3 sunDir = GraphicsManager.Instance.ShadowMapRenderer?.LightDirection ?? Constants.SUN_DIRECTION;
                if (sunDir.LengthSquared() < 0.0001f)
                    sunDir = new Vector3(1f, 0f, -0.6f);
                sunDir = Vector3.Normalize(sunDir);
                bool worldAllowsSun = first.World is WorldControl wc ? wc.IsSunWorld : true;
                bool sunEnabled = Constants.SUN_ENABLED && worldAllowsSun && first.UseSunLight && !first.HasWalkerAncestor();

                bindings.WorldViewProjection?.SetValue(first.WorldPosition * Camera.Instance.ViewProjection);
                bindings.World?.SetValue(first.WorldPosition);
                bindings.LightDirection?.SetValue(sunDir);
                bindings.ShadowStrength?.SetValue(sunEnabled ? SunCycleManager.GetEffectiveShadowStrength() : 0f);
                first.ApplyItemMaterialUpgradeBindings(bindings);
                bindings.Time?.SetValue(first.GetShaderTimeSeconds());
                bindings.Alpha?.SetValue(1f);

                for (int i = 0; i < parts.Count; i++)
                {
                    var p = parts[i];
                    int group = p.MaterialItemGroup, index = p.MaterialItemIndex;
                    Vector3 chrome = Core.Utilities.ItemVisualRules.GetChromeColor(group, index);
                    Vector3 ancient = Core.Utilities.ItemVisualRules.GetAncientColor(group, index);
                    s_partChromeColors[i] = new Vector4(chrome, 0f);
                    s_partChrome2Tints[i] = Core.Utilities.ItemVisualRules.GetChrome2Tint(group, index);
                    s_partAncientColors[i] = new Vector4(ancient, 0f);
                }

                effect.Parameters["AtlasTexture"]?.SetValue(atlas);
                effect.Parameters["AtlasSize"]?.SetValue((float)PlayerPartAtlas.PageSize);
                effect.Parameters["PartChromeColor"]?.SetValue(s_partChromeColors);
                effect.Parameters["PartChrome2Tint"]?.SetValue(s_partChrome2Tints);
                effect.Parameters["PartAncientColor"]?.SetValue(s_partAncientColors);

                gd.BlendState = BlendState.Opaque;
                gd.DepthStencilState = DepthStencilState.Default;
                gd.SetVertexBuffer(composite.VertexBuffer);
                gd.Indices = composite.IndexBuffer;

                if (composite.OneSidedPrimitives > 0)
                {
                    gd.RasterizerState = _cullClockwise;
                    effect.CurrentTechnique.Passes[0].Apply();
                    gd.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, composite.OneSidedPrimitives);
                }

                if (composite.TwoSidedPrimitives > 0)
                {
                    gd.RasterizerState = _cullNone;
                    effect.CurrentTechnique.Passes[0].Apply();
                    gd.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, composite.OneSidedPrimitives * 3, composite.TwoSidedPrimitives);
                }

                RegisterGpuSkinnedMeshDraw();
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Drawing the merged player mesh failed.");
                return false;
            }
            finally
            {
                effect.CurrentTechnique = prevTechnique;
                gd.BlendState = prevBlend;
                gd.DepthStencilState = prevDepth;
                gd.RasterizerState = prevRaster;
            }
        }
    }
}
