using System;
using System.Collections.Generic;
using Client.Main.Controllers;
using Client.Main.Graphics;
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
            public ProjectedShadowDraw(ModelObject owner, int mesh, bool skinned, Matrix shadowWorld, float opacity)
            {
                Owner = owner;
                Mesh = mesh;
                Skinned = skinned;
                ShadowWorld = shadowWorld;
                Opacity = opacity;
            }

            public ModelObject Owner { get; }
            public int Mesh { get; }
            public bool Skinned { get; }
            public Matrix ShadowWorld { get; }
            public float Opacity { get; }
        }

        // Mesh index of the merged player body shadow (drawn from the actor root's composite buffers).
        private const int ProjectedShadowCompositeMesh = -1;

        private static readonly List<ProjectedShadowDraw> s_projectedShadowDraws = new(512);
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

            bool compositeCovered = IsCompositeCoveredThisFrame;
            if (compositeCovered && GetShadowActorRoot() is { } root &&
                root._compositeShadowQueuedFrame != MuGame.FrameIndex)
            {
                root._compositeShadowQueuedFrame = MuGame.FrameIndex;
                s_projectedShadowDraws.Add(new ProjectedShadowDraw(this, ProjectedShadowCompositeMesh, true, shadowWorld, shadowOpacity));
            }

            bool gpuGeometry = CanUseGpuSkinGeometry() && GetVertexDeformer() == null && !UsesMutableMeshData;
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

                    if (gpuGeometry && CanUseGpuSkinningForMesh(mesh) && EnsureGpuSkinnedMeshForMainPass(mesh))
                        s_projectedShadowDraws.Add(new ProjectedShadowDraw(this, mesh, true, shadowWorld, shadowOpacity));
                    else if (_meshes[mesh].CpuVertexBuffer != null)
                        s_projectedShadowDraws.Add(new ProjectedShadowDraw(this, mesh, false, shadowWorld, shadowOpacity));
                }
            }

            // Outside the ground shadow window of the world pass (other scenes) the shadows draw right away.
            if (!s_blobShadowBatchActive)
                DrawQueuedProjectedShadows(GraphicsDevice);
        }

        /// <summary>Draws the queued projected shadows, skinned ones first; called at the ground shadow batch end.</summary>
        private static void DrawQueuedProjectedShadows(GraphicsDevice gd)
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
                gd.DepthStencilState = DepthStencilState.DepthRead;
                gd.RasterizerState = GraphicsManager.GetCachedRasterizerState(constBias * -20, CullMode.None);
                bindings.ViewProjection?.SetValue(camera.ViewProjection);
                bindings.GroundHeightTexture?.SetValue(groundHeights);

                // Two passes keep each technique's draws together (one shader switch).
                for (int pass = 0; pass < 2; pass++)
                {
                    bool skinnedPass = pass == 0;
                    EffectTechnique technique = skinnedPass ? skinnedTechnique : staticTechnique;
                    if (technique == null)
                        continue;

                    for (int i = 0; i < s_projectedShadowDraws.Count; i++)
                    {
                        ProjectedShadowDraw draw = s_projectedShadowDraws[i];
                        if (draw.Skinned == skinnedPass)
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
            bool opaque;
            Texture2D texture = null;
            if (draw.Mesh == ProjectedShadowCompositeMesh)
            {
                var composite = owner.GetShadowActorRoot()?._playerComposite;
                if (composite?.VertexBuffer is not { IsDisposed: false } || composite.IndexBuffer is not { IsDisposed: false })
                    return;
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
                opaque = !state.IsRgba;
                texture = state.Texture;
            }

            if (primitiveCount <= 0)
                return;

            effect.CurrentTechnique = technique;
            if (draw.Skinned && !owner.TryUploadGpuSkinBoneMatrices(effect, bindings, boneCount))
                return;

            if (bindings.GroundPatch != null)
                SetGroundPatch(bindings, terrain, draw.ShadowWorld.Translation);
            bindings.ProjectedShadowMatrix?.SetValue(draw.ShadowWorld);
            bindings.ProjectedShadowOpacity?.SetValue(draw.Opacity);
            bindings.ShadowOpaqueCaster?.SetValue(opaque ? 1f : 0f);
            if (!opaque)
                bindings.DiffuseTexture?.SetValue(texture);

            technique.Passes[0].Apply();
            gd.SetVertexBuffer(vertexBuffer);
            gd.Indices = indexBuffer;
            gd.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, primitiveCount);
            DiagProjectedShadowDraws++;
        }
    }
}
