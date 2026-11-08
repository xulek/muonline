using System;
using System.Collections.Generic;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects
{
    // Projected ground shadows of player/NPC actors on the GPU. The CPU path skins every shadow vertex,
    // projects it, samples the terrain under it, merges the parts of an actor into one buffer and uploads it
    // every frame. Here the GPU-skinned meshes the main pass already draws are flattened in the vertex shader
    // (ProjectedShadow_Skinned, DynamicLighting.fx) and put on the terrain height read from a height texture,
    // the same placement the CPU path computes. The merged player body (ModelObject.PlayerComposite) casts one
    // shadow for all its parts. OpenGL backends (no GPU skinning), the snow surface of Devias, vertex deformers
    // and meshes without GPU geometry keep the CPU path.
    public partial class ModelObject
    {
        private readonly struct GpuShadowDraw
        {
            public GpuShadowDraw(ModelObject owner, int mesh, Matrix shadowWorld, float opacity)
            {
                Owner = owner;
                Mesh = mesh;
                ShadowWorld = shadowWorld;
                Opacity = opacity;
            }

            public ModelObject Owner { get; }
            public int Mesh { get; }
            public Matrix ShadowWorld { get; }
            public float Opacity { get; }
        }

        // Mesh index of the merged player body shadow (drawn from the actor root's composite buffers).
        private const int GpuShadowCompositeMesh = -1;

        private static readonly List<GpuShadowDraw> s_gpuShadowDraws = new(512);
        private int _compositeShadowQueuedFrame = -1;

        /// <summary>Diagnostics: projected shadow draws issued by the GPU path.</summary>
        public static long DiagGpuShadowDraws;

        private bool CanUseGpuActorShadows()
        {
            if (!Constants.ENABLE_GPU_ACTOR_SHADOWS || !Constants.ENABLE_GPU_SKINNING)
                return false;

            var terrain = World?.Terrain;
            if (terrain == null || terrain.HasSnowShadowSurface || !IsPlayerOrNpcShadowPart() ||
                GetVertexDeformer() != null || UsesMutableMeshData || !CanUseGpuSkinGeometry())
                return false;

            return TryGetTechnique(GraphicsManager.Instance.DynamicLightingEffect, "ProjectedShadow_Skinned") != null;
        }

        /// <summary>
        /// Queues the projected shadow of this actor part's opaque-pass meshes. Meshes without GPU geometry
        /// are drawn through the CPU path. Returns false when the part cannot use the GPU path at all.
        /// </summary>
        private bool TryQueueGpuActorShadows(Matrix shadowWorld, float shadowOpacity)
        {
            if (!CanUseGpuActorShadows())
                return false;

            if (MuGame.Instance.ActiveScene?.World is Controls.WorldControl world && !world.EnableShadows)
                return true;
            if (!ValidateWorldMatrix(WorldPosition) || !ValidateWorldMatrix(shadowWorld))
                return true;

            bool compositeCovered = IsCompositeCoveredThisFrame;
            if (compositeCovered && GetShadowActorRoot() is { } root &&
                root._compositeShadowQueuedFrame != MuGame.FrameIndex)
            {
                // The merged body of the player casts the shadow of every part it covers.
                root._compositeShadowQueuedFrame = MuGame.FrameIndex;
                s_gpuShadowDraws.Add(new GpuShadowDraw(this, GpuShadowCompositeMesh, shadowWorld, shadowOpacity));
            }

            List<int> cpuFallback = null;
            foreach (var group in GetMeshRenderPlan(false))
            {
                if (compositeCovered && group.Key.ShaderKind == MeshShaderKind.ItemMaterial)
                    continue;

                List<int> meshes = group.Value;
                for (int n = 0; n < meshes.Count; n++)
                {
                    int mesh = meshes[n];
                    if ((uint)mesh >= (uint)_meshes.Length || IsHiddenMesh(mesh) ||
                        IsStaticMapMeshQueuedForInstancing(mesh) || _meshes[mesh]?.Texture == null)
                        continue;

                    if (CanUseGpuSkinningForMesh(mesh) && EnsureGpuSkinnedMeshForMainPass(mesh))
                    {
                        s_gpuShadowDraws.Add(new GpuShadowDraw(this, mesh, shadowWorld, shadowOpacity));
                    }
                    else
                    {
                        (cpuFallback ??= new List<int>(4)).Add(mesh);
                    }
                }
            }

            if (cpuFallback != null)
            {
                var camera = Camera.Instance;
                DrawMeshesShadow(cpuFallback, shadowWorld, camera.View, camera.Projection, shadowOpacity);
            }

            // Outside the ground shadow window of the world pass (other scenes) the shadows draw right away.
            if (!s_blobShadowBatchActive)
                DrawQueuedGpuShadows(GraphicsDevice);
            return true;
        }

        /// <summary>Draws the queued GPU projected shadows with one technique; called at the ground shadow batch end.</summary>
        private static void DrawQueuedGpuShadows(GraphicsDevice gd)
        {
            if (s_gpuShadowDraws.Count == 0 || gd == null)
                return;

            var effect = GraphicsManager.Instance.DynamicLightingEffect;
            ModelEffectBindings bindings = GetModelEffectBindings(effect);
            EffectTechnique technique = bindings?.GetTechnique("ProjectedShadow_Skinned");
            Texture2D groundHeights = s_gpuShadowDraws[0].Owner.World?.Terrain?.GetGroundHeightTexture();
            var camera = Camera.Instance;
            if (technique == null || groundHeights == null || camera == null)
            {
                s_gpuShadowDraws.Clear();
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
                gd.DepthStencilState = DepthStencilState.DepthRead;
                gd.RasterizerState = GraphicsManager.GetCachedRasterizerState(constBias * -20, CullMode.None);
                bindings.ViewProjection?.SetValue(camera.ViewProjection);
                bindings.GroundHeightTexture?.SetValue(groundHeights);

                for (int i = 0; i < s_gpuShadowDraws.Count; i++)
                {
                    GpuShadowDraw draw = s_gpuShadowDraws[i];
                    ModelObject owner = draw.Owner;
                    if (owner.Status == Models.GameControlStatus.Disposed || !owner.Visible)
                        continue;

                    VertexBuffer vertexBuffer;
                    IndexBuffer indexBuffer;
                    int boneCount, primitiveCount;
                    bool opaque;
                    Texture2D texture = null;
                    if (draw.Mesh == GpuShadowCompositeMesh)
                    {
                        var composite = owner.GetShadowActorRoot()?._playerComposite;
                        if (composite?.VertexBuffer is not { IsDisposed: false } || composite.IndexBuffer is not { IsDisposed: false })
                            continue;
                        vertexBuffer = composite.VertexBuffer;
                        indexBuffer = composite.IndexBuffer;
                        boneCount = composite.BoneCount;
                        primitiveCount = composite.OneSidedPrimitives + composite.TwoSidedPrimitives;
                        // Merged meshes are never RGBA (TryCollectCompositeMeshes): their shadow mask is solid.
                        opaque = true;
                    }
                    else
                    {
                        var state = owner._meshes?[draw.Mesh];
                        if (state?.GpuVertexBuffer is not { IsDisposed: false } || state.GpuIndexBuffer is not { IsDisposed: false })
                            continue;
                        vertexBuffer = state.GpuVertexBuffer;
                        indexBuffer = state.GpuIndexBuffer;
                        boneCount = state.GpuBoneCount;
                        primitiveCount = indexBuffer.IndexCount / 3;
                        opaque = !state.IsRgba;
                        texture = state.Texture;
                    }

                    effect.CurrentTechnique = technique;
                    if (primitiveCount <= 0 || !owner.TryUploadGpuSkinBoneMatrices(effect, bindings, boneCount))
                        continue;

                    bindings.ProjectedShadowMatrix?.SetValue(draw.ShadowWorld);
                    bindings.ProjectedShadowOpacity?.SetValue(draw.Opacity);
                    bindings.ShadowOpaqueCaster?.SetValue(opaque ? 1f : 0f);
                    if (!opaque)
                        bindings.DiffuseTexture?.SetValue(texture);

                    technique.Passes[0].Apply();
                    gd.SetVertexBuffer(vertexBuffer);
                    gd.Indices = indexBuffer;
                    gd.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, primitiveCount);
                    DiagGpuShadowDraws++;
                }
            }
            catch (Exception ex)
            {
                AppLoggerFactory?.CreateLogger<ModelObject>()?.LogDebug(ex, "GPU projected shadows failed");
            }
            finally
            {
                s_gpuShadowDraws.Clear();
                // The effect is shared; the shadow-map casters rely on the default.
                bindings.ShadowOpaqueCaster?.SetValue(0f);
                if (previousTechnique != null)
                    effect.CurrentTechnique = previousTechnique;
                gd.BlendState = previousBlend;
                gd.DepthStencilState = previousDepth;
                gd.RasterizerState = previousRasterizer;
            }
        }
    }
}
