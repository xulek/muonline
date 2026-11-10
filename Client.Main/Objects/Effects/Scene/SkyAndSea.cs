#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Client.Main.Objects.Effects.Particles;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects.Scene
{
    /// <summary>
    /// The moon's path on open water: a mesh on the sea drawn with SceneWater.fx (Sheen), glints flashing in a band
    /// that runs from the viewer towards the light. The mesh follows the terrain under the water (the sea surface has
    /// bumps of a few dozen units that would cut a flat quad into patches); it is built once into a static buffer.
    /// </summary>
    internal sealed class SeaSheen : WorldObject
    {
        private const int Along = 64;
        private const int Across = 24;
        private const float Lift = 8f;

        private readonly Vector3 _near;
        private readonly Vector3 _far;
        private readonly Vector2 _start;
        private readonly Vector2 _dir;
        private readonly float _length;
        private readonly Vector2 _width;
        private VertexBuffer? _vertices;
        private IndexBuffer? _indices;

        public float Intensity { get; init; } = 1f;
        public Vector3 Tint { get; init; } = new(0.75f, 0.85f, 1f);

        /// <param name="near">Start of the path on the water; its Z is the sea level.</param>
        /// <param name="far">End of the path, under the light.</param>
        public SeaSheen(Vector3 near, Vector3 far, float nearWidth, float farWidth)
        {
            _near = near;
            _far = new Vector3(far.X, far.Y, near.Z);
            _start = new Vector2(near.X, near.Y);
            Vector2 along = new Vector2(far.X, far.Y) - _start;
            _length = along.Length();
            _dir = along / _length;
            _width = new Vector2(nearWidth, farWidth);
            IsTransparent = true;
            AffectedByTransparency = false;

            Position = (_near + _far) * 0.5f;
            float reach = _length * 0.5f + farWidth * 3f;
            BoundingBoxLocal = new BoundingBox(new Vector3(-reach, -reach, -100f), new Vector3(reach, reach, 200f));
        }

        public override WorldObjectRenderPolicy RenderPolicy => base.RenderPolicy.With(forceVisible: true);

        // Right after the opaque world: everything transparent on the water (wakes, spray) goes over it.
        public override float Depth => float.MaxValue;

        /// <summary>Grid over the band (2.5 half-widths each side), each vertex just above the highest ground around it.</summary>
        private bool Build(GraphicsDevice gd)
        {
            var terrain = World?.Terrain;
            if (terrain == null)
                return false;

            Vector3 side = new(-_dir.Y, _dir.X, 0f);
            Vector3 dir = new(_dir, 0f);
            var vertices = new VertexPositionColorTexture[(Along + 1) * (Across + 1)];
            for (int i = 0; i <= Along; i++)
            {
                float k = i / (float)Along;
                Vector3 center = Vector3.Lerp(_near - dir * 200f, _far, k);
                float half = MathHelper.Lerp(_width.X, _width.Y, k) * 2.5f;
                float step = MathF.Max(half * 2f / Across, _length / Along);
                for (int j = 0; j <= Across; j++)
                {
                    Vector3 p = center + side * MathHelper.Lerp(-half, half, j / (float)Across);
                    float h = _near.Z;
                    for (int s = 0; s < 5; s++)
                    {
                        float ox = s == 1 ? step * 0.5f : s == 2 ? -step * 0.5f : 0f;
                        float oy = s == 3 ? step * 0.5f : s == 4 ? -step * 0.5f : 0f;
                        h = MathF.Max(h, terrain.RequestTerrainHeight(p.X + ox, p.Y + oy));
                    }
                    p.Z = h + Lift;
                    vertices[i * (Across + 1) + j] = new VertexPositionColorTexture(p, Color.White, Vector2.Zero);
                }
            }

            var indices = new short[Along * Across * 6];
            int n = 0;
            for (int i = 0; i < Along; i++)
            {
                for (int j = 0; j < Across; j++)
                {
                    short a = (short)(i * (Across + 1) + j), b = (short)(a + 1), c = (short)(a + Across + 1), d = (short)(c + 1);
                    indices[n++] = a; indices[n++] = c; indices[n++] = b;
                    indices[n++] = b; indices[n++] = c; indices[n++] = d;
                }
            }

            _vertices = new VertexBuffer(gd, VertexPositionColorTexture.VertexDeclaration, vertices.Length, BufferUsage.WriteOnly);
            _vertices.SetData(vertices);
            _indices = new IndexBuffer(gd, IndexElementSize.SixteenBits, indices.Length, BufferUsage.WriteOnly);
            _indices.SetData(indices);
            return true;
        }

        public override void Draw(GameTime gameTime)
        {
            Effect? effect = GraphicsManager.Instance.SceneWaterEffect;
            if (!Visible || effect == null)
                return;

            var gd = GraphicsManager.Instance.GraphicsDevice;
            if ((_vertices == null || _vertices.IsDisposed) && !Build(gd))
                return;

            var previousBlend = gd.BlendState;
            var previousDepth = gd.DepthStencilState;
            var previousRasterizer = gd.RasterizerState;

            effect.CurrentTechnique = effect.Techniques["Sheen"];
            effect.Parameters["WorldViewProjection"]?.SetValue(Camera.Instance.ViewProjection);
            effect.Parameters["Time"]?.SetValue((float)(gameTime.TotalGameTime.TotalSeconds % 3600.0));
            effect.Parameters["Intensity"]?.SetValue(Intensity);
            effect.Parameters["Tint"]?.SetValue(Tint);
            effect.Parameters["PathStart"]?.SetValue(_start);
            effect.Parameters["PathDir"]?.SetValue(_dir);
            effect.Parameters["PathLength"]?.SetValue(_length);
            effect.Parameters["PathWidth"]?.SetValue(_width);

            gd.BlendState = BlendState.Additive;
            gd.DepthStencilState = DepthStencilState.DepthRead;
            gd.RasterizerState = RasterizerState.CullNone;
            gd.SetVertexBuffer(_vertices);
            gd.Indices = _indices;
            foreach (var pass in effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                gd.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, Along * Across * 2);
            }

            gd.BlendState = previousBlend;
            gd.DepthStencilState = previousDepth;
            gd.RasterizerState = previousRasterizer;
        }

        public override void Dispose()
        {
            _vertices?.Dispose();
            _indices?.Dispose();
            _vertices = null;
            _indices = null;
            base.Dispose();
        }
    }

    /// <summary>
    /// Lightning inside distant clouds: every few seconds a soft flash flickers two or three times somewhere in the
    /// cloud band (a large additive glow in front of the sky).
    /// </summary>
    internal sealed class CloudLightning : SourceParticleSystem
    {
        private readonly Vector3 _min;
        private readonly Vector3 _max;
        private readonly float _size;
        private float _wait = 3f;

        public CloudLightning(Vector3 min, Vector3 max, float size) : base(6)
        {
            _min = min;
            _max = max;
            _size = size;
            Position = (min + max) * 0.5f;
            BlendState = SceneFxTextures.AddPremultiplied;
            MaxDistance = 60000f;
            ReferenceDistance = 800f;
            MinDistanceScale = 1f;
            ScaleGrowth = 0f;
        }

        protected override Texture2D? ParticleTexture => SceneFxTextures.Glow;
        protected override Vector2 ParticleTextureCenter => new(32f, 32f);

        protected override void OnBeforeParticlesUpdated(float dt)
        {
            _wait -= dt;
            if (_wait > 0f)
                return;

            _wait = RandomRange(5f, 12f);
            Vector3 at = new(RandomRange(_min.X, _max.X), RandomRange(_min.Y, _max.Y), RandomRange(_min.Z, _max.Z));
            CreateParticle(0, at, Vector3.Zero, new Vector3(0.78f, 0.72f, 1f));
            // Sometimes a second, smaller flash nearby a moment later.
            if (MuGame.Random.Next(2) == 0)
                CreateParticle(0, at + new Vector3(RandomRange(-900f, 900f), RandomRange(-900f, 900f), RandomRange(-300f, 300f)), new Vector3(0.6f, 0f, 0f), new Vector3(0.7f, 0.7f, 1f));
        }

        protected override void OnParticleCreated(ref SourceParticle p)
        {
            p.LifeTime = p.MaxLifeTime = RandomRange(0.45f, 0.75f);
            p.Scale = _size * RandomRange(0.7f, 1.2f) * (p.Angle.X > 0f ? 0.6f : 1f);
            p.Alpha = RandomRange(0.35f, 0.6f);
            p.TurningForce = new Vector3(RandomRange(0f, 6.28f), RandomRange(18f, 30f), p.Angle.X);   // phase, flicker rate, delay
            p.EnableMove = false;
        }

        protected override void UpdateLiveParticle(ref SourceParticle p, float dt)
        {
        }

        protected override Color GetParticleColor(in SourceParticle p, float lifeRatio)
        {
            float age = (1f - lifeRatio) * p.MaxLifeTime - p.TurningForce.Z * 0.3f;
            if (age < 0f)
                return Color.Transparent;
            // Two or three hard flickers under a fast decay, like a discharge seen through cloud.
            float flicker = MathF.Max(0f, MathF.Sin(age * p.TurningForce.Y + p.TurningForce.X));
            float a = p.Alpha * flicker * MathF.Exp(-age * 4f);
            return new Color(new Vector4(p.Light * a, a));
        }
    }

    /// <summary>
    /// Aura of a column of magic light: a pulsing halo around it and rings of light rising up the column, widening and
    /// fading out (camera-facing halo, horizontal rings).
    /// </summary>
    internal sealed class PillarAura : WorldObject
    {
        private readonly float _height;
        private readonly float _radius;
        private readonly Vector3 _color;
        private readonly VertexPositionColorTexture[] _quad = new VertexPositionColorTexture[6];
        private Texture2D? _ring;
        private BasicEffect? _effect;

        public PillarAura(Vector3 baseCenter, float height, float radius, Vector3 color)
        {
            Position = baseCenter;
            _height = height;
            _radius = radius;
            _color = color;
            IsTransparent = true;
            AffectedByTransparency = false;
            BoundingBoxLocal = new BoundingBox(new Vector3(-radius * 4f, -radius * 4f, -100f), new Vector3(radius * 4f, radius * 4f, height + 200f));
        }

        public override WorldObjectRenderPolicy RenderPolicy => base.RenderPolicy.With(forceVisible: true);

        public override async Task LoadContent()
        {
            _ring = await TextureLoader.Instance.PrepareAndGetTexture("Effect/Magic_Ground1.OZJ");
            await base.LoadContent();
        }

        public override void Draw(GameTime gameTime)
        {
            if (!Visible)
                return;

            var gd = GraphicsManager.Instance.GraphicsDevice;
            _effect ??= new BasicEffect(gd) { TextureEnabled = true, VertexColorEnabled = true, LightingEnabled = false };
            _effect.World = Matrix.Identity;
            _effect.View = Camera.Instance.View;
            _effect.Projection = Camera.Instance.Projection;

            var previousBlend = gd.BlendState;
            var previousDepth = gd.DepthStencilState;
            var previousRasterizer = gd.RasterizerState;
            var previousSampler = gd.SamplerStates[0];
            gd.BlendState = SceneFxTextures.AddPremultiplied;
            gd.DepthStencilState = DepthStencilState.DepthRead;
            gd.RasterizerState = RasterizerState.CullNone;
            gd.SamplerStates[0] = SamplerState.LinearClamp;

            float t = (float)(gameTime.TotalGameTime.TotalSeconds % 3600.0);
            float pulse = 0.75f + 0.25f * MathF.Sin(t * 1.4f) * MathF.Sin(t * 0.53f + 1f);

            // Halo: two soft glows facing the camera, a wide faint one and a tighter one at the foot of the column.
            DrawBillboard(gd, SceneFxTextures.Glow, Position + new Vector3(0f, 0f, _height * 0.3f), _radius * 7f, _color * 0.4f * pulse);
            DrawBillboard(gd, SceneFxTextures.Glow, Position + new Vector3(0f, 0f, _height * 0.04f), _radius * 3.4f, _color * 0.8f * pulse);

            // Rings: four in flight, each rising the whole height in a few seconds.
            if (_ring != null && !_ring.IsDisposed)
            {
                const float period = 3.2f;
                for (int i = 0; i < 4; i++)
                {
                    float k = ((t / period) + i * 0.25f) % 1f;
                    float fade = MathF.Sin(k * MathF.PI);
                    float radius = _radius * (1.2f + k * 2.2f);
                    DrawRing(gd, Position + new Vector3(0f, 0f, k * _height), radius, k * 2f + i, _color * 0.85f * fade);
                }
            }

            gd.BlendState = previousBlend;
            gd.DepthStencilState = previousDepth;
            gd.RasterizerState = previousRasterizer;
            gd.SamplerStates[0] = previousSampler;
        }

        private void DrawBillboard(GraphicsDevice gd, Texture2D texture, Vector3 center, float size, Vector3 color)
        {
            Vector3 right = Camera.Instance.Right * size;
            Vector3 up = Camera.Instance.Up * size;
            Draw(gd, texture, center - right + up, center + right + up, center + right - up, center - right - up, color);
        }

        // The camera sees the column from the side, where a flat ring is only a line: draw it facing the camera,
        // squashed into an ellipse as if seen slightly from above.
        private void DrawRing(GraphicsDevice gd, Vector3 center, float radius, float angle, Vector3 color)
        {
            Vector3 right = Camera.Instance.Right * radius;
            Vector3 up = Camera.Instance.Up * radius * 0.32f;
            Draw(gd, _ring!, center - right + up, center + right + up, center + right - up, center - right - up, color);
        }

        private void Draw(GraphicsDevice gd, Texture2D texture, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 color)
        {
            var tint = new Color(color);
            var p0 = new VertexPositionColorTexture(a, tint, new Vector2(0f, 0f));
            var p1 = new VertexPositionColorTexture(b, tint, new Vector2(1f, 0f));
            var p2 = new VertexPositionColorTexture(c, tint, new Vector2(1f, 1f));
            var p3 = new VertexPositionColorTexture(d, tint, new Vector2(0f, 1f));
            _quad[0] = p0; _quad[1] = p1; _quad[2] = p2;
            _quad[3] = p0; _quad[4] = p2; _quad[5] = p3;
            _effect!.Texture = texture;
            foreach (var pass in _effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                gd.DrawUserPrimitives(PrimitiveType.TriangleList, _quad, 0, 2);
            }
        }

        public override void Dispose()
        {
            _effect?.Dispose();
            _effect = null;
            base.Dispose();
        }
    }
}
