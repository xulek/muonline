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

        // Terrain-conformed projected shadows use persistent topology and GPU buffers.
        // Animated geometry is refreshed in the same frame as its source pose. Terrain samples
        // are cached per covered terrain patch, so animation stays exact without repeatedly
        // querying unchanged terrain-grid nodes.
        private const float TerrainShadowSurfaceBias = 1.25f;
        private const int TerrainShadowMaxTopologyVertices = 131_072;
        private const int TerrainShadowMaxTopologyIndices = 393_216;
        private const int TerrainShadowMaxPatchNodesPerAxis = 10;

        private readonly struct TerrainShadowSourceKey : IEquatable<TerrainShadowSourceKey>
        {
            private readonly short _node;
            private readonly int _x;
            private readonly int _y;
            private readonly int _z;

            public TerrainShadowSourceKey(short node, float x, float y, float z)
            {
                _node = node;
                _x = BitConverter.SingleToInt32Bits(x);
                _y = BitConverter.SingleToInt32Bits(y);
                _z = BitConverter.SingleToInt32Bits(z);
            }

            public bool Equals(TerrainShadowSourceKey other) =>
                _node == other._node && _x == other._x && _y == other._y && _z == other._z;

            public override bool Equals(object obj) =>
                obj is TerrainShadowSourceKey other && Equals(other);

            public override int GetHashCode() => HashCode.Combine(_node, _x, _y, _z);
        }

        private readonly struct TerrainShadowVertexKey : IEquatable<TerrainShadowVertexKey>
        {
            private readonly int _sourceSlot;
            private readonly int _u;
            private readonly int _v;

            public TerrainShadowVertexKey(int sourceSlot, float u, float v)
            {
                _sourceSlot = sourceSlot;
                _u = BitConverter.SingleToInt32Bits(u);
                _v = BitConverter.SingleToInt32Bits(v);
            }

            public bool Equals(TerrainShadowVertexKey other) =>
                _sourceSlot == other._sourceSlot && _u == other._u && _v == other._v;

            public override bool Equals(object obj) =>
                obj is TerrainShadowVertexKey other && Equals(other);

            public override int GetHashCode() => HashCode.Combine(_sourceSlot, _u, _v);
        }

        private sealed class TerrainShadowTopology
        {
            public Client.Data.BMD.BMDTextureMesh MeshReference;
            public int[] UsedSourceVertexIndices;
            public int[] VertexSourceSlots;
            public Vector2[] VertexTexCoords;
            public ushort[] Indices16;
            public int[] Indices32;
            public bool Uses32BitIndices;
            public int VertexCount;
            public int IndexCount;
            public int PrimitiveCount;
            public bool IsValid;
        }

        private sealed class TerrainShadowMeshCache : IDisposable
        {
            public TerrainShadowTopology Topology;
            public VertexPositionTexture[] UploadVertices;
            public Vector3[] ProjectedPositions;
            public float[] TerrainPatchHeights;
            public int TerrainPatchMinTileX;
            public int TerrainPatchMinTileY;
            public int TerrainPatchNodesX;
            public int TerrainPatchNodesY;
            public bool TerrainPatchValid;
            public DynamicVertexBuffer VertexBuffer;
            public IndexBuffer IndexBuffer;
            public Matrix[] LastBoneSource;
            public uint LastPoseVersion = uint.MaxValue;
            public Matrix LastShadowWorld;
            public bool HasLastShadowWorld;
            public object LastSnowSurface;
            public long LastSnowSurfaceVersion;
            public bool IsValid;

            public void Dispose()
            {
                VertexBuffer?.Dispose();
                VertexBuffer = null;
                IndexBuffer?.Dispose();
                IndexBuffer = null;
                Topology = null;
                UploadVertices = null;
                ProjectedPositions = null;
                TerrainPatchHeights = null;
                TerrainPatchValid = false;
                IsValid = false;
            }
        }

        private static readonly ConditionalWeakTable<Client.Data.BMD.BMDTextureMesh, TerrainShadowTopology>
            TerrainShadowTopologyCache = new();

        private TerrainShadowMeshCache[] _terrainShadowMeshCaches;
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
                    float localWidth = BoundingBoxLocal.Max.X - BoundingBoxLocal.Min.X;
                    float localDepth = BoundingBoxLocal.Max.Y - BoundingBoxLocal.Min.Y;
                    float scaleX = MathF.Max(45f, localWidth * 0.55f);
                    float scaleZ = MathF.Max(45f, localDepth * 0.55f);

                    Matrix blobWorld = Matrix.CreateScale(scaleX, scaleZ, 1f) * shadowWorld;
                    Matrix viewProjection = view * projection;

                    bindings.ShadowTint?.SetValue(new Vector4(0f, 0f, 0f, shadowOpacity));
                    bindings.ShadowTexture?.SetValue(blobTexture);

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

        public virtual void DrawShadowMesh(int mesh, Matrix view, Matrix projection, Matrix shadowWorld, float shadowOpacity)
        {
            try
            {
                // Skip shadow rendering if shadows are disabled for this world.
                if (MuGame.Instance.ActiveScene?.World is WorldControl world && !world.EnableShadows)
                    return;

                if (IsHiddenMesh(mesh) || _meshes == null)
                    return;

                if (!ValidateWorldMatrix(WorldPosition) || !ValidateWorldMatrix(shadowWorld))
                {
                    _logger?.LogDebug("Invalid shadow matrix detected - skipping shadow rendering");
                    return;
                }

                var effect = GraphicsManager.Instance.ShadowEffect;
                if (effect == null || _meshes?[mesh]?.Texture == null)
                    return;

                ModelEffectBindings bindings = GetModelEffectBindings(effect);
                EffectTechnique shadowTechnique = TryGetTechnique(effect, "Shadow");
                if (shadowTechnique == null)
                    return;

                var previousBlendState = GraphicsDevice.BlendState;
                var previousDepthState = GraphicsDevice.DepthStencilState;
                var previousRasterizerState = GraphicsDevice.RasterizerState;
                var previousTechnique = effect.CurrentTechnique;

                float constBias = 1f / (1 << 24);
                RasterizerState shadowRasterizer = GraphicsManager.GetCachedRasterizerState(
                    constBias * -20,
                    CullMode.None);

                GraphicsDevice.BlendState = Blendings.ShadowBlend;
                GraphicsDevice.DepthStencilState = DepthStencilState.DepthRead;
                GraphicsDevice.RasterizerState = shadowRasterizer;
                effect.CurrentTechnique = shadowTechnique;

                try
                {
                    Matrix viewProjection = view * projection;
                    bindings.ShadowTint?.SetValue(new Vector4(0f, 0f, 0f, shadowOpacity));
                    bindings.ShadowTexture?.SetValue(_meshes[mesh].Texture);

                    // Preferred path: persistent topology and buffers avoid rebuilding or
                    // tessellating the mesh. The vertex buffer follows the source animation in
                    // the same frame; only unchanged poses/transforms are skipped.
                    if (TryPrepareTerrainConformedShadowMesh(
                        mesh,
                        shadowWorld,
                        out DynamicVertexBuffer conformedVertexBuffer,
                        out IndexBuffer conformedIndexBuffer,
                        out int conformedPrimitiveCount))
                    {
                        bindings.WorldViewProjection?.SetValue(viewProjection);
                        effect.CurrentTechnique.Passes[0].Apply();
                        GraphicsDevice.SetVertexBuffer(conformedVertexBuffer);
                        GraphicsDevice.Indices = conformedIndexBuffer;
                        GraphicsDevice.DrawIndexedPrimitives(
                            PrimitiveType.TriangleList,
                            0,
                            0,
                            conformedPrimitiveCount);

                        return;
                    }

                    // Compatibility fallback for malformed models or unavailable terrain data.
                    VertexBuffer vertexBuffer = _meshes[mesh].CpuVertexBuffer;
                    IndexBuffer indexBuffer = _meshes[mesh].CpuIndexBuffer;
                    if (vertexBuffer == null || indexBuffer == null)
                        return;

                    bindings.WorldViewProjection?.SetValue(shadowWorld * viewProjection);
                    int primitiveCount = indexBuffer.IndexCount / 3;
                    effect.CurrentTechnique.Passes[0].Apply();
                    GraphicsDevice.SetVertexBuffer(vertexBuffer);
                    GraphicsDevice.Indices = indexBuffer;
                    GraphicsDevice.DrawIndexedPrimitives(
                        PrimitiveType.TriangleList,
                        0,
                        0,
                        primitiveCount);
                }
                finally
                {
                    GraphicsDevice.BlendState = previousBlendState;
                    GraphicsDevice.DepthStencilState = previousDepthState;
                    GraphicsDevice.RasterizerState = previousRasterizerState;
                    if (previousTechnique != null)
                        effect.CurrentTechnique = previousTechnique;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogDebug("Error in DrawShadowMesh: {Message}", ex.Message);
            }
        }

        private bool CanUseCachedTerrainConformedShadowPath()
        {
            return World?.Terrain != null && Model?.Meshes != null;
        }

        private bool TryPrepareTerrainConformedShadowMesh(
            int meshIndex,
            Matrix shadowWorld,
            out DynamicVertexBuffer vertexBuffer,
            out IndexBuffer indexBuffer,
            out int primitiveCount)
        {
            vertexBuffer = null;
            indexBuffer = null;
            primitiveCount = 0;

            var terrain = World?.Terrain;
            if (terrain == null ||
                Model?.Meshes == null ||
                (uint)meshIndex >= (uint)Model.Meshes.Length)
            {
                return false;
            }

            if (!TryGetOrCreateTerrainShadowMeshCache(meshIndex, out TerrainShadowMeshCache cache))
                return false;

            if (!TryUpdateTerrainShadowMeshCache(cache, shadowWorld, terrain))
                return false;

            vertexBuffer = cache.VertexBuffer;
            indexBuffer = cache.IndexBuffer;
            primitiveCount = cache.Topology?.PrimitiveCount ?? 0;
            return vertexBuffer != null && !vertexBuffer.IsDisposed &&
                   indexBuffer != null && !indexBuffer.IsDisposed &&
                   primitiveCount > 0;
        }

        private bool TryGetOrCreateTerrainShadowMeshCache(
            int meshIndex,
            out TerrainShadowMeshCache cache)
        {
            cache = null;
            var meshes = Model?.Meshes;
            if (meshes == null || (uint)meshIndex >= (uint)meshes.Length)
                return false;

            if (_terrainShadowMeshCaches == null || _terrainShadowMeshCaches.Length != meshes.Length)
            {
                ReleaseTerrainShadowMeshCaches();
                _terrainShadowMeshCaches = new TerrainShadowMeshCache[meshes.Length];
            }

            var mesh = meshes[meshIndex];
            cache = _terrainShadowMeshCaches[meshIndex];
            if (cache != null &&
                ReferenceEquals(cache.Topology?.MeshReference, mesh) &&
                cache.VertexBuffer != null &&
                !cache.VertexBuffer.IsDisposed &&
                cache.IndexBuffer != null &&
                !cache.IndexBuffer.IsDisposed)
            {
                return true;
            }

            cache?.Dispose();
            cache = BuildTerrainShadowMeshCache(mesh);
            _terrainShadowMeshCaches[meshIndex] = cache;
            return cache != null;
        }

        private TerrainShadowMeshCache BuildTerrainShadowMeshCache(
            Client.Data.BMD.BMDTextureMesh mesh)
        {
            if (mesh == null)
                return null;

            TerrainShadowTopology topology = TerrainShadowTopologyCache.GetValue(
                mesh,
                static meshKey => BuildTerrainShadowTopology(meshKey));
            if (topology == null || !topology.IsValid)
                return null;

            var uploadVertices = new VertexPositionTexture[topology.VertexCount];
            for (int i = 0; i < topology.VertexCount; i++)
                uploadVertices[i] = new VertexPositionTexture(Vector3.Zero, topology.VertexTexCoords[i]);

            DynamicVertexBuffer vertexBuffer = null;
            IndexBuffer indexBuffer = null;
            try
            {
                vertexBuffer = new DynamicVertexBuffer(
                    GraphicsDevice,
                    VertexPositionTexture.VertexDeclaration,
                    topology.VertexCount,
                    BufferUsage.WriteOnly);

                indexBuffer = new IndexBuffer(
                    GraphicsDevice,
                    topology.Uses32BitIndices ? IndexElementSize.ThirtyTwoBits : IndexElementSize.SixteenBits,
                    topology.IndexCount,
                    BufferUsage.WriteOnly);

                if (topology.Uses32BitIndices)
                    indexBuffer.SetData(topology.Indices32);
                else
                    indexBuffer.SetData(topology.Indices16);

                return new TerrainShadowMeshCache
                {
                    Topology = topology,
                    UploadVertices = uploadVertices,
                    ProjectedPositions = new Vector3[topology.UsedSourceVertexIndices.Length],
                    TerrainPatchHeights = new float[16],
                    VertexBuffer = vertexBuffer,
                    IndexBuffer = indexBuffer
                };
            }
            catch
            {
                vertexBuffer?.Dispose();
                indexBuffer?.Dispose();
                return null;
            }
        }

        private static TerrainShadowTopology BuildTerrainShadowTopology(
            Client.Data.BMD.BMDTextureMesh mesh)
        {
            var invalid = new TerrainShadowTopology { MeshReference = mesh, IsValid = false };
            if (mesh?.Vertices == null ||
                mesh.Triangles == null ||
                mesh.TexCoords == null ||
                mesh.Vertices.Length == 0 ||
                mesh.Triangles.Length == 0)
            {
                return invalid;
            }

            var usedSourceSlots = new Dictionary<TerrainShadowSourceKey, int>(mesh.Vertices.Length);
            var usedSourceVertexIndices = new List<int>(mesh.Vertices.Length);
            var shadowVertexSlots = new Dictionary<TerrainShadowVertexKey, int>(mesh.Vertices.Length * 2);
            var vertexSourceSlots = new List<int>(mesh.Vertices.Length * 2);
            var vertexTexCoords = new List<Vector2>(mesh.Vertices.Length * 2);
            int estimatedIndexCount = mesh.Triangles.Length > TerrainShadowMaxTopologyIndices / 3
                ? TerrainShadowMaxTopologyIndices
                : mesh.Triangles.Length * 3;
            var indices = new List<int>(estimatedIndexCount);

            for (int triangleIndex = 0; triangleIndex < mesh.Triangles.Length; triangleIndex++)
            {
                var triangle = mesh.Triangles[triangleIndex];
                int polygonVertexCount = triangle.Polygon;
                if (polygonVertexCount < 3 ||
                    triangle.VertexIndex == null ||
                    triangle.TexCoordIndex == null ||
                    triangle.VertexIndex.Length < polygonVertexCount ||
                    triangle.TexCoordIndex.Length < polygonVertexCount)
                {
                    continue;
                }

                for (int corner = 1; corner < polygonVertexCount - 1; corner++)
                {
                    int vertex0 = triangle.VertexIndex[0];
                    int vertex1 = triangle.VertexIndex[corner];
                    int vertex2 = triangle.VertexIndex[corner + 1];
                    int texCoord0 = triangle.TexCoordIndex[0];
                    int texCoord1 = triangle.TexCoordIndex[corner];
                    int texCoord2 = triangle.TexCoordIndex[corner + 1];

                    if ((uint)vertex0 >= (uint)mesh.Vertices.Length ||
                        (uint)vertex1 >= (uint)mesh.Vertices.Length ||
                        (uint)vertex2 >= (uint)mesh.Vertices.Length ||
                        (uint)texCoord0 >= (uint)mesh.TexCoords.Length ||
                        (uint)texCoord1 >= (uint)mesh.TexCoords.Length ||
                        (uint)texCoord2 >= (uint)mesh.TexCoords.Length ||
                        indices.Count + 3 > TerrainShadowMaxTopologyIndices)
                    {
                        continue;
                    }

                    int shadowVertex0 = GetOrAddTerrainShadowTopologyVertex(
                        mesh, vertex0, texCoord0,
                        usedSourceSlots, usedSourceVertexIndices,
                        shadowVertexSlots, vertexSourceSlots, vertexTexCoords);
                    int shadowVertex1 = GetOrAddTerrainShadowTopologyVertex(
                        mesh, vertex1, texCoord1,
                        usedSourceSlots, usedSourceVertexIndices,
                        shadowVertexSlots, vertexSourceSlots, vertexTexCoords);
                    int shadowVertex2 = GetOrAddTerrainShadowTopologyVertex(
                        mesh, vertex2, texCoord2,
                        usedSourceSlots, usedSourceVertexIndices,
                        shadowVertexSlots, vertexSourceSlots, vertexTexCoords);

                    if (shadowVertex0 < 0 || shadowVertex1 < 0 || shadowVertex2 < 0)
                        continue;

                    indices.Add(shadowVertex0);
                    indices.Add(shadowVertex1);
                    indices.Add(shadowVertex2);
                }
            }

            int vertexCount = vertexSourceSlots.Count;
            int indexCount = indices.Count;
            if (vertexCount < 3 || indexCount < 3)
                return invalid;

            bool uses32BitIndices = vertexCount > ushort.MaxValue;
            ushort[] indices16 = null;
            int[] indices32 = null;
            if (uses32BitIndices)
            {
                indices32 = indices.ToArray();
            }
            else
            {
                indices16 = new ushort[indexCount];
                for (int i = 0; i < indexCount; i++)
                    indices16[i] = (ushort)indices[i];
            }

            return new TerrainShadowTopology
            {
                MeshReference = mesh,
                UsedSourceVertexIndices = usedSourceVertexIndices.ToArray(),
                VertexSourceSlots = vertexSourceSlots.ToArray(),
                VertexTexCoords = vertexTexCoords.ToArray(),
                Indices16 = indices16,
                Indices32 = indices32,
                Uses32BitIndices = uses32BitIndices,
                VertexCount = vertexCount,
                IndexCount = indexCount,
                PrimitiveCount = indexCount / 3,
                IsValid = true
            };
        }

        private static int GetOrAddTerrainShadowTopologyVertex(
            Client.Data.BMD.BMDTextureMesh mesh,
            int sourceVertexIndex,
            int texCoordIndex,
            Dictionary<TerrainShadowSourceKey, int> usedSourceSlots,
            List<int> usedSourceVertexIndices,
            Dictionary<TerrainShadowVertexKey, int> shadowVertexSlots,
            List<int> vertexSourceSlots,
            List<Vector2> vertexTexCoords)
        {
            var sourceVertex = mesh.Vertices[sourceVertexIndex];
            var sourceKey = new TerrainShadowSourceKey(
                sourceVertex.Node,
                sourceVertex.Position.X,
                sourceVertex.Position.Y,
                sourceVertex.Position.Z);
            if (!usedSourceSlots.TryGetValue(sourceKey, out int sourceSlot))
            {
                sourceSlot = usedSourceVertexIndices.Count;
                usedSourceSlots.Add(sourceKey, sourceSlot);
                usedSourceVertexIndices.Add(sourceVertexIndex);
            }

            var texCoord = mesh.TexCoords[texCoordIndex];
            var vertexKey = new TerrainShadowVertexKey(sourceSlot, texCoord.U, texCoord.V);
            if (shadowVertexSlots.TryGetValue(vertexKey, out int shadowVertexSlot))
                return shadowVertexSlot;

            if (vertexSourceSlots.Count >= TerrainShadowMaxTopologyVertices)
                return -1;

            shadowVertexSlot = vertexSourceSlots.Count;
            shadowVertexSlots.Add(vertexKey, shadowVertexSlot);
            vertexSourceSlots.Add(sourceSlot);
            vertexTexCoords.Add(new Vector2(texCoord.U, texCoord.V));
            return shadowVertexSlot;
        }

        private bool TryUpdateTerrainShadowMeshCache(
            TerrainShadowMeshCache cache,
            Matrix shadowWorld,
            TerrainControl terrain)
        {
            Matrix[] bones = GetEffectiveBoneTransforms();
            if (bones == null || bones.Length == 0)
                return cache.IsValid;

            uint poseVersion = GetTerrainShadowEffectivePoseVersion();
            bool poseChanged = !ReferenceEquals(cache.LastBoneSource, bones) ||
                               cache.LastPoseVersion != poseVersion;
            bool transformChanged = !cache.HasLastShadowWorld ||
                                    HasTerrainShadowMatrixChanged(cache.LastShadowWorld, shadowWorld);
            IVertexDeformer vertexDeformer = GetVertexDeformer();
            var snowSurface = terrain.SnowShadowSurface;
            long snowVersion = snowSurface?.SurfaceVersion ?? 0;

            if (cache.IsValid && !poseChanged && !transformChanged && vertexDeformer == null &&
                ReferenceEquals(cache.LastSnowSurface, snowSurface) && cache.LastSnowSurfaceVersion == snowVersion)
                return true;

            TerrainShadowTopology topology = cache.Topology;
            var mesh = topology?.MeshReference;
            if (mesh?.Vertices == null || topology == null || !topology.IsValid)
                return cache.IsValid;

            Vector3 min = new(float.MaxValue, float.MaxValue, 0f);
            Vector3 max = new(float.MinValue, float.MinValue, 0f);

            for (int i = 0; i < topology.UsedSourceVertexIndices.Length; i++)
            {
                int sourceVertexIndex = topology.UsedSourceVertexIndices[i];
                if ((uint)sourceVertexIndex >= (uint)mesh.Vertices.Length)
                    return cache.IsValid;

                var sourceVertex = mesh.Vertices[sourceVertexIndex];
                Vector3 skinnedPosition = (uint)sourceVertex.Node < (uint)bones.Length
                    ? Vector3.Transform(sourceVertex.Position, bones[sourceVertex.Node])
                    : sourceVertex.Position;

                if (vertexDeformer != null)
                    skinnedPosition = vertexDeformer.DeformVertex(in sourceVertex, in skinnedPosition);

                Vector3 projectedPosition = Vector3.Transform(skinnedPosition, shadowWorld);
                if (!float.IsFinite(projectedPosition.X) || !float.IsFinite(projectedPosition.Y))
                    return cache.IsValid;

                projectedPosition.Z = 0f;
                cache.ProjectedPositions[i] = projectedPosition;

                if (projectedPosition.X < min.X) min.X = projectedPosition.X;
                if (projectedPosition.Y < min.Y) min.Y = projectedPosition.Y;
                if (projectedPosition.X > max.X) max.X = projectedPosition.X;
                if (projectedPosition.Y > max.Y) max.Y = projectedPosition.Y;
            }

            bool hasExactTerrainPatch = TryBuildExactTerrainShadowPatch(
                terrain,
                min.X,
                min.Y,
                max.X,
                max.Y,
                cache,
                out int minTileX,
                out int minTileY,
                out int patchNodesX,
                out int patchNodesY);

            for (int i = 0; i < cache.ProjectedPositions.Length; i++)
            {
                Vector3 position = cache.ProjectedPositions[i];
                float terrainHeight = hasExactTerrainPatch
                    ? EvaluateTerrainShadowPatchHeight(
                        position.X,
                        position.Y,
                        minTileX,
                        minTileY,
                        patchNodesX,
                        patchNodesY,
                        cache.TerrainPatchHeights)
                    : terrain.RequestTerrainHeight(position.X, position.Y);

                if (!float.IsFinite(terrainHeight))
                    return cache.IsValid;

                if (terrain.HasSnowShadowSurface)
                    terrainHeight = terrain.RequestShadowSurfaceHeight(position.X, position.Y);
                position.Z = terrainHeight + TerrainShadowSurfaceBias;
                cache.ProjectedPositions[i] = position;
            }

            for (int i = 0; i < topology.VertexCount; i++)
            {
                int sourceSlot = topology.VertexSourceSlots[i];
                cache.UploadVertices[i].Position = cache.ProjectedPositions[sourceSlot];
            }

            try
            {
                cache.VertexBuffer.SetData(
                    cache.UploadVertices,
                    0,
                    topology.VertexCount,
                    SetDataOptions.Discard);
            }
            catch
            {
                cache.IsValid = false;
                return false;
            }

            cache.LastBoneSource = bones;
            cache.LastPoseVersion = poseVersion;
            cache.LastShadowWorld = shadowWorld;
            cache.HasLastShadowWorld = true;
            cache.LastSnowSurface = snowSurface;
            cache.LastSnowSurfaceVersion = snowVersion;
            cache.IsValid = true;
            return true;
        }

        private static bool TryBuildExactTerrainShadowPatch(
            TerrainControl terrain,
            float minX,
            float minY,
            float maxX,
            float maxY,
            TerrainShadowMeshCache cache,
            out int minTileX,
            out int minTileY,
            out int patchNodesX,
            out int patchNodesY)
        {
            minTileX = (int)MathF.Floor(minX / Constants.TERRAIN_SCALE);
            minTileY = (int)MathF.Floor(minY / Constants.TERRAIN_SCALE);
            int maxTileX = (int)MathF.Floor(maxX / Constants.TERRAIN_SCALE);
            int maxTileY = (int)MathF.Floor(maxY / Constants.TERRAIN_SCALE);

            patchNodesX = maxTileX - minTileX + 2;
            patchNodesY = maxTileY - minTileY + 2;

            if (minTileX < 0 ||
                minTileY < 0 ||
                patchNodesX < 2 ||
                patchNodesY < 2 ||
                patchNodesX > TerrainShadowMaxPatchNodesPerAxis ||
                patchNodesY > TerrainShadowMaxPatchNodesPerAxis)
            {
                cache.TerrainPatchValid = false;
                return false;
            }

            int required = patchNodesX * patchNodesY;
            bool patchUnchanged = cache.TerrainPatchValid &&
                                  cache.TerrainPatchMinTileX == minTileX &&
                                  cache.TerrainPatchMinTileY == minTileY &&
                                  cache.TerrainPatchNodesX == patchNodesX &&
                                  cache.TerrainPatchNodesY == patchNodesY &&
                                  cache.TerrainPatchHeights != null &&
                                  cache.TerrainPatchHeights.Length >= required;
            if (patchUnchanged)
                return true;

            if (cache.TerrainPatchHeights == null || cache.TerrainPatchHeights.Length < required)
                cache.TerrainPatchHeights = new float[required];

            int writeIndex = 0;
            for (int y = 0; y < patchNodesY; y++)
            {
                float worldY = (minTileY + y) * Constants.TERRAIN_SCALE;
                for (int x = 0; x < patchNodesX; x++)
                {
                    float worldX = (minTileX + x) * Constants.TERRAIN_SCALE;
                    float height = terrain.RequestTerrainHeight(worldX, worldY);
                    if (!float.IsFinite(height))
                    {
                        cache.TerrainPatchValid = false;
                        return false;
                    }

                    cache.TerrainPatchHeights[writeIndex++] = height;
                }
            }

            cache.TerrainPatchMinTileX = minTileX;
            cache.TerrainPatchMinTileY = minTileY;
            cache.TerrainPatchNodesX = patchNodesX;
            cache.TerrainPatchNodesY = patchNodesY;
            cache.TerrainPatchValid = true;
            return true;
        }

        private static float EvaluateTerrainShadowPatchHeight(
            float worldX,
            float worldY,
            int minTileX,
            int minTileY,
            int patchNodesX,
            int patchNodesY,
            float[] heights)
        {
            const float inverseTerrainScale = 1f / Constants.TERRAIN_SCALE;
            float tileX = worldX * inverseTerrainScale;
            float tileY = worldY * inverseTerrainScale;
            int terrainTileX = (int)MathF.Floor(tileX);
            int terrainTileY = (int)MathF.Floor(tileY);
            float tx = tileX - terrainTileX;
            float ty = tileY - terrainTileY;

            // The patch bounds are derived from these same projected positions, so the
            // local tile coordinates are already guaranteed to be inside the patch.
            int localX = terrainTileX - minTileX;
            int localY = terrainTileY - minTileY;
            int row0 = localY * patchNodesX;
            int row1 = row0 + patchNodesX;

            float h00 = heights[row0 + localX];
            float h10 = heights[row0 + localX + 1];
            float h11 = heights[row1 + localX + 1];
            float h01 = heights[row1 + localX];

            // Match TerrainPhysics/Renderer exactly: h00 -> h11 is the tile diagonal.
            if (tx >= ty)
            {
                return (1f - tx) * h00
                     + (tx - ty) * h10
                     + ty * h11;
            }

            return (1f - ty) * h00
                 + tx * h11
                 + (ty - tx) * h01;
        }

        private uint GetTerrainShadowEffectivePoseVersion()
        {
            if (LinkParentAnimation && Parent is ModelObject parentModel)
                return parentModel.GetTerrainShadowEffectivePoseVersion();

            return GetEffectiveBonePoseVersion();
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

        private void ReleaseTerrainShadowMeshCaches()
        {
            if (_terrainShadowMeshCaches == null)
                return;

            for (int i = 0; i < _terrainShadowMeshCaches.Length; i++)
                _terrainShadowMeshCaches[i]?.Dispose();

            _terrainShadowMeshCaches = null;
        }

        private void ReleaseTerrainShadowResources()
        {
            ReleaseTerrainShadowMeshCaches();
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
