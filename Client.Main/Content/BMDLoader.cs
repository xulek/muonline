using Client.Data.BMD;
using Client.Main.Data;
using Client.Main.Graphics;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text.Json;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using static Client.Main.Core.Utilities.Utils;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Client.Main.Content
{
    public class BMDLoader
    {
        public static BMDLoader Instance { get; } = new BMDLoader();

        private readonly BMDReader _reader = new();
        private readonly Dictionary<string, Task<BMD>> _bmds = [];
        private readonly Dictionary<BMD, Dictionary<string, string>> _texturePathMap = [];
        private Dictionary<string, Dictionary<int, string>> _blendingConfig;

        private readonly struct MeshCacheKey : IEquatable<MeshCacheKey>
        {
            public MeshCacheKey(int assetId, int meshIndex)
            {
                AssetId = assetId;
                MeshIndex = meshIndex;
            }

            public int AssetId { get; }
            public int MeshIndex { get; }

            public bool Equals(MeshCacheKey other) => AssetId == other.AssetId && MeshIndex == other.MeshIndex;

            public override bool Equals(object obj) => obj is MeshCacheKey other && Equals(other);

            public override int GetHashCode() => HashCode.Combine(AssetId, MeshIndex);
        }

        private readonly struct GpuSkinBatchCacheKey : IEquatable<GpuSkinBatchCacheKey>
        {
            public GpuSkinBatchCacheKey(int assetId, ulong meshSequenceHash, int meshCount)
            {
                AssetId = assetId;
                MeshSequenceHash = meshSequenceHash;
                MeshCount = meshCount;
            }

            public int AssetId { get; }
            public ulong MeshSequenceHash { get; }
            public int MeshCount { get; }

            public bool Equals(GpuSkinBatchCacheKey other) =>
                AssetId == other.AssetId &&
                MeshSequenceHash == other.MeshSequenceHash &&
                MeshCount == other.MeshCount;

            public override bool Equals(object obj) => obj is GpuSkinBatchCacheKey other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(AssetId, MeshSequenceHash, MeshCount);
        }

        private sealed class GpuSkinBatchCacheEntry
        {
            public required int[] MeshIndices { get; init; }
            public required VertexBuffer VertexBuffer { get; init; }
            public required IndexBuffer IndexBuffer { get; init; }
            public int BoneCount { get; init; }
            public int LastUsedFrame;
        }

        private readonly struct MeshCornerKey : IEquatable<MeshCornerKey>
        {
            public MeshCornerKey(int vertexIndex, int normalIndex, int texCoordIndex)
            {
                VertexIndex = vertexIndex;
                NormalIndex = normalIndex;
                TexCoordIndex = texCoordIndex;
            }

            public int VertexIndex { get; }
            public int NormalIndex { get; }
            public int TexCoordIndex { get; }

            public bool Equals(MeshCornerKey other) =>
                VertexIndex == other.VertexIndex &&
                NormalIndex == other.NormalIndex &&
                TexCoordIndex == other.TexCoordIndex;

            public override bool Equals(object obj) => obj is MeshCornerKey other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(VertexIndex, NormalIndex, TexCoordIndex);
        }

        private readonly struct MeshCorner
        {
            public MeshCorner(int vertexIndex, int normalIndex, int texCoordIndex)
            {
                VertexIndex = vertexIndex;
                NormalIndex = normalIndex;
                TexCoordIndex = texCoordIndex;
            }

            public int VertexIndex { get; }
            public int NormalIndex { get; }
            public int TexCoordIndex { get; }
        }

        private sealed class MeshTopology
        {
            public required MeshCorner[] Vertices { get; init; }
            public required int[] Indices { get; init; }
        }

        private const bool DisablePerBufferMeshCache = false;
        // Cache is tied to the concrete vertex-buffer instance. A cache keyed only by
        // asset+mesh is incorrect because every ModelObject owns a different buffer.
        private ConditionalWeakTable<DynamicVertexBuffer, BufferCacheEntry> _bufferCacheState = new();
        private ConditionalWeakTable<DynamicIndexBuffer, IndexBufferCacheEntry> _indexBufferCacheState = new();
        // Immutable indexed topology per mesh. It removes duplicated triangle-corner vertices
        // from CPU skinning, GPU skinning, VRAM storage and vertex-shader work.
        private readonly Dictionary<MeshCacheKey, MeshTopology> _meshTopologyCache = [];
        private readonly Dictionary<MeshCacheKey, int> _meshTopologyLastUsedFrames = [];
        private readonly List<MeshCacheKey> _staleMeshTopologyKeys = new(64);
        private readonly object _meshTopologyLock = new();
        // Static buffers for GPU skinning path (no per-frame vertex uploads)
        private readonly Dictionary<MeshCacheKey, VertexBuffer> _gpuSkinVertexBuffers = [];
        private readonly Dictionary<MeshCacheKey, IndexBuffer> _gpuSkinIndexBuffers = [];
        private readonly Dictionary<MeshCacheKey, int> _gpuSkinBoneCounts = [];
        private readonly Dictionary<MeshCacheKey, int> _gpuSkinLastUsedFrames = [];
        private readonly Dictionary<GpuSkinBatchCacheKey, GpuSkinBatchCacheEntry> _gpuSkinBatchBuffers = [];
        private readonly List<MeshCacheKey> _staleGpuMeshKeys = new(64);
        private readonly List<GpuSkinBatchCacheKey> _staleGpuBatchKeys = new(32);
        private readonly object _gpuSkinBufferLock = new();
        private readonly Dictionary<int, int> _frameCacheMissCountsByAsset = new(128);
        private string _frameTopCacheMissModelName = string.Empty;
        private int _frameTopCacheMissModelCount;
        private const int GpuCachePruneIntervalFrames = 300;
        private const int GpuCacheRetentionFrames = 3600;
        private int _lastGpuCachePruneFrame;
        // Parallel.For has a measurable setup/synchronization cost. Keep small and
        // medium meshes on the calling thread and parallelize only genuinely large jobs.
        private const int ParallelCpuSkinningVertexThreshold = 24000;
        private const int ParallelTriangleAssemblyThreshold = 12000;
        private static readonly bool EnableParallelCpuSkinning = Environment.ProcessorCount > 1;
        private static readonly ParallelOptions CpuSkinningParallelOptions = new()
        {
            MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1)
        };

        // Per-frame instrumentation (queried by DebugPanel)
        public int FrameVBUpdates { get; private set; }
        public int FrameIBUploads { get; private set; }
        public int FrameVerticesTransformed { get; private set; }
        public int FrameMeshesProcessed { get; private set; }
        public int FrameCacheHits { get; private set; }
        public int FrameCacheMisses { get; private set; }
        public int FrameCacheMissIndexUpload { get; private set; }
        public int FrameCacheMissMissingEntry { get; private set; }
        public int FrameCacheMissInvalidEntry { get; private set; }
        public int FrameCacheMissOwner { get; private set; }
        public int FrameCacheMissAsset { get; private set; }
        public int FrameCacheMissMesh { get; private set; }
        public int FrameCacheMissPose { get; private set; }
        public int FrameCacheMissVertexCount { get; private set; }
        public int FrameCacheMissColor { get; private set; }
        public int FrameCacheBypasses { get; private set; }
        public int FrameTopCacheMissModelCount => _frameTopCacheMissModelCount;
        public string FrameTopCacheMissModelName => _frameTopCacheMissModelName;
        public int FrameMeshBatchBuilds { get; private set; }
        public int FrameMeshBatchMeshes { get; private set; }

        // Snapshot of previous frame (stable for UI)
        public int LastFrameVBUpdates { get; private set; }
        public int LastFrameIBUploads { get; private set; }
        public int LastFrameVerticesTransformed { get; private set; }
        public int LastFrameMeshesProcessed { get; private set; }
        public int LastFrameCacheHits { get; private set; }
        public int LastFrameCacheMisses { get; private set; }
        public int LastFrameCacheMissIndexUpload { get; private set; }
        public int LastFrameCacheMissMissingEntry { get; private set; }
        public int LastFrameCacheMissInvalidEntry { get; private set; }
        public int LastFrameCacheMissOwner { get; private set; }
        public int LastFrameCacheMissAsset { get; private set; }
        public int LastFrameCacheMissMesh { get; private set; }
        public int LastFrameCacheMissPose { get; private set; }
        public int LastFrameCacheMissVertexCount { get; private set; }
        public int LastFrameCacheMissColor { get; private set; }
        public int LastFrameCacheBypasses { get; private set; }
        public string LastFrameTopCacheMissModelName { get; private set; } = string.Empty;
        public int LastFrameTopCacheMissModelCount { get; private set; }
        public int LastFrameMeshBatchBuilds { get; private set; }
        public int LastFrameMeshBatchMeshes { get; private set; }
        public int LastFrameGpuMeshBuffersPruned { get; private set; }
        public int LastFrameGpuBatchBuffersPruned { get; private set; }
        public int LastFrameMeshTopologiesPruned { get; private set; }
        public int GpuMeshBufferCacheCount
        {
            get { lock (_gpuSkinBufferLock) return _gpuSkinVertexBuffers.Count; }
        }
        public int GpuBatchBufferCacheCount
        {
            get { lock (_gpuSkinBufferLock) return _gpuSkinBatchBuffers.Count; }
        }
        public int MeshTopologyCacheCount
        {
            get { lock (_meshTopologyLock) return _meshTopologyCache.Count; }
        }

        private sealed class BufferCacheEntry
        {
            public BufferCacheEntry()
            {
            }

            public int OwnerId;
            public int AssetId;
            public int MeshIndex;
            public uint PoseVersion;
            public int VertexCount;
            public Color LastColor;
            public bool IsValid;
        }

        private sealed class IndexBufferCacheEntry
        {
            public int AssetId;
            public int TopologyHash;
            public int IndexCount;
            public bool Is16Bit;
            public bool IsValid;
        }

        private GraphicsDevice _graphicsDevice;
        private ILogger _logger = MuGame.AppLoggerFactory?.CreateLogger<BMDLoader>();

        // for custom blending from json

        private BMDLoader()
        {
            LoadBlendingConfig();
        }

        private void LoadBlendingConfig()
        {
            _blendingConfig = new(StringComparer.OrdinalIgnoreCase);

            try
            {
                var asm = Assembly.GetExecutingAssembly();

                // Looking for exactly one resource ending with the file name
                var resName = asm.GetManifestResourceNames()
                                 .SingleOrDefault(n =>
                                     n.EndsWith("bmd_blending_config.json",
                                                StringComparison.OrdinalIgnoreCase));

                if (resName == null)
                {
                    _logger?.LogWarning(
                        "Embedded resource 'bmd_blending_config.json' not found " +
                        "(check Build Action = Embedded Resource and RootNamespace).");
                    return;
                }

                using var stream = asm.GetManifestResourceStream(resName);
                if (stream == null)
                {
                    _logger?.LogWarning($"Failed to open stream '{resName}'.");
                    return;
                }

                using var reader = new StreamReader(stream);
                var json = reader.ReadToEnd();

                using var doc = JsonDocument.Parse(json);
                var cleanObj = new Dictionary<string, Dictionary<int, string>>(StringComparer.OrdinalIgnoreCase);

                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (prop.Name.StartsWith("comment", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var innerDict = new Dictionary<int, string>();
                    foreach (var mesh in prop.Value.EnumerateObject())
                        innerDict[int.Parse(mesh.Name)] = mesh.Value.GetString();

                    cleanObj[prop.Name] = innerDict;
                }

                _blendingConfig = cleanObj;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to load embedded BMD blending config.");
            }
        }

        //

        public void SetGraphicsDevice(GraphicsDevice graphicsDevice)
        {
            if (!ReferenceEquals(_graphicsDevice, graphicsDevice))
            {
                DisposeGpuSkinnedBuffers();
            }

            _graphicsDevice = graphicsDevice;
        }

        /// <summary>
        /// Call this at the start of each frame to enable DISCARD/NoOverwrite optimization
        /// </summary>
        public void BeginFrame()
        {
            // Snapshot previous frame for UI stability
            LastFrameVBUpdates = FrameVBUpdates;
            LastFrameIBUploads = FrameIBUploads;
            LastFrameVerticesTransformed = FrameVerticesTransformed;
            LastFrameMeshesProcessed = FrameMeshesProcessed;
            LastFrameCacheHits = FrameCacheHits;
            LastFrameCacheMisses = FrameCacheMisses;
            LastFrameCacheMissIndexUpload = FrameCacheMissIndexUpload;
            LastFrameCacheMissMissingEntry = FrameCacheMissMissingEntry;
            LastFrameCacheMissInvalidEntry = FrameCacheMissInvalidEntry;
            LastFrameCacheMissOwner = FrameCacheMissOwner;
            LastFrameCacheMissAsset = FrameCacheMissAsset;
            LastFrameCacheMissMesh = FrameCacheMissMesh;
            LastFrameCacheMissPose = FrameCacheMissPose;
            LastFrameCacheMissVertexCount = FrameCacheMissVertexCount;
            LastFrameCacheMissColor = FrameCacheMissColor;
            LastFrameCacheBypasses = FrameCacheBypasses;
            LastFrameTopCacheMissModelName = _frameTopCacheMissModelName;
            LastFrameTopCacheMissModelCount = _frameTopCacheMissModelCount;
            LastFrameMeshBatchBuilds = FrameMeshBatchBuilds;
            LastFrameMeshBatchMeshes = FrameMeshBatchMeshes;

            // Reset counters for the new frame
            FrameVBUpdates = 0;
            FrameIBUploads = 0;
            FrameVerticesTransformed = 0;
            FrameMeshesProcessed = 0;
            FrameCacheHits = 0;
            FrameCacheMisses = 0;
            FrameCacheMissIndexUpload = 0;
            FrameCacheMissMissingEntry = 0;
            FrameCacheMissInvalidEntry = 0;
            FrameCacheMissOwner = 0;
            FrameCacheMissAsset = 0;
            FrameCacheMissMesh = 0;
            FrameCacheMissPose = 0;
            FrameCacheMissVertexCount = 0;
            FrameCacheMissColor = 0;
            FrameCacheBypasses = 0;
            _frameCacheMissCountsByAsset.Clear();
            _frameTopCacheMissModelName = string.Empty;
            _frameTopCacheMissModelCount = 0;
            FrameMeshBatchBuilds = 0;
            FrameMeshBatchMeshes = 0;

            LastFrameGpuMeshBuffersPruned = 0;
            LastFrameGpuBatchBuffersPruned = 0;
            LastFrameMeshTopologiesPruned = 0;
            int frame = MuGame.FrameIndex;
            if (frame >= _lastGpuCachePruneFrame &&
                frame - _lastGpuCachePruneFrame >= GpuCachePruneIntervalFrames)
            {
                _lastGpuCachePruneFrame = frame;
                PruneUnusedGpuSkinBuffers(frame);
                PruneUnusedMeshTopologies(frame);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector3 FastTransformPosition(in Matrix m, in System.Numerics.Vector3 p)
        {
            // Row-major transform (matching XNA):
            // x' = p.x*m.M11 + p.y*m.M21 + p.z*m.M31 + m.M41, etc.
            return new Vector3(
                p.X * m.M11 + p.Y * m.M21 + p.Z * m.M31 + m.M41,
                p.X * m.M12 + p.Y * m.M22 + p.Z * m.M32 + m.M42,
                p.X * m.M13 + p.Y * m.M23 + p.Z * m.M33 + m.M43);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector3 FastTransformNormal(in Matrix m, in System.Numerics.Vector3 n)
        {
            return new Vector3(
                n.X * m.M11 + n.Y * m.M21 + n.Z * m.M31,
                n.X * m.M12 + n.Y * m.M22 + n.Z * m.M32,
                n.X * m.M13 + n.Y * m.M23 + n.Z * m.M33);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool TryResolveNormalBoneIndex(BMDTextureMesh mesh, int normalIndex, out int boneIndex)
        {
            boneIndex = 0;
            if (mesh == null || mesh.Normals == null || mesh.Vertices == null ||
                (uint)normalIndex >= (uint)mesh.Normals.Length)
            {
                return false;
            }

            var normal = mesh.Normals[normalIndex];
            if (normal.Node >= 0)
            {
                boneIndex = normal.Node;
                return true;
            }

            int bindVertexIndex = normal.BindVertex;
            if ((uint)bindVertexIndex < (uint)mesh.Vertices.Length)
            {
                short bindVertexBone = mesh.Vertices[bindVertexIndex].Node;
                if (bindVertexBone >= 0)
                {
                    boneIndex = bindVertexBone;
                    return true;
                }
            }

            return false;
        }

        public Task<BMD> Prepare(string path, string textureFolder = null)
        {
            lock (_bmds)
            {
                string normalizedPath = path.Replace('\\', '/');
                if (normalizedPath.Equals("Player/Player.bmd", StringComparison.OrdinalIgnoreCase))
                {
                    if (_bmds.TryGetValue(normalizedPath, out Task<BMD> embeddedModelTask))
                        return embeddedModelTask;

                    embeddedModelTask = LoadEmbeddedPlayerAsync(normalizedPath, textureFolder);
                    _bmds.Add(normalizedPath, embeddedModelTask);
                    return embeddedModelTask;
                }

                path = GetActualPath(Path.Combine(Constants.DataPath, path));
                if (_bmds.TryGetValue(path, out Task<BMD> modelTask))
                    return modelTask;

                modelTask = LoadAssetAsync(path, textureFolder);
                _bmds.Add(path, modelTask);
                return modelTask;
            }
        }

        public Task<bool> AssestExist(string path)
        {
            if (path.Replace('\\', '/').Equals("Player/Player.bmd", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(true);

            string finalPath = Path.Combine(Constants.DataPath, path);
            return Task.FromResult(File.Exists(finalPath));
        }
        private async Task<BMD> LoadAssetAsync(string path, string textureFolder = null)
        {
            try
            {
                // 'path' is already resolved to an absolute path in Prepare(); don't re-combine here.

                if (!File.Exists(path))
                {
                    _logger?.LogDebug($"Model not found: {path}");
                    return null;
                }

                var asset = await _reader.Load(path);

                // SourceMain5.2 OpenMonsterModel(): apply original per-model action
                // PlaySpeed defaults/overrides to every monster model at load time.
                var relativeModelPath = Path.GetRelativePath(Constants.DataPath, path).Replace("\\", "/");
                var monsterMatch = System.Text.RegularExpressions.Regex.Match(
                    relativeModelPath, @"^Monster/Monster(\d+)\.bmd$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (monsterMatch.Success && int.TryParse(monsterMatch.Groups[1].Value, out int monsterFileNumber))
                {
                    MonsterActionSpeedDatabase.Apply(asset, monsterFileNumber);
                }

                // for custom blending from json
                var relativePath = Path.GetRelativePath(Constants.DataPath, path).Replace("\\", "/");
                if (_blendingConfig.TryGetValue(relativePath, out var meshConfig))
                {
                    for (int i = 0; i < asset.Meshes.Length; i++)
                    {
                        if (meshConfig.TryGetValue(i, out var blendMode))
                        {
                            asset.Meshes[i].BlendingMode = blendMode;
                        }
                    }
                }
                //

                var texturePathMap = new Dictionary<string, string>();

                lock (_texturePathMap)
                    _texturePathMap.Add(asset, texturePathMap);

                var dir = !string.IsNullOrEmpty(textureFolder)
                    ? textureFolder
                    : Path.GetRelativePath(Constants.DataPath, Path.GetDirectoryName(path));

                var tasks = new List<Task>();
                foreach (var mesh in asset.Meshes)
                {
                    var fullPath = Path.Combine(dir, mesh.TexturePath);
                    if (
                        mesh.TexturePath == "unicon.jpg"
                        || mesh.TexturePath == "unicon01.tga"
                    )
                    {
                        fullPath = Path.Combine("Item", mesh.TexturePath);
                    }
                    if (texturePathMap.TryAdd(mesh.TexturePath.ToLowerInvariant(), fullPath))
                        tasks.Add(TextureLoader.Instance.Prepare(fullPath));
                }

                await Task.WhenAll(tasks);

                return asset;
            }
            catch (Exception e)
            {
                Console.WriteLine($"Failed to load asset {path}: {e.Message}");
                return null;
            }
        }

        private async Task<BMD> LoadEmbeddedPlayerAsync(string path, string textureFolder)
        {
            try
            {
                using var stream = EmbeddedS6Data.Open("player.bmd");
                var asset = await _reader.Load(stream);

                if (_blendingConfig.TryGetValue(path, out var meshConfig))
                {
                    for (int i = 0; i < asset.Meshes.Length; i++)
                    {
                        if (meshConfig.TryGetValue(i, out var blendMode))
                            asset.Meshes[i].BlendingMode = blendMode;
                    }
                }

                var texturePathMap = new Dictionary<string, string>();
                lock (_texturePathMap)
                    _texturePathMap.Add(asset, texturePathMap);

                var dir = !string.IsNullOrEmpty(textureFolder)
                    ? textureFolder
                    : Path.GetDirectoryName(path.Replace('/', Path.DirectorySeparatorChar));

                var tasks = new List<Task>();
                foreach (var mesh in asset.Meshes)
                {
                    var fullPath = Path.Combine(dir, mesh.TexturePath);
                    if (texturePathMap.TryAdd(mesh.TexturePath.ToLowerInvariant(), fullPath))
                        tasks.Add(TextureLoader.Instance.Prepare(fullPath));
                }

                await Task.WhenAll(tasks);
                return asset;
            }
            catch (Exception e)
            {
                _logger?.LogError(e, "Failed to load embedded player model");
                return null;
            }
        }


        private void RecordCacheMissAsset(int assetId, string assetName)
        {
            if (MuGame.Diagnostics?.Enabled != true)
                return;

            int count = _frameCacheMissCountsByAsset.TryGetValue(assetId, out int current)
                ? current + 1
                : 1;
            _frameCacheMissCountsByAsset[assetId] = count;

            if (count <= _frameTopCacheMissModelCount)
                return;

            _frameTopCacheMissModelCount = count;
            _frameTopCacheMissModelName = string.IsNullOrWhiteSpace(assetName)
                ? $"asset:{assetId}"
                : assetName;
        }

        private MeshTopology GetOrCreateMeshTopology(BMDTextureMesh mesh, MeshCacheKey cacheKey)
        {
            lock (_meshTopologyLock)
            {
                if (_meshTopologyCache.TryGetValue(cacheKey, out var cached))
                {
                    _meshTopologyLastUsedFrames[cacheKey] = MuGame.FrameIndex;
                    return cached;
                }

                int estimatedCornerCount = 0;
                var triangles = mesh.Triangles;
                for (int i = 0; i < triangles.Length; i++)
                    estimatedCornerCount += triangles[i].Polygon;

                var uniqueCorners = new List<MeshCorner>(estimatedCornerCount);
                var indices = new int[estimatedCornerCount];
                var lookup = new Dictionary<MeshCornerKey, int>(estimatedCornerCount);
                int indexOffset = 0;

                for (int triangleIndex = 0; triangleIndex < triangles.Length; triangleIndex++)
                {
                    var triangle = triangles[triangleIndex];
                    for (int cornerIndex = 0; cornerIndex < triangle.Polygon; cornerIndex++)
                    {
                        int vertexIndex = triangle.VertexIndex[cornerIndex];
                        int normalIndex = triangle.NormalIndex[cornerIndex];
                        int texCoordIndex = triangle.TexCoordIndex[cornerIndex];
                        var key = new MeshCornerKey(vertexIndex, normalIndex, texCoordIndex);

                        if (!lookup.TryGetValue(key, out int uniqueIndex))
                        {
                            uniqueIndex = uniqueCorners.Count;
                            lookup.Add(key, uniqueIndex);
                            uniqueCorners.Add(new MeshCorner(vertexIndex, normalIndex, texCoordIndex));
                        }

                        indices[indexOffset++] = uniqueIndex;
                    }
                }

                var topology = new MeshTopology
                {
                    Vertices = uniqueCorners.ToArray(),
                    Indices = indices
                };
                _meshTopologyCache.Add(cacheKey, topology);
                _meshTopologyLastUsedFrames[cacheKey] = MuGame.FrameIndex;
                return topology;
            }
        }

        /// <summary>
        /// Builds (or updates) the dynamic vertex/index buffers for the given mesh.
        /// Uses ArrayPool to eliminate per‑frame allocations and intelligent caching.
        /// </summary>
        public void GetModelBuffers(
            BMD asset,
            int meshIndex,
            Color color,
            Matrix[] boneMatrix,
            ref DynamicVertexBuffer vertexBuffer,
            ref DynamicIndexBuffer indexBuffer,
            bool skipCache = false,
            IVertexDeformer vertexDeformer = null,
            int bufferOwnerId = 0,
            uint poseVersion = 0)
        {
            if (asset == null || boneMatrix == null || _graphicsDevice == null)
            {
                vertexBuffer = null;
                indexBuffer = null;
                return;
            }

            if (meshIndex < 0 || asset.Meshes == null || meshIndex >= asset.Meshes.Length)
            {
                vertexBuffer = null;
                indexBuffer = null;
                return;
            }

            var mesh = asset.Meshes[meshIndex];
            if (mesh?.Triangles == null || mesh.Vertices == null || mesh.Normals == null || mesh.TexCoords == null)
            {
                vertexBuffer = null;
                indexBuffer = null;
                return;
            }

            int assetId = RuntimeHelpers.GetHashCode(asset);
            var cacheKey = new MeshCacheKey(assetId, meshIndex);
            MeshTopology topology = GetOrCreateMeshTopology(mesh, cacheKey);
            int totalVertices = topology.Vertices.Length;
            int totalIndices = topology.Indices.Length;
            if (totalVertices <= 0 || totalIndices <= 0)
                return;

            bool prefer16Bit = totalVertices <= ushort.MaxValue;
            bool useCache = !DisablePerBufferMeshCache && !skipCache && bufferOwnerId != 0;

            if (vertexBuffer != null && vertexBuffer.IsDisposed)
                vertexBuffer = null;

            if (vertexBuffer == null || vertexBuffer.VertexCount < totalVertices)
            {
                DynamicBufferPool.ReturnVertexBuffer(vertexBuffer);
                vertexBuffer = DynamicBufferPool.RentVertexBuffer(totalVertices)
                    ?? new DynamicVertexBuffer(
                        _graphicsDevice,
                        VertexPositionColorNormalTexture.VertexDeclaration,
                        totalVertices,
                        BufferUsage.WriteOnly);
            }

            if (indexBuffer != null && indexBuffer.IsDisposed)
                indexBuffer = null;

            IndexElementSize desiredIndexElementSize = prefer16Bit
                ? IndexElementSize.SixteenBits
                : IndexElementSize.ThirtyTwoBits;
            if (indexBuffer == null ||
                indexBuffer.IndexCount != totalIndices ||
                indexBuffer.IndexElementSize != desiredIndexElementSize)
            {
                DynamicBufferPool.ReturnIndexBuffer(indexBuffer);
                indexBuffer = DynamicBufferPool.RentIndexBuffer(totalIndices, prefer16Bit)
                    ?? new DynamicIndexBuffer(
                        _graphicsDevice,
                        desiredIndexElementSize,
                        totalIndices,
                        BufferUsage.WriteOnly);
            }

            bool uploadIndexData = !_indexBufferCacheState.TryGetValue(indexBuffer, out var indexCacheEntry) ||
                                   !indexCacheEntry.IsValid ||
                                   indexCacheEntry.AssetId != assetId ||
                                   indexCacheEntry.TopologyHash != meshIndex ||
                                   indexCacheEntry.IndexCount != totalIndices ||
                                   indexCacheEntry.Is16Bit != prefer16Bit;

            if (useCache &&
                !uploadIndexData &&
                vertexBuffer != null &&
                indexBuffer != null &&
                _bufferCacheState.TryGetValue(vertexBuffer, out var cacheEntry) &&
                cacheEntry.IsValid &&
                cacheEntry.OwnerId == bufferOwnerId &&
                cacheEntry.AssetId == assetId &&
                cacheEntry.MeshIndex == meshIndex &&
                cacheEntry.PoseVersion == poseVersion &&
                cacheEntry.VertexCount == totalVertices &&
                cacheEntry.LastColor.PackedValue == color.PackedValue)
            {
                FrameCacheHits++;
                return;
            }

            if (!useCache)
            {
                FrameCacheBypasses++;
            }
            else
            {
                FrameCacheMisses++;
                RecordCacheMissAsset(assetId, asset.Name);
                if (uploadIndexData)
                {
                    FrameCacheMissIndexUpload++;
                }
                else if (!_bufferCacheState.TryGetValue(vertexBuffer, out var missEntry))
                {
                    FrameCacheMissMissingEntry++;
                }
                else if (!missEntry.IsValid)
                {
                    FrameCacheMissInvalidEntry++;
                }
                else if (missEntry.OwnerId != bufferOwnerId)
                {
                    FrameCacheMissOwner++;
                }
                else if (missEntry.AssetId != assetId)
                {
                    FrameCacheMissAsset++;
                }
                else if (missEntry.MeshIndex != meshIndex)
                {
                    FrameCacheMissMesh++;
                }
                else if (missEntry.PoseVersion != poseVersion)
                {
                    FrameCacheMissPose++;
                }
                else if (missEntry.VertexCount != totalVertices)
                {
                    FrameCacheMissVertexCount++;
                }
                else if (missEntry.LastColor.PackedValue != color.PackedValue)
                {
                    FrameCacheMissColor++;
                }
                else
                {
                    FrameCacheMissInvalidEntry++;
                }
            }

            FrameMeshesProcessed++;

            VertexPositionColorNormalTexture[] vertices = null;
            Vector3[] positionCache = null;
            Vector3[] normalCache = null;
            bool[] positionVisited = null;
            bool[] normalVisited = null;
            ITexCoordDeformer texCoordDeformer = vertexDeformer as ITexCoordDeformer;

            try
            {
                vertices = ArrayPool<VertexPositionColorNormalTexture>.Shared.Rent(totalVertices);
                positionCache = ArrayPool<Vector3>.Shared.Rent(mesh.Vertices.Length);
                normalCache = ArrayPool<Vector3>.Shared.Rent(mesh.Normals.Length);

                bool useParallelTransform = EnableParallelCpuSkinning &&
                                            vertexDeformer == null &&
                                            mesh.Vertices.Length >= ParallelCpuSkinningVertexThreshold;
                bool useParallelAssembly = useParallelTransform &&
                                           totalVertices >= ParallelTriangleAssemblyThreshold;
                int uniqueTransformed = 0;

                if (useParallelTransform)
                {
                    var sourceVertices = mesh.Vertices;
                    var sourceNormals = mesh.Normals;
                    int boneCount = boneMatrix.Length;

                    Parallel.For(0, sourceVertices.Length, CpuSkinningParallelOptions, vertexIndex =>
                    {
                        var source = sourceVertices[vertexIndex];
                        positionCache[vertexIndex] = source.Node >= 0 && source.Node < boneCount
                            ? FastTransformPosition(in boneMatrix[source.Node], in source.Position)
                            : source.Position;
                    });

                    Parallel.For(0, sourceNormals.Length, CpuSkinningParallelOptions, normalIndex =>
                    {
                        var source = sourceNormals[normalIndex];
                        normalCache[normalIndex] = TryResolveNormalBoneIndex(mesh, normalIndex, out int boneIndex) &&
                                                   (uint)boneIndex < (uint)boneCount
                            ? FastTransformNormal(in boneMatrix[boneIndex], in source.Normal)
                            : source.Normal;
                    });

                    uniqueTransformed = sourceVertices.Length;
                }
                else
                {
                    positionVisited = ArrayPool<bool>.Shared.Rent(mesh.Vertices.Length);
                    normalVisited = ArrayPool<bool>.Shared.Rent(mesh.Normals.Length);
                    Array.Clear(positionVisited, 0, mesh.Vertices.Length);
                    Array.Clear(normalVisited, 0, mesh.Normals.Length);
                }

                if (useParallelAssembly)
                {
                    Parallel.For(0, totalVertices, CpuSkinningParallelOptions, outputIndex =>
                    {
                        MeshCorner corner = topology.Vertices[outputIndex];
                        var uv = mesh.TexCoords[corner.TexCoordIndex];
                        vertices[outputIndex] = new VertexPositionColorNormalTexture(
                            positionCache[corner.VertexIndex],
                            color,
                            normalCache[corner.NormalIndex],
                            new Vector2(uv.U, uv.V));
                    });
                }
                else
                {
                    for (int outputIndex = 0; outputIndex < totalVertices; outputIndex++)
                    {
                        MeshCorner corner = topology.Vertices[outputIndex];
                        int vertexIndex = corner.VertexIndex;
                        if (!useParallelTransform && !positionVisited[vertexIndex])
                        {
                            positionVisited[vertexIndex] = true;
                            uniqueTransformed++;
                            var source = mesh.Vertices[vertexIndex];
                            positionCache[vertexIndex] = source.Node >= 0 && source.Node < boneMatrix.Length
                                ? FastTransformPosition(in boneMatrix[source.Node], in source.Position)
                                : source.Position;

                            if (vertexDeformer != null)
                                positionCache[vertexIndex] = vertexDeformer.DeformVertex(in source, in positionCache[vertexIndex]);
                        }

                        int normalIndex = corner.NormalIndex;
                        if (!useParallelTransform && !normalVisited[normalIndex])
                        {
                            normalVisited[normalIndex] = true;
                            var source = mesh.Normals[normalIndex];
                            normalCache[normalIndex] = TryResolveNormalBoneIndex(mesh, normalIndex, out int boneIndex) &&
                                                       (uint)boneIndex < (uint)boneMatrix.Length
                                ? FastTransformNormal(in boneMatrix[boneIndex], in source.Normal)
                                : source.Normal;
                        }

                        var uv = mesh.TexCoords[corner.TexCoordIndex];
                        Vector2 texCoord = texCoordDeformer != null
                            ? texCoordDeformer.DeformTexCoord(uv.U, uv.V)
                            : new Vector2(uv.U, uv.V);

                        vertices[outputIndex] = new VertexPositionColorNormalTexture(
                            positionCache[vertexIndex],
                            color,
                            normalCache[normalIndex],
                            texCoord);
                    }
                }

                vertexBuffer.SetData(vertices, 0, totalVertices, SetDataOptions.Discard);
                FrameVBUpdates++;
                FrameVerticesTransformed += uniqueTransformed;

                if (uploadIndexData)
                {
                    if (prefer16Bit)
                    {
                        var indices = ArrayPool<ushort>.Shared.Rent(totalIndices);
                        try
                        {
                            for (int i = 0; i < totalIndices; i++)
                                indices[i] = (ushort)topology.Indices[i];
                            indexBuffer.SetData(indices, 0, totalIndices, SetDataOptions.Discard);
                        }
                        finally
                        {
                            ArrayPool<ushort>.Shared.Return(indices, clearArray: false);
                        }
                    }
                    else
                    {
                        indexBuffer.SetData(topology.Indices, 0, totalIndices, SetDataOptions.Discard);
                    }

                    FrameIBUploads++;
                    var updatedIndexEntry = _indexBufferCacheState.GetOrCreateValue(indexBuffer);
                    updatedIndexEntry.AssetId = assetId;
                    updatedIndexEntry.TopologyHash = meshIndex;
                    updatedIndexEntry.IndexCount = totalIndices;
                    updatedIndexEntry.Is16Bit = prefer16Bit;
                    updatedIndexEntry.IsValid = true;
                }

                if (useCache)
                {
                    var entry = _bufferCacheState.GetOrCreateValue(vertexBuffer);
                    entry.OwnerId = bufferOwnerId;
                    entry.AssetId = assetId;
                    entry.MeshIndex = meshIndex;
                    entry.PoseVersion = poseVersion;
                    entry.VertexCount = totalVertices;
                    entry.LastColor = color;
                    entry.IsValid = true;
                }
                else if (vertexBuffer != null)
                {
                    _bufferCacheState.Remove(vertexBuffer);
                }
            }
            finally
            {
                if (vertices != null)
                    ArrayPool<VertexPositionColorNormalTexture>.Shared.Return(vertices, clearArray: false);
                if (positionCache != null)
                    ArrayPool<Vector3>.Shared.Return(positionCache, clearArray: false);
                if (normalCache != null)
                    ArrayPool<Vector3>.Shared.Return(normalCache, clearArray: false);
                if (positionVisited != null)
                    ArrayPool<bool>.Shared.Return(positionVisited, clearArray: false);
                if (normalVisited != null)
                    ArrayPool<bool>.Shared.Return(normalVisited, clearArray: false);
            }
        }

        public bool GetModelBatchBuffers(
            BMD asset,
            IReadOnlyList<int> meshIndices,
            Color color,
            Matrix[] boneMatrix,
            ref DynamicVertexBuffer vertexBuffer,
            ref DynamicIndexBuffer indexBuffer,
            ref bool indexBufferIs16Bit)
        {
            if (asset?.Meshes == null || meshIndices == null || meshIndices.Count <= 1 ||
                boneMatrix == null || _graphicsDevice == null)
            {
                return false;
            }

            var topologies = ArrayPool<MeshTopology>.Shared.Rent(meshIndices.Count);
            int topologyHash = 17;
            int totalVertices = 0;
            int totalIndices = 0;
            int maxSourceVertices = 0;
            int maxSourceNormals = 0;

            try
            {
                int assetId = RuntimeHelpers.GetHashCode(asset);
                for (int i = 0; i < meshIndices.Count; i++)
                {
                    int meshIndex = meshIndices[i];
                    if ((uint)meshIndex >= (uint)asset.Meshes.Length)
                        return false;

                    topologyHash = unchecked(topologyHash * 31 + meshIndex);
                    var mesh = asset.Meshes[meshIndex];
                    if (mesh?.Triangles == null || mesh.Vertices == null || mesh.Normals == null || mesh.TexCoords == null)
                        return false;

                    MeshTopology topology = GetOrCreateMeshTopology(mesh, new MeshCacheKey(assetId, meshIndex));
                    topologies[i] = topology;
                    totalVertices += topology.Vertices.Length;
                    totalIndices += topology.Indices.Length;
                    maxSourceVertices = Math.Max(maxSourceVertices, mesh.Vertices.Length);
                    maxSourceNormals = Math.Max(maxSourceNormals, mesh.Normals.Length);
                }

                if (totalVertices <= 0 || totalIndices <= 0)
                    return false;

                bool prefer16Bit = totalVertices <= ushort.MaxValue;

                if (vertexBuffer != null && vertexBuffer.IsDisposed)
                    vertexBuffer = null;

                if (vertexBuffer == null || vertexBuffer.VertexCount < totalVertices)
                {
                    DynamicBufferPool.ReturnVertexBuffer(vertexBuffer);
                    vertexBuffer = DynamicBufferPool.RentVertexBuffer(totalVertices)
                        ?? new DynamicVertexBuffer(
                            _graphicsDevice,
                            VertexPositionColorNormalTexture.VertexDeclaration,
                            totalVertices,
                            BufferUsage.WriteOnly);
                }

                if (indexBuffer != null && indexBuffer.IsDisposed)
                    indexBuffer = null;

                IndexElementSize desiredIndexElementSize = prefer16Bit
                    ? IndexElementSize.SixteenBits
                    : IndexElementSize.ThirtyTwoBits;
                if (indexBuffer == null ||
                    indexBuffer.IndexCount != totalIndices ||
                    indexBuffer.IndexElementSize != desiredIndexElementSize)
                {
                    DynamicBufferPool.ReturnIndexBuffer(indexBuffer);
                    indexBuffer = DynamicBufferPool.RentIndexBuffer(totalIndices, prefer16Bit)
                        ?? new DynamicIndexBuffer(
                            _graphicsDevice,
                            desiredIndexElementSize,
                            totalIndices,
                            BufferUsage.WriteOnly);
                }
                indexBufferIs16Bit = prefer16Bit;

                bool uploadIndexData = !_indexBufferCacheState.TryGetValue(indexBuffer, out var indexCacheEntry) ||
                                       !indexCacheEntry.IsValid ||
                                       indexCacheEntry.AssetId != assetId ||
                                       indexCacheEntry.TopologyHash != topologyHash ||
                                       indexCacheEntry.IndexCount != totalIndices ||
                                       indexCacheEntry.Is16Bit != prefer16Bit;

                var vertices = ArrayPool<VertexPositionColorNormalTexture>.Shared.Rent(totalVertices);
                var positionCache = ArrayPool<Vector3>.Shared.Rent(maxSourceVertices);
                var normalCache = ArrayPool<Vector3>.Shared.Rent(maxSourceNormals);
                var positionVisited = ArrayPool<bool>.Shared.Rent(maxSourceVertices);
                var normalVisited = ArrayPool<bool>.Shared.Rent(maxSourceNormals);

                try
                {
                    int vertexBase = 0;
                    int transformedVertices = 0;

                    for (int groupIndex = 0; groupIndex < meshIndices.Count; groupIndex++)
                    {
                        var mesh = asset.Meshes[meshIndices[groupIndex]];
                        MeshTopology topology = topologies[groupIndex];
                        Array.Clear(positionVisited, 0, mesh.Vertices.Length);
                        Array.Clear(normalVisited, 0, mesh.Normals.Length);

                        for (int localVertex = 0; localVertex < topology.Vertices.Length; localVertex++)
                        {
                            MeshCorner corner = topology.Vertices[localVertex];
                            int sourceVertexIndex = corner.VertexIndex;
                            if (!positionVisited[sourceVertexIndex])
                            {
                                positionVisited[sourceVertexIndex] = true;
                                transformedVertices++;
                                var source = mesh.Vertices[sourceVertexIndex];
                                positionCache[sourceVertexIndex] = source.Node >= 0 && source.Node < boneMatrix.Length
                                    ? FastTransformPosition(in boneMatrix[source.Node], in source.Position)
                                    : source.Position;
                            }

                            int sourceNormalIndex = corner.NormalIndex;
                            if (!normalVisited[sourceNormalIndex])
                            {
                                normalVisited[sourceNormalIndex] = true;
                                var source = mesh.Normals[sourceNormalIndex];
                                normalCache[sourceNormalIndex] = TryResolveNormalBoneIndex(mesh, sourceNormalIndex, out int boneIndex) &&
                                                                 (uint)boneIndex < (uint)boneMatrix.Length
                                    ? FastTransformNormal(in boneMatrix[boneIndex], in source.Normal)
                                    : source.Normal;
                            }

                            var uv = mesh.TexCoords[corner.TexCoordIndex];
                            vertices[vertexBase + localVertex] = new VertexPositionColorNormalTexture(
                                positionCache[sourceVertexIndex],
                                color,
                                normalCache[sourceNormalIndex],
                                new Vector2(uv.U, uv.V));
                        }

                        vertexBase += topology.Vertices.Length;
                    }

                    vertexBuffer.SetData(vertices, 0, totalVertices, SetDataOptions.Discard);
                    FrameVBUpdates++;
                    FrameVerticesTransformed += transformedVertices;

                    if (uploadIndexData && prefer16Bit)
                    {
                        var indices = ArrayPool<ushort>.Shared.Rent(totalIndices);
                        try
                        {
                            int outputIndex = 0;
                            int baseVertex = 0;
                            for (int groupIndex = 0; groupIndex < meshIndices.Count; groupIndex++)
                            {
                                MeshTopology topology = topologies[groupIndex];
                                for (int i = 0; i < topology.Indices.Length; i++)
                                    indices[outputIndex++] = (ushort)(baseVertex + topology.Indices[i]);
                                baseVertex += topology.Vertices.Length;
                            }
                            indexBuffer.SetData(indices, 0, totalIndices, SetDataOptions.Discard);
                        }
                        finally
                        {
                            ArrayPool<ushort>.Shared.Return(indices, clearArray: false);
                        }
                    }
                    else if (uploadIndexData)
                    {
                        var indices = ArrayPool<int>.Shared.Rent(totalIndices);
                        try
                        {
                            int outputIndex = 0;
                            int baseVertex = 0;
                            for (int groupIndex = 0; groupIndex < meshIndices.Count; groupIndex++)
                            {
                                MeshTopology topology = topologies[groupIndex];
                                for (int i = 0; i < topology.Indices.Length; i++)
                                    indices[outputIndex++] = baseVertex + topology.Indices[i];
                                baseVertex += topology.Vertices.Length;
                            }
                            indexBuffer.SetData(indices, 0, totalIndices, SetDataOptions.Discard);
                        }
                        finally
                        {
                            ArrayPool<int>.Shared.Return(indices, clearArray: false);
                        }
                    }

                    if (uploadIndexData)
                    {
                        FrameIBUploads++;
                        var updatedIndexEntry = _indexBufferCacheState.GetOrCreateValue(indexBuffer);
                        updatedIndexEntry.AssetId = assetId;
                        updatedIndexEntry.TopologyHash = topologyHash;
                        updatedIndexEntry.IndexCount = totalIndices;
                        updatedIndexEntry.Is16Bit = prefer16Bit;
                        updatedIndexEntry.IsValid = true;
                    }

                    FrameMeshBatchBuilds++;
                    FrameMeshBatchMeshes += meshIndices.Count;
                    return true;
                }
                finally
                {
                    ArrayPool<VertexPositionColorNormalTexture>.Shared.Return(vertices, clearArray: false);
                    ArrayPool<Vector3>.Shared.Return(positionCache, clearArray: false);
                    ArrayPool<Vector3>.Shared.Return(normalCache, clearArray: false);
                    ArrayPool<bool>.Shared.Return(positionVisited, clearArray: false);
                    ArrayPool<bool>.Shared.Return(normalVisited, clearArray: false);
                }
            }
            finally
            {
                Array.Clear(topologies, 0, meshIndices.Count);
                ArrayPool<MeshTopology>.Shared.Return(topologies, clearArray: false);
            }
        }

        /// <summary>
        /// Returns immutable mesh buffers for GPU skinning path.
        /// Buffers store bind-pose positions and per-vertex bone index.
        /// </summary>
        public bool TryGetGpuSkinnedMeshBuffers(
            BMD asset,
            int meshIndex,
            out VertexBuffer vertexBuffer,
            out IndexBuffer indexBuffer,
            out int boneCount)
        {
            vertexBuffer = null;
            indexBuffer = null;
            boneCount = 0;

            if (asset == null || _graphicsDevice == null || asset.Meshes == null ||
                meshIndex < 0 || meshIndex >= asset.Meshes.Length)
            {
                return false;
            }

            int assetId = RuntimeHelpers.GetHashCode(asset);
            var cacheKey = new MeshCacheKey(assetId, meshIndex);

            lock (_gpuSkinBufferLock)
            {
                if (TryGetCachedGpuSkinnedBuffersNoLock(
                    cacheKey,
                    out vertexBuffer,
                    out indexBuffer,
                    out boneCount))
                {
                    _gpuSkinLastUsedFrames[cacheKey] = MuGame.FrameIndex;
                    return true;
                }
            }

            var mesh = asset.Meshes[meshIndex];
            if (mesh?.Triangles == null || mesh.Vertices == null || mesh.Normals == null || mesh.TexCoords == null)
                return false;

            MeshTopology topology = GetOrCreateMeshTopology(mesh, cacheKey);
            int uniqueVertexCount = topology.Vertices.Length;
            int indexCount = topology.Indices.Length;
            if (uniqueVertexCount <= 0 || indexCount <= 0)
                return false;

            bool prefer16Bit = uniqueVertexCount <= ushort.MaxValue;
            var vertices = ArrayPool<SkinnedVertexPositionColorNormalTexture>.Shared.Rent(uniqueVertexCount);

            try
            {
                int maxBoneIndex = 0;
                for (int outputIndex = 0; outputIndex < uniqueVertexCount; outputIndex++)
                {
                    MeshCorner corner = topology.Vertices[outputIndex];
                    var sourceVertex = mesh.Vertices[corner.VertexIndex];
                    int positionBoneIndex = sourceVertex.Node >= 0 ? sourceVertex.Node : 0;
                    int normalBoneIndex = positionBoneIndex;
                    if (TryResolveNormalBoneIndex(mesh, corner.NormalIndex, out int resolvedNormalBone) && resolvedNormalBone >= 0)
                        normalBoneIndex = resolvedNormalBone;

                    maxBoneIndex = Math.Max(maxBoneIndex, Math.Max(positionBoneIndex, normalBoneIndex));
                    var normal = mesh.Normals[corner.NormalIndex].Normal;
                    var uv = mesh.TexCoords[corner.TexCoordIndex];

                    vertices[outputIndex] = new SkinnedVertexPositionColorNormalTexture(
                        sourceVertex.Position,
                        Color.White,
                        normal,
                        new Vector2(uv.U, uv.V),
                        new Vector2(positionBoneIndex, normalBoneIndex));
                }

                var newVertexBuffer = new VertexBuffer(
                    _graphicsDevice,
                    SkinnedVertexPositionColorNormalTexture.VertexDeclaration,
                    uniqueVertexCount,
                    BufferUsage.WriteOnly);
                newVertexBuffer.SetData(vertices, 0, uniqueVertexCount);

                IndexBuffer newIndexBuffer;
                if (prefer16Bit)
                {
                    var indices = ArrayPool<ushort>.Shared.Rent(indexCount);
                    try
                    {
                        for (int i = 0; i < indexCount; i++)
                            indices[i] = (ushort)topology.Indices[i];

                        newIndexBuffer = new IndexBuffer(
                            _graphicsDevice,
                            IndexElementSize.SixteenBits,
                            indexCount,
                            BufferUsage.WriteOnly);
                        newIndexBuffer.SetData(indices, 0, indexCount);
                    }
                    finally
                    {
                        ArrayPool<ushort>.Shared.Return(indices, clearArray: false);
                    }
                }
                else
                {
                    newIndexBuffer = new IndexBuffer(
                        _graphicsDevice,
                        IndexElementSize.ThirtyTwoBits,
                        indexCount,
                        BufferUsage.WriteOnly);
                    newIndexBuffer.SetData(topology.Indices, 0, indexCount);
                }

                lock (_gpuSkinBufferLock)
                {
                    // Another object of the same model may have populated the shared cache
                    // while this mesh was being built. Reuse that buffer and never dispose a
                    // valid buffer that may already be referenced by live monster instances.
                    if (TryGetCachedGpuSkinnedBuffersNoLock(
                        cacheKey,
                        out vertexBuffer,
                        out indexBuffer,
                        out boneCount))
                    {
                        _gpuSkinLastUsedFrames[cacheKey] = MuGame.FrameIndex;
                        newVertexBuffer.Dispose();
                        newIndexBuffer.Dispose();
                        return true;
                    }

                    if (_gpuSkinVertexBuffers.TryGetValue(cacheKey, out var invalidVertexBuffer))
                        invalidVertexBuffer?.Dispose();
                    if (_gpuSkinIndexBuffers.TryGetValue(cacheKey, out var invalidIndexBuffer))
                        invalidIndexBuffer?.Dispose();

                    _gpuSkinVertexBuffers[cacheKey] = newVertexBuffer;
                    _gpuSkinIndexBuffers[cacheKey] = newIndexBuffer;
                    _gpuSkinBoneCounts[cacheKey] = maxBoneIndex + 1;
                    _gpuSkinLastUsedFrames[cacheKey] = MuGame.FrameIndex;

                    vertexBuffer = newVertexBuffer;
                    indexBuffer = newIndexBuffer;
                    boneCount = maxBoneIndex + 1;
                    return true;
                }
            }
            finally
            {
                ArrayPool<SkinnedVertexPositionColorNormalTexture>.Shared.Return(vertices, clearArray: false);
            }
        }

        /// <summary>
        /// Returns one immutable GPU-skinned vertex/index buffer for a group of meshes.
        /// The caller guarantees that all meshes share the same render state and texture.
        /// Combining them removes per-mesh vertex/index binds and draw calls while keeping
        /// the same bone palette and material parameters.
        /// </summary>
        public bool TryGetGpuSkinnedMeshBatchBuffers(
            BMD asset,
            IReadOnlyList<int> meshIndices,
            out VertexBuffer vertexBuffer,
            out IndexBuffer indexBuffer,
            out int boneCount)
        {
            vertexBuffer = null;
            indexBuffer = null;
            boneCount = 0;

            if (asset?.Meshes == null || _graphicsDevice == null ||
                meshIndices == null || meshIndices.Count <= 1)
            {
                return false;
            }

            int assetId = RuntimeHelpers.GetHashCode(asset);
            ulong sequenceHash = CalculateMeshSequenceHash(meshIndices);
            var cacheKey = new GpuSkinBatchCacheKey(assetId, sequenceHash, meshIndices.Count);

            lock (_gpuSkinBufferLock)
            {
                if (_gpuSkinBatchBuffers.TryGetValue(cacheKey, out var cached))
                {
                    if (!MeshSequenceEquals(cached.MeshIndices, meshIndices))
                        return false; // Extremely unlikely hash collision: keep correctness over batching.

                    if (cached.VertexBuffer != null && !cached.VertexBuffer.IsDisposed &&
                        cached.IndexBuffer != null && !cached.IndexBuffer.IsDisposed &&
                        cached.BoneCount > 0)
                    {
                        cached.LastUsedFrame = MuGame.FrameIndex;
                        vertexBuffer = cached.VertexBuffer;
                        indexBuffer = cached.IndexBuffer;
                        boneCount = cached.BoneCount;
                        return true;
                    }

                    _gpuSkinBatchBuffers.Remove(cacheKey);
                    cached.VertexBuffer?.Dispose();
                    cached.IndexBuffer?.Dispose();
                }
            }

            var topologies = ArrayPool<MeshTopology>.Shared.Rent(meshIndices.Count);
            int totalVertices = 0;
            int totalIndices = 0;
            try
            {
                for (int i = 0; i < meshIndices.Count; i++)
                {
                    int meshIndex = meshIndices[i];
                    if ((uint)meshIndex >= (uint)asset.Meshes.Length)
                        return false;

                    var mesh = asset.Meshes[meshIndex];
                    if (mesh?.Triangles == null || mesh.Vertices == null ||
                        mesh.Normals == null || mesh.TexCoords == null)
                    {
                        return false;
                    }

                    var topology = GetOrCreateMeshTopology(mesh, new MeshCacheKey(assetId, meshIndex));
                    if (topology.Vertices.Length == 0 || topology.Indices.Length == 0)
                        return false;

                    topologies[i] = topology;
                    checked
                    {
                        totalVertices += topology.Vertices.Length;
                        totalIndices += topology.Indices.Length;
                    }
                }

                if (totalVertices <= 0 || totalIndices <= 0)
                    return false;

                bool use16BitIndices = totalVertices <= ushort.MaxValue;
                var vertices = ArrayPool<SkinnedVertexPositionColorNormalTexture>.Shared.Rent(totalVertices);
                try
                {
                    int outputVertex = 0;
                    int maxBoneIndex = 0;
                    for (int groupIndex = 0; groupIndex < meshIndices.Count; groupIndex++)
                    {
                        int meshIndex = meshIndices[groupIndex];
                        var mesh = asset.Meshes[meshIndex];
                        var topology = topologies[groupIndex];

                        for (int localVertex = 0; localVertex < topology.Vertices.Length; localVertex++)
                        {
                            MeshCorner corner = topology.Vertices[localVertex];
                            var sourceVertex = mesh.Vertices[corner.VertexIndex];
                            int positionBoneIndex = sourceVertex.Node >= 0 ? sourceVertex.Node : 0;
                            int normalBoneIndex = positionBoneIndex;
                            if (TryResolveNormalBoneIndex(mesh, corner.NormalIndex, out int resolvedNormalBone) &&
                                resolvedNormalBone >= 0)
                            {
                                normalBoneIndex = resolvedNormalBone;
                            }

                            maxBoneIndex = Math.Max(maxBoneIndex, Math.Max(positionBoneIndex, normalBoneIndex));
                            var normal = mesh.Normals[corner.NormalIndex].Normal;
                            var uv = mesh.TexCoords[corner.TexCoordIndex];
                            vertices[outputVertex++] = new SkinnedVertexPositionColorNormalTexture(
                                sourceVertex.Position,
                                Color.White,
                                normal,
                                new Vector2(uv.U, uv.V),
                                new Vector2(positionBoneIndex, normalBoneIndex));
                        }
                    }

                    VertexBuffer newVertexBuffer = null;
                    IndexBuffer newIndexBuffer = null;
                    try
                    {
                        newVertexBuffer = new VertexBuffer(
                            _graphicsDevice,
                            SkinnedVertexPositionColorNormalTexture.VertexDeclaration,
                            totalVertices,
                            BufferUsage.WriteOnly);
                        newVertexBuffer.SetData(vertices, 0, totalVertices);

                        if (use16BitIndices)
                        {
                            var indices = ArrayPool<ushort>.Shared.Rent(totalIndices);
                            try
                            {
                                int outputIndex = 0;
                                int baseVertex = 0;
                                for (int groupIndex = 0; groupIndex < meshIndices.Count; groupIndex++)
                                {
                                    var topology = topologies[groupIndex];
                                    for (int i = 0; i < topology.Indices.Length; i++)
                                        indices[outputIndex++] = (ushort)(baseVertex + topology.Indices[i]);
                                    baseVertex += topology.Vertices.Length;
                                }

                                newIndexBuffer = new IndexBuffer(
                                    _graphicsDevice,
                                    IndexElementSize.SixteenBits,
                                    totalIndices,
                                    BufferUsage.WriteOnly);
                                newIndexBuffer.SetData(indices, 0, totalIndices);
                            }
                            finally
                            {
                                ArrayPool<ushort>.Shared.Return(indices, clearArray: false);
                            }
                        }
                        else
                        {
                            var indices = ArrayPool<int>.Shared.Rent(totalIndices);
                            try
                            {
                                int outputIndex = 0;
                                int baseVertex = 0;
                                for (int groupIndex = 0; groupIndex < meshIndices.Count; groupIndex++)
                                {
                                    var topology = topologies[groupIndex];
                                    for (int i = 0; i < topology.Indices.Length; i++)
                                        indices[outputIndex++] = baseVertex + topology.Indices[i];
                                    baseVertex += topology.Vertices.Length;
                                }

                                newIndexBuffer = new IndexBuffer(
                                    _graphicsDevice,
                                    IndexElementSize.ThirtyTwoBits,
                                    totalIndices,
                                    BufferUsage.WriteOnly);
                                newIndexBuffer.SetData(indices, 0, totalIndices);
                            }
                            finally
                            {
                                ArrayPool<int>.Shared.Return(indices, clearArray: false);
                            }
                        }

                        int resolvedBoneCount = maxBoneIndex + 1;
                        int[] cachedMeshIndices = new int[meshIndices.Count];
                        for (int i = 0; i < meshIndices.Count; i++)
                            cachedMeshIndices[i] = meshIndices[i];

                        lock (_gpuSkinBufferLock)
                        {
                            if (_gpuSkinBatchBuffers.TryGetValue(cacheKey, out var existing))
                            {
                                if (MeshSequenceEquals(existing.MeshIndices, meshIndices) &&
                                    existing.VertexBuffer != null && !existing.VertexBuffer.IsDisposed &&
                                    existing.IndexBuffer != null && !existing.IndexBuffer.IsDisposed)
                                {
                                    newVertexBuffer.Dispose();
                                    newIndexBuffer.Dispose();
                                    existing.LastUsedFrame = MuGame.FrameIndex;
                                    vertexBuffer = existing.VertexBuffer;
                                    indexBuffer = existing.IndexBuffer;
                                    boneCount = existing.BoneCount;
                                    return true;
                                }

                                newVertexBuffer.Dispose();
                                newIndexBuffer.Dispose();
                                return false;
                            }

                            var entry = new GpuSkinBatchCacheEntry
                            {
                                MeshIndices = cachedMeshIndices,
                                VertexBuffer = newVertexBuffer,
                                IndexBuffer = newIndexBuffer,
                                BoneCount = resolvedBoneCount,
                                LastUsedFrame = MuGame.FrameIndex
                            };
                            _gpuSkinBatchBuffers.Add(cacheKey, entry);
                            vertexBuffer = newVertexBuffer;
                            indexBuffer = newIndexBuffer;
                            boneCount = resolvedBoneCount;
                            return true;
                        }
                    }
                    catch
                    {
                        newVertexBuffer?.Dispose();
                        newIndexBuffer?.Dispose();
                        throw;
                    }
                }
                finally
                {
                    ArrayPool<SkinnedVertexPositionColorNormalTexture>.Shared.Return(vertices, clearArray: false);
                }
            }
            finally
            {
                Array.Clear(topologies, 0, meshIndices.Count);
                ArrayPool<MeshTopology>.Shared.Return(topologies, clearArray: false);
            }
        }

        private static ulong CalculateMeshSequenceHash(IReadOnlyList<int> meshIndices)
        {
            const ulong offsetBasis = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;
            ulong hash = offsetBasis;
            for (int i = 0; i < meshIndices.Count; i++)
            {
                hash ^= unchecked((uint)meshIndices[i]);
                hash *= prime;
            }
            return hash;
        }

        private static bool MeshSequenceEquals(int[] cached, IReadOnlyList<int> current)
        {
            if (cached == null || current == null || cached.Length != current.Count)
                return false;

            for (int i = 0; i < cached.Length; i++)
            {
                if (cached[i] != current[i])
                    return false;
            }
            return true;
        }

        private bool TryGetCachedGpuSkinnedBuffersNoLock(
            MeshCacheKey cacheKey,
            out VertexBuffer vertexBuffer,
            out IndexBuffer indexBuffer,
            out int boneCount)
        {
            vertexBuffer = null;
            indexBuffer = null;
            boneCount = 0;

            if (!_gpuSkinVertexBuffers.TryGetValue(cacheKey, out var cachedVertexBuffer) ||
                !_gpuSkinIndexBuffers.TryGetValue(cacheKey, out var cachedIndexBuffer) ||
                !_gpuSkinBoneCounts.TryGetValue(cacheKey, out int cachedBoneCount) ||
                cachedVertexBuffer == null || cachedVertexBuffer.IsDisposed ||
                cachedIndexBuffer == null || cachedIndexBuffer.IsDisposed ||
                cachedBoneCount <= 0)
            {
                return false;
            }

            vertexBuffer = cachedVertexBuffer;
            indexBuffer = cachedIndexBuffer;
            boneCount = cachedBoneCount;
            return true;
        }

        public void TouchGpuSkinnedMeshBuffers(BMD asset, int meshIndex)
        {
            if (asset == null || meshIndex < 0)
                return;

            var cacheKey = new MeshCacheKey(RuntimeHelpers.GetHashCode(asset), meshIndex);
            lock (_gpuSkinBufferLock)
            {
                if (_gpuSkinVertexBuffers.ContainsKey(cacheKey))
                    _gpuSkinLastUsedFrames[cacheKey] = MuGame.FrameIndex;
            }
        }

        private void PruneUnusedGpuSkinBuffers(int currentFrame)
        {
            lock (_gpuSkinBufferLock)
            {
                _staleGpuMeshKeys.Clear();
                foreach (var pair in _gpuSkinLastUsedFrames)
                {
                    int age = currentFrame - pair.Value;
                    if (age >= 0 && age > GpuCacheRetentionFrames)
                        _staleGpuMeshKeys.Add(pair.Key);
                }

                for (int i = 0; i < _staleGpuMeshKeys.Count; i++)
                {
                    MeshCacheKey key = _staleGpuMeshKeys[i];
                    if (_gpuSkinVertexBuffers.Remove(key, out var vb))
                        vb?.Dispose();
                    if (_gpuSkinIndexBuffers.Remove(key, out var ib))
                        ib?.Dispose();
                    _gpuSkinBoneCounts.Remove(key);
                    _gpuSkinLastUsedFrames.Remove(key);
                    LastFrameGpuMeshBuffersPruned++;
                }

                _staleGpuBatchKeys.Clear();
                foreach (var pair in _gpuSkinBatchBuffers)
                {
                    int age = currentFrame - pair.Value.LastUsedFrame;
                    if (age >= 0 && age > GpuCacheRetentionFrames)
                        _staleGpuBatchKeys.Add(pair.Key);
                }

                for (int i = 0; i < _staleGpuBatchKeys.Count; i++)
                {
                    GpuSkinBatchCacheKey key = _staleGpuBatchKeys[i];
                    if (_gpuSkinBatchBuffers.Remove(key, out var entry))
                    {
                        entry.VertexBuffer?.Dispose();
                        entry.IndexBuffer?.Dispose();
                        LastFrameGpuBatchBuffersPruned++;
                    }
                }
            }
        }

        private void PruneUnusedMeshTopologies(int currentFrame)
        {
            lock (_meshTopologyLock)
            {
                _staleMeshTopologyKeys.Clear();
                foreach (var pair in _meshTopologyLastUsedFrames)
                {
                    int age = currentFrame - pair.Value;
                    if (age >= 0 && age > GpuCacheRetentionFrames)
                        _staleMeshTopologyKeys.Add(pair.Key);
                }

                for (int i = 0; i < _staleMeshTopologyKeys.Count; i++)
                {
                    MeshCacheKey key = _staleMeshTopologyKeys[i];
                    if (_meshTopologyCache.Remove(key))
                        LastFrameMeshTopologiesPruned++;
                    _meshTopologyLastUsedFrames.Remove(key);
                }
            }
        }

        public string GetTexturePath(BMD bmd, string texturePath)
        {
            texturePath = texturePath.ToLowerInvariant();

            string result = null;

            if (_texturePathMap.TryGetValue(bmd, out Dictionary<string, string> value) && value.TryGetValue(texturePath, out string fullTexturePath))
                result = fullTexturePath;

            if (result == null)
                _logger?.LogDebug($"Texture path not found: {texturePath}");

            return result;
        }

        public void RegisterDerivedModel(BMD source, BMD derived)
        {
            if (source == null || derived == null)
                return;

            lock (_texturePathMap)
            {
                if (_texturePathMap.TryGetValue(source, out Dictionary<string, string> texturePaths))
                    _texturePathMap[derived] = texturePaths;
            }
        }

        // Clear cache when needed (e.g., when objects are disposed)
        public void ClearBufferCache()
        {
            _bufferCacheState = new ConditionalWeakTable<DynamicVertexBuffer, BufferCacheEntry>();
            _indexBufferCacheState = new ConditionalWeakTable<DynamicIndexBuffer, IndexBufferCacheEntry>();
            lock (_meshTopologyLock)
            {
                _meshTopologyCache.Clear();
                _meshTopologyLastUsedFrames.Clear();
                _staleMeshTopologyKeys.Clear();
            }
            DisposeGpuSkinnedBuffers();
        }

        private void DisposeGpuSkinnedBuffers()
        {
            lock (_gpuSkinBufferLock)
            {
                foreach (var vb in _gpuSkinVertexBuffers.Values)
                    vb?.Dispose();

                foreach (var ib in _gpuSkinIndexBuffers.Values)
                    ib?.Dispose();

                foreach (var batch in _gpuSkinBatchBuffers.Values)
                {
                    batch.VertexBuffer?.Dispose();
                    batch.IndexBuffer?.Dispose();
                }

                _gpuSkinVertexBuffers.Clear();
                _gpuSkinIndexBuffers.Clear();
                _gpuSkinBoneCounts.Clear();
                _gpuSkinLastUsedFrames.Clear();
                _gpuSkinBatchBuffers.Clear();
                _staleGpuMeshKeys.Clear();
                _staleGpuBatchKeys.Clear();
            }
        }
    }

}
