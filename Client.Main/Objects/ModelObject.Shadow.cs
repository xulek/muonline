using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Core.Utilities;
using Client.Main.Graphics;
using Client.Main.Models;
using Client.Main.Objects.Player;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Client.Main.Objects
{
    public abstract partial class ModelObject
    {
        private static readonly VertexPositionTexture[] BlobShadowVertices =
        [
            // MU worlds use X/Y as the ground plane and Z as height. Keeping the blob in
            // XY avoids the legacy projected-mesh rotations which could turn actor shadows
            // nearly vertical or push them below the terrain.
            new VertexPositionTexture(new Vector3(-1f, -1f, 0f), new Vector2(0f, 1f)),
            new VertexPositionTexture(new Vector3(1f, -1f, 0f), new Vector2(1f, 1f)),
            new VertexPositionTexture(new Vector3(1f, 1f, 0f), new Vector2(1f, 0f)),
            new VertexPositionTexture(new Vector3(-1f, 1f, 0f), new Vector2(0f, 0f))
        ];

        private static readonly short[] BlobShadowIndices = [0, 1, 2, 0, 2, 3];
        private const int BlobShadowGridSegments = 2;
        private static readonly VertexPositionTexture[] BlobShadowGridTemplate = CreateBlobShadowGridTemplate();
        private static readonly short[] BlobShadowGridIndices = CreateBlobShadowGridIndices();
        private static readonly object BlobShadowTextureLock = new();
        private static Texture2D _blobShadowTexture;

        // Blob shadows follow the terrain with a small grid lifted by this bias (the projected mesh shadows use
        // the same value in the shader, DynamicLighting.fx GroundShadowBias).
        private const float TerrainShadowSurfaceBias = 1.25f;
        private VertexPositionTexture[] _terrainBlobShadowVertices;
        private Matrix _terrainBlobLastWorld;
        private bool _terrainBlobHasLastWorld;
        private bool _terrainBlobCacheValid;
        private object _terrainBlobLastSnowSurface;
        private long _terrainBlobLastSnowSurfaceVersion;

        private ModelObject GetShadowActorRoot()
        {
            ModelObject root = this;
            while (root.Parent is ModelObject parentModel)
                root = parentModel;
            return root;
        }

        private bool IsPlayerOrNpcShadowPart()
        {
            ModelObject root = GetShadowActorRoot();
            return root is PlayerObject || root is NPCObject;
        }

        private bool UsesRenderedShadowMapForCurrentObject()
        {
            if (!Constants.ENABLE_DYNAMIC_LIGHTING_SHADER)
                return false;

            var renderer = GraphicsManager.Instance.ShadowMapRenderer;
            ModelObject casterRoot = GetShadowActorRoot();
            return renderer?.IsReady == true && renderer.HasRenderedCaster(casterRoot);
        }

        private bool SupportsGpuSkinnedShadowCaster()
        {
            var effect = GraphicsManager.Instance.DynamicLightingEffect;
            return TryGetTechnique(effect, "ShadowCaster_Skinned") != null;
        }

        private bool NeedsModularShadowSafetyBlob()
        {
            if (this is not WalkerObject)
                return false;

            int substantialParts = 0;
            var children = Children.GetSnapshotArray();
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i] is not ModelObject child ||
                    child.Hidden ||
                    !child.RenderShadow ||
                    child.Status != GameControlStatus.Ready ||
                    child.Model?.Meshes == null)
                {
                    continue;
                }

                substantialParts++;
                if (substantialParts >= 2)
                    return true;
            }

            return false;
        }

        private bool RequiresPersistentActorGroundShadow()
        {
            // Player-model actors are assembled from multiple asynchronously prepared parts.
            // A shadow-map update may temporarily contain only one part (for example a helmet),
            // so keep a stable root footprint independently of caster completeness.
            return this is PlayerObject || this is NPCObject;
        }

        private bool ValidateWorldMatrix(Matrix matrix)
        {
            for (int i = 0; i < 16; i++)
            {
                if (!float.IsFinite(matrix[i]))
                    return false;
            }
            return true;
        }

        private bool TryGetGroundBlobShadowMatrix(out Matrix shadowWorld)
        {
            shadowWorld = Matrix.Identity;

            try
            {
                if (World?.Terrain == null)
                    return false;

                ModelObject root = this;
                while (root.Parent is ModelObject parentModel)
                    root = parentModel;

                Vector3 position = root.WorldPosition.Translation;
                float terrainHeight = World.Terrain.RequestTerrainHeight(position.X, position.Y);
                if (!float.IsFinite(terrainHeight))
                    return false;

                const float groundBias = 0.75f;
                shadowWorld = Matrix.CreateRotationZ(root.TotalAngle.Z) *
                              Matrix.CreateTranslation(position.X, position.Y, terrainHeight + groundBias);
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogDebug("Error creating ground blob shadow matrix: {Message}", ex.Message);
                return false;
            }
        }

        private bool TryGetShadowMatrix(out Matrix shadowWorld)
        {
            shadowWorld = Matrix.Identity;

            try
            {
                // For bone-attached models (weapons, wings, etc.) reuse the parent's blob-shadow basis
                // so attachments share the same shadow anchor/orientation as the character.
                if (ParentBoneLink >= 0 && Parent is ModelObject parentModel)
                {
                    if (!parentModel.TryGetShadowMatrix(out Matrix parentShadowWorld))
                        return false;

                    Matrix localMatrix = Matrix.CreateScale(Scale) *
                                         Matrix.CreateFromQuaternion(MathUtils.AngleQuaternion(Angle)) *
                                         Matrix.CreateTranslation(Position);

                    shadowWorld = localMatrix * ParentBodyOrigin * parentShadowWorld;
                    return true;
                }

                Vector3 position = WorldPosition.Translation;
                float terrainH = World.Terrain.RequestTerrainHeight(position.X, position.Y);

                float heightAboveTerrain = position.Z - terrainH;
                float angleRad = MathHelper.ToRadians(45);

                Vector3 shadowPos = new(
                    position.X - (heightAboveTerrain / 2),
                    position.Y - (heightAboveTerrain / 4.5f),
                    terrainH + 1f);

                float yaw = TotalAngle.Y + MathHelper.ToRadians(110);
                float pitch = TotalAngle.X + MathHelper.ToRadians(120);
                float roll = TotalAngle.Z + MathHelper.ToRadians(90);

                Quaternion rotQ = Quaternion.CreateFromYawPitchRoll(yaw, pitch, roll);

                const float shadowBias = 0.1f;
                shadowWorld =
                      Matrix.CreateFromQuaternion(rotQ)
                    * Matrix.CreateScale(1.0f * TotalScale, 0.01f * TotalScale, 1.0f * TotalScale)
                    * Matrix.CreateRotationX(-MathHelper.PiOver2) // build the 2D footprint; terrain supplies final Z per vertex
                    * Matrix.CreateRotationZ(angleRad)
                    * Matrix.CreateTranslation(shadowPos + new Vector3(0f, 0f, shadowBias));

                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogDebug("Error creating shadow matrix: {Message}", ex.Message);
                return false;
            }
        }

        public static long DiagBlobShadowDraws;

        // Ground shadows of the opaque world pass are deferred to its end: blob shadows are collected and drawn
        // in one call, projected mesh shadows (ModelObject.ProjectedShadows) one technique after the other
        // instead of between the models. The shadow colour is black with SrcAlpha/InvSrcAlpha blending
        // (dst * (1 - a)), so the order between shadows does not change the result, and depth testing keeps them
        // under the models. Outside the window shadows draw immediately.
        private const int BlobShadowBatchMaxVertices = short.MaxValue;
        private static bool s_blobShadowBatchActive;
        private static VertexPositionColorTexture[] s_blobShadowBatchVertices = new VertexPositionColorTexture[9 * 128];
        private static short[] s_blobShadowBatchIndices = new short[24 * 128];
        private static int s_blobShadowBatchVertexCount;
        private static int s_blobShadowBatchIndexCount;
        private static Matrix s_blobShadowBatchViewProjection;

        internal static void BeginGroundShadowBatch()
        {
            s_blobShadowBatchActive = true;
            s_blobShadowBatchVertexCount = 0;
            s_blobShadowBatchIndexCount = 0;
        }

        internal static void EndGroundShadowBatch(GraphicsDevice graphicsDevice)
        {
            s_blobShadowBatchActive = false;
            var pathToken = Client.Main.Diagnostics.DrawPathCounter.Start();
            try
            {
                FlushBlobShadowBatch(graphicsDevice);
            }
            finally
            {
                DrawQueuedProjectedShadows(graphicsDevice);
                Client.Main.Diagnostics.DrawPathCounter.StopExtra(pathToken, Client.Main.Diagnostics.DrawPathCounter.Extra.GroundShadows);
            }
        }

        private Matrix GetBlobShadowWorld(Matrix shadowWorld)
        {
            float localWidth = BoundingBoxLocal.Max.X - BoundingBoxLocal.Min.X;
            float localDepth = BoundingBoxLocal.Max.Y - BoundingBoxLocal.Min.Y;
            float scaleX = MathF.Max(45f, localWidth * 0.55f);
            float scaleZ = MathF.Max(45f, localDepth * 0.55f);
            return Matrix.CreateScale(scaleX, scaleZ, 1f) * shadowWorld;
        }

        private bool TryQueueBlobShadow(Matrix shadowWorld, float shadowOpacity, Matrix viewProjection)
        {
            if (TryGetTechnique(GraphicsManager.Instance.ShadowEffect, "ShadowBatched") == null)
                return false;

            // One batch is drawn with one camera; a blob for another camera starts a new batch.
            if (s_blobShadowBatchIndexCount > 0 && s_blobShadowBatchViewProjection != viewProjection)
                FlushBlobShadowBatch(GraphicsDevice);
            s_blobShadowBatchViewProjection = viewProjection;

            Matrix blobWorld = GetBlobShadowWorld(shadowWorld);
            VertexPositionTexture[] source;
            short[] sourceIndices;
            if (TryPrepareTerrainConformedBlobShadow(blobWorld, out VertexPositionTexture[] conformed))
            {
                source = conformed;
                sourceIndices = BlobShadowGridIndices;
            }
            else
            {
                source = BlobShadowVertices;
                sourceIndices = BlobShadowIndices;
            }

            if (s_blobShadowBatchVertexCount + source.Length > BlobShadowBatchMaxVertices)
                FlushBlobShadowBatch(GraphicsDevice);

            EnsureBlobShadowBatchCapacity(s_blobShadowBatchVertexCount + source.Length, s_blobShadowBatchIndexCount + sourceIndices.Length);

            var color = new Color((byte)0, (byte)0, (byte)0, (byte)(MathHelper.Clamp(shadowOpacity, 0f, 1f) * 255f + 0.5f));
            int baseVertex = s_blobShadowBatchVertexCount;
            bool conformedToTerrain = ReferenceEquals(source, conformed);
            for (int i = 0; i < source.Length; i++)
            {
                // Terrain-conformed vertices are already in world space; the flat fallback is local.
                Vector3 position = conformedToTerrain ? source[i].Position : Vector3.Transform(source[i].Position, blobWorld);
                s_blobShadowBatchVertices[baseVertex + i] = new VertexPositionColorTexture(position, color, source[i].TextureCoordinate);
            }

            for (int i = 0; i < sourceIndices.Length; i++)
                s_blobShadowBatchIndices[s_blobShadowBatchIndexCount + i] = (short)(baseVertex + sourceIndices[i]);

            s_blobShadowBatchVertexCount += source.Length;
            s_blobShadowBatchIndexCount += sourceIndices.Length;
            return true;
        }

        private static void EnsureBlobShadowBatchCapacity(int vertices, int indices)
        {
            if (vertices > s_blobShadowBatchVertices.Length)
                Array.Resize(ref s_blobShadowBatchVertices, Math.Min(BlobShadowBatchMaxVertices, Math.Max(vertices, s_blobShadowBatchVertices.Length * 2)));
            if (indices > s_blobShadowBatchIndices.Length)
                Array.Resize(ref s_blobShadowBatchIndices, Math.Max(indices, s_blobShadowBatchIndices.Length * 2));
        }

        private static void FlushBlobShadowBatch(GraphicsDevice graphicsDevice)
        {
            int vertexCount = s_blobShadowBatchVertexCount;
            int indexCount = s_blobShadowBatchIndexCount;
            s_blobShadowBatchVertexCount = 0;
            s_blobShadowBatchIndexCount = 0;
            if (indexCount == 0 || graphicsDevice == null)
                return;

            var effect = GraphicsManager.Instance.ShadowEffect;
            var blobTexture = _blobShadowTexture;
            EffectTechnique batchedTechnique = TryGetTechnique(effect, "ShadowBatched");
            ModelEffectBindings bindings = GetModelEffectBindings(effect);
            if (batchedTechnique == null || bindings == null || blobTexture == null || blobTexture.IsDisposed)
                return;

            var previousBlend = graphicsDevice.BlendState;
            var previousDepth = graphicsDevice.DepthStencilState;
            var previousRaster = graphicsDevice.RasterizerState;
            var previousTechnique = effect.CurrentTechnique;
            try
            {
                float constBias = 1f / (1 << 24);
                graphicsDevice.BlendState = Blendings.ShadowBlend;
                graphicsDevice.DepthStencilState = DepthStencilState.DepthRead;
                graphicsDevice.RasterizerState = GraphicsManager.GetCachedRasterizerState(constBias * -20, CullMode.None);
                effect.CurrentTechnique = batchedTechnique;
                bindings.ShadowTexture?.SetValue(blobTexture);
                bindings.WorldViewProjection?.SetValue(s_blobShadowBatchViewProjection);
                batchedTechnique.Passes[0].Apply();
                graphicsDevice.DrawUserIndexedPrimitives(
                    PrimitiveType.TriangleList,
                    s_blobShadowBatchVertices,
                    0,
                    vertexCount,
                    s_blobShadowBatchIndices,
                    0,
                    indexCount / 3);
                DiagBlobShadowDraws++;
            }
            finally
            {
                graphicsDevice.BlendState = previousBlend;
                graphicsDevice.DepthStencilState = previousDepth;
                graphicsDevice.RasterizerState = previousRaster;
                if (previousTechnique != null)
                    effect.CurrentTechnique = previousTechnique;
            }
        }

        public virtual void DrawBlobShadow(Matrix view, Matrix projection, Matrix shadowWorld, float shadowOpacity)
        {
            if (shadowOpacity <= 0.001f)
                return;

            try
            {
                // Skip shadow rendering if shadows are disabled for this world
                if (MuGame.Instance.ActiveScene?.World is WorldControl world && !world.EnableShadows)
                    return;

                var effect = GraphicsManager.Instance.ShadowEffect;
                var blobTexture = GetOrCreateBlobShadowTexture();
                if (effect == null || blobTexture == null)
                    return;

                if (s_blobShadowBatchActive && TryQueueBlobShadow(shadowWorld, shadowOpacity, view * projection))
                    return;

                ModelEffectBindings bindings = GetModelEffectBindings(effect);
                EffectTechnique shadowTechnique = TryGetTechnique(effect, "Shadow");
                if (shadowTechnique == null)
                    return;

                var previousBlend = GraphicsDevice.BlendState;
                var previousDepth = GraphicsDevice.DepthStencilState;
                var previousRaster = GraphicsDevice.RasterizerState;
                var previousTechnique = effect.CurrentTechnique;

                float constBias = 1f / (1 << 24);
                RasterizerState shadowRasterizer = GraphicsManager.GetCachedRasterizerState(constBias * -20, CullMode.None);

                GraphicsDevice.BlendState = Blendings.ShadowBlend;
                GraphicsDevice.DepthStencilState = DepthStencilState.DepthRead;
                GraphicsDevice.RasterizerState = shadowRasterizer;
                effect.CurrentTechnique = shadowTechnique;

                try
                {
                    Matrix blobWorld = GetBlobShadowWorld(shadowWorld);
                    Matrix viewProjection = view * projection;

                    bindings.ShadowTint?.SetValue(new Vector4(0f, 0f, 0f, shadowOpacity));
                    bindings.ShadowTexture?.SetValue(blobTexture);

                    DiagBlobShadowDraws++;
                    if (TryPrepareTerrainConformedBlobShadow(blobWorld, out VertexPositionTexture[] conformedBlobVertices))
                    {
                        bindings.WorldViewProjection?.SetValue(viewProjection);
                        effect.CurrentTechnique.Passes[0].Apply();
                        GraphicsDevice.DrawUserIndexedPrimitives(
                            PrimitiveType.TriangleList,
                            conformedBlobVertices,
                            0,
                            BlobShadowGridTemplate.Length,
                            BlobShadowGridIndices,
                            0,
                            BlobShadowGridIndices.Length / 3);
                    }
                    else
                    {
                        bindings.WorldViewProjection?.SetValue(blobWorld * viewProjection);
                        effect.CurrentTechnique.Passes[0].Apply();
                        GraphicsDevice.DrawUserIndexedPrimitives(
                            PrimitiveType.TriangleList,
                            BlobShadowVertices,
                            0,
                            BlobShadowVertices.Length,
                            BlobShadowIndices,
                            0,
                            2);
                    }
                }
                finally
                {
                    GraphicsDevice.BlendState = previousBlend;
                    GraphicsDevice.DepthStencilState = previousDepth;
                    GraphicsDevice.RasterizerState = previousRaster;
                    if (previousTechnique != null)
                        effect.CurrentTechnique = previousTechnique;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogDebug("Error in DrawBlobShadow: {Message}", ex.Message);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool HasTerrainShadowMatrixChanged(in Matrix previous, in Matrix current)
        {
            // Compare the complete matrix because projected vertices use all components,
            // including rotations that mix the source Z coordinate into the ground plane.
            for (int i = 0; i < 16; i++)
            {
                if (previous[i] != current[i])
                    return true;
            }

            return false;
        }

        private static VertexPositionTexture[] CreateBlobShadowGridTemplate()
        {
            int verticesPerSide = BlobShadowGridSegments + 1;
            var vertices = new VertexPositionTexture[verticesPerSide * verticesPerSide];
            int index = 0;

            for (int y = 0; y < verticesPerSide; y++)
            {
                float v = y / (float)BlobShadowGridSegments;
                float localY = -1f + v * 2f;

                for (int x = 0; x < verticesPerSide; x++)
                {
                    float u = x / (float)BlobShadowGridSegments;
                    float localX = -1f + u * 2f;
                    vertices[index++] = new VertexPositionTexture(
                        new Vector3(localX, localY, 0f),
                        new Vector2(u, 1f - v));
                }
            }

            return vertices;
        }

        private static short[] CreateBlobShadowGridIndices()
        {
            int verticesPerSide = BlobShadowGridSegments + 1;
            var indices = new short[BlobShadowGridSegments * BlobShadowGridSegments * 6];
            int index = 0;

            for (int y = 0; y < BlobShadowGridSegments; y++)
            {
                for (int x = 0; x < BlobShadowGridSegments; x++)
                {
                    short topLeft = (short)(y * verticesPerSide + x);
                    short topRight = (short)(topLeft + 1);
                    short bottomLeft = (short)(topLeft + verticesPerSide);
                    short bottomRight = (short)(bottomLeft + 1);

                    indices[index++] = topLeft;
                    indices[index++] = topRight;
                    indices[index++] = bottomRight;
                    indices[index++] = topLeft;
                    indices[index++] = bottomRight;
                    indices[index++] = bottomLeft;
                }
            }

            return indices;
        }

        private bool TryPrepareTerrainConformedBlobShadow(
            Matrix blobWorld,
            out VertexPositionTexture[] outputVertices)
        {
            outputVertices = null;
            var terrain = World?.Terrain;
            if (terrain == null)
                return false;

            _terrainBlobShadowVertices ??= new VertexPositionTexture[BlobShadowGridTemplate.Length];
            bool transformChanged = !_terrainBlobHasLastWorld ||
                                    HasTerrainShadowMatrixChanged(_terrainBlobLastWorld, blobWorld);
            var snowSurface = terrain.SnowShadowSurface;
            long snowVersion = snowSurface?.SurfaceVersion ?? 0;

            if (_terrainBlobCacheValid && !transformChanged && ReferenceEquals(_terrainBlobLastSnowSurface, snowSurface) &&
                _terrainBlobLastSnowSurfaceVersion == snowVersion)
            {
                outputVertices = _terrainBlobShadowVertices;
                return true;
            }

            for (int i = 0; i < BlobShadowGridTemplate.Length; i++)
            {
                VertexPositionTexture templateVertex = BlobShadowGridTemplate[i];
                Vector3 position = Vector3.Transform(templateVertex.Position, blobWorld);
                if (!float.IsFinite(position.X) || !float.IsFinite(position.Y))
                    return _terrainBlobCacheValid;

                float terrainHeight = terrain.RequestShadowSurfaceHeight(position.X, position.Y);
                if (!float.IsFinite(terrainHeight))
                    return _terrainBlobCacheValid;

                position.Z = terrainHeight + TerrainShadowSurfaceBias;
                _terrainBlobShadowVertices[i] = new VertexPositionTexture(
                    position,
                    templateVertex.TextureCoordinate);
            }

            _terrainBlobLastWorld = blobWorld;
            _terrainBlobHasLastWorld = true;
            _terrainBlobLastSnowSurface = snowSurface;
            _terrainBlobLastSnowSurfaceVersion = snowVersion;
            _terrainBlobCacheValid = true;
            outputVertices = _terrainBlobShadowVertices;
            return true;
        }

        private void ReleaseTerrainShadowResources()
        {
            _terrainBlobShadowVertices = null;
            _terrainBlobHasLastWorld = false;
            _terrainBlobCacheValid = false;
        }

        private Texture2D GetOrCreateBlobShadowTexture()
        {
            var texture = _blobShadowTexture;
            if (texture != null && !texture.IsDisposed)
                return texture;

            lock (BlobShadowTextureLock)
            {
                texture = _blobShadowTexture;
                if (texture != null && !texture.IsDisposed)
                    return texture;

                const int size = 64;
                texture = new Texture2D(GraphicsDevice, size, size, false, SurfaceFormat.Color);
                var data = new Color[size * size];
                float center = (size - 1) * 0.5f;
                float invRadius = 1f / center;

                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float dx = (x - center) * invRadius;
                        float dy = (y - center) * invRadius;
                        float dist = MathF.Sqrt(dx * dx + dy * dy);
                        float alpha = MathF.Max(0f, 1f - dist);
                        alpha *= alpha;
                        data[y * size + x] = new Color((byte)255, (byte)255, (byte)255, (byte)(alpha * 255f));
                    }
                }

                texture.SetData(data);
                _blobShadowTexture = texture;
                return texture;
            }
        }

        public virtual int DrawShadowCaster(Effect shadowEffect, Matrix lightViewProjection)
        {
            if (shadowEffect == null)
                return 0;

            int drawnMeshCount = 0;
            int shadowSize = GraphicsManager.Instance.ShadowMapRenderer?.ShadowMap?.Width ?? Math.Max(256, Constants.SHADOW_MAP_SIZE);
            Vector2 shadowTexel = new Vector2(1f / shadowSize, 1f / shadowSize);

            // Draw own meshes if available. A missing technique or one invalid body part
            // must not suppress valid child casters.
            if (Model?.Meshes != null && _meshes != null)
            {
                try
                {
                    var gd = GraphicsDevice;
                    var prevBlend = gd.BlendState;
                    var prevDepth = gd.DepthStencilState;
                    var prevRaster = gd.RasterizerState;
                    var prevTechnique = shadowEffect.CurrentTechnique;

                    try
                    {
                        ModelEffectBindings bindings = GetModelEffectBindings(shadowEffect);
                        var shadowCasterTechnique = bindings.GetTechnique("ShadowCaster");
                        var shadowCasterSkinnedTechnique = bindings.GetTechnique("ShadowCaster_Skinned");
                        if (shadowCasterTechnique != null)
                        {
                            bindings.World?.SetValue(WorldPosition);
                            bindings.LightViewProjection?.SetValue(lightViewProjection);
                            bindings.ShadowMapTexelSize?.SetValue(shadowTexel);
                            bindings.ShadowBias?.SetValue(Constants.SHADOW_BIAS);
                            bindings.ShadowNormalBias?.SetValue(Constants.SHADOW_NORMAL_BIAS);
                            bindings.SunDirection?.SetValue(GraphicsManager.Instance.ShadowMapRenderer?.LightDirection ?? Constants.SUN_DIRECTION);
                            bindings.UseProceduralTerrainUv?.SetValue(0.0f);
                            bindings.IsWaterTexture?.SetValue(0.0f);
                            bindings.TextureCoordinateOffset?.SetValue(Vector2.Zero);

                            gd.BlendState = BlendState.Opaque;
                            gd.DepthStencilState = DepthStencilState.Default;

                            int meshCount = Model.Meshes.Length;
                            EffectTechnique activeTechnique = null;
                            int uploadedSkinnedBoneCount = 0;

                            for (int i = 0; i < meshCount; i++)
                            {
                                if (IsHiddenMesh(i))
                                    continue;

                                bool useGpuSkinning = shadowCasterSkinnedTechnique != null &&
                                                      EnsureGpuSkinnedMeshForMainPass(i);

                                VertexBuffer vb = useGpuSkinning ? _meshes[i].GpuVertexBuffer : _meshes?[i]?.CpuVertexBuffer;
                                IndexBuffer ib = useGpuSkinning ? _meshes[i].GpuIndexBuffer : _meshes?[i]?.CpuIndexBuffer;
                                var tex = _meshes[i].Texture;
                                if (vb == null || ib == null || tex == null)
                                    continue;

                                if (useGpuSkinning)
                                {
                                    int requiredBoneCount = _meshes != null && (uint)i < (uint)_meshes.Length
                                        ? _meshes[i].GpuBoneCount
                                        : 0;

                                    if (requiredBoneCount > uploadedSkinnedBoneCount)
                                    {
                                        if (!TryUploadGpuSkinBoneMatrices(shadowEffect, bindings, requiredBoneCount))
                                        {
                                            useGpuSkinning = false;
                                            vb = _meshes?[i]?.CpuVertexBuffer;
                                            ib = _meshes?[i]?.CpuIndexBuffer;
                                            if (vb == null || ib == null)
                                                continue;
                                        }
                                        else
                                        {
                                            uploadedSkinnedBoneCount = requiredBoneCount;
                                        }
                                    }
                                }

                                var targetTechnique = useGpuSkinning ? shadowCasterSkinnedTechnique : shadowCasterTechnique;
                                if (targetTechnique != activeTechnique)
                                {
                                    shadowEffect.CurrentTechnique = targetTechnique;
                                    activeTechnique = targetTechnique;
                                }

                                bool isTwoSided = IsMeshTwoSided(i, IsBlendMesh(i));
                                gd.RasterizerState = isTwoSided ? _cullNone : _cullClockwise;
                                bindings.DiffuseTexture?.SetValue(tex);

                                shadowEffect.CurrentTechnique.Passes[0].Apply();
                                gd.SetVertexBuffer(vb);
                                gd.Indices = ib;
                                gd.DrawIndexedPrimitives(
                                    PrimitiveType.TriangleList,
                                    0, 0, ib.IndexCount / 3);

                                drawnMeshCount++;
                            }
                        }
                    }
                    finally
                    {
                        gd.BlendState = prevBlend;
                        gd.DepthStencilState = prevDepth;
                        gd.RasterizerState = prevRaster;
                        if (prevTechnique != null)
                            shadowEffect.CurrentTechnique = prevTechnique;
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug("Error drawing shadow caster: {Message}", ex.Message);
                }
            }

            // Recursively draw shadow casters for all children (armor, weapons, helm, etc.).
            var children = Children.GetSnapshotArray();
            bool skipSmallParts = Constants.SHADOW_SKIP_SMALL_PARTS;
            for (int i = 0; i < children.Length; i++)
            {
                var child = children[i];
                if (child is ModelObject modelChild &&
                    modelChild.Status == GameControlStatus.Ready &&
                    !modelChild.Hidden &&
                    modelChild.RenderShadow)
                {
                    if (skipSmallParts && IsSmallShadowPart(modelChild))
                        continue;

                    drawnMeshCount += modelChild.DrawShadowCaster(shadowEffect, lightViewProjection);
                }
            }

            return drawnMeshCount;
        }

        /// <summary>
        /// Checks if a model child is a small part that can be skipped for shadow casting.
        /// Small parts like weapons, gloves, and boots don't contribute much to shadow silhouette.
        /// </summary>
        private static bool IsSmallShadowPart(ModelObject modelChild)
        {
            return modelChild is WeaponObject ||
                   modelChild is PlayerGloveObject ||
                   modelChild is PlayerBootObject ||
                   modelChild is PlayerMaskHelmObject;
        }
    }
}
