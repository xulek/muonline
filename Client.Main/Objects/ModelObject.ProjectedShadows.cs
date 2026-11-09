using System;
using System.Collections.Generic;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Client.Main.Objects.Player;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects
{
    // Projected ground shadows of model meshes, drawn on the GPU like the planar shadows of the original
    // client: the vertex shader flattens the mesh with the shadow matrix and puts every vertex on the rendered
    // ground height read from TerrainControl.GetGroundHeightTexture (ProjectedShadow techniques,
    // DynamicLighting.fx). GPU-skinned meshes reuse the geometry of the main pass (ProjectedShadow_Skinned);
    // CPU-built buffers (OpenGL backends, static and deformed meshes) use ProjectedShadow. The merged player
    // body (ModelObject.PlayerComposite) casts one shadow for all parts it covers. In the opaque world pass the
    // draws are queued and submitted together after the list (ground shadow batch, ModelObject.Shadow).
    public partial class ModelObject
    {
        private readonly struct ProjectedShadowDraw
        {
            public ProjectedShadowDraw(ModelObject owner, int mesh, bool skinned, bool opaque, Matrix shadowWorld, float opacity)
            {
                Owner = owner;
                Mesh = mesh;
                Skinned = skinned;
                Opaque = opaque;
                ShadowWorld = shadowWorld;
                Opacity = opacity;
            }

            public ModelObject Owner { get; }
            public int Mesh { get; }
            public bool Skinned { get; }
            /// <summary>Solid shadow mask (else masked by the texture alpha).</summary>
            public bool Opaque { get; }
            public Matrix ShadowWorld { get; }
            public float Opacity { get; }
        }

        // Mesh index of the merged player body shadow (drawn from the actor root's composite buffers).
        private const int ProjectedShadowCompositeMesh = -1;

        private static readonly List<ProjectedShadowDraw> s_projectedShadowDraws = new(512);

        // Shadow parameters last set on the shared effect while drawing the queue.
        private struct ShadowParams
        {
            public bool Valid;
            public Matrix ShadowWorld;
            public float Opacity;
            public bool Opaque;
            public Texture2D Texture;
        }

        private static ShadowParams s_shadowParams;
        private int _compositeShadowQueuedFrame = -1;

        /// <summary>Diagnostics: projected shadow draws issued.</summary>
        public static long DiagProjectedShadowDraws;

        /// <summary>
        /// Queues the projected shadow of this object's opaque-pass meshes. Covered player body parts leave their
        /// merged meshes to the body shadow drawn by the actor root.
        /// </summary>
        private void QueueProjectedShadows(Matrix shadowWorld, float shadowOpacity)
        {
            if (_meshes == null || Model?.Meshes == null || World?.Terrain == null)
                return;
            if (MuGame.Instance.ActiveScene?.World is Controls.WorldControl world && !world.EnableShadows)
                return;
            if (!ValidateWorldMatrix(WorldPosition) || !ValidateWorldMatrix(shadowWorld))
                return;

            bool gpuGeometry = CanUseGpuSkinGeometry() && GetVertexDeformer() == null && !UsesMutableMeshData;
            // Body parts posed by their player hand their solid meshes to the player's merged shadow (one draw);
            // it includes the meshes of the merged body, so the composite shadow is not needed for them.
            PlayerObject mergeRoot = gpuGeometry && LinkParentAnimation && Parent is PlayerObject player ? player : null;
            bool compositeCovered = mergeRoot == null && IsCompositeCoveredThisFrame;
            if (compositeCovered && GetShadowActorRoot() is { } root &&
                root._compositeShadowQueuedFrame != MuGame.FrameIndex)
            {
                root._compositeShadowQueuedFrame = MuGame.FrameIndex;
                // Merged meshes are never RGBA (TryCollectCompositeMeshes): their shadow mask is solid.
                s_projectedShadowDraws.Add(new ProjectedShadowDraw(this, ProjectedShadowCompositeMesh, true, true, shadowWorld, shadowOpacity));
            }

            QueueProjectedShadowMeshes(GetMeshRenderPlan(false), compositeCovered, gpuGeometry, mergeRoot, shadowWorld, shadowOpacity);
            // Like BMD::AddMeshShadowTriangles every mesh but the blend mesh casts a shadow, so the RGBA meshes
            // of the transparent plan (wing feathers, capes) do too, masked by their texture alpha.
            QueueProjectedShadowMeshes(GetMeshRenderPlan(true), compositeCovered: false, gpuGeometry, mergeRoot, shadowWorld, shadowOpacity);

            // Outside the ground shadow window of the world pass (other scenes) the shadows draw right away.
            if (!s_blobShadowBatchActive)
                DrawQueuedProjectedShadows(GraphicsDevice);
        }

        private void QueueProjectedShadowMeshes(Dictionary<MeshStateKey, List<int>> plan, bool compositeCovered,
            bool gpuGeometry, PlayerObject mergeRoot, Matrix shadowWorld, float shadowOpacity)
        {
            foreach (var group in plan)
            {
                if (compositeCovered && group.Key.ShaderKind == MeshShaderKind.ItemMaterial)
                    continue;

                List<int> meshes = group.Value;
                for (int n = 0; n < meshes.Count; n++)
                {
                    int mesh = meshes[n];
                    if ((uint)mesh >= (uint)_meshes.Length || IsHiddenMesh(mesh) || IsBlendMesh(mesh) ||
                        IsStaticMapMeshQueuedForInstancing(mesh) || _meshes[mesh]?.Texture == null)
                        continue;

                    bool opaque = !_meshes[mesh].IsRgba;
                    if (gpuGeometry && CanUseGpuSkinningForMesh(mesh) && EnsureGpuSkinnedMeshForMainPass(mesh))
                    {
                        if (!opaque || mergeRoot == null || !mergeRoot.TryAddMergedShadowMesh(this, mesh, shadowWorld, shadowOpacity))
                            s_projectedShadowDraws.Add(new ProjectedShadowDraw(this, mesh, true, opaque, shadowWorld, shadowOpacity));
                    }
                    else if (_meshes[mesh].CpuVertexBuffer != null)
                        s_projectedShadowDraws.Add(new ProjectedShadowDraw(this, mesh, false, opaque, shadowWorld, shadowOpacity));
                }
            }
        }

        // Merged shadow of a player's body parts: the solid shadow meshes of the parts posed by the player (same
        // world matrix and bone palette) in one skinned buffer, rebuilt when the set of meshes changes.
        private const int ProjectedShadowMergedMesh = -2;

        private sealed class MergedShadowMesh : IDisposable
        {
            public VertexBuffer VertexBuffer;
            public IndexBuffer IndexBuffer;
            public int PrimitiveCount;
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

        private MergedShadowMesh _mergedShadow;
        private List<(ModelObject Part, int Mesh)> _mergedShadowSources;
        private int _mergedShadowFrame = -1;
        private Matrix _mergedShadowWorld;
        private float _mergedShadowOpacity;

        /// <summary>
        /// Adds a solid shadow mesh of a part posed by this player to its merged shadow; false when the part's
        /// shadow differs from the merged one (it then draws on its own).
        /// </summary>
        private bool TryAddMergedShadowMesh(ModelObject part, int mesh, Matrix shadowWorld, float opacity)
        {
            int frame = MuGame.FrameIndex;
            if (_mergedShadowFrame != frame)
            {
                _mergedShadowFrame = frame;
                (_mergedShadowSources ??= new List<(ModelObject, int)>(16)).Clear();
                _mergedShadowWorld = shadowWorld;
                _mergedShadowOpacity = opacity;
                // The first part uploads the shared palette for the merged draw.
                s_projectedShadowDraws.Add(new ProjectedShadowDraw(part, ProjectedShadowMergedMesh, true, true, shadowWorld, opacity));
            }
            else if (_mergedShadowWorld != shadowWorld || _mergedShadowOpacity != opacity)
            {
                return false;
            }

            _mergedShadowSources.Add((part, mesh));
            return true;
        }

        private MergedShadowMesh GetMergedShadowMesh()
        {
            var sources = _mergedShadowSources;
            if (sources == null || sources.Count == 0)
                return null;

            long signature;
            unchecked
            {
                signature = 1469598103934665603L;
                for (int i = 0; i < sources.Count; i++)
                {
                    signature = (signature ^ System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(sources[i].Part.Model)) * 1099511628211L;
                    signature = (signature ^ sources[i].Mesh) * 1099511628211L;
                }
            }

            if (_mergedShadow is { VertexBuffer.IsDisposed: false } cached && cached.Signature == signature)
                return cached;

            _mergedShadow?.Dispose();
            _mergedShadow = BuildMergedShadowMesh(sources, signature);
            return _mergedShadow;
        }

        private MergedShadowMesh BuildMergedShadowMesh(List<(ModelObject Part, int Mesh)> sources, long signature)
        {
            try
            {
                var vertices = new List<SkinnedVertexPositionColorNormalTexture>(4096);
                var indices = new List<int>(8192);
                int boneCount = 0;
                for (int i = 0; i < sources.Count; i++)
                {
                    if (!Content.BMDLoader.Instance.TryGetSkinnedMeshGeometry(sources[i].Part.Model, sources[i].Mesh,
                            out var meshVertices, out int[] meshIndices, out int meshBones))
                        return null;

                    boneCount = Math.Max(boneCount, meshBones);
                    int baseVertex = vertices.Count;
                    vertices.AddRange(meshVertices);
                    for (int k = 0; k < meshIndices.Length; k++)
                        indices.Add(baseVertex + meshIndices[k]);
                }

                if (vertices.Count == 0 || boneCount > MaxGpuSkinBones)
                    return null;

                var gd = GraphicsDevice;
                var merged = new MergedShadowMesh
                {
                    Signature = signature,
                    BoneCount = boneCount,
                    PrimitiveCount = indices.Count / 3,
                    VertexBuffer = new VertexBuffer(gd, SkinnedVertexPositionColorNormalTexture.VertexDeclaration,
                        vertices.Count, BufferUsage.WriteOnly),
                };
                merged.VertexBuffer.SetData(vertices.ToArray());
                if (vertices.Count <= ushort.MaxValue)
                {
                    var shortIndices = new ushort[indices.Count];
                    for (int i = 0; i < shortIndices.Length; i++)
                        shortIndices[i] = (ushort)indices[i];
                    merged.IndexBuffer = new IndexBuffer(gd, IndexElementSize.SixteenBits, shortIndices.Length, BufferUsage.WriteOnly);
                    merged.IndexBuffer.SetData(shortIndices);
                }
                else
                {
                    merged.IndexBuffer = new IndexBuffer(gd, IndexElementSize.ThirtyTwoBits, indices.Count, BufferUsage.WriteOnly);
                    merged.IndexBuffer.SetData(indices.ToArray());
                }
                return merged;
            }
            catch (Exception ex)
            {
                AppLoggerFactory?.CreateLogger<ModelObject>()?.LogDebug(ex, "Building the merged player shadow failed");
                return null;
            }
        }

        /// <summary>Draws the queued projected shadows, skinned ones first; called at the ground shadow batch end.</summary>
        /// <param name="maskViewProjection">Set when drawing into <see cref="GroundShadowMask"/> (no depth test).</param>
        /// <param name="keepQueued">Leaves the queue for the screen draw that follows.</param>
        private static void DrawQueuedProjectedShadows(GraphicsDevice gd, Matrix? maskViewProjection = null, bool keepQueued = false)
        {
            if (s_projectedShadowDraws.Count == 0 || gd == null)
                return;

            var effect = GraphicsManager.Instance.DynamicLightingEffect;
            ModelEffectBindings bindings = GetModelEffectBindings(effect);
            EffectTechnique staticTechnique = bindings?.GetTechnique("ProjectedShadow");
            EffectTechnique skinnedTechnique = bindings?.GetTechnique("ProjectedShadow_Skinned");
            var terrain = s_projectedShadowDraws[0].Owner.World?.Terrain;
            Texture2D groundHeights = terrain?.GetGroundHeightTexture();
            var camera = Camera.Instance;
            // DirectX/Vulkan read the height texture; OpenGL gets a node window per draw (GroundPatch).
            if (staticTechnique == null || groundHeights == null || camera == null)
            {
                if (!keepQueued)
                    s_projectedShadowDraws.Clear();
                return;
            }

            BlendState previousBlend = gd.BlendState;
            DepthStencilState previousDepth = gd.DepthStencilState;
            RasterizerState previousRasterizer = gd.RasterizerState;
            EffectTechnique previousTechnique = effect.CurrentTechnique;
            try
            {
                float constBias = 1f / (1 << 24);
                gd.BlendState = Blendings.ShadowBlend;
                gd.DepthStencilState = maskViewProjection.HasValue ? DepthStencilState.None : DepthStencilState.DepthRead;
                gd.RasterizerState = maskViewProjection.HasValue
                    ? RasterizerState.CullNone
                    : GraphicsManager.GetCachedRasterizerState(constBias * -20, CullMode.None);
                bindings.ViewProjection?.SetValue(maskViewProjection ?? camera.ViewProjection);
                bindings.GroundHeightTexture?.SetValue(groundHeights);

                // The shadow blend only darkens, so the draws may go in any order: grouped by technique (one
                // shader switch) and by mask kind, the parts of one actor follow each other and share their
                // shadow matrix, opacity and palette, which are then not set again.
                s_shadowParams = default;
                for (int pass = 0; pass < 4; pass++)
                {
                    bool skinnedPass = pass < 2;
                    bool opaquePass = (pass & 1) == 0;
                    EffectTechnique technique = skinnedPass ? skinnedTechnique : staticTechnique;
                    if (technique == null)
                        continue;

                    for (int i = 0; i < s_projectedShadowDraws.Count; i++)
                    {
                        ProjectedShadowDraw draw = s_projectedShadowDraws[i];
                        if (draw.Skinned == skinnedPass && draw.Opaque == opaquePass)
                            DrawProjectedShadow(gd, effect, bindings, technique, terrain, in draw);
                    }
                }
            }
            catch (Exception ex)
            {
                AppLoggerFactory?.CreateLogger<ModelObject>()?.LogDebug(ex, "Projected shadows failed");
            }
            finally
            {
                if (!keepQueued)
                    s_projectedShadowDraws.Clear();
                // The effect is shared; the shadow-map casters rely on the default.
                bindings.ShadowOpaqueCaster?.SetValue(0f);
                if (previousTechnique != null)
                    effect.CurrentTechnique = previousTechnique;
                gd.BlendState = previousBlend;
                gd.DepthStencilState = previousDepth;
                gd.RasterizerState = previousRasterizer;
            }
        }

        // OpenGL height window: 10x10 nodes around the shadow, four heights per constant (DynamicLighting.fx).
        private const int GroundPatchNodes = 10;
        private static readonly Vector4[] s_groundPatch = new Vector4[GroundPatchNodes * GroundPatchNodes / 4];
        private static readonly float[] s_groundPatchHeights = new float[GroundPatchNodes * GroundPatchNodes];

        private static void SetGroundPatch(ModelEffectBindings bindings, Controls.TerrainControl terrain, Vector3 center)
        {
            int originX = (int)MathF.Floor(center.X / Constants.TERRAIN_SCALE) - GroundPatchNodes / 2 + 1;
            int originY = (int)MathF.Floor(center.Y / Constants.TERRAIN_SCALE) - GroundPatchNodes / 2 + 1;
            for (int y = 0; y < GroundPatchNodes; y++)
            {
                for (int x = 0; x < GroundPatchNodes; x++)
                    s_groundPatchHeights[y * GroundPatchNodes + x] = terrain.GetGroundNodeHeight(originX + x, originY + y);
            }

            for (int i = 0; i < s_groundPatch.Length; i++)
            {
                s_groundPatch[i] = new Vector4(s_groundPatchHeights[i * 4], s_groundPatchHeights[i * 4 + 1],
                    s_groundPatchHeights[i * 4 + 2], s_groundPatchHeights[i * 4 + 3]);
            }

            bindings.GroundPatch.SetValue(s_groundPatch);
            bindings.GroundPatchOrigin?.SetValue(new Vector2(originX, originY));
        }

        private static void DrawProjectedShadow(GraphicsDevice gd, Effect effect, ModelEffectBindings bindings,
            EffectTechnique technique, Controls.TerrainControl terrain, in ProjectedShadowDraw draw)
        {
            ModelObject owner = draw.Owner;
            if (owner.Status == Models.GameControlStatus.Disposed || !owner.Visible)
                return;

            VertexBuffer vertexBuffer;
            IndexBuffer indexBuffer;
            int boneCount = 0, primitiveCount;
            bool opaque = draw.Opaque;
            Texture2D texture = null;
            if (draw.Mesh == ProjectedShadowMergedMesh)
            {
                if (owner.Parent is not ModelObject root)
                    return;
                MergedShadowMesh merged = root.GetMergedShadowMesh();
                if (merged == null)
                {
                    // Not buildable: the parts' meshes draw one by one.
                    var sources = root._mergedShadowSources;
                    for (int i = 0; sources != null && i < sources.Count; i++)
                    {
                        var single = new ProjectedShadowDraw(sources[i].Part, sources[i].Mesh, true, true, draw.ShadowWorld, draw.Opacity);
                        DrawProjectedShadow(gd, effect, bindings, technique, terrain, in single);
                    }
                    return;
                }
                vertexBuffer = merged.VertexBuffer;
                indexBuffer = merged.IndexBuffer;
                boneCount = merged.BoneCount;
                primitiveCount = merged.PrimitiveCount;
            }
            else if (draw.Mesh == ProjectedShadowCompositeMesh)
            {
                var composite = owner.GetShadowActorRoot()?._playerComposite;
                if (composite?.VertexBuffer is not { IsDisposed: false } || composite.IndexBuffer is not { IsDisposed: false })
                    return;
                vertexBuffer = composite.VertexBuffer;
                indexBuffer = composite.IndexBuffer;
                boneCount = composite.BoneCount;
                primitiveCount = composite.OneSidedPrimitives + composite.TwoSidedPrimitives;
            }
            else
            {
                var state = owner._meshes?[draw.Mesh];
                if (state == null)
                    return;
                if (draw.Skinned)
                {
                    vertexBuffer = state.GpuVertexBuffer;
                    indexBuffer = state.GpuIndexBuffer;
                    boneCount = state.GpuBoneCount;
                }
                else
                {
                    vertexBuffer = state.CpuVertexBuffer;
                    indexBuffer = state.CpuIndexBuffer;
                }

                if (vertexBuffer is not { IsDisposed: false } || indexBuffer is not { IsDisposed: false })
                    return;
                primitiveCount = indexBuffer.IndexCount / 3;
                texture = state.Texture;
            }

            if (primitiveCount <= 0)
                return;

            if (!ReferenceEquals(effect.CurrentTechnique, technique))
                effect.CurrentTechnique = technique;
            if (draw.Skinned && !owner.TryUploadGpuSkinBoneMatrices(effect, bindings, boneCount))
                return;

            // Every SetValue marks the constant buffer for upload; values shared with the previous draw are kept.
            ref ShadowParams last = ref s_shadowParams;
            if (!last.Valid || last.ShadowWorld != draw.ShadowWorld)
            {
                if (bindings.GroundPatch != null)
                    SetGroundPatch(bindings, terrain, draw.ShadowWorld.Translation);
                bindings.ProjectedShadowMatrix?.SetValue(draw.ShadowWorld);
                last.ShadowWorld = draw.ShadowWorld;
            }
            if (!last.Valid || last.Opacity != draw.Opacity)
            {
                bindings.ProjectedShadowOpacity?.SetValue(draw.Opacity);
                last.Opacity = draw.Opacity;
            }
            if (!last.Valid || last.Opaque != opaque)
            {
                bindings.ShadowOpaqueCaster?.SetValue(opaque ? 1f : 0f);
                last.Opaque = opaque;
            }
            if (!opaque && !ReferenceEquals(last.Texture, texture))
            {
                bindings.DiffuseTexture?.SetValue(texture);
                last.Texture = texture;
            }
            last.Valid = true;

            technique.Passes[0].Apply();
            gd.SetVertexBuffer(vertexBuffer);
            gd.Indices = indexBuffer;
            gd.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, primitiveCount);
            DiagProjectedShadowDraws++;
        }
    }
}
