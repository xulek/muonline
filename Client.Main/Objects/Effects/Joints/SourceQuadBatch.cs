#nullable enable
using System;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects.Joints
{
    /// <summary>
    /// Allocation-free world-space quad batch used by the SourceMain5.2 skill ports to draw
    /// sprites (RenderSprite), joint tails (RenderJoints) and jagged lightning bolts with one
    /// shared <see cref="BasicEffect"/>. One batch is owned by one effect: call
    /// <see cref="Initialize"/> from LoadContent, then Begin / Add* / End from DrawAfter.
    /// </summary>
    internal sealed class SourceQuadBatch : IDisposable
    {
        public enum TrailShade
        {
            /// <summary>glColor3fv(o-&gt;Light) for the whole joint.</summary>
            Flat,
            /// <summary>BITMAP_FLARE_FORCE sub 0-4: Light * (NumTails - 1 - j) / MaxTails * 2.</summary>
            FlareForce,
            /// <summary>BITMAP_JOINT_FORCE sub 0: Light * (MaxTails - j) / MaxTails * 2.</summary>
            JointForce
        }

        [Flags]
        public enum TrailFaces
        {
            /// <summary>RENDER_FACE_ONE: quad through tail corners 2/3 (local Z axis).</summary>
            One = 1,
            /// <summary>RENDER_FACE_TWO: quad through tail corners 0/1 (local X axis).</summary>
            Two = 2,
            Both = One | Two
        }

        private readonly VertexPositionColorTexture[] _vertices;
        private readonly short[] _indices;
        private readonly int _maxQuads;
        private BasicEffect? _effect;
        private GraphicsDevice? _device;
        private Texture2D? _texture;
        private BlendState _blend = Blendings.OneOneAdditive;
        private DepthStencilState _depth = DepthStencilState.DepthRead;
        private SamplerState _sampler = SamplerState.LinearClamp;
        private int _quadCount;
        private bool _begun;
        private Vector3? _flatColor;

        public SourceQuadBatch(int maxQuads)
        {
            _maxQuads = Math.Max(1, maxQuads);
            _vertices = new VertexPositionColorTexture[_maxQuads * 4];
            _indices = QuadIndexCache.Get(_maxQuads);
        }

        public bool IsReady => _effect != null;

        public void Initialize(GraphicsDevice device)
        {
            if (_effect != null || device == null)
                return;

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

        public bool Begin(Texture2D? texture, BlendState blend, DepthStencilState depth, SamplerState sampler)
        {
            if (_effect == null || texture == null || texture.IsDisposed)
                return false;

            _texture = texture;
            _blend = blend;
            _depth = depth;
            _sampler = sampler;
            _quadCount = 0;
            _begun = true;
            _flatColor = null;
            return true;
        }

        /// <summary>
        /// Like <see cref="Begin"/>, but every quad outputs <paramref name="flatColor"/> with the texture alpha
        /// (BasicEffect fog forced to 1). Used to split GL_TEXTURE_ENV_MODE GL_ADD (<c>rgb = tex + Light</c>) into
        /// an alpha pass plus a <c>Light * alpha</c> pass.
        /// </summary>
        public bool BeginFlatColor(Texture2D? texture, BlendState blend, DepthStencilState depth, SamplerState sampler, Vector3 flatColor)
        {
            if (!Begin(texture, blend, depth, sampler))
                return false;
            _flatColor = flatColor;
            return true;
        }

        /// <summary>
        /// RenderSprite under glTexEnvi(GL_ADD) + EnableAlphaBlend3: <c>dst = (tex + light) * a + dst * (1 - a)</c>,
        /// drawn as tex * a (alpha blend) then +/- light * a. The per-channel clamp of <c>tex + light</c> is not reproduced.
        /// </summary>
        public void DrawSpriteTexEnvAdd(Texture2D? texture, Vector3 center, float scale, float light, float rotationDegrees)
        {
            if (Begin(texture, Blendings.Alpha, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                AddSprite(center, scale, Vector3.One, rotationDegrees);
                End();
            }
            DrawSpriteTexEnvAddLight(texture, center, scale, light, rotationDegrees);
        }

        /// <summary>The <c>+/- light * alpha</c> half of <see cref="DrawSpriteTexEnvAdd"/> (tex * a batched by the caller).</summary>
        public void DrawSpriteTexEnvAddLight(Texture2D? texture, Vector3 center, float scale, float light, float rotationDegrees)
        {
            if (light == 0f)
                return;
            BlendState blend = light > 0f ? AlphaAdd : AlphaReverseSubtract;
            if (BeginFlatColor(texture, blend, DepthStencilState.DepthRead, SamplerState.LinearClamp, new Vector3(MathF.Min(1f, MathF.Abs(light)))))
            {
                AddSprite(center, scale, Vector3.One, rotationDegrees);
                End();
            }
        }

        private static readonly BlendState AlphaAdd = new()
        {
            ColorSourceBlend = Blend.SourceAlpha,
            ColorDestinationBlend = Blend.One,
            AlphaSourceBlend = Blend.Zero,
            AlphaDestinationBlend = Blend.One,
        };

        private static readonly BlendState AlphaReverseSubtract = new()
        {
            ColorBlendFunction = BlendFunction.ReverseSubtract,
            ColorSourceBlend = Blend.SourceAlpha,
            ColorDestinationBlend = Blend.One,
            AlphaBlendFunction = BlendFunction.Add,
            AlphaSourceBlend = Blend.Zero,
            AlphaDestinationBlend = Blend.One,
        };

        public void End()
        {
            Flush();
            _begun = false;
            _texture = null;
        }

        // ----------------------------------------------------------------- primitives

        public void AddQuad(
            Vector3 p0, Vector2 uv0,
            Vector3 p1, Vector2 uv1,
            Vector3 p2, Vector2 uv2,
            Vector3 p3, Vector2 uv3,
            Color color)
            => AddQuad(p0, uv0, p1, uv1, p2, uv2, p3, uv3, color, color);

        /// <summary>Quad with <paramref name="color01"/> on p0/p1 and <paramref name="color23"/> on p2/p3.</summary>
        public void AddQuad(
            Vector3 p0, Vector2 uv0,
            Vector3 p1, Vector2 uv1,
            Vector3 p2, Vector2 uv2,
            Vector3 p3, Vector2 uv3,
            Color color01,
            Color color23)
        {
            if (!_begun)
                return;

            if (_quadCount >= _maxQuads)
                Flush();

            int v = _quadCount * 4;
            _vertices[v] = new VertexPositionColorTexture(p0, color01, uv0);
            _vertices[v + 1] = new VertexPositionColorTexture(p1, color01, uv1);
            _vertices[v + 2] = new VertexPositionColorTexture(p2, color23, uv2);
            _vertices[v + 3] = new VertexPositionColorTexture(p3, color23, uv3);
            _quadCount++;
        }

        /// <summary>
        /// RenderSprite: camera-facing quad of <c>texture.Width * scale</c> world units,
        /// rotated by <paramref name="rotationDegrees"/> around the view axis.
        /// </summary>
        public void AddSprite(Vector3 center, float scale, Vector3 light, float rotationDegrees = 0f)
        {
            if (!_begun || _texture == null || scale <= 0f)
                return;

            float half = _texture.Width * scale * 0.5f;
            float halfHeight = _texture.Height * scale * 0.5f;
            AddBillboard(center, half, halfHeight, MathHelper.ToRadians(rotationDegrees), ToColor(light));
        }

        public void AddBillboard(Vector3 center, float halfWidth, float halfHeight, float rotationRadians, Color color)
        {
            Vector3 right = Camera.Instance.Right;
            Vector3 up = Camera.Instance.Up;
            if (rotationRadians != 0f)
            {
                float c = MathF.Cos(rotationRadians);
                float s = MathF.Sin(rotationRadians);
                Vector3 r = right * c + up * s;
                up = up * c - right * s;
                right = r;
            }

            right *= halfWidth;
            up *= halfHeight;

            AddQuad(
                center - right - up, new Vector2(0f, 1f),
                center + right - up, new Vector2(1f, 1f),
                center + right + up, new Vector2(1f, 0f),
                center - right + up, new Vector2(0f, 0f),
                color);
        }

        /// <summary>
        /// RenderSprite with a texture region (sprite sheets: <c>Frame % 4 * 0.25</c>, ...): camera-facing quad of
        /// <paramref name="width"/> x <paramref name="height"/> world units showing UV
        /// [<paramref name="u"/>, <paramref name="u"/> + <paramref name="uWidth"/>] x [<paramref name="v"/>, <paramref name="v"/> + <paramref name="vHeight"/>].
        /// </summary>
        public void AddSpriteRegion(Vector3 center, float width, float height, Vector3 light, float rotationDegrees,
            float u, float v, float uWidth, float vHeight)
        {
            if (!_begun || width <= 0f || height <= 0f)
                return;

            Vector3 right = Camera.Instance.Right;
            Vector3 up = Camera.Instance.Up;
            if (rotationDegrees != 0f)
            {
                float rad = MathHelper.ToRadians(rotationDegrees);
                float c = MathF.Cos(rad);
                float sn = MathF.Sin(rad);
                Vector3 r = right * c + up * sn;
                up = up * c - right * sn;
                right = r;
            }

            right *= width * 0.5f;
            up *= height * 0.5f;
            Color color = ToColor(light);
            AddQuad(
                center - right - up, new Vector2(u, v + vHeight),
                center + right - up, new Vector2(u + uWidth, v + vHeight),
                center + right + up, new Vector2(u + uWidth, v),
                center - right + up, new Vector2(u, v),
                color);
        }

        /// <summary>Texture of the current Begin (for sprite sizes in texture pixels).</summary>
        public Texture2D? CurrentTexture => _texture;

        /// <summary>Flat quad lying on the XY plane (ground decals such as BITMAP_MAGIC).</summary>
        public void AddGroundQuad(Vector3 center, float halfSize, float rotationRadians, Color color)
        {
            float c = MathF.Cos(rotationRadians) * halfSize;
            float s = MathF.Sin(rotationRadians) * halfSize;
            Vector3 axisX = new(c, s, 0f);
            Vector3 axisY = new(-s, c, 0f);

            AddQuad(
                center - axisX - axisY, new Vector2(0f, 0f),
                center + axisX - axisY, new Vector2(1f, 0f),
                center + axisX + axisY, new Vector2(1f, 1f),
                center - axisX + axisY, new Vector2(0f, 1f),
                color);
        }

        /// <summary>
        /// RenderTerrainAlphaBitmap (ZzzLodTerrain.cpp): decal centred on (<paramref name="xf"/>,
        /// <paramref name="yf"/>), <paramref name="sizeX"/> x <paramref name="sizeY"/> tiles, draped over
        /// the terrain tiles <paramref name="height"/> units above the ground, UV rotated by
        /// <paramref name="rotationDegrees"/> around the centre. Tiles lying completely outside the
        /// texture are skipped.
        /// </summary>
        public void AddTerrainDecal(
            Client.Main.Controls.TerrainControl? terrain,
            float xf,
            float yf,
            float sizeX,
            float sizeY,
            Vector3 light,
            float rotationDegrees = 0f,
            float alpha = 1f,
            float height = 5f)
        {
            if (!_begun || terrain == null || sizeX <= 0f || sizeY <= 0f)
                return;

            float mxf = xf / Constants.TERRAIN_SCALE;
            float myf = yf / Constants.TERRAIN_SCALE;
            int mxi = (int)mxf;
            int myi = (int)myf;

            float size = sizeX >= sizeY ? sizeX : sizeY;
            float texU = (mxi - mxf) + 0.5f * size;
            float texV = (myi - myf) + 0.5f * size;
            float texScale = 1f / size;
            int range = (int)size + 1;
            float aspect = sizeX / sizeY;

            float rotation = MathHelper.ToRadians(rotationDegrees);
            float cos = MathF.Cos(rotation);
            float sin = MathF.Sin(rotation);

            Color color = ToColor(light);
            if (alpha != 1f)
                color.A = (byte)(MathHelper.Clamp(alpha, 0f, 1f) * 255f);

            for (int y = -range; y <= range; y++)
            {
                int tileY = myi + y;
                if (tileY < 0 || tileY >= Constants.TERRAIN_SIZE - 1)
                    continue;

                for (int x = -range; x <= range; x++)
                {
                    int tileX = mxi + x;
                    if (tileX < 0 || tileX >= Constants.TERRAIN_SIZE - 1)
                        continue;

                    Vector2 uv0 = DecalUv((texU + x) * texScale, (texV + y) * texScale, cos, sin, aspect);
                    Vector2 uv1 = DecalUv((texU + x + 1f) * texScale, (texV + y) * texScale, cos, sin, aspect);
                    Vector2 uv2 = DecalUv((texU + x + 1f) * texScale, (texV + y + 1f) * texScale, cos, sin, aspect);
                    Vector2 uv3 = DecalUv((texU + x) * texScale, (texV + y + 1f) * texScale, cos, sin, aspect);

                    if ((uv0.X < 0f && uv1.X < 0f && uv2.X < 0f && uv3.X < 0f) ||
                        (uv0.X > 1f && uv1.X > 1f && uv2.X > 1f && uv3.X > 1f) ||
                        (uv0.Y < 0f && uv1.Y < 0f && uv2.Y < 0f && uv3.Y < 0f) ||
                        (uv0.Y > 1f && uv1.Y > 1f && uv2.Y > 1f && uv3.Y > 1f))
                    {
                        continue;
                    }

                    float sx = tileX * Constants.TERRAIN_SCALE;
                    float sy = tileY * Constants.TERRAIN_SCALE;
                    float ex = sx + Constants.TERRAIN_SCALE;
                    float ey = sy + Constants.TERRAIN_SCALE;

                    AddQuad(
                        new Vector3(sx, sy, terrain.RequestTerrainHeight(sx, sy) + height), uv0,
                        new Vector3(ex, sy, terrain.RequestTerrainHeight(ex, sy) + height), uv1,
                        new Vector3(ex, ey, terrain.RequestTerrainHeight(ex, ey) + height), uv2,
                        new Vector3(sx, ey, terrain.RequestTerrainHeight(sx, ey) + height), uv3,
                        color);
                }
            }
        }

        private static Vector2 DecalUv(float u, float v, float cos, float sin, float aspect)
        {
            u -= 0.5f;
            v -= 0.5f;
            return new Vector2((u * cos - v * sin) * aspect + 0.5f, u * sin + v * cos + 0.5f);
        }

        /// <summary>
        /// RenderJoints for one joint: quads between consecutive tails with the C++ U mapping
        /// <c>Light1 = (NumTails - j) / uDenominator - scroll</c>.
        /// <paramref name="faceTwoUOffset"/> is added to U of the RENDER_FACE_TWO quad only
        /// (BITMAP_JOINT_THUNDER: <c>L1 += Scroll * 2</c>). <paramref name="reverseUV"/> is o-&gt;m_byReverseUV:
        /// 1 = V swapped (same as <paramref name="reverseV"/>), 2 = <c>L = 1 - L</c>, 3 = <c>L = 1 - j / (MaxTails - 1)</c>.
        /// </summary>
        public void AddTrail(
            SourceTrail trail,
            TrailFaces faces,
            Vector3 light,
            TrailShade shade,
            float uDenominator,
            float scroll = 0f,
            bool reverseV = false,
            float faceTwoUOffset = 0f,
            int reverseUV = 0)
        {
            if (reverseUV == 1)
                reverseV = true;
            int segments = trail.SegmentCount;
            if (!_begun || segments <= 0)
                return;

            float denominator = MathF.Max(uDenominator, 0.0001f);
            float v1 = reverseV ? 1f : 0f;
            float v2 = reverseV ? 0f : 1f;
            Color flat = ToColor(light);

            for (int j = 0; j < segments; j++)
            {
                float l1, l2;
                if (reverseUV == 3)
                {
                    l1 = 1f - j / (float)(trail.MaxTails - 1);
                    l2 = 1f - (j + 1) / (float)(trail.MaxTails - 1);
                }
                else
                {
                    l1 = (segments - j) / denominator - scroll;
                    l2 = (segments - (j + 1)) / denominator - scroll;
                }
                if (reverseUV == 2)
                {
                    l1 = 1f - l1;
                    l2 = 1f - l2;
                }

                Color color = flat;
                if (shade == TrailShade.FlareForce)
                    color = ToColor(light * ((segments - 1 - j) / (float)trail.MaxTails * 2f));
                else if (shade == TrailShade.JointForce)
                    color = ToColor(light * ((trail.MaxTails - j) / (float)trail.MaxTails * 2f));

                if ((faces & TrailFaces.One) != 0)
                {
                    AddQuad(
                        trail.Get(j, 2), new Vector2(l1, v2),
                        trail.Get(j, 3), new Vector2(l1, v1),
                        trail.Get(j + 1, 3), new Vector2(l2, v1),
                        trail.Get(j + 1, 2), new Vector2(l2, v2),
                        color);
                }

                if ((faces & TrailFaces.Two) != 0)
                {
                    float t1 = l1 + faceTwoUOffset;
                    float t2 = l2 + faceTwoUOffset;
                    AddQuad(
                        trail.Get(j, 0), new Vector2(t1, v1),
                        trail.Get(j, 1), new Vector2(t1, v2),
                        trail.Get(j + 1, 1), new Vector2(t2, v2),
                        trail.Get(j + 1, 0), new Vector2(t2, v1),
                        color);
                }
            }
        }

        /// <summary>
        /// Jagged camera-facing lightning strip from <paramref name="start"/> to
        /// <paramref name="end"/> (visual stand-in for short-lived BITMAP_JOINT_THUNDER joints).
        /// </summary>
        public void AddBolt(Vector3 start, Vector3 end, int segments, float jitter, float width, Color color, int seed)
        {
            if (!_begun || segments <= 0)
                return;

            Vector3 axis = end - start;
            float length = axis.Length();
            if (length < 0.001f)
                return;

            Vector3 viewDir = Camera.Instance.Target - Camera.Instance.Position;
            Vector3 side = Vector3.Cross(axis, viewDir);
            if (side.LengthSquared() < 0.0001f)
                side = Vector3.Cross(axis, Vector3.UnitX);
            side.Normalize();
            Vector3 jitterAxis = Vector3.Cross(axis / length, side);

            uint state = (uint)seed * 2654435761u + 1u;
            Vector3 previous = start;
            float halfWidth = width * 0.5f;
            float scroll = 0f;

            for (int i = 1; i <= segments; i++)
            {
                float t = i / (float)segments;
                Vector3 point = Vector3.Lerp(start, end, t);
                if (i < segments)
                {
                    point += side * (NextSigned(ref state) * jitter);
                    point += jitterAxis * (NextSigned(ref state) * jitter * 0.5f);
                }

                Vector3 segmentSide = Vector3.Cross(point - previous, viewDir);
                if (segmentSide.LengthSquared() < 0.0001f)
                    segmentSide = side;
                else
                    segmentSide.Normalize();
                segmentSide *= halfWidth;

                float u0 = scroll;
                float u1 = scroll + 1f / segments;
                AddQuad(
                    previous - segmentSide, new Vector2(u0, 0f),
                    previous + segmentSide, new Vector2(u0, 1f),
                    point + segmentSide, new Vector2(u1, 1f),
                    point - segmentSide, new Vector2(u1, 0f),
                    color);

                scroll = u1;
                previous = point;
            }
        }

        // ----------------------------------------------------------------- helpers

        public static Color ToColor(Vector3 light)
        {
            light = Vector3.Clamp(light, Vector3.Zero, Vector3.One);
            return new Color(light.X, light.Y, light.Z, 1f);
        }

        private static float NextSigned(ref uint state)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return (state & 0xFFFF) / 32767.5f - 1f;
        }

        private void Flush()
        {
            if (_quadCount <= 0 || _effect == null || _device == null || _texture == null || _texture.IsDisposed)
            {
                _quadCount = 0;
                return;
            }

            GraphicsDevice device = _device;
            BlendState previousBlend = device.BlendState;
            DepthStencilState previousDepth = device.DepthStencilState;
            RasterizerState previousRasterizer = device.RasterizerState;
            SamplerState previousSampler = device.SamplerStates[0];

            try
            {
                device.BlendState = _blend;
                device.DepthStencilState = _depth;
                device.RasterizerState = RasterizerState.CullNone;
                device.SamplerStates[0] = _sampler;

                _effect.Texture = _texture;
                _effect.View = Camera.Instance.View;
                _effect.Projection = Camera.Instance.Projection;
                _effect.DiffuseColor = Vector3.One;
                _effect.Alpha = 1f;
                if (_flatColor is { } flat)
                {
                    _effect.FogEnabled = true;
                    _effect.FogColor = flat;
                    _effect.FogStart = -2f;
                    _effect.FogEnd = -1f;
                }
                else
                {
                    _effect.FogEnabled = false;
                }
                _effect.CurrentTechnique.Passes[0].Apply();

                device.DrawUserIndexedPrimitives(
                    PrimitiveType.TriangleList,
                    _vertices,
                    0,
                    _quadCount * 4,
                    _indices,
                    0,
                    _quadCount * 2);
            }
            finally
            {
                device.BlendState = previousBlend;
                device.DepthStencilState = previousDepth;
                device.RasterizerState = previousRasterizer;
                device.SamplerStates[0] = previousSampler;
                _quadCount = 0;
                if (_flatColor != null)
                    _effect.FogEnabled = false;
            }
        }

        public void Dispose()
        {
            _effect?.Dispose();
            _effect = null;
            _device = null;
            _texture = null;
        }
    }
}
