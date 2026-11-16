#nullable enable
using System;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects.Support
{
    /// <summary>
    /// Shared immediate-mode quad batch for <see cref="SourceFxEffect"/> (main thread only).
    /// Mirrors the glBegin(GL_QUADS) style of the original RenderJoints / RenderSprite /
    /// RenderTerrainAlphaBitmap calls without per-frame allocations.
    /// </summary>
    internal static class SourceFxBatch
    {
        private const int MaxQuads = 4096;

        private static readonly VertexPositionColorTexture[] Vertices = new VertexPositionColorTexture[MaxQuads * 4];
        private static readonly short[] Indices = QuadIndexCache.Get(MaxQuads);
        private static int _quadCount;
        private static Texture2D? _texture;
        private static SamplerState _sampler = SamplerState.LinearClamp;
        private static BasicEffect? _effect;
        private static GraphicsDevice? _device;

        public static void Begin(GraphicsDevice device, BasicEffect effect, Texture2D texture, SamplerState sampler)
        {
            _device = device;
            _effect = effect;
            _texture = texture;
            _sampler = sampler;
            _quadCount = 0;
        }

        public static void Quad(
            Vector3 a, Vector2 uvA,
            Vector3 b, Vector2 uvB,
            Vector3 c, Vector2 uvC,
            Vector3 d, Vector2 uvD,
            Color color)
        {
            if (_texture == null)
                return;
            if (_quadCount >= MaxQuads)
                Flush();

            int v = _quadCount * 4;
            Vertices[v] = new VertexPositionColorTexture(a, color, uvA);
            Vertices[v + 1] = new VertexPositionColorTexture(b, color, uvB);
            Vertices[v + 2] = new VertexPositionColorTexture(c, color, uvC);
            Vertices[v + 3] = new VertexPositionColorTexture(d, color, uvD);
            _quadCount++;
        }

        /// <summary>RenderSprite: camera-facing quad, <paramref name="size"/> is the full world width.</summary>
        public static void Billboard(Vector3 center, float size, float rotationDeg, Color color)
        {
            var camera = Camera.Instance;
            if (camera == null || size <= 0f)
                return;

            float half = size * 0.5f;
            Vector3 right = camera.Right;
            Vector3 up = camera.Up;
            if (rotationDeg != 0f)
            {
                float r = MathHelper.ToRadians(rotationDeg);
                float cos = MathF.Cos(r);
                float sin = MathF.Sin(r);
                Vector3 r2 = right * cos + up * sin;
                up = up * cos - right * sin;
                right = r2;
            }

            right *= half;
            up *= half;
            Quad(
                center - right - up, new Vector2(0f, 1f),
                center + right - up, new Vector2(1f, 1f),
                center + right + up, new Vector2(1f, 0f),
                center - right + up, new Vector2(0f, 0f),
                color);
        }

        /// <summary>
        /// RenderTerrainAlphaBitmap approximation: flat ground quad spanning
        /// <paramref name="sizeTiles"/> terrain tiles, rotated by <paramref name="rotationDeg"/>.
        /// </summary>
        public static void Ground(Vector3 center, float sizeTiles, float rotationDeg, Color color)
        {
            if (sizeTiles <= 0f)
                return;

            float half = sizeTiles * 0.5f * Constants.TERRAIN_SCALE;
            float r = MathHelper.ToRadians(rotationDeg);
            float cos = MathF.Cos(r) * half;
            float sin = MathF.Sin(r) * half;
            Vector3 ax = new(cos, sin, 0f);
            Vector3 ay = new(-sin, cos, 0f);
            Vector3 p = center + new Vector3(0f, 0f, 5f);
            Quad(
                p - ax - ay, new Vector2(0f, 0f),
                p + ax - ay, new Vector2(1f, 0f),
                p + ax + ay, new Vector2(1f, 1f),
                p - ax + ay, new Vector2(0f, 1f),
                color);
        }

        public static void Flush()
        {
            if (_quadCount == 0 || _texture == null || _effect == null || _device == null)
            {
                _quadCount = 0;
                return;
            }

            _device.SamplerStates[0] = _sampler;
            _effect.Texture = _texture;
            foreach (EffectPass pass in _effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                _device.DrawUserIndexedPrimitives(
                    PrimitiveType.TriangleList,
                    Vertices,
                    0,
                    _quadCount * 4,
                    Indices,
                    0,
                    _quadCount * 2);
            }

            _quadCount = 0;
        }

        public static void End()
        {
            Flush();
            _texture = null;
            _effect = null;
            _device = null;
        }
    }
}
