using Client.Data.ATT;
using Client.Data.MAP;
using Client.Main.Controls.Terrain;
using Client.Main.Configuration;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Client.Main.Objects;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Client.Main.Controls
{
    /// <summary>
    /// Manages and coordinates all terrain-related systems, acting as a facade
    /// for loading, rendering, and querying terrain information.
    /// </summary>
    public class TerrainControl : GameControl
    {
        // --- Sub-systems ---
        private TerrainData _data;
        private TerrainLoader _loader;
        private TerrainPhysics _physics;
        private TerrainLightManager _lightManager;
        private WindSimulator _wind;
        private TerrainVisibilityManager _visibility;
        private TerrainRenderer _renderer;
        private GrassRenderer _grassRenderer;

        // --- Public Properties (Facades) ---
        public short WorldIndex { get; set; }
        // Use the opposite of SUN_DIRECTION because the lightmap expects a vector pointing toward the sun.
        public Vector3 LightDirection { get; set; } = Vector3.Normalize(-SunCycleManager.BaseSunDirection);
        public IReadOnlyList<DynamicLight> DynamicLights => _lightManager.DynamicLights;
        public IReadOnlyList<DynamicLightSnapshot> ActiveLights => _lightManager.ActiveLights;
        public IReadOnlyList<DynamicLightSnapshot> VisibleLights => _lightManager.VisibleLights;
        public int ActiveLightsVersion => _lightManager.ActiveLightsVersion;
        public int VisibleLightsVersion => _lightManager.VisibleLightsVersion;
        public int VisibleLightsLayoutVersion => _lightManager.VisibleLightsLayoutVersion;
        public int DynamicLightsOrphansPruned => _lightManager?.OrphanLightsPrunedCount ?? 0;
        public int DynamicLightsDuplicateAddsRejected => _lightManager?.DuplicateAddsRejectedCount ?? 0;
        public int LastFrameRegisteredDynamicLights => _lightManager?.LastFrameRegisteredCount ?? 0;
        public int LastFrameActiveDynamicLights => _lightManager?.LastFrameActiveCount ?? 0;
        public int LastFrameVisibleDynamicLights => _lightManager?.LastFrameVisibleCount ?? 0;
        public int LastUploadedDynamicLights => _renderer?.LastUploadedDynamicLights ?? 0;
        public Texture2D HeightMapTexture => _data?.HeightMapTexture;

        public Texture2D GetWaterAnimationFrame(int frame)
        {
            Texture2D[] frames = _data?.WaterCausticsTextures;
            if (frames == null || frames.Length == 0)
                return null;

            int normalizedFrame = frame % frames.Length;
            if (normalizedFrame < 0)
                normalizedFrame += frames.Length;

            Texture2D texture = frames[normalizedFrame];
            return texture != null && !texture.IsDisposed ? texture : null;
        }
        private Dictionary<int, string> _pendingTextureMap = new();
        private bool _replaceTextureMapping;
        private bool _preferIndexBatching;

        public Dictionary<int, string> TextureMappingFiles
        {
            get => _data != null ? _data.TextureMappingFiles : _pendingTextureMap;
            set
            {
                _replaceTextureMapping = true;
                if (_data != null)
                    _data.TextureMappingFiles = value ?? new Dictionary<int, string>();
                else
                    _pendingTextureMap = value ?? new Dictionary<int, string>();
            }
        }

        public float WaterSpeed { get => _renderer.WaterSpeed; set => _renderer.WaterSpeed = value; }
        public float DistortionAmplitude { get => _renderer.DistortionAmplitude; set => _renderer.DistortionAmplitude = value; }
        public float DistortionFrequency { get => _renderer.DistortionFrequency; set => _renderer.DistortionFrequency = value; }
        public float AmbientLight
        {
            get => _renderer.AmbientLight;
            set
            {
                _renderer.AmbientLight = value;
                if (_grassRenderer != null)
                {
                    _grassRenderer.AmbientLight = value;
                    _grassRenderer.BuildAllGrass();
                }
            }
        }
        public float GrassBrightness { get => _grassRenderer.GrassBrightness; set => _grassRenderer.GrassBrightness = value; }
        public Vector2 WaterFlowDirection { get => _renderer.WaterFlowDirection; set => _renderer.WaterFlowDirection = value; }
        public HashSet<byte> GrassTextureIndices => _grassRenderer.GrassTextureIndices;
        public bool IsGpuTerrainLighting => _renderer?.IsGpuLightingActive == true;
        public bool IsDynamicLightingShaderAvailable => _renderer?.IsDynamicLightingShaderAvailable == true;
        public bool PreferIndexBatching
        {
            get => _renderer?.PreferIndexBatching ?? _preferIndexBatching;
            set
            {
                _preferIndexBatching = value;
                if (_renderer != null)
                    _renderer.PreferIndexBatching = value;
            }
        }

        /// <summary>
        /// Configures grass settings for specific world requirements.
        /// This should be called in world's AfterLoad() method.
        /// </summary>
        /// <param name="brightness">Grass brightness multiplier (default: 1.0f)</param>
        /// <param name="textureIndices">Valid texture indices for grass rendering (default: {0, 1, 2})</param>
        public void ConfigureGrass(float brightness = 1f, params byte[] textureIndices)
        {
            var oldBrightness = _grassRenderer.GrassBrightness;
            _grassRenderer.GrassBrightness = brightness;

            if (textureIndices != null && textureIndices.Length > 0)
            {
                _grassRenderer.GrassTextureIndices.Clear();
                foreach (var index in textureIndices)
                {
                    _grassRenderer.GrassTextureIndices.Add(index);
                }
            }

            _grassRenderer.BuildAllGrass();

            Console.WriteLine($"[TerrainControl] ConfigureGrass for World{WorldIndex} - Brightness: {oldBrightness:F2} → {brightness:F2}, TextureIndices: [{string.Join(", ", textureIndices ?? new byte[] { 0 })}]");
        }

        /// <summary>
        /// Ensures grass textures are loaded when DRAW_GRASS setting is toggled on.
        /// Call this after changing Constants.DRAW_GRASS from false to true.
        /// </summary>
        public void ReloadGrassIfNeeded()
        {
            _grassRenderer?.EnsureContentLoaded(WorldIndex);
            _grassRenderer?.BuildAllGrass();
        }
        public TerrainFrameMetrics FrameMetrics { get; } = new TerrainFrameMetrics();

        public void ConfigureDeviasSnow(DeviasGroundSnowSettings settings)
        {
            if (WorldIndex != 3 || _renderer == null) return;
            _renderer.Snow?.Dispose();
            _renderer.Snow = settings.Enabled
                ? new DeviasSnowRenderer(GraphicsDevice, _data, _physics, _visibility, settings)
                : null;
            // Coarse base triangles must not cut through a deeply compressed snow mesh.
            for (int i = 0; i < _visibility.LodSteps.Length; i++)
                _visibility.LodSteps[i] = settings.Enabled ? 1 : 1 << i;
        }

        public void UpdateDeviasSnow(GameTime time, WalkableWorldControl world)
        {
            _renderer?.Snow?.ResetMetrics();
            _renderer?.Snow?.Update(time, world);
        }

        public TerrainControl()
        {
            AutoViewSize = false;
            ViewSize = new Point(MuGame.Instance.Width, MuGame.Instance.Height);
        }

        public override async Task Load()
        {
            _loader = new TerrainLoader(WorldIndex);

            if (_pendingTextureMap.Count > 0 || _replaceTextureMapping)
            {
                _loader.SetTextureMapping(_pendingTextureMap, _replaceTextureMapping);
            }

            _data = await _loader.LoadAsync();
            _pendingTextureMap.Clear();
            _replaceTextureMapping = false;

            if (_data == null)
            {
                Status = Models.GameControlStatus.Error;
                return;
            }

            // Initialize sub-systems in order of dependency
            _lightManager = new TerrainLightManager(_data, this);
            _physics = new TerrainPhysics(_data, _lightManager);
            _wind = new WindSimulator(_data);
            _wind.WorldIndex = WorldIndex;
            _visibility = new TerrainVisibilityManager(_data);
            _grassRenderer = new GrassRenderer(GraphicsDevice, _data, _physics, _wind, _lightManager);
            _renderer = new TerrainRenderer(GraphicsDevice, _data, _visibility, _lightManager, _grassRenderer)
            {
                WorldIndex = this.WorldIndex,
                PreferIndexBatching = _preferIndexBatching
            };

            // Post-load processing
            _lightManager.CreateTerrainNormals();
            _lightManager.CreateFinalLightmap(LightDirection);
            _renderer.CreateHeightMapTexture();
            _grassRenderer.AmbientLight = _renderer.AmbientLight;

            // Reset grass to defaults before loading world-specific content
            _grassRenderer.LoadContent(WorldIndex);
            _grassRenderer.BuildAllGrass();

            Camera.Instance.AspectRatio = GraphicsDevice.Viewport.AspectRatio;

            await base.Load();
        }

        public override void Update(GameTime time)
        {
            base.Update(time);
            if (Status != Models.GameControlStatus.Ready || _visibility == null)
                return;

            var camPos2D = new Vector2(Camera.Instance.Position.X, Camera.Instance.Position.Y);

            _visibility.Update(camPos2D);
            _lightManager.UpdateActiveLights((float)time.ElapsedGameTime.TotalSeconds);

            _wind.Update(time);
            _renderer.Update(time);
        }

        internal void PrepareInitialRenderResources()
        {
            if (Status != Models.GameControlStatus.Ready || _visibility == null || _renderer == null)
                return;

            var camera = Camera.Instance;
            _visibility.Update(new Vector2(camera.Position.X, camera.Position.Y));
            _renderer.PrepareFirstDrawResources();
        }

        internal async Task PrepareInitialRenderResourcesAsync(string phaseName)
        {
            if (Status != Models.GameControlStatus.Ready || _visibility == null || _renderer == null)
                return;

            var camera = Camera.Instance;
            _visibility.Update(new Vector2(camera.Position.X, camera.Position.Y));
            await _renderer.PrepareFirstDrawResourcesAsync(phaseName);
        }

        public override void Draw(GameTime time)
        {
            if (!Visible || Status != Models.GameControlStatus.Ready || _renderer == null || _grassRenderer == null)
                return;

            FrameMetrics.Reset();
            var terrainStarted = RenderPassProfiler.Start();
            _renderer.Draw();
            RenderPassProfiler.AddTerrainOpaque(terrainStarted);

            // Aggregate metrics from renderers
            FrameMetrics.DrawCalls = _renderer.DrawCalls + _grassRenderer.Flushes; // Grass flush is a draw call
            FrameMetrics.DrawnTriangles = _renderer.DrawnTriangles + _grassRenderer.DrawnTriangles;
            FrameMetrics.DrawnBlocks = _renderer.DrawnBlocks;
            FrameMetrics.DrawnCells = _renderer.DrawnCells;
            FrameMetrics.GrassFlushes = _grassRenderer.Flushes;
            FrameMetrics.IndexedCells = _renderer.IndexedCells;
            FrameMetrics.StreamedCells = _renderer.StreamedCells;
            FrameMetrics.IndexUploads = _renderer.IndexUploads;
            FrameMetrics.VertexUploads = _renderer.VertexUploads;
            FrameMetrics.UploadedIndices = _renderer.UploadedIndices;
            FrameMetrics.UploadedVertices = _renderer.UploadedVertices;
            FrameMetrics.UsedIndexBatching = _renderer.UsedIndexBatching;
            if (_renderer.Snow is { } snow)
            {
                FrameMetrics.DrawCalls += snow.DrawCalls;
                FrameMetrics.DrawnTriangles += snow.DrawnTriangles;
                FrameMetrics.VertexUploads += snow.VertexUploads;
                FrameMetrics.UploadedVertices += snow.UploadedVertices;
            }

            base.Draw(time);
        }

        public override void DrawAfter(GameTime gameTime)
        {
            if (!Visible || Status != Models.GameControlStatus.Ready)
                return;

            // Terrain alpha layers are already rendered in TerrainRenderer.Draw().
            // The previous after-pass still configured the complete renderer and walked every
            // visible block, while RenderTerrainTile returned immediately for after == true.
            base.DrawAfter(gameTime);
        }


        // --- Public Query Methods (Facade) ---
        public int GetHeroTile(float xf, float yf) => _data.Mapping.Layer1[TerrainPhysics.GetTerrainIndex(xf, yf)];
        public TWFlags RequestTerrainFlag(int x, int y) => _physics?.RequestTerrainFlag(x, y) ?? 0f;
        public float RequestTerrainHeight(float xf, float yf) => _physics?.RequestTerrainHeight(xf, yf) ?? 0f;

        /// <summary>
        /// Sets or clears <paramref name="flag"/> on every tile of the given inclusive rectangles
        /// (server-driven change, e.g. Blood Castle gates or the Chaos Castle floor collapsing).
        /// </summary>
        /// <returns>Number of tiles whose flags actually changed.</returns>
        public int ApplyAttributeChange(TWFlags flag, bool remove, IEnumerable<(int StartX, int StartY, int EndX, int EndY)> areas)
        {
            var walls = _data?.Attributes?.TerrainWall;
            if (walls == null)
                return 0;

            int changed = 0;
            foreach (var (startX, startY, endX, endY) in areas)
            {
                int maxX = Math.Min(endX, Constants.TERRAIN_SIZE - 1);
                int maxY = Math.Min(endY, Constants.TERRAIN_SIZE - 1);
                for (int y = Math.Max(startY, 0); y <= maxY; y++)
                {
                    for (int x = Math.Max(startX, 0); x <= maxX; x++)
                    {
                        int index = y * Constants.TERRAIN_SIZE + x;
                        TWFlags before = walls[index];
                        TWFlags after = remove ? before & ~flag : before | flag;
                        if (after == before)
                            continue;

                        walls[index] = after;
                        changed++;
                    }
                }
            }

            if (changed > 0 && (flag & TWFlags.NoGround) != 0)
                _renderer?.InvalidateTerrainAttributes();
            if (changed > 0 && (flag & TWFlags.Height) != 0)
                _groundHeightTextureValid = false;

            return changed;
        }

        private Texture2D _groundHeightTexture;
        private bool _groundHeightTextureValid;
        private float[] _groundHeights;
        private float[] _surfaceHeights;
        private float[] _snowWindowHeights;
        private long _snowHeightsVersion = -1;
        private int _snowHeightsFrame = int.MinValue;

        // Snow surface refresh: a node window around the camera, at most every few frames after a change.
        private const int SnowHeightWindow = 64;
        private const int SnowHeightRefreshFrames = 15;

        /// <summary>
        /// Rendered ground height per height-map node (R32F, TERRAIN_SIZE squared): the values
        /// <see cref="RequestTerrainHeight"/> interpolates, raised to the Devias snow surface where it lies above.
        /// Used by shaders that put geometry on the ground.
        /// </summary>
        internal Texture2D GetGroundHeightTexture()
        {
            if (_physics == null || GraphicsDevice == null)
                return null;

            const int size = Constants.TERRAIN_SIZE;
            if (!_groundHeightTextureValid || _groundHeightTexture is not { IsDisposed: false })
            {
                _groundHeights ??= new float[size * size];
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                        _groundHeights[y * size + x] = _physics.GetRenderedNodeHeight(x, y);
                }

                // Terrain only; the snow surface goes into _surfaceHeights (and the texture) on top of it.
                _surfaceHeights ??= new float[size * size];
                Array.Copy(_groundHeights, _surfaceHeights, _groundHeights.Length);
                _groundHeightTexture ??= new Texture2D(GraphicsDevice, size, size, false, SurfaceFormat.Single);
                _groundHeightTexture.SetData(_surfaceHeights);
                _groundHeightTextureValid = true;
                _snowHeightsVersion = -1;
            }

            if (_renderer?.Snow is { } snow && snow.SurfaceVersion != _snowHeightsVersion &&
                (uint)(MuGame.FrameIndex - _snowHeightsFrame) >= SnowHeightRefreshFrames)
                UpdateSnowGroundHeights(snow);

            return _groundHeightTexture;
        }

        /// <summary>Height of one node of <see cref="GetGroundHeightTexture"/> (wrapped like the terrain lookups).</summary>
        internal float GetGroundNodeHeight(int x, int y) =>
            _surfaceHeights == null ? 0f : _surfaceHeights[(y & Constants.TERRAIN_SIZE_MASK) * Constants.TERRAIN_SIZE + (x & Constants.TERRAIN_SIZE_MASK)];

        private void UpdateSnowGroundHeights(DeviasSnowRenderer snow)
        {
            const int size = Constants.TERRAIN_SIZE;
            var target = Camera.Instance?.Target ?? Vector3.Zero;
            int x0 = Math.Clamp((int)(target.X / Constants.TERRAIN_SCALE) - SnowHeightWindow / 2, 0, size - SnowHeightWindow);
            int y0 = Math.Clamp((int)(target.Y / Constants.TERRAIN_SCALE) - SnowHeightWindow / 2, 0, size - SnowHeightWindow);

            _snowWindowHeights ??= new float[SnowHeightWindow * SnowHeightWindow];
            for (int y = 0; y < SnowHeightWindow; y++)
            {
                for (int x = 0; x < SnowHeightWindow; x++)
                {
                    int nodeX = x0 + x, nodeY = y0 + y;
                    float ground = _groundHeights[nodeY * size + nodeX];
                    float worldX = nodeX * Constants.TERRAIN_SCALE, worldY = nodeY * Constants.TERRAIN_SCALE;
                    float height = snow.TryGetSurfaceHeight(worldX, worldY, out float surface) ? MathF.Max(ground, surface) : ground;
                    _snowWindowHeights[y * SnowHeightWindow + x] = height;
                    _surfaceHeights[nodeY * size + nodeX] = height;
                }
            }

            _groundHeightTexture.SetData(0, new Rectangle(x0, y0, SnowHeightWindow, SnowHeightWindow),
                _snowWindowHeights, 0, _snowWindowHeights.Length);
            _snowHeightsVersion = snow.SurfaceVersion;
            _snowHeightsFrame = MuGame.FrameIndex;
        }

        internal bool HasSnowShadowSurface => _renderer?.Snow != null;
        internal DeviasSnowRenderer SnowShadowSurface => _renderer?.Snow;
        internal float RequestShadowSurfaceHeight(float x, float y)
        {
            float ground = RequestTerrainHeight(x, y);
            return _renderer?.Snow is { } snow && snow.TryGetSurfaceHeight(x, y, out float height)
                ? MathF.Max(ground, height)
                : ground;
        }
        public Vector3 EvaluateTerrainLight(float xf, float yf) => _physics?.RequestTerrainLight(xf, yf, AmbientLight) ?? Vector3.Zero;
        public Vector3 EvaluateDynamicLight(Vector2 position) => _lightManager.EvaluateDynamicLight(position);
        public byte GetBaseTextureIndexAt(int x, int y) => _physics.GetBaseTextureIndexAt(x, y);
        public float GetWindValue(int x, int y) => _wind.GetWindValue(x, y);

        /// <summary>
        /// Renders the terrain into the shared shadow map.
        /// </summary>
        public void RenderShadowMap(Effect shadowEffect, Matrix lightViewProjection)
        {
            _renderer?.DrawShadowMap(shadowEffect, lightViewProjection);
        }

        // --- Light Management (Facade) ---
        public void AddDynamicLight(DynamicLight light) => _lightManager?.AddDynamicLight(light);
        public void RemoveDynamicLight(DynamicLight light) => _lightManager?.RemoveDynamicLight(light);
        public void RemoveDynamicLightsByOwner(WorldObject owner) => _lightManager?.RemoveDynamicLightsByOwner(owner);

        public override void Dispose()
        {
            _renderer?.Dispose();
            _grassRenderer?.Dispose();
            _groundHeightTexture?.Dispose();
            _groundHeightTexture = null;
            base.Dispose();
            _data = null; // Allow GC to collect all data
            GC.SuppressFinalize(this);
        }
    }
}
