using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Threading.Tasks;

namespace Client.Main.Objects.Worlds.Icarus
{
    /// <summary>
    /// Low-cost persistent and animated cloud shell for Icarus.
    ///
    /// Cloud patches are generated deterministically from absolute world cells. Each
    /// layer drifts independently while its anchor follows the inverse drift, so only
    /// a distant edge row is replaced and overlapping patches remain continuous.
    /// </summary>
    public sealed class IcarusCloudLayer : WorldObject
    {
        private const int LayerCount = 3;
        private const int GridRadius = 4;
        private const int GridDiameter = GridRadius * 2 + 1;
        private const int PatchesPerLayer = GridDiameter * GridDiameter;
        private const int PatchCount = LayerCount * PatchesPerLayer;
        private const int VerticesPerPatch = 4;
        private const int IndicesPerPatch = 6;
        private const int IndicesPerLayer = PatchesPerLayer * IndicesPerPatch;
        private const float CellSize = 760f;
        private const double MaxAnimationStepSeconds = 0.1;

        private static readonly float[] LayerHeights = { -100f, 15f, 145f };
        private static readonly float[] LayerBaseSizes = { 1250f, 980f, 760f };
        private static readonly float[] LayerJitterFactors = { 0.18f, 0.34f, 0.42f };
        private static readonly Vector2[] LayerDriftVelocity =
        {
            new(5.5f, 2.0f),
            new(-8.0f, 3.5f),
            new(11.0f, -4.5f)
        };
        private static readonly float[] LayerVerticalAmplitude = { 5f, 9f, 13f };
        private static readonly float[] LayerVerticalFrequency = { 0.17f, 0.23f, 0.29f };
        private static readonly float[] LayerAlphaFrequency = { 0.24f, 0.31f, 0.38f };
        private static readonly BlendState CloudBlendState = new()
        {
            ColorBlendFunction = BlendFunction.Add,
            ColorSourceBlend = Blend.SourceAlpha,
            ColorDestinationBlend = Blend.One,
            AlphaBlendFunction = BlendFunction.Add,
            AlphaSourceBlend = Blend.One,
            AlphaDestinationBlend = Blend.One
        };
        private static readonly Color[] LayerColors =
        {
            new(0.50f, 0.58f, 0.72f, 0.28f),
            new(0.66f, 0.73f, 0.86f, 0.34f),
            new(0.78f, 0.84f, 0.96f, 0.28f)
        };

        private readonly VertexPositionColorTexture[] _vertices =
            new VertexPositionColorTexture[PatchCount * VerticesPerPatch];
        private static readonly short[] Indices = QuadIndexCache.Get(PatchCount);
        private readonly Vector4[] _patchBaseColors = new Vector4[PatchCount];
        private readonly float[] _patchPulsePhases = new float[PatchCount];
        private readonly int[] _anchorCellX = new int[LayerCount];
        private readonly int[] _anchorCellY = new int[LayerCount];

        private Texture2D _cloudTexture;
        private BasicEffect _effect;
        private double _animationTimeSeconds;
        private bool _verticesDirty = true;

        public override bool ForceVisibleInWorld => true;

        public IcarusCloudLayer()
        {
            IsTransparent = true;
            AffectedByTransparency = false;
            DepthState = GraphicsManager.ReadOnlyDepth;
            RenderOrder = int.MinValue;
            BoundingBoxLocal = new BoundingBox(
                new Vector3(-4600f, -4600f, -300f),
                new Vector3(4600f, 4600f, 400f));

            Array.Fill(_anchorCellX, int.MinValue);
            Array.Fill(_anchorCellY, int.MinValue);
        }

        public override async Task Load()
        {
            try
            {
                _cloudTexture = await TextureLoader.Instance.PrepareAndGetTexture(
                    "Effect/clouds.jpg");
            }
            catch
            {
                _cloudTexture = null;
            }

            _effect = new BasicEffect(GraphicsDevice)
            {
                TextureEnabled = true,
                VertexColorEnabled = true,
                LightingEnabled = false,
                FogEnabled = false,
                World = Matrix.Identity,
                // The original clouds are additive BITMAP_CLOUD particles with light 0.1: a dim, grey-blue haze.
                DiffuseColor = new Vector3(0.8f, 0.86f, 0.98f),
                Alpha = 1f
            };

            UpdateAnchors(force: true);
            await base.Load();
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            double elapsedSeconds = gameTime.ElapsedGameTime.TotalSeconds;
            if (elapsedSeconds > 0d)
                _animationTimeSeconds += Math.Min(elapsedSeconds, MaxAnimationStepSeconds);

            UpdateAnchors(force: false);
            UpdateAnimatedColors();
        }

        public override void Draw(GameTime gameTime)
        {
            // Transparent content is emitted only from DrawAfter.
        }

        public override void DrawAfter(GameTime gameTime)
        {
            if (!Visible || _cloudTexture == null || _effect == null)
                return;

            if (_verticesDirty)
            {
                RebuildVertices();
                UpdateAnimatedColors();
            }

            BlendState previousBlend = GraphicsDevice.BlendState;
            DepthStencilState previousDepth = GraphicsDevice.DepthStencilState;
            RasterizerState previousRasterizer = GraphicsDevice.RasterizerState;
            SamplerState previousSampler = GraphicsDevice.SamplerStates[0];

            try
            {
                GraphicsDevice.BlendState = CloudBlendState;
                GraphicsDevice.DepthStencilState = GraphicsManager.ReadOnlyDepth;
                GraphicsDevice.RasterizerState = RasterizerState.CullNone;
                GraphicsDevice.SamplerStates[0] = SamplerState.LinearClamp;

                _effect.Texture = _cloudTexture;
                _effect.View = Camera.Instance.View;
                _effect.Projection = Camera.Instance.Projection;

                for (int layer = 0; layer < LayerCount; layer++)
                {
                    Vector2 drift = GetLayerDrift(layer);
                    float verticalOffset = MathF.Sin(
                        (float)_animationTimeSeconds * LayerVerticalFrequency[layer]
                        + layer * 1.91f) * LayerVerticalAmplitude[layer];

                    _effect.World = Matrix.CreateTranslation(
                        drift.X,
                        drift.Y,
                        verticalOffset);

                    _effect.CurrentTechnique.Passes[0].Apply();
                    GraphicsDevice.DrawUserIndexedPrimitives(
                        PrimitiveType.TriangleList,
                        _vertices,
                        0,
                        _vertices.Length,
                        Indices,
                        layer * IndicesPerLayer,
                        PatchesPerLayer * 2);
                }
            }
            finally
            {
                _effect.World = Matrix.Identity;
                GraphicsDevice.SamplerStates[0] = previousSampler;
                GraphicsDevice.RasterizerState = previousRasterizer;
                GraphicsDevice.DepthStencilState = previousDepth;
                GraphicsDevice.BlendState = previousBlend;
            }
        }

        public override void Dispose()
        {
            _effect?.Dispose();
            _effect = null;
            _cloudTexture = null; // Owned by TextureLoader.
            base.Dispose();
        }

        private void UpdateAnchors(bool force)
        {
            Vector3 target = Camera.Instance.Target;
            bool changed = false;

            for (int layer = 0; layer < LayerCount; layer++)
            {
                Vector2 drift = GetLayerDrift(layer);

                // The rendered cell center is its absolute world position plus drift.
                // Following the inverse drift keeps the finite grid centered on the
                // camera while preserving the position of every overlapping cell.
                int nextCellX = (int)MathF.Floor(
                    (target.X - drift.X + CellSize * 0.5f) / CellSize);
                int nextCellY = (int)MathF.Floor(
                    (target.Y - drift.Y + CellSize * 0.5f) / CellSize);

                if (!force &&
                    nextCellX == _anchorCellX[layer] &&
                    nextCellY == _anchorCellY[layer])
                {
                    continue;
                }

                _anchorCellX[layer] = nextCellX;
                _anchorCellY[layer] = nextCellY;
                changed = true;
            }

            if (!changed)
                return;

            Position = target;
            _verticesDirty = true;
        }

        private void RebuildVertices()
        {
            int patchIndex = 0;

            for (int layer = 0; layer < LayerCount; layer++)
            {
                for (int relativeY = -GridRadius; relativeY <= GridRadius; relativeY++)
                {
                    int worldCellY = _anchorCellY[layer] + relativeY;

                    for (int relativeX = -GridRadius; relativeX <= GridRadius; relativeX++)
                    {
                        int worldCellX = _anchorCellX[layer] + relativeX;
                        uint randomState = CreateCellSeed(layer, worldCellX, worldCellY);

                        float jitterFactor = LayerJitterFactors[layer];
                        float jitterX = (NextFloat(ref randomState) - 0.5f) * CellSize * jitterFactor;
                        float jitterY = (NextFloat(ref randomState) - 0.5f) * CellSize * jitterFactor;
                        float baseSize = LayerBaseSizes[layer];
                        float sizeX = baseSize * (0.92f + NextFloat(ref randomState) * 0.20f);
                        float sizeY = baseSize * (0.84f + NextFloat(ref randomState) * 0.24f);
                        float rotation = NextFloat(ref randomState) * MathHelper.TwoPi;
                        float heightJitter = (NextFloat(ref randomState) - 0.5f) * 30f;
                        float intensity = 0.88f + NextFloat(ref randomState) * 0.16f;
                        Color baseColor = LayerColors[layer];
                        Vector4 patchColor = new(
                            baseColor.R / 255f * intensity,
                            baseColor.G / 255f * intensity,
                            baseColor.B / 255f * intensity,
                            baseColor.A / 255f);

                        Vector2 halfSize = new(sizeX * 0.5f, sizeY * 0.5f);
                        float cosine = MathF.Cos(rotation);
                        float sine = MathF.Sin(rotation);
                        Vector3 right = new(
                            cosine * halfSize.X,
                            sine * halfSize.X,
                            0f);
                        Vector3 up = new(
                            -sine * halfSize.Y,
                            cosine * halfSize.Y,
                            0f);
                        Vector3 center = new(
                            worldCellX * CellSize + jitterX,
                            worldCellY * CellSize + jitterY,
                            LayerHeights[layer] + heightJitter);

                        _patchBaseColors[patchIndex] = patchColor;
                        _patchPulsePhases[patchIndex] = NextFloat(ref randomState) * MathHelper.TwoPi;
                        WritePatchVertices(patchIndex++, center, right, up, patchColor);
                    }
                }
            }

            _verticesDirty = false;
        }

        private void UpdateAnimatedColors()
        {
            if (_verticesDirty)
                return;

            float time = (float)_animationTimeSeconds;

            for (int patch = 0; patch < PatchCount; patch++)
            {
                int layer = patch / PatchesPerLayer;
                float phase = _patchPulsePhases[patch];
                float primaryWave = MathF.Sin(time * LayerAlphaFrequency[layer] + phase);
                float secondaryWave = MathF.Sin(
                    time * (LayerAlphaFrequency[layer] * 0.43f)
                    + phase * 1.73f);
                float alphaMultiplier = MathHelper.Clamp(
                    0.88f + primaryWave * 0.10f + secondaryWave * 0.05f,
                    0.72f,
                    1.03f);
                float brightnessMultiplier = 0.97f + secondaryWave * 0.03f;
                Vector4 baseColor = _patchBaseColors[patch];
                Color animatedColor = new(
                    MathHelper.Clamp(baseColor.X * brightnessMultiplier, 0f, 1f),
                    MathHelper.Clamp(baseColor.Y * brightnessMultiplier, 0f, 1f),
                    MathHelper.Clamp(baseColor.Z * brightnessMultiplier, 0f, 1f),
                    MathHelper.Clamp(baseColor.W * alphaMultiplier, 0f, 1f));

                int vertex = patch * VerticesPerPatch;
                _vertices[vertex].Color = animatedColor;
                _vertices[vertex + 1].Color = animatedColor;
                _vertices[vertex + 2].Color = animatedColor;
                _vertices[vertex + 3].Color = animatedColor;
            }
        }

        private void WritePatchVertices(
            int patchIndex,
            Vector3 center,
            Vector3 right,
            Vector3 up,
            Vector4 color)
        {
            Color vertexColor = new(color.X, color.Y, color.Z, color.W);
            int vertex = patchIndex * VerticesPerPatch;
            _vertices[vertex] = new VertexPositionColorTexture(
                center - right - up,
                vertexColor,
                new Vector2(0f, 0f));
            _vertices[vertex + 1] = new VertexPositionColorTexture(
                center + right - up,
                vertexColor,
                new Vector2(1f, 0f));
            _vertices[vertex + 2] = new VertexPositionColorTexture(
                center + right + up,
                vertexColor,
                new Vector2(1f, 1f));
            _vertices[vertex + 3] = new VertexPositionColorTexture(
                center - right + up,
                vertexColor,
                new Vector2(0f, 1f));
        }

        private Vector2 GetLayerDrift(int layer)
        {
            return LayerDriftVelocity[layer] * (float)_animationTimeSeconds;
        }

        private static uint CreateCellSeed(int layer, int cellX, int cellY)
        {
            unchecked
            {
                uint hash = 2166136261u;
                hash = (hash ^ (uint)layer) * 16777619u;
                hash = (hash ^ (uint)cellX) * 16777619u;
                hash = (hash ^ (uint)cellY) * 16777619u;
                hash ^= hash >> 16;
                hash *= 0x7FEB352Du;
                hash ^= hash >> 15;
                hash *= 0x846CA68Bu;
                hash ^= hash >> 16;
                return hash == 0u ? 0xA341316Cu : hash;
            }
        }

        private static float NextFloat(ref uint state)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return (state & 0x00FFFFFFu) / 16777216f;
        }
    }
}
