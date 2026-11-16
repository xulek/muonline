#nullable enable
using System;
using Client.Main.Controls;
using Client.Main.Graphics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects.RageFighter
{
    /// <summary>
    /// Fixed-capacity world-space quad batch shared by the Rage Fighter skill effects.
    /// It covers the three SourceMain5.2 primitives those skills use besides models:
    /// RenderSprite billboards (particles / CreateSprite), RenderTerrainAlphaBitmap ground
    /// decals (clouds, magic circles, red floor glow) and tail ribbons of CreateJoint trails.
    /// Everything is drawn with GL_ONE/GL_ONE (EnableAlphaBlend) like the original.
    /// </summary>
    internal sealed class RageFxBatch : IDisposable
    {
        public const int MaxQuads = 1024;
        private const int DecalGrid = 4;

        private readonly VertexPositionColorTexture[] _vertices = new VertexPositionColorTexture[MaxQuads * 4];
        private static readonly short[] Indices = QuadIndexCache.Get(MaxQuads);
        private BasicEffect? _effect;
        private int _quads;

        public void Begin() => _quads = 0;

        public bool IsFull => _quads >= MaxQuads;

        /// <summary>Camera-facing sprite. Sizes are half extents in world units, rotation in degrees.</summary>
        public void Billboard(Vector3 center, float halfWidth, float halfHeight, float rotationDegrees, Vector3 light,
            float u0 = 0f, float v0 = 0f, float u1 = 1f, float v1 = 1f)
        {
            if (_quads >= MaxQuads || halfWidth <= 0f || halfHeight <= 0f)
                return;

            Camera camera = Camera.Instance;
            Vector3 right = camera.Right;
            Vector3 up = camera.Up;
            if (rotationDegrees != 0f)
            {
                float radians = MathHelper.ToRadians(rotationDegrees);
                float c = MathF.Cos(radians);
                float s = MathF.Sin(radians);
                Vector3 r = right * c + up * s;
                up = up * c - right * s;
                right = r;
            }

            right *= halfWidth;
            up *= halfHeight;
            Color color = ToColor(light);
            int v = _quads++ * 4;
            _vertices[v] = new VertexPositionColorTexture(center - right - up, color, new Vector2(u0, v1));
            _vertices[v + 1] = new VertexPositionColorTexture(center + right - up, color, new Vector2(u1, v1));
            _vertices[v + 2] = new VertexPositionColorTexture(center + right + up, color, new Vector2(u1, v0));
            _vertices[v + 3] = new VertexPositionColorTexture(center - right + up, color, new Vector2(u0, v0));
        }

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color colorAB, Color colorCD,
            float u0, float u1)
        {
            if (_quads >= MaxQuads)
                return;

            int v = _quads++ * 4;
            _vertices[v] = new VertexPositionColorTexture(a, colorAB, new Vector2(u0, 0f));
            _vertices[v + 1] = new VertexPositionColorTexture(b, colorAB, new Vector2(u0, 1f));
            _vertices[v + 2] = new VertexPositionColorTexture(c, colorCD, new Vector2(u1, 1f));
            _vertices[v + 3] = new VertexPositionColorTexture(d, colorCD, new Vector2(u1, 0f));
        }

        /// <summary>
        /// RenderTerrainAlphaBitmap(tex, x, y, size, size, light, rotation): the texture spans
        /// <paramref name="sizeTiles"/> terrain tiles and follows the ground height.
        /// </summary>
        public void GroundDecal(TerrainControl? terrain, Vector3 center, float sizeTiles, float rotationDegrees,
            Vector3 light, float heightOffset = 2f)
        {
            if (_quads + DecalGrid * DecalGrid > MaxQuads || sizeTiles <= 0f)
                return;

            float half = sizeTiles * Constants.TERRAIN_SCALE * 0.5f;
            float radians = MathHelper.ToRadians(rotationDegrees);
            float c = MathF.Cos(radians);
            float s = MathF.Sin(radians);
            Color color = ToColor(light);
            float step = 1f / DecalGrid;

            for (int y = 0; y < DecalGrid; y++)
            {
                for (int x = 0; x < DecalGrid; x++)
                {
                    float u0 = x * step;
                    float v0 = y * step;
                    float u1 = u0 + step;
                    float v1 = v0 + step;
                    int v = _quads++ * 4;
                    _vertices[v] = DecalVertex(terrain, center, half, c, s, u0, v0, color, heightOffset);
                    _vertices[v + 1] = DecalVertex(terrain, center, half, c, s, u1, v0, color, heightOffset);
                    _vertices[v + 2] = DecalVertex(terrain, center, half, c, s, u1, v1, color, heightOffset);
                    _vertices[v + 3] = DecalVertex(terrain, center, half, c, s, u0, v1, color, heightOffset);
                }
            }
        }

        private static VertexPositionColorTexture DecalVertex(TerrainControl? terrain, Vector3 center, float half,
            float c, float s, float u, float v, Color color, float heightOffset)
        {
            float lx = (u * 2f - 1f) * half;
            float ly = (v * 2f - 1f) * half;
            float wx = center.X + lx * c - ly * s;
            float wy = center.Y + lx * s + ly * c;
            float wz = terrain != null ? terrain.RequestTerrainHeight(wx, wy) : center.Z;
            return new VertexPositionColorTexture(new Vector3(wx, wy, wz + heightOffset), color, new Vector2(u, v));
        }

        public void Flush(GraphicsDevice device, Texture2D? texture)
        {
            int quads = _quads;
            _quads = 0;
            if (quads <= 0 || texture == null || texture.IsDisposed || device == null)
                return;

            _effect ??= new BasicEffect(device)
            {
                TextureEnabled = true,
                VertexColorEnabled = true,
                LightingEnabled = false,
                FogEnabled = false,
                World = Matrix.Identity
            };

            BlendState previousBlend = device.BlendState;
            DepthStencilState previousDepth = device.DepthStencilState;
            RasterizerState previousRasterizer = device.RasterizerState;
            SamplerState previousSampler = device.SamplerStates[0];

            try
            {
                device.BlendState = Blendings.OneOneAdditive;
                device.DepthStencilState = DepthStencilState.DepthRead;
                device.RasterizerState = RasterizerState.CullNone;
                device.SamplerStates[0] = SamplerState.LinearClamp;

                _effect.Texture = texture;
                _effect.View = Camera.Instance.View;
                _effect.Projection = Camera.Instance.Projection;
                _effect.CurrentTechnique.Passes[0].Apply();
                device.DrawUserIndexedPrimitives(PrimitiveType.TriangleList, _vertices, 0, quads * 4,
                    Indices, 0, quads * 2);
            }
            finally
            {
                device.BlendState = previousBlend;
                device.DepthStencilState = previousDepth;
                device.RasterizerState = previousRasterizer;
                device.SamplerStates[0] = previousSampler;
            }
        }

        public static Color ToColor(Vector3 light)
        {
            light = Vector3.Clamp(light, Vector3.Zero, Vector3.One);
            return new Color(light.X, light.Y, light.Z, 1f);
        }

        public void Dispose()
        {
            _effect?.Dispose();
            _effect = null;
        }
    }
}
