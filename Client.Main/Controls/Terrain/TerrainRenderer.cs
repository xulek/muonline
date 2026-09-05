using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Graphics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Extensions.Logging;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Client.Main.Controls.Terrain
{
    /// <summary>
    /// Handles the rendering of terrain tiles, including opaque and alpha-blended layers.
    /// </summary>
    public class TerrainRenderer : IDisposable
    {
        private const float SpecialHeight = 1200f;
        private const int BlockSize = 4;
        private const short AtlansWorldIndex = 8;
        private const short DoppelgangerUnderwaterWorldIndex = 68;

        // SourceMain IsDoppelGanger3(): both worlds share the animated-water flipbook path.
        private bool IsAnimatedWaterWorld => WorldIndex == AtlansWorldIndex || WorldIndex == DoppelgangerUnderwaterWorldIndex;
        private const byte AtlansCausticsLayer = 5;
        private const int WaterCausticsFrameCount = 32;
        private const int MaxWaterCausticsVertices =
            Constants.TERRAIN_SIZE * Constants.TERRAIN_SIZE * 6;
        private const double WaterCausticsFrameDurationSeconds = 1.0 / 25.0;

        private static readonly BlendState WaterCausticsBlendState = new()
        {
            ColorBlendFunction = BlendFunction.Add,
            ColorSourceBlend = Blend.One,
            ColorDestinationBlend = Blend.One,
            AlphaBlendFunction = BlendFunction.Add,
            AlphaSourceBlend = Blend.One,
            AlphaDestinationBlend = Blend.One
        };

        private static readonly DepthStencilState WaterCausticsDepthState = new()
        {
            DepthBufferEnable = true,
            DepthBufferWriteEnable = false,
            DepthBufferFunction = CompareFunction.LessEqual
        };

        private static readonly RasterizerState WaterCausticsRasterizerState = new()
        {
            CullMode = CullMode.None
        };
        // Static terrain vertices with per-texture index batches avoid uploading the complete
        // visible vertex stream every frame. LOD transition/edge tiles still use the explicit-UV
        // streaming path, so enabling this remains visually safe for validated static scenes.
        private const bool EnableTerrainIndexBatching = true;
        // Set to false for an immediate rollback to the stage-2 per-pass index upload path.
        private const bool EnablePersistentTerrainIndexCache = true;
        private const int MaxTileBatchVerts = 16384 * 6;
        private const int InitialTileBatchVerts = 2048;
        private const int TileBatchIndices = 16384 * 6;
        private const int MaxPersistentTerrainIndices =
            Constants.TERRAIN_SIZE * Constants.TERRAIN_SIZE * 6;
        private const float LodSkirtDepth = Constants.TERRAIN_SCALE * 1.5f;

        private const byte LodEdgeNorth = 1;
        private const byte LodEdgeSouth = 2;
        private const byte LodEdgeWest = 4;
        private const byte LodEdgeEast = 8;

        private readonly GraphicsDevice _graphicsDevice;
        private readonly TerrainData _data;
        private readonly TerrainVisibilityManager _visibility;
        private readonly TerrainLightManager _lightManager;
        private readonly GrassRenderer _grassRenderer;

        // Per-texture tile batches
        private readonly TerrainVertexPositionColorNormalTexture[][] _tileBatches = new TerrainVertexPositionColorNormalTexture[256][];
        private readonly int[] _tileBatchCounts = new int[256];
        private readonly TerrainVertexPositionColorNormalTexture[][] _tileAlphaBatches = new TerrainVertexPositionColorNormalTexture[256][];
        private readonly int[] _tileAlphaCounts = new int[256];

        // Per-texture tile index batches (used when terrain can be rendered from a static VB)
        private readonly ushort[][] _tileIndexBatches = new ushort[256][];
        private readonly int[] _tileIndexCounts = new int[256];
        private readonly ushort[][] _tileAlphaIndexBatches = new ushort[256][];
        private readonly int[] _tileAlphaIndexCounts = new int[256];
        private readonly bool[] _tileIndexActive = new bool[256];
        private readonly bool[] _tileAlphaIndexActive = new bool[256];
        private readonly List<int> _activeTileIndexTextures = new(32);
        private readonly List<int> _activeTileAlphaIndexTextures = new(32);

        // Visibility-stable terrain batches remain resident on the GPU. The old path rebuilt
        // and uploaded the same per-texture index stream in both the shadow and color pass,
        // and repeated it every frame while the terrain visibility version was unchanged.
        private readonly DynamicIndexBuffer[] _persistentTileIndexBuffers = new DynamicIndexBuffer[256];
        private readonly DynamicIndexBuffer[] _persistentTileAlphaIndexBuffers = new DynamicIndexBuffer[256];
        private readonly int[] _persistentTileIndexCounts = new int[256];
        private readonly int[] _persistentTileAlphaIndexCounts = new int[256];
        private readonly List<int> _persistentActiveTileTextures = new(32);
        private readonly List<int> _persistentActiveTileAlphaTextures = new(32);
        private int _persistentTerrainIndexVersion = int.MinValue;
        private byte[] _persistentLayer1MapRef;
        private byte[] _persistentLayer2MapRef;
        private byte[] _persistentAlphaMapRef;
        private int _persistentTextureStateHash;
        private bool _buildingPersistentTerrainIndexCache;
        private bool _usingPersistentTerrainIndexCache;
        private bool _isShadowPass;
        private bool _replayingPersistentTerrainPlan;
        private readonly List<TerrainTileCommand> _persistentDynamicTileCommands = new(256);
        private int _persistentIndexedCellCount;
        private int _persistentStreamedCellCount;
        private int _persistentDrawnCellCount;
        private int _persistentDrawnBlockCount;
        private int _buildingIndexedCellCount;
        private int _buildingStreamedCellCount;
        private int _buildingDrawnCellCount;
        private int _buildingDrawnBlockCount;

        private readonly struct TerrainTileCommand
        {
            public TerrainTileCommand(int x, int y, int lod, byte edgeMask, bool causticsOnly)
            {
                X = x;
                Y = y;
                Lod = lod;
                EdgeMask = edgeMask;
                CausticsOnly = causticsOnly;
            }

            public int X { get; }
            public int Y { get; }
            public int Lod { get; }
            public byte EdgeMask { get; }
            public bool CausticsOnly { get; }
        }

        private readonly bool[] _tileBatchActive = new bool[256];
        private readonly bool[] _tileAlphaBatchActive = new bool[256];
        private readonly List<int> _activeTileBatchTextures = new(32);
        private readonly List<int> _activeTileAlphaBatchTextures = new(32);

        // Buffers for a single terrain tile quad
        private readonly TerrainVertexPositionColorNormalTexture[] _terrainVertices = new TerrainVertexPositionColorNormalTexture[6];
        private readonly Vector2[] _terrainTextureCoords = new Vector2[4];
        private readonly Vector3[] _tempTerrainVertex = new Vector3[4];
        private readonly Vector3[] _tempTerrainNormals = new Vector3[4];
        private readonly Color[] _tempTerrainLights = new Color[4];
        private readonly Color[] _tempTerrainLightsBase = new Color[4];
        private VertexPositionColorTexture[] _fallbackTileBuffer = new VertexPositionColorTexture[InitialTileBatchVerts];
        private VertexPositionColorTexture[] _waterCausticsVertices = new VertexPositionColorTexture[InitialTileBatchVerts];
        private int _waterCausticsVertexCount;
        private DynamicVertexBuffer _waterCausticsStreamBuffer;
        private BasicEffect _waterCausticsEffect;
        private double _waterCausticsAccumulator;
        private int _waterCausticsFrame;
        private Texture2D _activeWaterCausticsTexture;

        // Cached per-vertex data (built once per terrain) to avoid per-tile CPU work each frame.
        private bool _vertexCacheBuilt;
        private Color[] _cachedHeightMapRef;
        private Client.Data.ATT.TWFlags[] _cachedTerrainWallRef;
        private Vector3[] _cachedNormalsRef;
        private Vector3[] _cachedVertexPositions;
        private Vector3[] _cachedVertexNormals;

        private bool _lightCacheBuilt;
        private Color[] _cachedFinalLightMapRef;
        private float _cachedAmbientLight = float.NaN;
        private Color[] _cachedVertexBaseLights;

        // State tracking for GPU optimization
        private Texture2D _lastBoundTexture = null;
        private BlendState _lastBlendState = null;

        // Black-frame probe throttling (see LogTerrainBlackFrameProbe).
        private int _lastTerrainEmptyLog = -100000;
        private int _lastTerrainSkippedLog = -100000;
        private int _lastTerrainCliffLog = -100000;
        private int _prevFrameDrawnTiles;
        private int _prevFrameVisibleBlocks;
        private bool _useDynamicLightingShader = false;
        private bool _useTerrainIndexBatching = false;

        // Static terrain vertex buffers (terrain uses procedural UVs in the shader)
        private VertexBuffer _terrainVertexBufferBase;
        private VertexBuffer _terrainVertexBufferAlpha;
        // Terrain owns its streaming buffers. Sharing model buffers here can expose a
        // different vertex declaration or in-flight contents after animation uploads.
        private DynamicVertexBuffer _terrainStreamBufferOpaque;
        private DynamicVertexBuffer _terrainStreamBufferAlpha;
        private bool _terrainVertexBuffersBuilt;
        private Color[] _terrainBuffersHeightMapRef;
        private Client.Data.ATT.TWFlags[] _terrainBuffersTerrainWallRef;
        private Vector3[] _terrainBuffersNormalsRef;
        private Color[] _terrainBuffersFinalLightMapRef;
        private float _terrainBuffersAmbientLight = float.NaN;
        private byte[] _terrainBuffersAlphaMapRef;

        private const int DynamicLightArrayCapacity = 32;
        private readonly DynamicLightGpuUploader _dynamicLightUploader = new(DynamicLightArrayCapacity);

        private Vector2 _waterFlowDir = Vector2.UnitX;
        private float _waterTotal = 0f;

        // Precomputed UV scales to avoid divisions per tile
        private readonly Vector2[] _tileUvScale = new Vector2[256];
        private readonly Vector2[] _tileUvScaleWorld = new Vector2[256];

        private readonly int _blocksPerSide;
        private readonly sbyte[] _visibleBlockLod;
        private bool _hasLodTransitions;
        // Cache so the color and shadow passes don't both rebuild the LOD grid in the same frame.
        private int _lodGridBuiltVersion = int.MinValue;
        private bool _lodGridBuiltResult;

        public float WaterSpeed { get; set; } = 0f;
        public float DistortionAmplitude { get; set; } = 0f;
        public float DistortionFrequency { get; set; } = 0f;
        public float AmbientLight { get; set; } = 0.25f;
        public short WorldIndex { get; set; }
        public bool PreferIndexBatching { get; set; }

        public int DrawCalls { get; private set; }
        public int DrawnTriangles { get; private set; }
        public int DrawnBlocks { get; private set; }
        public int DrawnTiles { get; private set; }
        public int SkippedTilesNoTexture { get; private set; }
        public int DrawnCells { get; private set; }
        public int IndexedCells { get; private set; }
        public int StreamedCells { get; private set; }
        public int IndexUploads { get; private set; }
        public int VertexUploads { get; private set; }
        public int UploadedIndices { get; private set; }
        public int UploadedVertices { get; private set; }
        public bool UsedIndexBatching { get; private set; }
        public int LastUploadedDynamicLights { get; private set; }
        public bool IsGpuLightingActive => _useDynamicLightingShader;
        public bool IsDynamicLightingShaderAvailable => GraphicsManager.Instance.DynamicLightingEffect != null;

        public Vector2 WaterFlowDirection
        {
            get => _waterFlowDir;
            set => _waterFlowDir = value.LengthSquared() < 1e-4f
                                   ? Vector2.UnitX
                                   : Vector2.Normalize(value);
        }

        public TerrainRenderer(GraphicsDevice graphicsDevice, TerrainData data, TerrainVisibilityManager visibility, TerrainLightManager lightManager, GrassRenderer grassRenderer)
        {
            _graphicsDevice = graphicsDevice;
            _data = data;
            _visibility = visibility;
            _lightManager = lightManager;
            _grassRenderer = grassRenderer;
            _blocksPerSide = Constants.TERRAIN_SIZE / BlockSize;
            _visibleBlockLod = new sbyte[_blocksPerSide * _blocksPerSide];

            // Precompute UV scales for all textures
            PrecomputeUVScales();
        }

        private void PrecomputeUVScales()
        {
            for (int t = 0; t < _data.Textures.Length; t++)
            {
                if (_data.Textures[t] != null)
                {
                    float invW = 64f / _data.Textures[t].Width;
                    float invH = 64f / _data.Textures[t].Height;
                    _tileUvScale[t] = new Vector2(invW, invH);
                    _tileUvScaleWorld[t] = new Vector2(invW / Constants.TERRAIN_SCALE, invH / Constants.TERRAIN_SCALE);
                }
            }
        }

        public void CreateHeightMapTexture()
        {
            if (_data.HeightMap == null || _graphicsDevice == null) return;

            _data.HeightMapTexture = new Texture2D(_graphicsDevice, Constants.TERRAIN_SIZE, Constants.TERRAIN_SIZE, false, SurfaceFormat.Single);

            int valueCount = Constants.TERRAIN_SIZE * Constants.TERRAIN_SIZE;
            float[] heightData = ArrayPool<float>.Shared.Rent(valueCount);
            try
            {
                // ArrayPool may return a larger array. Only fill and upload the logical
                // terrain range; iterating over Length can read past HeightMap.
                for (int i = 0; i < valueCount; i++)
                    heightData[i] = _data.HeightMap[i].R / 255.0f;

                _data.HeightMapTexture.SetData(heightData, 0, valueCount);
            }
            finally
            {
                ArrayPool<float>.Shared.Return(heightData);
            }
        }

        public void Update(GameTime time)
        {
            double elapsedSeconds = time.ElapsedGameTime.TotalSeconds;
            _waterTotal += (float)elapsedSeconds * WaterSpeed;

            if (!IsAnimatedWaterWorld || _data.WaterCausticsTextures == null)
                return;

            _waterCausticsAccumulator += elapsedSeconds;
            if (_waterCausticsAccumulator < WaterCausticsFrameDurationSeconds)
                return;

            int elapsedFrames = (int)(_waterCausticsAccumulator / WaterCausticsFrameDurationSeconds);
            _waterCausticsAccumulator -= elapsedFrames * WaterCausticsFrameDurationSeconds;
            _waterCausticsFrame = (_waterCausticsFrame + elapsedFrames) % WaterCausticsFrameCount;
        }

        private void EnsureVertexCache()
        {
            if (_data.HeightMap == null)
                return;

            int total = Constants.TERRAIN_SIZE * Constants.TERRAIN_SIZE;
            var heightMap = _data.HeightMap;
            var terrainWall = _data.Attributes?.TerrainWall;
            var normals = _data.Normals;

            if (_vertexCacheBuilt &&
                _cachedVertexPositions != null &&
                _cachedVertexPositions.Length == total &&
                ReferenceEquals(heightMap, _cachedHeightMapRef) &&
                ReferenceEquals(terrainWall, _cachedTerrainWallRef) &&
                ReferenceEquals(normals, _cachedNormalsRef))
            {
                return;
            }

            _cachedVertexPositions ??= new Vector3[total];
            if (_cachedVertexPositions.Length != total)
                _cachedVertexPositions = new Vector3[total];

            _cachedVertexNormals ??= new Vector3[total];
            if (_cachedVertexNormals.Length != total)
                _cachedVertexNormals = new Vector3[total];

            for (int i = 0; i < total; i++)
            {
                int x = i % Constants.TERRAIN_SIZE;
                int y = i / Constants.TERRAIN_SIZE;

                float z = heightMap[i].R * 1.5f;
                if (terrainWall != null && (terrainWall[i] & Client.Data.ATT.TWFlags.Height) != 0)
                    z += SpecialHeight;

                _cachedVertexPositions[i] = new Vector3(x * Constants.TERRAIN_SCALE, y * Constants.TERRAIN_SCALE, z);

                if (normals == null || (uint)i >= (uint)normals.Length)
                {
                    _cachedVertexNormals[i] = Vector3.UnitZ;
                }
                else
                {
                    var n = normals[i];
                    _cachedVertexNormals[i] = n.LengthSquared() < 1e-6f ? Vector3.UnitZ : n;
                }
            }

            _cachedHeightMapRef = heightMap;
            _cachedTerrainWallRef = terrainWall;
            _cachedNormalsRef = normals;
            _vertexCacheBuilt = true;
        }

        private void EnsureLightCache()
        {
            int total = Constants.TERRAIN_SIZE * Constants.TERRAIN_SIZE;
            var finalLightMap = _data.FinalLightMap;

            if (_lightCacheBuilt &&
                _cachedVertexBaseLights != null &&
                _cachedVertexBaseLights.Length == total &&
                ReferenceEquals(finalLightMap, _cachedFinalLightMapRef) &&
                Math.Abs(_cachedAmbientLight - AmbientLight) < 0.0001f)
            {
                return;
            }

            _cachedVertexBaseLights ??= new Color[total];
            if (_cachedVertexBaseLights.Length != total)
                _cachedVertexBaseLights = new Color[total];

            if (finalLightMap == null || finalLightMap.Length < total)
            {
                // Match old behavior: if FinalLightMap is missing, BuildVertexLight returned Color.White (no ambient).
                for (int i = 0; i < total; i++)
                    _cachedVertexBaseLights[i] = Color.White;

                // Ambient is ignored in this fallback, but track it to avoid rebuilding every frame.
                _cachedAmbientLight = AmbientLight;
            }
            else
            {
                float ambient = AmbientLight * 255f;
                for (int i = 0; i < total; i++)
                {
                    var c = finalLightMap[i];
                    float r = MathF.Min(c.R + ambient, 255f);
                    float g = MathF.Min(c.G + ambient, 255f);
                    float b = MathF.Min(c.B + ambient, 255f);
                    _cachedVertexBaseLights[i] = new Color((byte)r, (byte)g, (byte)b, (byte)255);
                }

                _cachedAmbientLight = AmbientLight;
            }

            _cachedFinalLightMapRef = finalLightMap;
            _lightCacheBuilt = true;
        }

        public void PrepareFirstDrawResources()
        {
            if (_graphicsDevice == null || _data.HeightMap == null || Camera.Instance == null)
                return;

            EnsureVertexCache();
            EnsureLightCache();
            _hasLodTransitions = BuildVisibleLodGrid();
            PrepareWaterCausticsRenderResources();

            if (ShouldPrepareIndexBatching())
            {
                EnsureTerrainVertexBuffers();
                return;
            }

            // Allocate only a modest streaming capacity. Buffers grow on demand during later
            // frames rather than reserving the maximum terrain batch in the first draw.
            EnsureTerrainStreamBuffer(alphaLayer: false, InitialTileBatchVerts);
            EnsureTerrainStreamBuffer(alphaLayer: true, InitialTileBatchVerts);
        }

        public async Task PrepareFirstDrawResourcesAsync(string phaseName)
        {
            if (_graphicsDevice == null || _data.HeightMap == null || Camera.Instance == null)
                return;

            // Position/normal/light cache generation is CPU-only and the world is still hidden.
            // Running it on a worker keeps the loading screen responsive.
            await Task.Run(() =>
            {
                EnsureVertexCache();
                EnsureLightCache();
            }).ConfigureAwait(false);

            await MuGame.YieldToNextFrameAsync(
                $"{phaseName}.Visibility",
                MainThreadDispatcher.WorkPriority.High);
            _hasLodTransitions = BuildVisibleLodGrid();
            PrepareWaterCausticsRenderResources();

            if (ShouldPrepareIndexBatching())
            {
                await PrepareTerrainVertexBuffersAsync(phaseName);
                return;
            }

            await MuGame.YieldToNextFrameAsync(
                $"{phaseName}.StreamBuffers",
                MainThreadDispatcher.WorkPriority.High);
            EnsureTerrainStreamBuffer(alphaLayer: false, InitialTileBatchVerts);
            EnsureTerrainStreamBuffer(alphaLayer: true, InitialTileBatchVerts);
        }

        private bool ShouldPrepareIndexBatching()
        {
            if (!EnableTerrainIndexBatching || !PreferIndexBatching || !Constants.ENABLE_TERRAIN_GPU_LIGHTING ||
                !Constants.ENABLE_DYNAMIC_LIGHTING_SHADER)
            {
                return false;
            }

            return SupportsProceduralTerrainUv(GraphicsManager.Instance.DynamicLightingEffect);
        }

        public void Draw()
        {
            if (_graphicsDevice == null) return;
            if (_data.HeightMap == null) return;
            if (Camera.Instance == null) return;
            ResetMetrics();
            _grassRenderer.ResetMetrics();

            _useDynamicLightingShader = Constants.ENABLE_TERRAIN_GPU_LIGHTING &&
                                        Constants.ENABLE_DYNAMIC_LIGHTING_SHADER &&
                                        GraphicsManager.Instance.DynamicLightingEffect != null;
            _useTerrainIndexBatching = false;

            EnsureVertexCache();
            EnsureLightCache();
            _hasLodTransitions = BuildVisibleLodGrid();

            if (_useDynamicLightingShader)
            {
                var effect = GraphicsManager.Instance.DynamicLightingEffect;

                // Enable index batching only when the shader supports procedural terrain UVs.
                if (EnableTerrainIndexBatching && PreferIndexBatching && SupportsProceduralTerrainUv(effect))
                {
                    EnsureTerrainVertexBuffers();
                    _useTerrainIndexBatching = _terrainVertexBufferBase != null &&
                                               !_terrainVertexBufferBase.IsDisposed &&
                                               _terrainVertexBufferAlpha != null &&
                                               !_terrainVertexBufferAlpha.IsDisposed;
                }

                BeginPersistentTerrainIndexPass();

                if (effect != null)
                {
                    effect.CurrentTechnique = effect.Techniques["DynamicLighting"];
                }
                if (!ConfigureDynamicLightingEffect())
                {
                    // Whole-terrain silent skip: log it (throttled) instead of going dark quietly.
                    int configFrame = MuGame.FrameIndex;
                    if (configFrame - _lastTerrainSkippedLog >= 300)
                    {
                        _lastTerrainSkippedLog = configFrame;
                        MuGame.AppLoggerFactory?.CreateLogger("TerrainRenderer")?.LogWarning(
                            "Black-frame probe: terrain effect configuration failed on frame {Frame}, whole terrain skipped.",
                            configFrame);
                    }

                    return;
                }
            }
            else
            {
                var effect = GraphicsManager.Instance.BasicEffect3D;
                if (effect == null) return; // Check for effect

                effect.Projection = Camera.Instance.Projection;
                effect.View = Camera.Instance.View;
                effect.World = Matrix.Identity;
                // Configure alpha blending to match custom shader behavior
                effect.Alpha = 1.0f;
                effect.VertexColorEnabled = true;
            }

            var previousRasterizer = _graphicsDevice.RasterizerState;
            var previousDepth = _graphicsDevice.DepthStencilState;
            var previousBlend = _graphicsDevice.BlendState;
            var previousSampler = _graphicsDevice.SamplerStates[0];

            try
            {
                // LOD skirts were the only visible fragments when a leaked cull state
                // rejected the horizontal surface. Terrain is intentionally two-sided.
                _graphicsDevice.RasterizerState = RasterizerState.CullNone;
                _graphicsDevice.DepthStencilState = DepthStencilState.Default;
                _graphicsDevice.BlendState = BlendState.Opaque;
                _graphicsDevice.SamplerStates[0] = SamplerState.LinearWrap;
                _lastBlendState = BlendState.Opaque;

                _activeWaterCausticsTexture = ResolveWaterCausticsTexture();

                if (_useTerrainIndexBatching && _usingPersistentTerrainIndexCache)
                {
                    RenderPersistentDynamicTerrainTiles();
                }
                else
                {
                    foreach (var block in _visibility.VisibleBlocks)
                    {
                        if (block?.IsVisible == true)
                        {
                            RenderTerrainBlock(
                                block.Xi,
                                block.Yi,
                                _visibility.LodSteps[block.LODLevel],
                                block); // Pass block for hierarchical culling
                        }
                    }
                }

                if (_useTerrainIndexBatching)
                {
                    if (_usingPersistentTerrainIndexCache)
                    {
                        DrawPersistentTerrainIndexBatches();
                    }
                    else
                    {
                        FlushAllTileIndexBatches();
                        CompletePersistentTerrainIndexPass();
                    }

                    // LOD-transition and map-edge tiles intentionally fall back to explicit UVs.
                    FlushAllTileBatches();
                }
                else
                {
                    FlushAllTileBatches();
                }

                FlushWaterCaustics();

                // The original Atlans terrain pass does not render standard grass.
                if (!IsAnimatedWaterWorld)
                    _grassRenderer.Draw();
            }
            finally
            {
                _graphicsDevice.SetVertexBuffer(null);
                _graphicsDevice.Indices = null;
                _graphicsDevice.BlendState = previousBlend;
                _graphicsDevice.DepthStencilState = previousDepth;
                _graphicsDevice.RasterizerState = previousRasterizer;
                _graphicsDevice.SamplerStates[0] = previousSampler;
            }

            LogTerrainBlackFrameProbe();
        }

        /// <summary>
        /// Black-frame diagnostics for terrain-only blackouts (objects/HUD unaffected).
        /// Case A (zero visible blocks) points at culling; case B (blocks visible but
        /// zero draw calls submitted) points at the draw stage (skipped tiles, missing
        /// technique/textures). Both throttled; allocate nothing unless they fire.
        /// </summary>
        private void LogTerrainBlackFrameProbe()
        {
            int frame = MuGame.FrameIndex;
            int visible = _visibility?.VisibleBlocks?.Count ?? -1;

            if (visible == 0 && frame - _lastTerrainEmptyLog >= 300)
            {
                _lastTerrainEmptyLog = frame;
                Vector3 cam = Camera.Instance.Position;
                MuGame.AppLoggerFactory?.CreateLogger("TerrainRenderer")?.LogWarning(
                    "Black-frame probe: terrain culling left zero visible blocks on frame {Frame} (camera {X:F0},{Y:F0},{Z:F0}).",
                    frame, cam.X, cam.Y, cam.Z);
            }
            else if (visible > 0 && DrawCalls == 0 && frame - _lastTerrainSkippedLog >= 300)
            {
                _lastTerrainSkippedLog = frame;
                MuGame.AppLoggerFactory?.CreateLogger("TerrainRenderer")?.LogWarning(
                    "Black-frame probe: {VisibleBlocks} terrain blocks visible but zero draw calls submitted on frame {Frame}.",
                    visible, frame);
            }
            else if (visible > 0 && DrawnTiles == 0 && frame - _lastTerrainSkippedLog >= 300)
            {
                _lastTerrainSkippedLog = frame;
                MuGame.AppLoggerFactory?.CreateLogger("TerrainRenderer")?.LogWarning(
                    "Black-frame probe: {VisibleBlocks} terrain blocks visible but zero ground tiles drawn on frame {Frame} ({Skipped} skipped, no texture).",
                    visible, frame, SkippedTilesNoTexture);
            }
            else if (visible > 0 && SkippedTilesNoTexture > 0 && SkippedTilesNoTexture >= DrawnTiles &&
                     frame - _lastTerrainSkippedLog >= 300)
            {
                // Partial skip: most ground tiles dropped for missing textures while a
                // few still drew (props/edges). In a city the ground is 1-2 textures,
                // so losing the dominant one looks like the whole terrain going black.
                _lastTerrainSkippedLog = frame;
                MuGame.AppLoggerFactory?.CreateLogger("TerrainRenderer")?.LogWarning(
                    "Black-frame probe: terrain mostly skipped on frame {Frame} ({Skipped} skipped, no texture, {Drawn} drawn).",
                    frame, SkippedTilesNoTexture, DrawnTiles);
            }

            // Cliff detector: similar block coverage as last frame but drawn tiles
            // collapsed to under a quarter. Catches partial blackouts from any cause
            // (skips, batch loss, culling glitch) regardless of absolute counts.
            // Teleports change both numbers, so they do not trigger this.
            int prevVisible = _prevFrameVisibleBlocks;
            int prevDrawn = _prevFrameDrawnTiles;
            _prevFrameVisibleBlocks = visible;
            _prevFrameDrawnTiles = DrawnTiles;
            if (visible > 0 && prevVisible > 0 && prevDrawn > 100 &&
                Math.Abs(visible - prevVisible) <= Math.Max(4, prevVisible / 2) &&
                DrawnTiles * 4 < prevDrawn &&
                frame - _lastTerrainCliffLog >= 300)
            {
                _lastTerrainCliffLog = frame;
                var cam = Camera.Instance.Position;
                MuGame.AppLoggerFactory?.CreateLogger("TerrainRenderer")?.LogWarning(
                    "Black-frame probe: terrain tile cliff on frame {Frame} (blocks {PrevBlocks}->{Blocks}, drawn tiles {PrevDrawn}->{Drawn}, skipped {Skipped}, camera {X:F0},{Y:F0},{Z:F0}).",
                    frame, prevVisible, visible, prevDrawn, DrawnTiles, SkippedTilesNoTexture,
                    cam.X, cam.Y, cam.Z);
            }
        }

        private void ResetMetrics()
        {
            DrawCalls = 0;
            DrawnTriangles = 0;
            DrawnBlocks = 0;
            DrawnTiles = 0;
            SkippedTilesNoTexture = 0;
            DrawnCells = 0;
            IndexedCells = 0;
            StreamedCells = 0;
            IndexUploads = 0;
            VertexUploads = 0;
            UploadedIndices = 0;
            UploadedVertices = 0;
            UsedIndexBatching = false;
            LastUploadedDynamicLights = 0;
            _waterCausticsVertexCount = 0;
            _activeWaterCausticsTexture = null;

            // Reset state tracking for new frame
            _lastBoundTexture = null;
            _lastBlendState = null;
            ResetBatchTracking();
        }

        private void ResetBatchTracking()
        {
            for (int i = 0; i < _activeTileIndexTextures.Count; i++)
            {
                int t = _activeTileIndexTextures[i];
                _tileIndexCounts[t] = 0;
                _tileIndexActive[t] = false;
            }
            _activeTileIndexTextures.Clear();

            for (int i = 0; i < _activeTileAlphaIndexTextures.Count; i++)
            {
                int t = _activeTileAlphaIndexTextures[i];
                _tileAlphaIndexCounts[t] = 0;
                _tileAlphaIndexActive[t] = false;
            }
            _activeTileAlphaIndexTextures.Clear();

            for (int i = 0; i < _activeTileBatchTextures.Count; i++)
            {
                int t = _activeTileBatchTextures[i];
                _tileBatchCounts[t] = 0;
                _tileBatchActive[t] = false;
            }
            _activeTileBatchTextures.Clear();

            for (int i = 0; i < _activeTileAlphaBatchTextures.Count; i++)
            {
                int t = _activeTileAlphaBatchTextures[i];
                _tileAlphaCounts[t] = 0;
                _tileAlphaBatchActive[t] = false;
            }
            _activeTileAlphaBatchTextures.Clear();
        }


        private void BeginPersistentTerrainIndexPass()
        {
            _buildingPersistentTerrainIndexCache = false;
            _usingPersistentTerrainIndexCache = false;

            if (!_useTerrainIndexBatching || !EnablePersistentTerrainIndexCache)
                return;

            int visibilityVersion = _visibility?.Version ?? int.MinValue;
            int textureStateHash = ComputeTerrainTextureStateHash();
            if (IsPersistentTerrainIndexCacheUsable(visibilityVersion, textureStateHash))
            {
                _usingPersistentTerrainIndexCache = true;
                return;
            }

            _buildingPersistentTerrainIndexCache = true;
            Array.Clear(_persistentTileIndexCounts, 0, _persistentTileIndexCounts.Length);
            Array.Clear(_persistentTileAlphaIndexCounts, 0, _persistentTileAlphaIndexCounts.Length);
            _persistentActiveTileTextures.Clear();
            _persistentActiveTileAlphaTextures.Clear();
            _persistentDynamicTileCommands.Clear();
            _buildingIndexedCellCount = 0;
            _buildingStreamedCellCount = 0;
            _buildingDrawnCellCount = 0;
            _buildingDrawnBlockCount = 0;
        }

        private bool IsPersistentTerrainIndexCacheUsable(int visibilityVersion, int textureStateHash)
        {
            if (_persistentTerrainIndexVersion != visibilityVersion ||
                !ReferenceEquals(_persistentLayer1MapRef, _data.Mapping.Layer1) ||
                !ReferenceEquals(_persistentLayer2MapRef, _data.Mapping.Layer2) ||
                !ReferenceEquals(_persistentAlphaMapRef, _data.Mapping.Alpha) ||
                _persistentTextureStateHash != textureStateHash ||
                _terrainVertexBufferBase == null || _terrainVertexBufferBase.IsDisposed ||
                _terrainVertexBufferAlpha == null || _terrainVertexBufferAlpha.IsDisposed)
            {
                return false;
            }

            for (int i = 0; i < _persistentActiveTileTextures.Count; i++)
            {
                DynamicIndexBuffer buffer = _persistentTileIndexBuffers[_persistentActiveTileTextures[i]];
                if (buffer == null || buffer.IsDisposed)
                    return false;
            }

            for (int i = 0; i < _persistentActiveTileAlphaTextures.Count; i++)
            {
                DynamicIndexBuffer buffer = _persistentTileAlphaIndexBuffers[_persistentActiveTileAlphaTextures[i]];
                if (buffer == null || buffer.IsDisposed)
                    return false;
            }

            return true;
        }

        private int ComputeTerrainTextureStateHash()
        {
            unchecked
            {
                int hash = 17;
                Texture2D[] textures = _data.Textures;
                if (textures == null)
                    return hash;

                hash = hash * 31 + textures.Length;
                for (int i = 0; i < textures.Length; i++)
                {
                    Texture2D texture = textures[i];
                    hash = hash * 31 + (texture == null || texture.IsDisposed
                        ? 0
                        : RuntimeHelpers.GetHashCode(texture));
                }

                return hash;
            }
        }

        private void CompletePersistentTerrainIndexPass()
        {
            if (!_buildingPersistentTerrainIndexCache)
                return;

            _persistentTerrainIndexVersion = _visibility?.Version ?? int.MinValue;
            _persistentLayer1MapRef = _data.Mapping.Layer1;
            _persistentLayer2MapRef = _data.Mapping.Layer2;
            _persistentAlphaMapRef = _data.Mapping.Alpha;
            _persistentTextureStateHash = ComputeTerrainTextureStateHash();
            _persistentIndexedCellCount = _buildingIndexedCellCount;
            _persistentStreamedCellCount = _buildingStreamedCellCount;
            _persistentDrawnCellCount = _buildingDrawnCellCount;
            _persistentDrawnBlockCount = _buildingDrawnBlockCount;
            _buildingPersistentTerrainIndexCache = false;
            _usingPersistentTerrainIndexCache = true;
        }

        private void DrawPersistentTerrainIndexBatches()
        {
            for (int i = 0; i < _persistentActiveTileTextures.Count; i++)
            {
                int textureIndex = _persistentActiveTileTextures[i];
                DrawTerrainIndexedBuffer(
                    textureIndex,
                    alphaLayer: false,
                    _persistentTileIndexBuffers[textureIndex],
                    _persistentTileIndexCounts[textureIndex]);
            }

            for (int i = 0; i < _persistentActiveTileAlphaTextures.Count; i++)
            {
                int textureIndex = _persistentActiveTileAlphaTextures[i];
                DrawTerrainIndexedBuffer(
                    textureIndex,
                    alphaLayer: true,
                    _persistentTileAlphaIndexBuffers[textureIndex],
                    _persistentTileAlphaIndexCounts[textureIndex]);
            }

            if (_lastBlendState != BlendState.Opaque)
            {
                _graphicsDevice.BlendState = BlendState.Opaque;
                _lastBlendState = BlendState.Opaque;
            }
        }

        private void RenderPersistentDynamicTerrainTiles()
        {
            if (!_isShadowPass)
            {
                DrawnBlocks = _persistentDrawnBlockCount;
                DrawnCells = _persistentDrawnCellCount;
                IndexedCells = _persistentIndexedCellCount;
                StreamedCells = _persistentStreamedCellCount;
                UsedIndexBatching = _persistentIndexedCellCount > 0;
            }

            _replayingPersistentTerrainPlan = true;
            try
            {
                for (int i = 0; i < _persistentDynamicTileCommands.Count; i++)
                {
                    TerrainTileCommand command = _persistentDynamicTileCommands[i];
                    if (_isShadowPass && command.CausticsOnly)
                        continue;

                    RenderTerrainTile(
                        command.X,
                        command.Y,
                        command.Lod,
                        command.Lod,
                        command.EdgeMask);
                }
            }
            finally
            {
                _replayingPersistentTerrainPlan = false;
            }
        }

        private bool BuildVisibleLodGrid()
        {
            if (_visibility?.VisibleBlocks == null || _visibleBlockLod == null || _visibleBlockLod.Length == 0)
                return false;

            // Both color and shadow passes call this each frame. Reuse the result when the
            // visibility manager hasn't rebuilt its block set since the last grid build.
            int version = _visibility.Version;
            if (version == _lodGridBuiltVersion)
                return _lodGridBuiltResult;

            Array.Fill(_visibleBlockLod, (sbyte)-1);

            foreach (var block in _visibility.VisibleBlocks)
            {
                if (block == null || !block.IsVisible)
                    continue;

                int bx = block.Xi / BlockSize;
                int by = block.Yi / BlockSize;
                if ((uint)bx >= (uint)_blocksPerSide || (uint)by >= (uint)_blocksPerSide)
                    continue;

                int lodStep = _visibility.LodSteps[block.LODLevel];
                _visibleBlockLod[by * _blocksPerSide + bx] = (sbyte)lodStep;
            }

            foreach (var block in _visibility.VisibleBlocks)
            {
                if (block == null || !block.IsVisible)
                    continue;

                int lodStep = _visibility.LodSteps[block.LODLevel];
                if (lodStep <= 1)
                    continue;

                int bx = block.Xi / BlockSize;
                int by = block.Yi / BlockSize;
                if (HasLowerLodNeighbor(bx, by, lodStep))
                {
                    _lodGridBuiltVersion = version;
                    _lodGridBuiltResult = true;
                    return true;
                }
            }

            _lodGridBuiltVersion = version;
            _lodGridBuiltResult = false;
            return false;
        }

        private bool VisibleBlocksTouchTerrainEdge()
        {
            if (_visibility?.VisibleBlocks == null)
                return false;

            int edgeStart = Constants.TERRAIN_SIZE - BlockSize;
            foreach (var block in _visibility.VisibleBlocks)
            {
                if (block?.IsVisible != true)
                    continue;

                if (block.Xi >= edgeStart || block.Yi >= edgeStart)
                    return true;
            }

            return false;
        }

        private bool HasLowerLodNeighbor(int bx, int by, int lodStep)
        {
            return IsLowerLodNeighbor(bx, by - 1, lodStep) ||
                   IsLowerLodNeighbor(bx, by + 1, lodStep) ||
                   IsLowerLodNeighbor(bx - 1, by, lodStep) ||
                   IsLowerLodNeighbor(bx + 1, by, lodStep);
        }

        private bool IsLowerLodNeighbor(int bx, int by, int lodStep)
        {
            if ((uint)bx >= (uint)_blocksPerSide || (uint)by >= (uint)_blocksPerSide)
                return false;

            int neighbor = _visibleBlockLod[by * _blocksPerSide + bx];
            return neighbor > 0 && neighbor < lodStep;
        }

        private byte GetLodEdgeMask(TerrainBlock block, int lodStep)
        {
            if (block == null || lodStep <= 1 || !_hasLodTransitions)
                return 0;

            int bx = block.Xi / BlockSize;
            int by = block.Yi / BlockSize;
            byte mask = 0;

            if (IsLowerLodNeighbor(bx, by - 1, lodStep)) mask |= LodEdgeNorth;
            if (IsLowerLodNeighbor(bx, by + 1, lodStep)) mask |= LodEdgeSouth;
            if (IsLowerLodNeighbor(bx - 1, by, lodStep)) mask |= LodEdgeWest;
            if (IsLowerLodNeighbor(bx + 1, by, lodStep)) mask |= LodEdgeEast;

            return mask;
        }

        public void DrawShadowMap(Effect shadowEffect, Matrix lightViewProjection)
        {
            if (_graphicsDevice == null || _data.HeightMap == null || shadowEffect == null)
                return;

            EnsureVertexCache();
            EnsureLightCache();

            BlendState prevBlend = _graphicsDevice.BlendState;
            DepthStencilState prevDepth = _graphicsDevice.DepthStencilState;
            RasterizerState prevRaster = _graphicsDevice.RasterizerState;
            EffectTechnique prevTechnique = shadowEffect.CurrentTechnique;
            bool prevUseDynamic = _useDynamicLightingShader;
            bool prevUseIndex = _useTerrainIndexBatching;
            Texture2D prevLastBoundTexture = _lastBoundTexture;

            try
            {
                // Reset texture binding to ensure correct textures are bound for shadow pass.
                _lastBoundTexture = null;
                _useDynamicLightingShader = true;
                _useTerrainIndexBatching = false;
                _graphicsDevice.BlendState = BlendState.Opaque;
                _graphicsDevice.DepthStencilState = DepthStencilState.Default;
                _graphicsDevice.RasterizerState = RasterizerState.CullCounterClockwise;

                shadowEffect.CurrentTechnique = shadowEffect.Techniques["ShadowCaster"];
                shadowEffect.Parameters["World"]?.SetValue(Matrix.Identity);
                shadowEffect.Parameters["LightViewProjection"]?.SetValue(lightViewProjection);
                shadowEffect.Parameters["ShadowMapTexelSize"]?.SetValue(
                    new Vector2(1f / Constants.SHADOW_MAP_SIZE, 1f / Constants.SHADOW_MAP_SIZE));
                shadowEffect.Parameters["ShadowBias"]?.SetValue(Constants.SHADOW_BIAS);
                shadowEffect.Parameters["ShadowNormalBias"]?.SetValue(Constants.SHADOW_NORMAL_BIAS);
                shadowEffect.Parameters["Alpha"]?.SetValue(1f);
                shadowEffect.Parameters["TextureCoordinateOffset"]?.SetValue(Vector2.Zero);

                // Optional fast path for terrain: static VB + resident per-texture index buffers.
                _hasLodTransitions = BuildVisibleLodGrid();
                if (EnableTerrainIndexBatching && PreferIndexBatching && SupportsProceduralTerrainUv(shadowEffect))
                {
                    EnsureTerrainVertexBuffers();
                    _useTerrainIndexBatching = _terrainVertexBufferBase != null &&
                                               !_terrainVertexBufferBase.IsDisposed &&
                                               _terrainVertexBufferAlpha != null &&
                                               !_terrainVertexBufferAlpha.IsDisposed;
                }

                ResetBatchTracking();
                BeginPersistentTerrainIndexPass();

                shadowEffect.Parameters["UseProceduralTerrainUV"]?.SetValue(
                    _useTerrainIndexBatching ? 1.0f : 0.0f);
                shadowEffect.Parameters["IsWaterTexture"]?.SetValue(0.0f);
                shadowEffect.Parameters["TerrainUvScale"]?.SetValue(Vector2.Zero);
                shadowEffect.Parameters["WaterFlowDirection"]?.SetValue(_waterFlowDir);
                shadowEffect.Parameters["WaterTotal"]?.SetValue(_waterTotal);
                shadowEffect.Parameters["DistortionAmplitude"]?.SetValue(DistortionAmplitude);
                shadowEffect.Parameters["DistortionFrequency"]?.SetValue(DistortionFrequency);

                _activeWaterCausticsTexture = ResolveWaterCausticsTexture();
                _isShadowPass = true;

                if (_useTerrainIndexBatching && _usingPersistentTerrainIndexCache)
                {
                    RenderPersistentDynamicTerrainTiles();
                }
                else
                {
                    foreach (TerrainBlock block in _visibility.VisibleBlocks)
                    {
                        if (block?.IsVisible == true)
                        {
                            RenderTerrainBlock(
                                block.Xi,
                                block.Yi,
                                _visibility.LodSteps[block.LODLevel],
                                block);
                        }
                    }
                }

                if (_useTerrainIndexBatching)
                {
                    if (_usingPersistentTerrainIndexCache)
                    {
                        DrawPersistentTerrainIndexBatches();
                    }
                    else
                    {
                        FlushAllTileIndexBatches();
                        CompletePersistentTerrainIndexPass();
                    }

                    FlushAllTileBatches();
                }
                else
                {
                    FlushAllTileBatches();
                }
            }
            finally
            {
                _isShadowPass = false;
                _replayingPersistentTerrainPlan = false;

                // Restore for object shadow casters and for the following color pass.
                shadowEffect.Parameters["UseProceduralTerrainUV"]?.SetValue(0.0f);
                shadowEffect.Parameters["IsWaterTexture"]?.SetValue(0.0f);

                _graphicsDevice.SetVertexBuffer(null);
                _graphicsDevice.Indices = null;
                _useDynamicLightingShader = prevUseDynamic;
                _useTerrainIndexBatching = prevUseIndex;
                _lastBoundTexture = prevLastBoundTexture;
                _graphicsDevice.BlendState = prevBlend;
                _graphicsDevice.DepthStencilState = prevDepth;
                _graphicsDevice.RasterizerState = prevRaster;
                shadowEffect.CurrentTechnique = prevTechnique;
            }
        }

        private bool ConfigureDynamicLightingEffect()
        {
            var effect = GraphicsManager.Instance.DynamicLightingEffect;
            if (effect == null || Camera.Instance == null)
                return false;

            Matrix world = Matrix.Identity;
            effect.Parameters["World"]?.SetValue(world);
            effect.Parameters["WorldViewProjection"]?.SetValue(world * Camera.Instance.ViewProjection);
            // Use terrain-specific technique, with a reduced variant for integrated GPUs.
            string preferredTechnique = Constants.OPTIMIZE_FOR_INTEGRATED_GPU
                ? "DynamicLighting_Terrain_Low"
                : "DynamicLighting_Terrain";
            var terrainTechnique = TryGetTechnique(effect, preferredTechnique) ??
                                   TryGetTechnique(effect, "DynamicLighting_Terrain");
            if (terrainTechnique == null)
                return false;

            effect.CurrentTechnique = terrainTechnique;
            effect.Parameters["UseProceduralTerrainUV"]?.SetValue(_useTerrainIndexBatching ? 1.0f : 0.0f);
            effect.Parameters["IsWaterTexture"]?.SetValue(0.0f);
            effect.Parameters["TerrainUvScale"]?.SetValue(Vector2.Zero);
            effect.Parameters["WaterFlowDirection"]?.SetValue(_waterFlowDir);
            effect.Parameters["WaterTotal"]?.SetValue(_waterTotal);
            effect.Parameters["DistortionAmplitude"]?.SetValue(DistortionAmplitude);
            effect.Parameters["DistortionFrequency"]?.SetValue(DistortionFrequency);
            effect.Parameters["TerrainDynamicIntensityScale"]?.SetValue(1.0f);
            effect.Parameters["Alpha"]?.SetValue(1f);
            effect.Parameters["TextureCoordinateOffset"]?.SetValue(Vector2.Zero);
            effect.Parameters["DebugLightingAreas"]?.SetValue(Constants.DEBUG_LIGHTING_AREAS ? 1.0f : 0.0f);

            // Ambient and sun are ignored when using baked vertex lighting, but keep sane defaults.
            float ambientValue = AmbientLight * SunCycleManager.AmbientMultiplier;
            effect.Parameters["AmbientLight"]?.SetValue(new Vector3(ambientValue));
            // GlobalLightMultiplier dims vertex color lighting for day-night cycle
            effect.Parameters["GlobalLightMultiplier"]?.SetValue(SunCycleManager.AmbientMultiplier);
            Vector3 sunDir = GraphicsManager.Instance.ShadowMapRenderer?.LightDirection ?? Constants.SUN_DIRECTION;
            if (sunDir.LengthSquared() < 0.0001f)
                sunDir = new Vector3(1f, 0f, -0.6f);
            sunDir = Vector3.Normalize(sunDir);
            bool sunEnabled = Constants.SUN_ENABLED;
            effect.Parameters["SunDirection"]?.SetValue(sunDir);
            effect.Parameters["SunColor"]?.SetValue(new Vector3(1f, 0.95f, 0.85f));
            effect.Parameters["SunStrength"]?.SetValue(sunEnabled ? SunCycleManager.GetEffectiveSunStrength() : 0f);
            effect.Parameters["ShadowStrength"]?.SetValue(sunEnabled ? SunCycleManager.GetEffectiveShadowStrength() : 0f);

            // World-scoped linear fog (disabled unless a world opts in)
            Graphics.WorldFog.Apply(effect, Camera.Instance.Position);

            // Apply global shadow map parameters when available so terrain receives the same shadows as objects
            GraphicsManager.Instance.ShadowMapRenderer?.ApplyShadowParameters(effect);
            UploadDynamicLights(effect);
            return true;
        }

        private void UploadDynamicLights(Effect effect)
        {
            if (!Constants.ENABLE_DYNAMIC_LIGHTS)
            {
                _dynamicLightUploader.Clear(effect);
                LastUploadedDynamicLights = 0;
                return;
            }

            var visibleLights = _lightManager.VisibleLights;
            if (visibleLights == null || visibleLights.Count == 0)
            {
                _dynamicLightUploader.Clear(effect);
                LastUploadedDynamicLights = 0;
                return;
            }

            int maxLights = Math.Min(
                DynamicLightGpuUploader.ResolveEffectCapacity(effect, DynamicLightArrayCapacity),
                Constants.OPTIMIZE_FOR_INTEGRATED_GPU ? 16 : DynamicLightArrayCapacity);
            Vector2 focusPos = Camera.Instance != null
                ? new Vector2(Camera.Instance.Target.X, Camera.Instance.Target.Y)
                : Vector2.Zero;
            float focusRadius = ResolveTerrainDynamicLightFocusRadius();

            LastUploadedDynamicLights = _dynamicLightUploader.Upload(
                effect,
                visibleLights,
                focusPos,
                maxLights,
                focusRadius,
                _lightManager.VisibleLightsVersion,
                cacheCellSize: 128f);
        }

        private static float ResolveTerrainDynamicLightFocusRadius()
        {
            var camera = Camera.Instance;
            if (camera == null)
                return Constants.MAX_CAMERA_DISTANCE;

            float cameraDistance = Vector3.Distance(camera.Position, camera.Target);
            if (!float.IsFinite(cameraDistance) || cameraDistance <= 0f)
                return Constants.MAX_CAMERA_DISTANCE;

            return MathHelper.Clamp(cameraDistance * 1.9f, 1200f, 4200f);
        }

        private void RenderTerrainBlock(int xi, int yi, int lodStep, TerrainBlock block = null)
        {
            DrawnBlocks++;
            if (_buildingPersistentTerrainIndexCache)
                _buildingDrawnBlockCount++;

            byte edgeMask = block != null
                ? GetLodEdgeMask(block, lodStep)
                : (byte)0;

            if (_lastBlendState != BlendState.Opaque)
            {
                _graphicsDevice.BlendState = BlendState.Opaque;
                _lastBlendState = BlendState.Opaque;
            }

            for (int i = 0; i < 4; i += lodStep)
            {
                for (int j = 0; j < 4; j += lodStep)
                {
                    // Use hierarchical culling if available
                    bool shouldRender = true;
                    if (block != null && !block.FullyVisible)
                    {
                        if (lodStep >= 4)
                        {
                            // Rendering the whole 4x4 block as one tile: render if any subtile is visible
                            shouldRender = block.VisibleTileCount > 0;
                        }
                        else if (lodStep == 2)
                        {
                            // Rendering a 2x2 super-tile: render if any of the 4 subtiles are visible
                            bool anyVisible = false;
                            for (int dy = 0; dy < 2 && !anyVisible; dy++)
                            {
                                for (int dx = 0; dx < 2; dx++)
                                {
                                    int ii = i + dy;
                                    int jj = j + dx;
                                    int idx = ii * 4 + jj;
                                    if (block.IsTileVisible(idx))
                                    {
                                        anyVisible = true;
                                        break;
                                    }
                                }
                            }
                            shouldRender = anyVisible;
                        }
                        else
                        {
                            // lodStep == 1: per-tile visibility
                            int tileIdx = i * 4 + j;
                            shouldRender = block.IsTileVisible(tileIdx);
                        }
                    }

                    if (shouldRender)
                    {
                        byte tileEdgeMask = 0;
                        if (edgeMask != 0)
                        {
                            if ((edgeMask & LodEdgeNorth) != 0 && i == 0) tileEdgeMask |= LodEdgeNorth;
                            if ((edgeMask & LodEdgeSouth) != 0 && (i + lodStep) >= BlockSize) tileEdgeMask |= LodEdgeSouth;
                            if ((edgeMask & LodEdgeWest) != 0 && j == 0) tileEdgeMask |= LodEdgeWest;
                            if ((edgeMask & LodEdgeEast) != 0 && (j + lodStep) >= BlockSize) tileEdgeMask |= LodEdgeEast;
                        }

                        RenderTerrainTile(
                            xi + j,
                            yi + i,
                            (float)lodStep,
                            lodStep,
                            tileEdgeMask);
                    }
                }
            }
        }

        private void RenderTerrainTile(int xi, int yi, float lodFactor, int lodInt, byte edgeMask = 0)
        {
            if (_data.Attributes == null || _data.Attributes.TerrainWall == null)
                return;
            if (!_replayingPersistentTerrainPlan)
                DrawnCells++;
            if (_buildingPersistentTerrainIndexCache)
                _buildingDrawnCellCount++;

            if (!HasAnyGroundInTile(xi, yi, lodInt))
                return;

            int x1 = xi;
            int y1 = yi;
            int x2 = xi + lodInt;
            int y2 = yi;
            int x3 = xi + lodInt;
            int y3 = yi + lodInt;
            int x4 = xi;
            int y4 = yi + lodInt;

            int i1 = GetTerrainIndexRepeat(x1, y1);
            int i2 = GetTerrainIndexRepeat(x2, y2);
            int i3 = GetTerrainIndexRepeat(x3, y3);
            int i4 = GetTerrainIndexRepeat(x4, y4);

            bool canUseIndexedTile = _useTerrainIndexBatching &&
                                     edgeMask == 0 &&
                                     x1 >= 0 && x1 < Constants.TERRAIN_SIZE &&
                                     y1 >= 0 && y1 < Constants.TERRAIN_SIZE &&
                                     x2 >= 0 && x2 < Constants.TERRAIN_SIZE &&
                                     y2 >= 0 && y2 < Constants.TERRAIN_SIZE &&
                                     x3 >= 0 && x3 < Constants.TERRAIN_SIZE &&
                                     y3 >= 0 && y3 < Constants.TERRAIN_SIZE &&
                                     x4 >= 0 && x4 < Constants.TERRAIN_SIZE &&
                                     y4 >= 0 && y4 < Constants.TERRAIN_SIZE;

            if (canUseIndexedTile)
            {
                byte a1i = i1 < _data.Mapping.Alpha.Length ? _data.Mapping.Alpha[i1] : (byte)0;
                byte a2i = i2 < _data.Mapping.Alpha.Length ? _data.Mapping.Alpha[i2] : (byte)0;
                byte a3i = i3 < _data.Mapping.Alpha.Length ? _data.Mapping.Alpha[i3] : (byte)0;
                byte a4i = i4 < _data.Mapping.Alpha.Length ? _data.Mapping.Alpha[i4] : (byte)0;

                bool isOpaqueIndex = (a1i & a2i & a3i & a4i) == 255;
                bool hasAlphaIndex = (a1i | a2i | a3i | a4i) != 0;

                ushort u1 = (ushort)i1;
                ushort u2 = (ushort)i2;
                ushort u3 = (ushort)i3;
                ushort u4 = (ushort)i4;

                UsedIndexBatching = true;
                if (!_replayingPersistentTerrainPlan)
                    IndexedCells++;
                if (_buildingPersistentTerrainIndexCache)
                    _buildingIndexedCellCount++;

                bool isIndexedAtlansCaustics = IsAtlansCausticsTile(i1, hasAlphaIndex);
                if (_buildingPersistentTerrainIndexCache && isIndexedAtlansCaustics)
                {
                    _persistentDynamicTileCommands.Add(
                        new TerrainTileCommand(xi, yi, lodInt, edgeMask, causticsOnly: true));
                }

                if (_usingPersistentTerrainIndexCache)
                {
                    // Geometry and material grouping are already resident on the GPU for the
                    // current visibility version. Atlans caustics remain a dynamic overlay and
                    // still collect their animated vertices in the color pass.
                    if (!_isShadowPass && isIndexedAtlansCaustics)
                    {
                        AddWaterCausticsTile(
                            xi,
                            yi,
                            lodFactor,
                            _cachedVertexPositions[i1],
                            _cachedVertexPositions[i2],
                            _cachedVertexPositions[i3],
                            _cachedVertexPositions[i4],
                            a1i,
                            a2i,
                            a3i,
                            a4i);
                    }

                    return;
                }

                if (isIndexedAtlansCaustics)
                {
                    // Atlans layer 2 index 5 is not a regular terrain texture. The original
                    // client keeps layer 1 as the seabed and redraws the same geometry with
                    // an additive wt00-wt31 caustics frame.
                    RenderTextureIndexed(_data.Mapping.Layer1[i1], u1, u2, u3, u4, alphaLayer: false);
                    if (!_isShadowPass)
                    {
                        AddWaterCausticsTile(
                        xi,
                        yi,
                        lodFactor,
                        _cachedVertexPositions[i1],
                        _cachedVertexPositions[i2],
                        _cachedVertexPositions[i3],
                        _cachedVertexPositions[i4],
                        a1i,
                        a2i,
                        a3i,
                        a4i);
                    }
                }
                else if (isOpaqueIndex)
                {
                    RenderTextureIndexed(_data.Mapping.Layer2[i1], u1, u2, u3, u4, alphaLayer: false);
                }
                else
                {
                    RenderTextureIndexed(_data.Mapping.Layer1[i1], u1, u2, u3, u4, alphaLayer: false);
                    if (hasAlphaIndex)
                    {
                        RenderTextureIndexed(_data.Mapping.Layer2[i1], u1, u2, u3, u4, alphaLayer: true);
                    }
                }

                return;
            }

            if (!_replayingPersistentTerrainPlan)
                StreamedCells++;
            if (_buildingPersistentTerrainIndexCache)
            {
                _buildingStreamedCellCount++;
                _persistentDynamicTileCommands.Add(
                    new TerrainTileCommand(xi, yi, lodInt, edgeMask, causticsOnly: false));
            }

            PrepareTileVertices(x1, y1, x2, y2, x3, y3, x4, y4, i1, i2, i3, i4);
            PrepareTileLights(i1, i2, i3, i4);
            bool renderSkirts = edgeMask != 0;
            if (renderSkirts)
            {
                _tempTerrainLightsBase[0] = _tempTerrainLights[0];
                _tempTerrainLightsBase[1] = _tempTerrainLights[1];
                _tempTerrainLightsBase[2] = _tempTerrainLights[2];
                _tempTerrainLightsBase[3] = _tempTerrainLights[3];
            }

            byte a1 = i1 < _data.Mapping.Alpha.Length ? _data.Mapping.Alpha[i1] : (byte)0;
            byte a2 = i2 < _data.Mapping.Alpha.Length ? _data.Mapping.Alpha[i2] : (byte)0;
            byte a3 = i3 < _data.Mapping.Alpha.Length ? _data.Mapping.Alpha[i3] : (byte)0;
            byte a4 = i4 < _data.Mapping.Alpha.Length ? _data.Mapping.Alpha[i4] : (byte)0;

            bool isOpaque = (a1 & a2 & a3 & a4) == 255;
            bool hasAlpha = (a1 | a2 | a3 | a4) != 0;
            bool isAtlansCausticsTile = IsAtlansCausticsTile(i1, hasAlpha);
            int baseTextureIndex = isAtlansCausticsTile || !isOpaque
                ? _data.Mapping.Layer1[i1]
                : _data.Mapping.Layer2[i1];
            int alphaTextureIndex = _data.Mapping.Layer2[i1];

            RenderTexture(baseTextureIndex, xi, yi, lodFactor, useBatch: true, alphaLayer: false);

            if (isAtlansCausticsTile)
            {
                if (!_isShadowPass)
                {
                    AddWaterCausticsTile(
                    xi,
                    yi,
                    lodFactor,
                    _tempTerrainVertex[0],
                    _tempTerrainVertex[1],
                    _tempTerrainVertex[2],
                    _tempTerrainVertex[3],
                    a1,
                    a2,
                    a3,
                    a4);
                }
            }
            else if (!isOpaque && hasAlpha)
            {
                ApplyAlphaToLights(a1, a2, a3, a4);
                RenderTexture(alphaTextureIndex, xi, yi, lodFactor, useBatch: true, alphaLayer: true);
            }

            if (renderSkirts)
            {
                _tempTerrainLights[0] = _tempTerrainLightsBase[0];
                _tempTerrainLights[1] = _tempTerrainLightsBase[1];
                _tempTerrainLights[2] = _tempTerrainLightsBase[2];
                _tempTerrainLights[3] = _tempTerrainLightsBase[3];

                RenderTileSkirts(xi, yi, lodFactor, edgeMask, baseTextureIndex, alphaLayer: false);
                if (!isAtlansCausticsTile && !isOpaque && hasAlpha)
                {
                    ApplyAlphaToLights(a1, a2, a3, a4);
                    RenderTileSkirts(xi, yi, lodFactor, edgeMask, alphaTextureIndex, alphaLayer: true);
                }
            }

        }

        private void PrepareWaterCausticsRenderResources()
        {
            if (!IsAnimatedWaterWorld || ResolveWaterCausticsTexture() == null)
                return;

            EnsureWaterCausticsEffect();
            EnsureWaterCausticsStreamBuffer(InitialTileBatchVerts);
        }

        private bool IsAtlansCausticsTile(int terrainIndex, bool hasMappingAlpha)
        {
            return IsAnimatedWaterWorld &&
                   hasMappingAlpha &&
                   _data.Mapping.Layer2 != null &&
                   (uint)terrainIndex < (uint)_data.Mapping.Layer2.Length &&
                   _data.Mapping.Layer2[terrainIndex] == AtlansCausticsLayer;
        }

        private Texture2D ResolveWaterCausticsTexture()
        {
            if (!IsAnimatedWaterWorld)
                return null;

            Texture2D[] frames = _data.WaterCausticsTextures;
            if (frames == null || frames.Length == 0)
                return null;

            int frameCount = Math.Min(frames.Length, WaterCausticsFrameCount);
            int startFrame = _waterCausticsFrame % frameCount;
            for (int offset = 0; offset < frameCount; offset++)
            {
                Texture2D texture = frames[(startFrame + offset) % frameCount];
                if (texture != null && !texture.IsDisposed)
                    return texture;
            }

            return null;
        }

        private void AddWaterCausticsTile(
            float tileX,
            float tileY,
            float lodScale,
            Vector3 vertex0,
            Vector3 vertex1,
            Vector3 vertex2,
            Vector3 vertex3,
            byte alpha0,
            byte alpha1,
            byte alpha2,
            byte alpha3)
        {
            Texture2D texture = _activeWaterCausticsTexture;
            if (texture == null || texture.IsDisposed)
                return;

            int requiredVertexCount = _waterCausticsVertexCount + 6;
            EnsureWaterCausticsVertexCapacity(requiredVertexCount);

            // SourceMain uses FaceTexture(..., water: false, scale: true):
            // one terrain tile covers 16 source-image pixels and UVs remain world-anchored.
            float tileWidth = 16f / texture.Width;
            float tileHeight = 16f / texture.Height;
            float u0 = tileX * tileWidth;
            float v0 = tileY * tileHeight;
            float u1 = u0 + tileWidth * lodScale;
            float v1 = v0 + tileHeight * lodScale;

            Vector2 uv0 = new(u0, v0);
            Vector2 uv1 = new(u1, v0);
            Vector2 uv2 = new(u1, v1);
            Vector2 uv3 = new(u0, v1);

            // Mapping alpha is an RGB intensity multiplier in the original additive pass.
            Color color0 = new(alpha0, alpha0, alpha0, byte.MaxValue);
            Color color1 = new(alpha1, alpha1, alpha1, byte.MaxValue);
            Color color2 = new(alpha2, alpha2, alpha2, byte.MaxValue);
            Color color3 = new(alpha3, alpha3, alpha3, byte.MaxValue);

            int destination = _waterCausticsVertexCount;
            _waterCausticsVertices[destination + 0] = new VertexPositionColorTexture(vertex0, color0, uv0);
            _waterCausticsVertices[destination + 1] = new VertexPositionColorTexture(vertex1, color1, uv1);
            _waterCausticsVertices[destination + 2] = new VertexPositionColorTexture(vertex2, color2, uv2);
            _waterCausticsVertices[destination + 3] = new VertexPositionColorTexture(vertex2, color2, uv2);
            _waterCausticsVertices[destination + 4] = new VertexPositionColorTexture(vertex3, color3, uv3);
            _waterCausticsVertices[destination + 5] = new VertexPositionColorTexture(vertex0, color0, uv0);
            _waterCausticsVertexCount = requiredVertexCount;
        }

        private void EnsureWaterCausticsVertexCapacity(int requiredVertexCount)
        {
            if (_waterCausticsVertices.Length >= requiredVertexCount)
                return;

            int capacity = GetExpandedCapacity(
                requiredVertexCount,
                InitialTileBatchVerts,
                MaxWaterCausticsVertices);
            Array.Resize(ref _waterCausticsVertices, capacity);
        }

        private DynamicVertexBuffer EnsureWaterCausticsStreamBuffer(int requiredVertexCount)
        {
            if (_waterCausticsStreamBuffer == null ||
                _waterCausticsStreamBuffer.IsDisposed ||
                _waterCausticsStreamBuffer.VertexCount < requiredVertexCount)
            {
                _waterCausticsStreamBuffer?.Dispose();
                int capacity = GetExpandedCapacity(
                    requiredVertexCount,
                    InitialTileBatchVerts,
                    MaxWaterCausticsVertices);
                _waterCausticsStreamBuffer = new DynamicVertexBuffer(
                    _graphicsDevice,
                    VertexPositionColorTexture.VertexDeclaration,
                    capacity,
                    BufferUsage.WriteOnly);
            }

            return _waterCausticsStreamBuffer;
        }

        private BasicEffect EnsureWaterCausticsEffect()
        {
            if (_waterCausticsEffect == null || _waterCausticsEffect.IsDisposed)
            {
                _waterCausticsEffect = new BasicEffect(_graphicsDevice)
                {
                    TextureEnabled = true,
                    VertexColorEnabled = true,
                    LightingEnabled = false,
                    FogEnabled = false,
                    Alpha = 1f
                };
            }

            return _waterCausticsEffect;
        }

        private void FlushWaterCaustics()
        {
            int vertexCount = _waterCausticsVertexCount;
            Texture2D texture = _activeWaterCausticsTexture;
            if (vertexCount == 0 || texture == null || texture.IsDisposed)
                return;

            BlendState previousBlend = _graphicsDevice.BlendState;
            DepthStencilState previousDepth = _graphicsDevice.DepthStencilState;
            RasterizerState previousRasterizer = _graphicsDevice.RasterizerState;
            SamplerState previousSampler = _graphicsDevice.SamplerStates[0];

            try
            {
                BasicEffect effect = EnsureWaterCausticsEffect();
                effect.World = Matrix.Identity;
                effect.View = Camera.Instance.View;
                effect.Projection = Camera.Instance.Projection;
                effect.Texture = texture;

                _graphicsDevice.BlendState = WaterCausticsBlendState;
                _graphicsDevice.DepthStencilState = WaterCausticsDepthState;
                _graphicsDevice.RasterizerState = WaterCausticsRasterizerState;
                _graphicsDevice.SamplerStates[0] = SamplerState.LinearWrap;
                _graphicsDevice.Indices = null;

                DynamicVertexBuffer vertexBuffer = EnsureWaterCausticsStreamBuffer(vertexCount);
                vertexBuffer.SetData(
                    _waterCausticsVertices,
                    0,
                    vertexCount,
                    SetDataOptions.Discard);
                VertexUploads++;
                UploadedVertices += vertexCount;
                _graphicsDevice.SetVertexBuffer(vertexBuffer);

                int triangleCount = vertexCount / 3;
                foreach (EffectPass pass in effect.CurrentTechnique.Passes)
                {
                    pass.Apply();
                    _graphicsDevice.DrawPrimitives(
                        PrimitiveType.TriangleList,
                        0,
                        triangleCount);
                }

                DrawCalls++;
                DrawnTriangles += triangleCount;
            }
            finally
            {
                _graphicsDevice.SetVertexBuffer(null);
                _graphicsDevice.Indices = null;
                _graphicsDevice.BlendState = previousBlend;
                _graphicsDevice.DepthStencilState = previousDepth;
                _graphicsDevice.RasterizerState = previousRasterizer;
                _graphicsDevice.SamplerStates[0] = previousSampler;
                _waterCausticsVertexCount = 0;

                // The caustics pass binds a texture outside the regular terrain batching state.
                _lastBoundTexture = null;
                _lastBlendState = null;
            }
        }

        private void RenderTileSkirts(int xi, int yi, float lodFactor, byte edgeMask, int textureIndex, bool alphaLayer)
        {
            if (edgeMask == 0)
                return;
            if (textureIndex < 0 || textureIndex >= _data.Textures.Length || _data.Textures[textureIndex] == null)
                return;

            var uvScale = _tileUvScale[textureIndex];
            float baseW = uvScale.X;
            float baseH = uvScale.Y;
            float suf = xi * baseW;
            float svf = yi * baseH;
            float uvW = baseW * lodFactor;
            float uvH = baseH * lodFactor;

            Vector2 uv0 = new Vector2(suf, svf);
            Vector2 uv1 = new Vector2(suf + uvW, svf);
            Vector2 uv2 = new Vector2(suf + uvW, svf + uvH);
            Vector2 uv3 = new Vector2(suf, svf + uvH);

            Vector3 v0 = _tempTerrainVertex[0];
            Vector3 v1 = _tempTerrainVertex[1];
            Vector3 v2 = _tempTerrainVertex[2];
            Vector3 v3 = _tempTerrainVertex[3];

            Vector3 v0b = v0; v0b.Z -= LodSkirtDepth;
            Vector3 v1b = v1; v1b.Z -= LodSkirtDepth;
            Vector3 v2b = v2; v2b.Z -= LodSkirtDepth;
            Vector3 v3b = v3; v3b.Z -= LodSkirtDepth;

            Color c0 = _tempTerrainLights[0];
            Color c1 = _tempTerrainLights[1];
            Color c2 = _tempTerrainLights[2];
            Color c3 = _tempTerrainLights[3];

            Vector3 n0 = _tempTerrainNormals[0];
            Vector3 n1 = _tempTerrainNormals[1];
            Vector3 n2 = _tempTerrainNormals[2];
            Vector3 n3 = _tempTerrainNormals[3];

            if ((edgeMask & LodEdgeNorth) != 0)
                AddSkirtQuad(textureIndex, v0, v1, v0b, v1b, c0, c1, n0, n1, uv0, uv1, alphaLayer);
            if ((edgeMask & LodEdgeSouth) != 0)
                AddSkirtQuad(textureIndex, v3, v2, v3b, v2b, c3, c2, n3, n2, uv3, uv2, alphaLayer);
            if ((edgeMask & LodEdgeWest) != 0)
                AddSkirtQuad(textureIndex, v0, v3, v0b, v3b, c0, c3, n0, n3, uv0, uv3, alphaLayer);
            if ((edgeMask & LodEdgeEast) != 0)
                AddSkirtQuad(textureIndex, v1, v2, v1b, v2b, c1, c2, n1, n2, uv1, uv2, alphaLayer);
        }

        private void AddSkirtQuad(
            int textureIndex,
            Vector3 topA,
            Vector3 topB,
            Vector3 bottomA,
            Vector3 bottomB,
            Color colorA,
            Color colorB,
            Vector3 normalA,
            Vector3 normalB,
            Vector2 uvA,
            Vector2 uvB,
            bool alphaLayer)
        {
            _terrainVertices[0] = new TerrainVertexPositionColorNormalTexture(topA, colorA, normalA, uvA);
            _terrainVertices[1] = new TerrainVertexPositionColorNormalTexture(topB, colorB, normalB, uvB);
            _terrainVertices[2] = new TerrainVertexPositionColorNormalTexture(bottomB, colorB, normalB, uvB);
            _terrainVertices[3] = new TerrainVertexPositionColorNormalTexture(bottomB, colorB, normalB, uvB);
            _terrainVertices[4] = new TerrainVertexPositionColorNormalTexture(bottomA, colorA, normalA, uvA);
            _terrainVertices[5] = new TerrainVertexPositionColorNormalTexture(topA, colorA, normalA, uvA);

            AddTileToBatch(textureIndex, _terrainVertices, alphaLayer);

            _terrainVertices[0] = new TerrainVertexPositionColorNormalTexture(topA, colorA, normalA, uvA);
            _terrainVertices[1] = new TerrainVertexPositionColorNormalTexture(bottomA, colorA, normalA, uvA);
            _terrainVertices[2] = new TerrainVertexPositionColorNormalTexture(bottomB, colorB, normalB, uvB);
            _terrainVertices[3] = new TerrainVertexPositionColorNormalTexture(bottomB, colorB, normalB, uvB);
            _terrainVertices[4] = new TerrainVertexPositionColorNormalTexture(topB, colorB, normalB, uvB);
            _terrainVertices[5] = new TerrainVertexPositionColorNormalTexture(topA, colorA, normalA, uvA);

            AddTileToBatch(textureIndex, _terrainVertices, alphaLayer);
        }

        private bool HasAnyGroundInTile(int xi, int yi, int lodInt)
        {
            var terrainWall = _data.Attributes?.TerrainWall;
            if (terrainWall == null)
                return true;

            if (lodInt <= 1)
            {
                int idx = GetTerrainIndex(xi, yi);
                return (uint)idx < (uint)terrainWall.Length &&
                       !terrainWall[idx].HasFlag(Client.Data.ATT.TWFlags.NoGround);
            }

            int max = Constants.TERRAIN_SIZE - 1;
            int endX = Math.Min(xi + lodInt - 1, max);
            int endY = Math.Min(yi + lodInt - 1, max);

            for (int y = yi; y <= endY; y++)
            {
                int row = y * Constants.TERRAIN_SIZE;
                for (int x = xi; x <= endX; x++)
                {
                    int idx = row + x;
                    if ((uint)idx < (uint)terrainWall.Length &&
                        !terrainWall[idx].HasFlag(Client.Data.ATT.TWFlags.NoGround))
                        return true;
                }
            }

            return false;
        }

        private void RenderTexture(int textureIndex, float xf, float yf, float lodScale, bool useBatch, bool alphaLayer = false)
        {
            if (textureIndex < 0 || textureIndex >= _data.Textures.Length || _data.Textures[textureIndex] == null)
                return;

            var texture = _data.Textures[textureIndex];
            var uvScale = _tileUvScale[textureIndex];
            float baseW = uvScale.X;
            float baseH = uvScale.Y;
            float suf = xf * baseW;
            float svf = yf * baseH;
            float uvW = baseW * lodScale;
            float uvH = baseH * lodScale;

            bool noFlow = WaterSpeed == 0f && DistortionAmplitude == 0f;
            if (textureIndex == 5 && !noFlow) // Water with flow/distortion
            {
                var flowOff = _waterFlowDir * _waterTotal;
                float wrapPeriod = 2 * (float)Math.PI / Math.Max(0.01f, DistortionFrequency);
                float waterPhase = _waterTotal % wrapPeriod;

                _terrainTextureCoords[0].X = suf + flowOff.X + (float)Math.Sin((suf + waterPhase) * DistortionFrequency) * DistortionAmplitude;
                _terrainTextureCoords[0].Y = svf + flowOff.Y + (float)Math.Cos((svf + waterPhase) * DistortionFrequency) * DistortionAmplitude;
                _terrainTextureCoords[1].X = suf + uvW + flowOff.X + (float)Math.Sin((suf + uvW + waterPhase) * DistortionFrequency) * DistortionAmplitude;
                _terrainTextureCoords[1].Y = svf + flowOff.Y + (float)Math.Cos((svf + waterPhase) * DistortionFrequency) * DistortionAmplitude;
                _terrainTextureCoords[2].X = _terrainTextureCoords[1].X;
                _terrainTextureCoords[2].Y = svf + uvH + flowOff.Y + (float)Math.Cos((svf + uvH + waterPhase) * DistortionFrequency) * DistortionAmplitude;
                _terrainTextureCoords[3].X = _terrainTextureCoords[0].X;
                _terrainTextureCoords[3].Y = _terrainTextureCoords[2].Y;
            }
            else
            {
                _terrainTextureCoords[0] = new Vector2(suf, svf);
                _terrainTextureCoords[1] = new Vector2(suf + uvW, svf);
                _terrainTextureCoords[2] = new Vector2(suf + uvW, svf + uvH);
                _terrainTextureCoords[3] = new Vector2(suf, svf + uvH);
            }

            _terrainVertices[0] = new TerrainVertexPositionColorNormalTexture(_tempTerrainVertex[0], _tempTerrainLights[0], _tempTerrainNormals[0], _terrainTextureCoords[0]);
            _terrainVertices[1] = new TerrainVertexPositionColorNormalTexture(_tempTerrainVertex[1], _tempTerrainLights[1], _tempTerrainNormals[1], _terrainTextureCoords[1]);
            _terrainVertices[2] = new TerrainVertexPositionColorNormalTexture(_tempTerrainVertex[2], _tempTerrainLights[2], _tempTerrainNormals[2], _terrainTextureCoords[2]);
            _terrainVertices[3] = new TerrainVertexPositionColorNormalTexture(_tempTerrainVertex[2], _tempTerrainLights[2], _tempTerrainNormals[2], _terrainTextureCoords[2]);
            _terrainVertices[4] = new TerrainVertexPositionColorNormalTexture(_tempTerrainVertex[3], _tempTerrainLights[3], _tempTerrainNormals[3], _terrainTextureCoords[3]);
            _terrainVertices[5] = new TerrainVertexPositionColorNormalTexture(_tempTerrainVertex[0], _tempTerrainLights[0], _tempTerrainNormals[0], _terrainTextureCoords[0]);

            if (useBatch)
            {
                AddTileToBatch(textureIndex, _terrainVertices, alphaLayer);
            }
            else
            {
                if (_useDynamicLightingShader)
                {
                    var effect = GraphicsManager.Instance.DynamicLightingEffect;
                    if (effect == null) return;

                    effect.Parameters["DiffuseTexture"]?.SetValue(texture);
                    foreach (var pass in effect.CurrentTechnique.Passes)
                    {
                        pass.Apply();
                        _graphicsDevice.DrawUserPrimitives(PrimitiveType.TriangleList, _terrainVertices, 0, 2);
                    }
                }
                else
                {
                    var basicEffect = GraphicsManager.Instance.BasicEffect3D;
                    if (basicEffect == null) return;

                    basicEffect.Texture = texture;
                    for (int i = 0; i < 6; i++)
                    {
                        var v = _terrainVertices[i];
                        _fallbackTileBuffer[i] = new VertexPositionColorTexture(v.Position, v.Color, v.TextureCoordinate);
                    }
                    foreach (var pass in basicEffect.CurrentTechnique.Passes)
                    {
                        pass.Apply();
                        _graphicsDevice.DrawUserPrimitives(PrimitiveType.TriangleList, _fallbackTileBuffer, 0, 2);
                    }
                }
                DrawCalls++;
                DrawnTriangles += 2;
            }
        }

        private void PrepareTileVertices(
            int x1, int y1,
            int x2, int y2,
            int x3, int y3,
            int x4, int y4,
            int i1, int i2, int i3, int i4)
        {
            SetTempVertexFromSample(0, x1, y1, i1);
            SetTempVertexFromSample(1, x2, y2, i2);
            SetTempVertexFromSample(2, x3, y3, i3);
            SetTempVertexFromSample(3, x4, y4, i4);
        }

        private void SetTempVertexFromSample(int slot, int worldTileX, int worldTileY, int sampleIndex)
        {
            float z;
            Vector3 normal = Vector3.UnitZ;

            if (_cachedVertexPositions != null &&
                _cachedVertexNormals != null &&
                (uint)sampleIndex < (uint)_cachedVertexPositions.Length &&
                (uint)sampleIndex < (uint)_cachedVertexNormals.Length)
            {
                z = _cachedVertexPositions[sampleIndex].Z;
                normal = _cachedVertexNormals[sampleIndex];
            }
            else
            {
                z = 0f;
                if (_data.HeightMap != null && (uint)sampleIndex < (uint)_data.HeightMap.Length)
                {
                    z = _data.HeightMap[sampleIndex].R * 1.5f;
                    var terrainWall = _data.Attributes?.TerrainWall;
                    if (terrainWall != null &&
                        (uint)sampleIndex < (uint)terrainWall.Length &&
                        (terrainWall[sampleIndex] & Client.Data.ATT.TWFlags.Height) != 0)
                    {
                        z += SpecialHeight;
                    }
                }
            }

            _tempTerrainVertex[slot] = new Vector3(
                worldTileX * Constants.TERRAIN_SCALE,
                worldTileY * Constants.TERRAIN_SCALE,
                z);
            _tempTerrainNormals[slot] = normal;
        }

        private void PrepareTileLights(int i1, int i2, int i3, int i4)
        {
            _tempTerrainLights[0] = GetVertexBaseLight(i1);
            _tempTerrainLights[1] = GetVertexBaseLight(i2);
            _tempTerrainLights[2] = GetVertexBaseLight(i3);
            _tempTerrainLights[3] = GetVertexBaseLight(i4);

            // CPU fallback path: bake dynamic lights into vertex color (shader path does dynamic lighting on GPU).
            if (!_useDynamicLightingShader && _data.FinalLightMap != null)
            {
                ApplyCpuDynamicLight(ref _tempTerrainLights[0], _tempTerrainVertex[0]);
                ApplyCpuDynamicLight(ref _tempTerrainLights[1], _tempTerrainVertex[1]);
                ApplyCpuDynamicLight(ref _tempTerrainLights[2], _tempTerrainVertex[2]);
                ApplyCpuDynamicLight(ref _tempTerrainLights[3], _tempTerrainVertex[3]);
            }
        }

        private Color GetVertexBaseLight(int index)
        {
            if (_cachedVertexBaseLights != null && (uint)index < (uint)_cachedVertexBaseLights.Length)
                return _cachedVertexBaseLights[index];

            var finalLightMap = _data.FinalLightMap;
            if (finalLightMap == null)
                return Color.White;

            Vector3 baseColor = Vector3.Zero;
            if ((uint)index < (uint)finalLightMap.Length)
            {
                var c = finalLightMap[index];
                baseColor = new Vector3(c.R, c.G, c.B);
            }

            baseColor += new Vector3(AmbientLight * 255f);
            baseColor = Vector3.Clamp(baseColor, Vector3.Zero, new Vector3(255f));
            return new Color((int)baseColor.X, (int)baseColor.Y, (int)baseColor.Z);
        }

        private void ApplyCpuDynamicLight(ref Color baseLight, Vector3 pos)
        {
            var dyn = _lightManager.EvaluateVisibleDynamicLight(new Vector2(pos.X, pos.Y));
            if (dyn.LengthSquared() < 0.0001f)
                return;

            // Clamp to 0-255 range to prevent underflow with negative lights (shadows)
            float r = MathHelper.Clamp(baseLight.R + dyn.X, 0f, 255f);
            float g = MathHelper.Clamp(baseLight.G + dyn.Y, 0f, 255f);
            float b = MathHelper.Clamp(baseLight.B + dyn.Z, 0f, 255f);

            baseLight = new Color((byte)r, (byte)g, (byte)b, baseLight.A);
        }

        private void ApplyAlphaToLights(byte a1, byte a2, byte a3, byte a4)
        {
            // Keep lighting intact; alpha controls only the blend factor in shader.
            _tempTerrainLights[0].A = a1;
            _tempTerrainLights[1].A = a2;
            _tempTerrainLights[2].A = a3;
            _tempTerrainLights[3].A = a4;
        }

        private static bool SupportsProceduralTerrainUv(Effect effect)
        {
            if (effect?.Parameters == null)
                return false;

            return effect.Parameters["UseProceduralTerrainUV"] != null &&
                   effect.Parameters["TerrainUvScale"] != null &&
                   effect.Parameters["IsWaterTexture"] != null;
        }

        private static EffectTechnique TryGetTechnique(Effect effect, string name)
        {
            if (effect == null || string.IsNullOrEmpty(name))
                return null;

            var techniques = effect.Techniques;
            for (int i = 0; i < techniques.Count; i++)
            {
                var technique = techniques[i];
                if (string.Equals(technique.Name, name, StringComparison.Ordinal))
                    return technique;
            }

            return null;
        }

        private byte[] GetValidTerrainAlphaMap()
        {
            int total = Constants.TERRAIN_SIZE * Constants.TERRAIN_SIZE;
            var alphaMap = _data.Mapping.Alpha;
            return alphaMap != null && alphaMap.Length >= total ? alphaMap : null;
        }

        private bool NeedsTerrainVertexBufferRebuild(byte[] alphaMap)
        {
            return
                !_terrainVertexBuffersBuilt ||
                _terrainVertexBufferBase == null ||
                _terrainVertexBufferBase.IsDisposed ||
                _terrainVertexBufferAlpha == null ||
                _terrainVertexBufferAlpha.IsDisposed ||
                !ReferenceEquals(_terrainBuffersHeightMapRef, _cachedHeightMapRef) ||
                !ReferenceEquals(_terrainBuffersTerrainWallRef, _cachedTerrainWallRef) ||
                !ReferenceEquals(_terrainBuffersNormalsRef, _cachedNormalsRef) ||
                !ReferenceEquals(_terrainBuffersFinalLightMapRef, _cachedFinalLightMapRef) ||
                Math.Abs(_terrainBuffersAmbientLight - _cachedAmbientLight) > 0.0001f ||
                !ReferenceEquals(_terrainBuffersAlphaMapRef, alphaMap);
        }

        private TerrainVertexPositionColorNormalTexture[] BuildTerrainVertexUploadData(
            bool alphaLayer,
            byte[] alphaMap,
            int total)
        {
            var vertices = ArrayPool<TerrainVertexPositionColorNormalTexture>.Shared.Rent(total);
            for (int i = 0; i < total; i++)
            {
                Color baseLight = _cachedVertexBaseLights[i];
                Color color = alphaLayer
                    ? new Color(baseLight.R, baseLight.G, baseLight.B, alphaMap != null ? alphaMap[i] : (byte)0)
                    : baseLight;

                vertices[i] = new TerrainVertexPositionColorNormalTexture(
                    _cachedVertexPositions[i],
                    color,
                    _cachedVertexNormals[i],
                    Vector2.Zero);
            }

            return vertices;
        }

        private void UploadTerrainVertexBuffer(
            bool alphaLayer,
            TerrainVertexPositionColorNormalTexture[] vertices,
            int total)
        {
            var buffer = new VertexBuffer(
                _graphicsDevice,
                TerrainVertexPositionColorNormalTexture.VertexDeclaration,
                total,
                BufferUsage.WriteOnly);
            buffer.SetData(vertices, 0, total);

            if (alphaLayer)
            {
                _terrainVertexBufferAlpha?.Dispose();
                _terrainVertexBufferAlpha = buffer;
            }
            else
            {
                _terrainVertexBufferBase?.Dispose();
                _terrainVertexBufferBase = buffer;
            }
        }

        private void CompleteTerrainVertexBufferBuild(byte[] alphaMap)
        {
            _terrainBuffersHeightMapRef = _cachedHeightMapRef;
            _terrainBuffersTerrainWallRef = _cachedTerrainWallRef;
            _terrainBuffersNormalsRef = _cachedNormalsRef;
            _terrainBuffersFinalLightMapRef = _cachedFinalLightMapRef;
            _terrainBuffersAmbientLight = _cachedAmbientLight;
            _terrainBuffersAlphaMapRef = alphaMap;
            _terrainVertexBuffersBuilt = true;
            _persistentTerrainIndexVersion = int.MinValue;
            _usingPersistentTerrainIndexCache = false;
            _buildingPersistentTerrainIndexCache = false;
        }

        private async Task PrepareTerrainVertexBuffersAsync(string phaseName)
        {
            int total = Constants.TERRAIN_SIZE * Constants.TERRAIN_SIZE;
            if (_cachedVertexPositions == null || _cachedVertexNormals == null || _cachedVertexBaseLights == null ||
                _cachedVertexPositions.Length < total || _cachedVertexNormals.Length < total ||
                _cachedVertexBaseLights.Length < total)
            {
                return;
            }

            byte[] alphaMap = GetValidTerrainAlphaMap();
            if (!NeedsTerrainVertexBufferRebuild(alphaMap))
                return;

            var baseVertices = await Task.Run(
                () => BuildTerrainVertexUploadData(false, alphaMap, total)).ConfigureAwait(false);
            try
            {
                await MuGame.YieldToNextFrameAsync(
                    $"{phaseName}.BaseVertexUpload",
                    MainThreadDispatcher.WorkPriority.High);
                UploadTerrainVertexBuffer(alphaLayer: false, baseVertices, total);
            }
            finally
            {
                ArrayPool<TerrainVertexPositionColorNormalTexture>.Shared.Return(baseVertices);
            }

            var alphaVertices = await Task.Run(
                () => BuildTerrainVertexUploadData(true, alphaMap, total)).ConfigureAwait(false);
            try
            {
                await MuGame.YieldToNextFrameAsync(
                    $"{phaseName}.AlphaVertexUpload",
                    MainThreadDispatcher.WorkPriority.High);
                UploadTerrainVertexBuffer(alphaLayer: true, alphaVertices, total);
            }
            finally
            {
                ArrayPool<TerrainVertexPositionColorNormalTexture>.Shared.Return(alphaVertices);
            }

            CompleteTerrainVertexBufferBuild(alphaMap);
        }

        private void EnsureTerrainVertexBuffers()
        {
            if (_graphicsDevice == null)
                return;

            EnsureVertexCache();
            EnsureLightCache();

            int total = Constants.TERRAIN_SIZE * Constants.TERRAIN_SIZE;
            if (_cachedVertexPositions == null || _cachedVertexNormals == null || _cachedVertexBaseLights == null ||
                _cachedVertexPositions.Length < total || _cachedVertexNormals.Length < total ||
                _cachedVertexBaseLights.Length < total)
            {
                return;
            }

            byte[] alphaMap = GetValidTerrainAlphaMap();
            if (!NeedsTerrainVertexBufferRebuild(alphaMap))
                return;

            var vertices = BuildTerrainVertexUploadData(false, alphaMap, total);
            try
            {
                UploadTerrainVertexBuffer(alphaLayer: false, vertices, total);
            }
            finally
            {
                ArrayPool<TerrainVertexPositionColorNormalTexture>.Shared.Return(vertices);
            }

            vertices = BuildTerrainVertexUploadData(true, alphaMap, total);
            try
            {
                UploadTerrainVertexBuffer(alphaLayer: true, vertices, total);
            }
            finally
            {
                ArrayPool<TerrainVertexPositionColorNormalTexture>.Shared.Return(vertices);
            }

            CompleteTerrainVertexBufferBuild(alphaMap);
        }

        private void RenderTextureIndexed(int textureIndex, ushort i1, ushort i2, ushort i3, ushort i4, bool alphaLayer)
        {
            if (textureIndex < 0 || textureIndex >= _data.Textures.Length || _data.Textures[textureIndex] == null)
                return;

            AddTileToIndexBatch(textureIndex, i1, i2, i3, i4, alphaLayer);
        }

        private void AddTileToIndexBatch(int texIndex, ushort i1, ushort i2, ushort i3, ushort i4, bool alphaLayer)
        {
            var counters = alphaLayer ? _tileAlphaIndexCounts : _tileIndexCounts;
            int dstOff = counters[texIndex];
            int required = dstOff + 6;

            if (!_buildingPersistentTerrainIndexCache && required > TileBatchIndices)
            {
                FlushSingleTextureIndexed(texIndex, alphaLayer);
                dstOff = 0;
                required = 6;
            }

            var batch = GetTileIndexBatchBuffer(texIndex, alphaLayer, required);
            TrackIndexTexture(texIndex, alphaLayer);

            batch[dstOff + 0] = i1;
            batch[dstOff + 1] = i2;
            batch[dstOff + 2] = i3;
            batch[dstOff + 3] = i3;
            batch[dstOff + 4] = i4;
            batch[dstOff + 5] = i1;
            counters[texIndex] = dstOff + 6;
        }

        private void FlushSingleTextureIndexed(int texIndex, bool alphaLayer)
        {
            int indexCount = alphaLayer ? _tileAlphaIndexCounts[texIndex] : _tileIndexCounts[texIndex];
            if (indexCount == 0)
                return;

            var batch = GetTileIndexBatchBuffer(texIndex, alphaLayer, indexCount);

            if (_buildingPersistentTerrainIndexCache)
            {
                DynamicIndexBuffer persistentBuffer = EnsurePersistentTerrainIndexBuffer(
                    texIndex,
                    alphaLayer,
                    indexCount);
                if (persistentBuffer != null)
                {
                    persistentBuffer.SetData(batch, 0, indexCount, SetDataOptions.Discard);
                    IndexUploads++;
                    UploadedIndices += indexCount;

                    if (alphaLayer)
                    {
                        _persistentTileAlphaIndexCounts[texIndex] = indexCount;
                        _persistentActiveTileAlphaTextures.Add(texIndex);
                    }
                    else
                    {
                        _persistentTileIndexCounts[texIndex] = indexCount;
                        _persistentActiveTileTextures.Add(texIndex);
                    }

                    DrawTerrainIndexedBuffer(texIndex, alphaLayer, persistentBuffer, indexCount);
                }
            }
            else
            {
                var indexBuffer = DynamicBufferPool.RentIndexBuffer(indexCount, prefer16Bit: true);
                if (indexBuffer != null)
                {
                    try
                    {
                        indexBuffer.SetData(batch, 0, indexCount, SetDataOptions.Discard);
                        IndexUploads++;
                        UploadedIndices += indexCount;
                        DrawTerrainIndexedBuffer(texIndex, alphaLayer, indexBuffer, indexCount);
                    }
                    finally
                    {
                        _graphicsDevice.SetVertexBuffer(null);
                        _graphicsDevice.Indices = null;
                        DynamicBufferPool.ReturnIndexBuffer(indexBuffer);
                    }
                }
            }

            if (alphaLayer)
                _tileAlphaIndexCounts[texIndex] = 0;
            else
                _tileIndexCounts[texIndex] = 0;
        }

        private DynamicIndexBuffer EnsurePersistentTerrainIndexBuffer(
            int textureIndex,
            bool alphaLayer,
            int requiredIndexCount)
        {
            DynamicIndexBuffer[] buffers = alphaLayer
                ? _persistentTileAlphaIndexBuffers
                : _persistentTileIndexBuffers;
            DynamicIndexBuffer buffer = buffers[textureIndex];

            if (buffer != null && !buffer.IsDisposed && buffer.IndexCount >= requiredIndexCount)
                return buffer;

            buffer?.Dispose();
            int capacity = GetExpandedCapacity(
                requiredIndexCount,
                TileBatchIndices,
                MaxPersistentTerrainIndices);
            buffer = new DynamicIndexBuffer(
                _graphicsDevice,
                IndexElementSize.SixteenBits,
                capacity,
                BufferUsage.WriteOnly);
            buffers[textureIndex] = buffer;
            return buffer;
        }

        private void DrawTerrainIndexedBuffer(
            int textureIndex,
            bool alphaLayer,
            IndexBuffer indexBuffer,
            int indexCount)
        {
            if (indexBuffer == null || indexBuffer.IsDisposed || indexCount <= 0)
                return;

            var texture = _data.Textures[textureIndex];
            if (texture == null || texture.IsDisposed)
                return;

            var effect = GraphicsManager.Instance.DynamicLightingEffect;
            if (effect == null || effect.CurrentTechnique == null)
                return;

            var blendState = alphaLayer ? BlendState.NonPremultiplied : BlendState.Opaque;
            if (_lastBlendState != blendState)
            {
                _graphicsDevice.BlendState = blendState;
                _lastBlendState = blendState;
            }

            if (_lastBoundTexture != texture)
            {
                effect.Parameters["DiffuseTexture"]?.SetValue(texture);
                _lastBoundTexture = texture;
            }

            effect.Parameters["TerrainUvScale"]?.SetValue(_tileUvScaleWorld[textureIndex]);
            bool noFlow = WaterSpeed == 0f && DistortionAmplitude == 0f;
            float isWater = textureIndex == 5 && !noFlow ? 1.0f : 0.0f;
            effect.Parameters["IsWaterTexture"]?.SetValue(isWater);

            var vertexBuffer = alphaLayer ? _terrainVertexBufferAlpha : _terrainVertexBufferBase;
            if (vertexBuffer == null || vertexBuffer.IsDisposed)
                return;

            _graphicsDevice.SetVertexBuffer(vertexBuffer);
            _graphicsDevice.Indices = indexBuffer;

            int primitiveCount = indexCount / 3;
            foreach (var pass in effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                _graphicsDevice.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, primitiveCount);
            }

            DrawCalls++;
            DrawnTriangles += primitiveCount;
        }

        private void FlushAllTileIndexBatches()
        {
            // First pass: render all opaque batches
            for (int i = 0; i < _activeTileIndexTextures.Count; i++)
            {
                int t = _activeTileIndexTextures[i];
                if (_tileIndexCounts[t] > 0)
                    FlushSingleTextureIndexed(t, alphaLayer: false);
                _tileIndexActive[t] = false;
            }
            _activeTileIndexTextures.Clear();

            // Second pass: render all alpha batches
            for (int i = 0; i < _activeTileAlphaIndexTextures.Count; i++)
            {
                int t = _activeTileAlphaIndexTextures[i];
                if (_tileAlphaIndexCounts[t] > 0)
                    FlushSingleTextureIndexed(t, alphaLayer: true);
                _tileAlphaIndexActive[t] = false;
            }
            _activeTileAlphaIndexTextures.Clear();

            if (_lastBlendState != BlendState.Opaque)
            {
                _graphicsDevice.BlendState = BlendState.Opaque;
                _lastBlendState = BlendState.Opaque;
            }
        }

        private void AddTileToBatch(int texIndex, TerrainVertexPositionColorNormalTexture[] verts, bool alphaLayer)
        {
            var counters = alphaLayer ? _tileAlphaCounts : _tileBatchCounts;
            int dstOff = counters[texIndex];
            if (dstOff + 6 > MaxTileBatchVerts)
            {
                FlushSingleTexture(texIndex, alphaLayer);
                dstOff = 0;
            }

            var batch = GetTileBatchBuffer(texIndex, alphaLayer, dstOff + 6);
            TrackVertexTexture(texIndex, alphaLayer);

            // Manual unroll for better performance than Array.Copy.
            batch[dstOff + 0] = verts[0];
            batch[dstOff + 1] = verts[1];
            batch[dstOff + 2] = verts[2];
            batch[dstOff + 3] = verts[3];
            batch[dstOff + 4] = verts[4];
            batch[dstOff + 5] = verts[5];
            counters[texIndex] = dstOff + 6;
        }

        private DynamicVertexBuffer EnsureTerrainStreamBuffer(bool alphaLayer, int requiredVertexCount)
        {
            DynamicVertexBuffer buffer = alphaLayer
                ? _terrainStreamBufferAlpha
                : _terrainStreamBufferOpaque;

            if (buffer == null || buffer.IsDisposed || buffer.VertexCount < requiredVertexCount)
            {
                buffer?.Dispose();
                // Grow with demand instead of reserving the worst-case terrain batch during
                // the first visible draw. This avoids multi-megabyte cold-start allocations.
                int capacity = GetExpandedCapacity(requiredVertexCount, InitialTileBatchVerts, MaxTileBatchVerts);
                buffer = new DynamicVertexBuffer(
                    _graphicsDevice,
                    TerrainVertexPositionColorNormalTexture.VertexDeclaration,
                    capacity,
                    BufferUsage.WriteOnly);

                if (alphaLayer)
                    _terrainStreamBufferAlpha = buffer;
                else
                    _terrainStreamBufferOpaque = buffer;
            }

            return buffer;
        }

        private void FlushSingleTexture(int texIndex, bool alphaLayer)
        {
            int vertCount = alphaLayer ? _tileAlphaCounts[texIndex] : _tileBatchCounts[texIndex];
            if (vertCount == 0)
                return;

            // Always clear the logical count, even when a texture is still loading.
            try
            {
                var batch = GetTileBatchBuffer(texIndex, alphaLayer, vertCount);
                var texture = _data.Textures[texIndex];
                if (texture == null || texture.IsDisposed)
                {
                    SkippedTilesNoTexture += vertCount / 6;
                    return;
                }

                var blendState = alphaLayer ? BlendState.NonPremultiplied : BlendState.Opaque;
                if (_lastBlendState != blendState)
                {
                    _graphicsDevice.BlendState = blendState;
                    _lastBlendState = blendState;
                }

                if (_useDynamicLightingShader)
                {
                    var effect = GraphicsManager.Instance.DynamicLightingEffect;
                    if (effect == null || effect.CurrentTechnique == null)
                        return;

                    if (_lastBoundTexture != texture)
                    {
                        effect.Parameters["DiffuseTexture"]?.SetValue(texture);
                        _lastBoundTexture = texture;
                    }

                    var vertexBuffer = EnsureTerrainStreamBuffer(alphaLayer, vertCount);
                    vertexBuffer.SetData(batch, 0, vertCount, SetDataOptions.Discard);
                    VertexUploads++;
                    UploadedVertices += vertCount;
                    _graphicsDevice.Indices = null;
                    _graphicsDevice.SetVertexBuffer(vertexBuffer);

                    int triangleCount = vertCount / 3;
                    foreach (var pass in effect.CurrentTechnique.Passes)
                    {
                        pass.Apply();
                        _graphicsDevice.DrawPrimitives(PrimitiveType.TriangleList, 0, triangleCount);
                    }
                    DrawnTiles += vertCount / 6;
                }
                else
                {
                    var effect = GraphicsManager.Instance.BasicEffect3D;
                    if (effect == null || effect.CurrentTechnique == null)
                        return;

                    if (_lastBoundTexture != texture)
                    {
                        effect.Texture = texture;
                        _lastBoundTexture = texture;
                    }

                    if (_fallbackTileBuffer.Length < vertCount)
                    {
                        int capacity = GetExpandedCapacity(vertCount, InitialTileBatchVerts, MaxTileBatchVerts);
                        Array.Resize(ref _fallbackTileBuffer, capacity);
                    }

                    for (int i = 0; i < vertCount; i++)
                    {
                        var vertex = batch[i];
                        _fallbackTileBuffer[i] = new VertexPositionColorTexture(
                            vertex.Position, vertex.Color, vertex.TextureCoordinate);
                    }

                    VertexUploads++;
                    UploadedVertices += vertCount;
                    foreach (var pass in effect.CurrentTechnique.Passes)
                    {
                        pass.Apply();
                        _graphicsDevice.DrawUserPrimitives(
                            PrimitiveType.TriangleList,
                            _fallbackTileBuffer,
                            0,
                            vertCount / 3);
                    }
                }

                DrawCalls++;
                DrawnTriangles += vertCount / 3;
                DrawnTiles += vertCount / 6;
            }
            finally
            {
                if (alphaLayer)
                    _tileAlphaCounts[texIndex] = 0;
                else
                    _tileBatchCounts[texIndex] = 0;
            }
        }

        private void FlushAllTileBatches()
        {
            // IMPORTANT: For proper terrain blending, we must render ALL opaque layers first,
            // then ALL alpha layers. This preserves the correct depth/blend order.

            // First pass: render all opaque batches
            for (int i = 0; i < _activeTileBatchTextures.Count; i++)
            {
                int t = _activeTileBatchTextures[i];
                if (_tileBatchCounts[t] > 0)
                    FlushSingleTexture(t, alphaLayer: false);
                _tileBatchActive[t] = false;
            }
            _activeTileBatchTextures.Clear();

            // Second pass: render all alpha batches
            for (int i = 0; i < _activeTileAlphaBatchTextures.Count; i++)
            {
                int t = _activeTileAlphaBatchTextures[i];
                if (_tileAlphaCounts[t] > 0)
                    FlushSingleTexture(t, alphaLayer: true);
                _tileAlphaBatchActive[t] = false;
            }
            _activeTileAlphaBatchTextures.Clear();

            if (_lastBlendState != BlendState.Opaque)
            {
                _graphicsDevice.BlendState = BlendState.Opaque;
                _lastBlendState = BlendState.Opaque;
            }
        }

        private void TrackIndexTexture(int texIndex, bool alphaLayer)
        {
            if (alphaLayer)
            {
                if (_tileAlphaIndexActive[texIndex])
                    return;

                _tileAlphaIndexActive[texIndex] = true;
                _activeTileAlphaIndexTextures.Add(texIndex);
                return;
            }

            if (_tileIndexActive[texIndex])
                return;

            _tileIndexActive[texIndex] = true;
            _activeTileIndexTextures.Add(texIndex);
        }

        private void TrackVertexTexture(int texIndex, bool alphaLayer)
        {
            if (alphaLayer)
            {
                if (_tileAlphaBatchActive[texIndex])
                    return;

                _tileAlphaBatchActive[texIndex] = true;
                _activeTileAlphaBatchTextures.Add(texIndex);
                return;
            }

            if (_tileBatchActive[texIndex])
                return;

            _tileBatchActive[texIndex] = true;
            _activeTileBatchTextures.Add(texIndex);
        }

        private static int GetTerrainIndex(int x, int y)
            => y * Constants.TERRAIN_SIZE + x;

        private static int GetTerrainIndexRepeat(int x, int y)
            => ((y & Constants.TERRAIN_SIZE_MASK) * Constants.TERRAIN_SIZE)
             + (x & Constants.TERRAIN_SIZE_MASK);

        private TerrainVertexPositionColorNormalTexture[] GetTileBatchBuffer(
            int texIndex,
            bool alphaLayer,
            int requiredVertexCount)
        {
            var batches = alphaLayer ? _tileAlphaBatches : _tileBatches;
            var buffer = batches[texIndex];
            if (buffer == null || buffer.Length < requiredVertexCount)
            {
                int capacity = GetExpandedCapacity(
                    requiredVertexCount,
                    InitialTileBatchVerts,
                    MaxTileBatchVerts);
                buffer = new TerrainVertexPositionColorNormalTexture[capacity];
                batches[texIndex] = buffer;
            }
            return buffer;
        }

        private static int GetExpandedCapacity(int required, int minimum, int maximum)
        {
            int capacity = Math.Max(1, minimum);
            while (capacity < required && capacity < maximum)
                capacity = Math.Min(maximum, capacity * 2);

            return Math.Max(required, capacity);
        }

        private ushort[] GetTileIndexBatchBuffer(
            int texIndex,
            bool alphaLayer,
            int requiredIndexCount = TileBatchIndices)
        {
            var batches = alphaLayer ? _tileAlphaIndexBatches : _tileIndexBatches;
            var buffer = batches[texIndex];
            int maxCapacity = _buildingPersistentTerrainIndexCache
                ? MaxPersistentTerrainIndices
                : TileBatchIndices;
            int requiredCapacity = _buildingPersistentTerrainIndexCache
                ? GetExpandedCapacity(requiredIndexCount, TileBatchIndices, maxCapacity)
                : TileBatchIndices;

            if (buffer == null || buffer.Length < requiredCapacity ||
                (!_buildingPersistentTerrainIndexCache && buffer.Length != TileBatchIndices))
            {
                buffer = new ushort[requiredCapacity];
                batches[texIndex] = buffer;
            }

            return buffer;
        }
        public void Dispose()
        {
            _terrainStreamBufferOpaque?.Dispose();
            _terrainStreamBufferOpaque = null;
            _terrainStreamBufferAlpha?.Dispose();
            _terrainStreamBufferAlpha = null;
            _terrainVertexBufferBase?.Dispose();
            _terrainVertexBufferBase = null;
            _terrainVertexBufferAlpha?.Dispose();
            _terrainVertexBufferAlpha = null;

            for (int i = 0; i < _persistentTileIndexBuffers.Length; i++)
            {
                _persistentTileIndexBuffers[i]?.Dispose();
                _persistentTileIndexBuffers[i] = null;
                _persistentTileAlphaIndexBuffers[i]?.Dispose();
                _persistentTileAlphaIndexBuffers[i] = null;
            }
            _persistentActiveTileTextures.Clear();
            _persistentActiveTileAlphaTextures.Clear();
            _persistentTerrainIndexVersion = int.MinValue;
            _persistentLayer1MapRef = null;
            _persistentLayer2MapRef = null;
            _persistentAlphaMapRef = null;
            _persistentTextureStateHash = 0;

            _waterCausticsStreamBuffer?.Dispose();
            _waterCausticsStreamBuffer = null;
            _waterCausticsEffect?.Dispose();
            _waterCausticsEffect = null;
            _activeWaterCausticsTexture = null;
        }

    }
}

