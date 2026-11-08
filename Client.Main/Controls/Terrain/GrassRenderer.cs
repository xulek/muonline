using Client.Data.ATT;
using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Client.Main.Controls.Terrain
{
    /// <summary>
    /// Renders the original MU terrain-grass pass.
    ///
    /// SourceMain renders one vertical textured quad per terrain face. The quad uses
    /// Layer1, the four terrain light values, and the two upper terrain vertices are
    /// displaced by TerrainGrassWind. It is intentionally not a field of random blades.
    /// </summary>
    public sealed class GrassRenderer : IDisposable
    {
        private const int GrassTextureCount = 3;
        private const int GrassTextureBaseIndex = 0;
        private const int ChunkSize = 32;
        private const float GrassUvWidth = 64f / 256f;
        private const float GrassHeightMultiplier = 2f;
        private const float GrassHorizontalOffset = -50f;
        private const float SpecialHeight = 1200f;

        private readonly struct GrassQuad
        {
            public readonly Vector3 BottomLeft;
            public readonly Vector3 BottomRight;
            public readonly Color Light1;
            public readonly Color Light2;
            public readonly Color Light3;
            public readonly Color Light4;
            public readonly int WindIndex1;
            public readonly int WindIndex2;
            public readonly float U;
            public readonly float Height;

            public GrassQuad(
                Vector3 bottomLeft,
                Vector3 bottomRight,
                Color light1,
                Color light2,
                Color light3,
                Color light4,
                int windIndex1,
                int windIndex2,
                float u,
                float height)
            {
                BottomLeft = bottomLeft;
                BottomRight = bottomRight;
                Light1 = light1;
                Light2 = light2;
                Light3 = light3;
                Light4 = light4;
                WindIndex1 = windIndex1;
                WindIndex2 = windIndex2;
                U = u;
                Height = height;
            }
        }

        private sealed class GrassBatch : IDisposable
        {
            public readonly Texture2D Texture;
            public readonly GrassQuad[] Quads;
            public readonly VertexPositionColorTexture[] Vertices;
            public readonly BoundingBox Bounds;
            public DynamicVertexBuffer VertexBuffer;
            public int LastWindVersion = int.MinValue;

            public GrassBatch(
                GraphicsDevice graphicsDevice,
                Texture2D texture,
                List<GrassQuad> quads,
                BoundingBox bounds)
            {
                Texture = texture;
                Quads = quads.ToArray();
                Vertices = new VertexPositionColorTexture[Quads.Length * 6];
                Bounds = bounds;
                VertexBuffer = new DynamicVertexBuffer(
                    graphicsDevice,
                    VertexPositionColorTexture.VertexDeclaration,
                    Vertices.Length,
                    BufferUsage.WriteOnly);

                BuildStaticVertices();
                VertexBuffer.SetData(Vertices, 0, Vertices.Length, SetDataOptions.Discard);
            }

            private void BuildStaticVertices()
            {
                float halfTexelU = 0.5f / Texture.Width;
                float halfTexelV = 0.5f / Texture.Height;

                for (int i = 0; i < Quads.Length; i++)
                {
                    GrassQuad quad = Quads[i];
                    Vector3 topLeft = quad.BottomLeft;
                    topLeft.X += GrassHorizontalOffset;
                    topLeft.Z += quad.Height;

                    Vector3 topRight = quad.BottomRight;
                    topRight.X += GrassHorizontalOffset;
                    topRight.Z += quad.Height;

                    int vertex = i * 6;
                    Vector2 uvTopLeft = new(quad.U + halfTexelU, halfTexelV);
                    Vector2 uvTopRight = new(quad.U + GrassUvWidth - halfTexelU, halfTexelV);
                    Vector2 uvBottomRight = new(quad.U + GrassUvWidth - halfTexelU, 1f - halfTexelV);
                    Vector2 uvBottomLeft = new(quad.U + halfTexelU, 1f - halfTexelV);

                    Vertices[vertex + 0] = new VertexPositionColorTexture(topLeft, quad.Light1, uvTopLeft);
                    Vertices[vertex + 1] = new VertexPositionColorTexture(topRight, quad.Light2, uvTopRight);
                    Vertices[vertex + 2] = new VertexPositionColorTexture(quad.BottomRight, quad.Light3, uvBottomRight);
                    Vertices[vertex + 3] = new VertexPositionColorTexture(quad.BottomRight, quad.Light3, uvBottomRight);
                    Vertices[vertex + 4] = new VertexPositionColorTexture(quad.BottomLeft, quad.Light4, uvBottomLeft);
                    Vertices[vertex + 5] = new VertexPositionColorTexture(topLeft, quad.Light1, uvTopLeft);
                }
            }

            public void Dispose()
            {
                VertexBuffer?.Dispose();
                VertexBuffer = null;
            }
        }

        private readonly GraphicsDevice _graphicsDevice;
        private readonly TerrainData _data;
        private readonly TerrainPhysics _physics;
        private readonly WindSimulator _wind;
        private readonly Texture2D[] _grassTextures = new Texture2D[GrassTextureCount];
        private readonly List<GrassBatch> _batches = new();

        private readonly object _contentLoadLock = new();
        private Task _contentLoadTask;
        private BasicEffect _additiveEffect;
        private float[] _rowOffsets;
        private volatile bool _contentReady;
        private volatile bool _rebuildPending;
        private short _worldIndex;

        public float GrassBrightness { get; set; } = 1f;
        public float AmbientLight { get; set; } = 0.25f;
        public HashSet<byte> GrassTextureIndices { get; } = new() { 0, 1, 2 };
        public int Flushes { get; private set; }
        public int DrawnTriangles { get; private set; }

        public GrassRenderer(
            GraphicsDevice graphicsDevice,
            TerrainData data,
            TerrainPhysics physics,
            WindSimulator wind,
            TerrainLightManager lightManager)
        {
            _graphicsDevice = graphicsDevice;
            _data = data;
            _physics = physics;
            _wind = wind;
            _ = lightManager;
        }

        public void LoadContent(short worldIndex)
        {
            if (_worldIndex != worldIndex)
            {
                _worldIndex = worldIndex;
                _contentReady = false;
                _rebuildPending = true;
                _rowOffsets = null;
                lock (_contentLoadLock)
                    _contentLoadTask = null;
            }

            _ = EnsureContentLoadTask(worldIndex);
        }

        public void EnsureContentLoaded(short worldIndex)
        {
            if (Constants.DRAW_GRASS && !_contentReady)
                _ = EnsureContentLoadTask(worldIndex);
        }

        private Task EnsureContentLoadTask(short worldIndex)
        {
            lock (_contentLoadLock)
            {
                // An attempt made while grass was off completes without loading anything. Reusing that
                // finished task would leave the textures missing when grass is switched on later.
                if (_contentLoadTask == null || (_contentLoadTask.IsCompleted && !_contentReady))
                    _contentLoadTask = LoadContentCoreAsync(worldIndex);

                return _contentLoadTask;
            }
        }

        private async Task LoadContentCoreAsync(short worldIndex)
        {
            if (!Constants.DRAW_GRASS)
                return;

            _worldIndex = worldIndex;
            bool specialBlendMap = IsSpecialBlendMap(worldIndex);

            for (int i = 0; i < GrassTextureCount; i++)
            {
                // Grass sprites are the .tga files (BITMAP_MAPGRASS + Layer1). TileGrass02.jpg/.OZJ is the ground
                // tile (BITMAP_MAPTILE + 1), not a sprite, so a missing TileGrass02/03.ozt means no sprite.
                string fileName = i == GrassTextureBaseIndex && specialBlendMap
                    ? "TileGrass01_R.jpg"
                    : $"TileGrass0{i + 1}.ozt";
                string path = Path.Combine($"World{worldIndex}", fileName);

                try
                {
                    _grassTextures[i] = await TextureLoader.Instance
                        .PrepareAndGetTexture(path)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error loading grass texture '{path}': {ex.Message}");
                    _grassTextures[i] = null;
                }
            }

            _contentReady = true;
            _rebuildPending = true;
        }

        /// <summary>Diagnostics (StressHarness): what the grass pass currently holds.</summary>
        internal string DescribeForDiagnostics()
        {
            int textures = 0;
            for (int i = 0; i < _grassTextures.Length; i++)
                if (_grassTextures[i] != null) textures++;
            return $"draw={Constants.DRAW_GRASS} world={_worldIndex} ready={_contentReady} pending={_rebuildPending} " +
                   $"batches={_batches.Count} textures={textures}/{GrassTextureCount} indices={GrassTextureIndices.Count}";
        }

        /// <summary>Diagnostics (StressHarness): the grass tile closest to <paramref name="from"/> that is at least
        /// <paramref name="minDistance"/> away, as terrain tile coordinates.</summary>
        internal bool TryFindGrassTile(Vector3 from, float minDistance, out Vector2 tile)
        {
            tile = default;
            float best = float.MaxValue, min = minDistance * minDistance;
            foreach (var batch in _batches)
            {
                foreach (var quad in batch.Quads)
                {
                    float dx = quad.BottomLeft.X - from.X, dy = quad.BottomLeft.Y - from.Y;
                    float d = dx * dx + dy * dy;
                    if (d < min || d >= best)
                        continue;
                    best = d;
                    tile = new Vector2(MathF.Floor(quad.BottomLeft.X / Constants.TERRAIN_SCALE),
                                       MathF.Floor(quad.BottomLeft.Y / Constants.TERRAIN_SCALE));
                }
            }
            return best < float.MaxValue;
        }

        public void ResetMetrics()
        {
            Flushes = 0;
            DrawnTriangles = 0;
        }

        public void BuildAllGrass()
        {
            _rebuildPending = false;
            DisposeBatches();

            if (!Constants.DRAW_GRASS || IsGrassDisabledWorld(_worldIndex) ||
                _graphicsDevice == null || !_contentReady)
            {
                _rebuildPending = !_contentReady && Constants.DRAW_GRASS;
                return;
            }

            if (_data?.HeightMap == null || _data.Mapping.Layer1 == null ||
                _data.Mapping.Alpha == null || GrassTextureIndices.Count == 0)
            {
                return;
            }

            int chunksPerSide = (Constants.TERRAIN_SIZE + ChunkSize - 1) / ChunkSize;
            var quadLists = new List<GrassQuad>[chunksPerSide * chunksPerSide * GrassTextureCount];
            var boundsMin = new Vector3[quadLists.Length];
            var boundsMax = new Vector3[quadLists.Length];
            for (int i = 0; i < quadLists.Length; i++)
            {
                boundsMin[i] = new Vector3(float.MaxValue);
                boundsMax[i] = new Vector3(float.MinValue);
            }

            // SourceMain initializes one random 0/.25/.5/.75 U offset per terrain row
            // when the map is loaded, so rebuilds keep the same pattern.
            if (_rowOffsets == null)
            {
                _rowOffsets = new float[Constants.TERRAIN_SIZE];
                var random = new Random();
                for (int y = 0; y < _rowOffsets.Length; y++)
                    _rowOffsets[y] = random.Next(4) * 0.25f;
            }
            float[] rowOffsets = _rowOffsets;

            int terrainSize = Constants.TERRAIN_SIZE;
            int terrainMask = terrainSize - 1;
            var walls = _data.Attributes?.TerrainWall;
            var mapping = _data.Mapping;

            for (int y = 0; y < terrainSize; y++)
            {
                for (int x = 0; x < terrainSize; x++)
                {
                    int index1 = (y & terrainMask) * terrainSize + (x & terrainMask);
                    if (walls != null && (uint)index1 < (uint)walls.Length &&
                        (walls[index1] & TWFlags.NoGround) != 0)
                    {
                        continue;
                    }

                    int index2 = (y & terrainMask) * terrainSize + ((x + 1) & terrainMask);
                    int index3 = ((y + 1) & terrainMask) * terrainSize + ((x + 1) & terrainMask);
                    int index4 = ((y + 1) & terrainMask) * terrainSize + (x & terrainMask);

                    if (HasTerrainAlpha(mapping.Alpha, index1) ||
                        HasTerrainAlpha(mapping.Alpha, index2) ||
                        HasTerrainAlpha(mapping.Alpha, index3) ||
                        HasTerrainAlpha(mapping.Alpha, index4))
                    {
                        continue;
                    }

                    int textureIndex = mapping.Layer1[index1];
                    if ((uint)textureIndex >= GrassTextureCount ||
                        !GrassTextureIndices.Contains((byte)textureIndex) ||
                        _grassTextures[textureIndex] == null)
                    {
                        continue;
                    }

                    float height = _grassTextures[textureIndex].Height * GrassHeightMultiplier;
                    Vector3 bottomLeft = CreateTerrainPosition(x, y, index1);
                    Vector3 bottomRight = CreateTerrainPosition(x + 1, y + 1, index3);
                    float u = x * GrassUvWidth + rowOffsets[y & terrainMask];
                    var quad = new GrassQuad(
                        bottomLeft,
                        bottomRight,
                        GetTerrainLight(index1),
                        GetTerrainLight(index2),
                        GetTerrainLight(index3),
                        GetTerrainLight(index4),
                        index1,
                        index2,
                        u,
                        height);

                    int chunkX = x / ChunkSize;
                    int chunkY = y / ChunkSize;
                    int batchIndex = (chunkY * chunksPerSide + chunkX) * GrassTextureCount + textureIndex;
                    (quadLists[batchIndex] ??= new List<GrassQuad>(ChunkSize * ChunkSize / 2)).Add(quad);
                    ExpandBounds(ref boundsMin[batchIndex], ref boundsMax[batchIndex], quad);
                }
            }

            for (int i = 0; i < quadLists.Length; i++)
            {
                if (quadLists[i] == null || quadLists[i].Count == 0)
                    continue;

                int textureIndex = i % GrassTextureCount;
                _batches.Add(new GrassBatch(
                    _graphicsDevice,
                    _grassTextures[textureIndex],
                    quadLists[i],
                    new BoundingBox(boundsMin[i], boundsMax[i])));
            }
        }

        public void Draw()
        {
            if (_rebuildPending && _contentReady)
                BuildAllGrass();

            if (!Constants.DRAW_GRASS || IsGrassDisabledWorld(_worldIndex) ||
                _graphicsDevice == null || _batches.Count == 0 || Camera.Instance == null)
            {
                return;
            }

            AlphaTestEffect alphaEffect = GraphicsManager.Instance?.AlphaTestEffect3D;
            if (alphaEffect == null)
                return;

            bool additive = IsSpecialBlendMap(_worldIndex);
            BasicEffect additiveEffect = additive ? EnsureAdditiveEffect() : null;
            Effect effect = additive ? additiveEffect : alphaEffect;
            if (effect == null)
                return;

            BlendState previousBlend = _graphicsDevice.BlendState;
            DepthStencilState previousDepth = _graphicsDevice.DepthStencilState;
            RasterizerState previousRasterizer = _graphicsDevice.RasterizerState;
            SamplerState previousSampler = _graphicsDevice.SamplerStates[0];
            int previousReferenceAlpha = alphaEffect.ReferenceAlpha;
            CompareFunction previousAlphaFunction = alphaEffect.AlphaFunction;

            try
            {
                _graphicsDevice.RasterizerState = RasterizerState.CullNone;
                _graphicsDevice.SamplerStates[0] = SamplerState.LinearWrap;
                _graphicsDevice.BlendState = additive ? BlendState.Additive : BlendState.NonPremultiplied;
                _graphicsDevice.DepthStencilState = additive
                    ? DepthStencilState.DepthRead
                    : DepthStencilState.Default;

                ConfigureEffect(effect, additive ? additiveEffect : null);
                if (!additive)
                {
                    alphaEffect.AlphaFunction = CompareFunction.Greater;
                    alphaEffect.ReferenceAlpha = (int)(255f * 0.01f);
                }

                BoundingFrustum frustum = Camera.Instance.Frustum;
                int windVersion = _wind.Version;
                for (int i = 0; i < _batches.Count; i++)
                {
                    GrassBatch batch = _batches[i];
                    if (frustum.Contains(batch.Bounds) == ContainmentType.Disjoint)
                        continue;

                    if (batch.LastWindVersion != windVersion)
                    {
                        UpdateBatchWind(batch);
                        batch.LastWindVersion = windVersion;
                    }

                    _graphicsDevice.SetVertexBuffer(batch.VertexBuffer);
                    if (additive)
                        additiveEffect.Texture = batch.Texture;
                    else
                        alphaEffect.Texture = batch.Texture;

                    foreach (EffectPass pass in effect.CurrentTechnique.Passes)
                    {
                        pass.Apply();
                        _graphicsDevice.DrawPrimitives(
                            PrimitiveType.TriangleList,
                            0,
                            batch.Vertices.Length / 3);
                    }

                    Flushes++;
                    DrawnTriangles += batch.Vertices.Length / 3;
                }
            }
            finally
            {
                _graphicsDevice.SetVertexBuffer(null);
                _graphicsDevice.BlendState = previousBlend;
                _graphicsDevice.DepthStencilState = previousDepth;
                _graphicsDevice.RasterizerState = previousRasterizer;
                _graphicsDevice.SamplerStates[0] = previousSampler;
                alphaEffect.ReferenceAlpha = previousReferenceAlpha;
                alphaEffect.AlphaFunction = previousAlphaFunction;
            }
        }

        private void ConfigureEffect(Effect effect, BasicEffect additiveEffect)
        {
            Matrix view = Camera.Instance.View;
            Matrix projection = Camera.Instance.Projection;

            if (additiveEffect != null)
            {
                additiveEffect.World = Matrix.Identity;
                additiveEffect.View = view;
                additiveEffect.Projection = projection;
                additiveEffect.TextureEnabled = true;
                additiveEffect.VertexColorEnabled = true;
                additiveEffect.LightingEnabled = false;
                additiveEffect.DiffuseColor = Vector3.One;
                additiveEffect.Alpha = 1f;
                return;
            }

            var alphaEffect = (AlphaTestEffect)effect;
            alphaEffect.World = Matrix.Identity;
            alphaEffect.View = view;
            alphaEffect.Projection = projection;
            alphaEffect.VertexColorEnabled = true;
            alphaEffect.DiffuseColor = Vector3.One;
            alphaEffect.Alpha = 1f;
        }

        private BasicEffect EnsureAdditiveEffect()
        {
            if (_additiveEffect != null && !_additiveEffect.IsDisposed)
                return _additiveEffect;

            _additiveEffect = new BasicEffect(_graphicsDevice)
            {
                TextureEnabled = true,
                VertexColorEnabled = true,
                LightingEnabled = false,
                World = Matrix.Identity
            };
            return _additiveEffect;
        }

        private void UpdateBatchWind(GrassBatch batch)
        {
            for (int i = 0; i < batch.Quads.Length; i++)
            {
                GrassQuad quad = batch.Quads[i];
                int vertex = i * 6;
                float leftY = quad.BottomLeft.Y + _wind.GetWindValue(quad.WindIndex1);
                float rightY = quad.BottomRight.Y + _wind.GetWindValue(quad.WindIndex2);

                Vector3 topLeft = batch.Vertices[vertex + 0].Position;
                topLeft.Y = leftY;
                batch.Vertices[vertex + 0].Position = topLeft;
                batch.Vertices[vertex + 5].Position = topLeft;

                Vector3 topRight = batch.Vertices[vertex + 1].Position;
                topRight.Y = rightY;
                batch.Vertices[vertex + 1].Position = topRight;
            }

            batch.VertexBuffer.SetData(batch.Vertices, 0, batch.Vertices.Length, SetDataOptions.Discard);
        }

        private Vector3 CreateTerrainPosition(int tileX, int tileY, int sampleIndex)
        {
            float z = 0f;
            if (_data.HeightMap != null && (uint)sampleIndex < (uint)_data.HeightMap.Length)
                z = _data.HeightMap[sampleIndex].R * 1.5f;

            var walls = _data.Attributes?.TerrainWall;
            if (walls != null && (uint)sampleIndex < (uint)walls.Length &&
                (walls[sampleIndex] & TWFlags.Height) != 0)
            {
                z += SpecialHeight;
            }

            return new Vector3(
                tileX * Constants.TERRAIN_SCALE,
                tileY * Constants.TERRAIN_SCALE,
                z);
        }

        private Color GetTerrainLight(int index)
        {
            if (_data.FinalLightMap == null || (uint)index >= (uint)_data.FinalLightMap.Length)
                return Color.White;

            Color source = _data.FinalLightMap[index];
            float ambient = AmbientLight * 255f;
            float brightness = float.IsFinite(GrassBrightness) ? MathF.Max(0f, GrassBrightness) : 1f;
            return new Color(
                ClampColor((source.R + ambient) * brightness),
                ClampColor((source.G + ambient) * brightness),
                ClampColor((source.B + ambient) * brightness),
                (byte)255);
        }

        private static byte ClampColor(float value)
        {
            return (byte)Math.Clamp((int)value, 0, 255);
        }

        private static bool HasTerrainAlpha(byte[] alpha, int index)
        {
            return alpha != null && (uint)index < (uint)alpha.Length && alpha[index] > 0;
        }

        private static void ExpandBounds(ref Vector3 min, ref Vector3 max, GrassQuad quad)
        {
            Vector3 quadMin = Vector3.Min(quad.BottomLeft, quad.BottomRight);
            Vector3 quadMax = Vector3.Max(quad.BottomLeft, quad.BottomRight);
            quadMin.X -= 100f;
            quadMin.Y -= 80f;
            quadMin.Z -= 1f;
            quadMax.X += 100f;
            quadMax.Y += 80f;
            quadMax.Z += quad.Height;
            min = Vector3.Min(min, quadMin);
            max = Vector3.Max(max, quadMax);
        }

        private static bool IsSpecialBlendMap(short worldIndex)
        {
            return worldIndex == 63 || worldIndex == 66;
        }

        private static bool IsGrassDisabledWorld(short worldIndex)
        {
            return worldIndex == 7 || worldIndex == 67 ||
                   (worldIndex >= 11 && worldIndex <= 17) || worldIndex == 52;
        }

        private void DisposeBatches()
        {
            for (int i = 0; i < _batches.Count; i++)
                _batches[i].Dispose();
            _batches.Clear();
        }

        public void Dispose()
        {
            DisposeBatches();
            _additiveEffect?.Dispose();
            _additiveEffect = null;
        }
    }
}
