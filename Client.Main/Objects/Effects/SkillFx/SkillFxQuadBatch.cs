#nullable enable
using System;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects.SkillFx
{
    /// <summary>
    /// Shared, allocation-free quad batch used by the SourceMain5.2 skill ports for
    /// RenderSprite billboards, RenderTerrainAlphaBitmap ground decals and joint tail ribbons.
    /// One texture/blend per Begin..End; flushes automatically when the scratch buffer fills.
    /// Render-thread only (static scratch state).
    /// </summary>
    public static class SkillFxQuadBatch
    {
        private const int MaxQuads = 512;

        private static readonly VertexPositionColorTexture[] Vertices = new VertexPositionColorTexture[MaxQuads * 4];
        private static readonly short[] Indices = CreateIndices();

        private static BasicEffect? _effect;
        private static GraphicsDevice? _device;
        private static Texture2D? _texture;
        private static BlendState _blend = Blendings.OneOneAdditive;
        private static int _quadCount;
        private static bool _active;

        private static short[] CreateIndices()
        {
            var indices = new short[MaxQuads * 6];
            for (int q = 0; q < MaxQuads; q++)
            {
                int v = q * 4;
                int i = q * 6;
                indices[i] = (short)v;
                indices[i + 1] = (short)(v + 1);
                indices[i + 2] = (short)(v + 2);
                indices[i + 3] = (short)v;
                indices[i + 4] = (short)(v + 2);
                indices[i + 5] = (short)(v + 3);
            }
            return indices;
        }

        /// <summary>Starts a batch. Returns false when nothing can be drawn.</summary>
        public static bool Begin(Texture2D? texture, BlendState? blend = null)
        {
            if (texture == null || texture.IsDisposed || Camera.Instance == null)
                return false;

            var device = GraphicsManager.Instance.GraphicsDevice;
            if (device == null)
                return false;

            if (_effect == null || !ReferenceEquals(_device, device))
            {
                _effect?.Dispose();
                _device = device;
                _effect = new BasicEffect(device)
                {
                    TextureEnabled = true,
                    VertexColorEnabled = true,
                    LightingEnabled = false,
                    FogEnabled = false,
                    World = Matrix.Identity
                };
            }

            _texture = texture;
            _blend = blend ?? Blendings.OneOneAdditive;
            _quadCount = 0;
            _active = true;
            return true;
        }

        /// <summary>Camera-facing sprite (RenderSprite). Sizes are world units.</summary>
        public static void Billboard(Vector3 center, float halfWidth, float halfHeight, float rotation, Color color,
            float u0 = 0f, float v0 = 0f, float u1 = 1f, float v1 = 1f)
        {
            if (!_active || halfWidth <= 0f || halfHeight <= 0f)
                return;

            var camera = Camera.Instance;
            Vector3 right = camera.Right;
            Vector3 up = camera.Up;
            if (rotation != 0f)
            {
                float c = MathF.Cos(rotation);
                float s = MathF.Sin(rotation);
                Vector3 r = right * c + up * s;
                up = up * c - right * s;
                right = r;
            }

            right *= halfWidth;
            up *= halfHeight;
            Quad(center - right + up, center + right + up, center + right - up, center - right - up,
                color, color, u0, v0, u1, v1);
        }

        /// <summary>Flat decal on the XY plane (RenderTerrainAlphaBitmap approximation).</summary>
        public static void Ground(Vector3 center, float halfSize, float rotation, Color color)
        {
            if (!_active || halfSize <= 0f)
                return;

            float c = MathF.Cos(rotation) * halfSize;
            float s = MathF.Sin(rotation) * halfSize;
            Vector3 ax = new(c, s, 0f);
            Vector3 ay = new(-s, c, 0f);
            Quad(center - ax - ay, center + ax - ay, center + ax + ay, center - ax + ay, color, color, 0f, 0f, 1f, 1f);
        }

        /// <summary>Camera-facing ribbon segment between two points (joint tail).</summary>
        public static void Segment(Vector3 from, Vector3 to, float halfWidthFrom, float halfWidthTo,
            Color colorFrom, Color colorTo, float uFrom, float uTo)
        {
            if (!_active)
                return;

            Vector3 dir = to - from;
            Vector3 toCamera = Camera.Instance.Position - (from + to) * 0.5f;
            Vector3 side = Vector3.Cross(dir, toCamera);
            float len = side.Length();
            if (len < 0.0001f)
                return;
            side /= len;

            Vector3 a = side * halfWidthFrom;
            Vector3 b = side * halfWidthTo;
            int vtx = Reserve();
            if (vtx < 0)
                return;
            Vertices[vtx] = new VertexPositionColorTexture(from - a, colorFrom, new Vector2(uFrom, 0f));
            Vertices[vtx + 1] = new VertexPositionColorTexture(from + a, colorFrom, new Vector2(uFrom, 1f));
            Vertices[vtx + 2] = new VertexPositionColorTexture(to + b, colorTo, new Vector2(uTo, 1f));
            Vertices[vtx + 3] = new VertexPositionColorTexture(to - b, colorTo, new Vector2(uTo, 0f));
        }

        public static void Quad(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Color c0, Color c1,
            float u0, float v0, float u1, float v1)
        {
            int vtx = Reserve();
            if (vtx < 0)
                return;
            Vertices[vtx] = new VertexPositionColorTexture(p0, c0, new Vector2(u0, v0));
            Vertices[vtx + 1] = new VertexPositionColorTexture(p1, c0, new Vector2(u1, v0));
            Vertices[vtx + 2] = new VertexPositionColorTexture(p2, c1, new Vector2(u1, v1));
            Vertices[vtx + 3] = new VertexPositionColorTexture(p3, c1, new Vector2(u0, v1));
        }

        private static int Reserve()
        {
            if (!_active)
                return -1;
            if (_quadCount >= MaxQuads)
                Flush();
            return _quadCount++ * 4;
        }

        public static void End()
        {
            if (!_active)
                return;
            Flush();
            _active = false;
            _texture = null;
        }

        private static void Flush()
        {
            if (_quadCount == 0 || _effect == null || _device == null || _texture == null)
            {
                _quadCount = 0;
                return;
            }

            var device = _device;
            BlendState previousBlend = device.BlendState;
            DepthStencilState previousDepth = device.DepthStencilState;
            RasterizerState previousRasterizer = device.RasterizerState;
            SamplerState previousSampler = device.SamplerStates[0];
            try
            {
                device.BlendState = _blend;
                device.DepthStencilState = DepthStencilState.DepthRead;
                device.RasterizerState = RasterizerState.CullNone;
                device.SamplerStates[0] = SamplerState.LinearClamp;

                _effect.Texture = _texture;
                _effect.View = Camera.Instance.View;
                _effect.Projection = Camera.Instance.Projection;
                _effect.DiffuseColor = Vector3.One;
                _effect.Alpha = 1f;

                foreach (EffectPass pass in _effect.CurrentTechnique.Passes)
                {
                    pass.Apply();
                    device.DrawUserIndexedPrimitives(PrimitiveType.TriangleList, Vertices, 0, _quadCount * 4,
                        Indices, 0, _quadCount * 2);
                }
            }
            finally
            {
                device.BlendState = previousBlend;
                device.DepthStencilState = previousDepth;
                device.RasterizerState = previousRasterizer;
                device.SamplerStates[0] = previousSampler;
                _quadCount = 0;
            }
        }

        /// <summary>Converts a SourceMain light (0..n) into an additive vertex color.</summary>
        public static Color Light(Vector3 light, float scale = 1f)
        {
            return new Color(
                MathHelper.Clamp(light.X * scale, 0f, 1f),
                MathHelper.Clamp(light.Y * scale, 0f, 1f),
                MathHelper.Clamp(light.Z * scale, 0f, 1f),
                1f);
        }
    }
}
