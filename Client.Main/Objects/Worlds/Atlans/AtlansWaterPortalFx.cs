#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Content;
using Client.Main.Graphics;
using Client.Main.Models;
using Client.Main.Objects.Effects.Joints;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Worlds.Atlans
{
    /// <summary>
    /// Enhanced look for the Atlans water portal (type 23, Object24.bmd). Not in the original, which only
    /// pulses the model's additive water mesh. Layers, all lying in the portal plane (taken from bone 0 so
    /// they follow the model) and drawn additively after it:
    /// two slow counter-rotating vortex spirals, faint expanding ripples, a thin pulsing rim and a small
    /// soft core. Kept dim so the water flipbook remains the main surface. Allocation-free.
    /// </summary>
    internal sealed class AtlansWaterPortalFx : EffectObject
    {
        // Object24 mesh 0 (bone-local): x -281..265, y -196..165, z 0.
        private static readonly Vector3 LocalCenter = new(-8f, -15.5f, 0f);
        private const float HalfX = 275f;
        private const float HalfY = 182f;

        private const int RippleCount = 3;
        private const float RipplePeriod = 2.4f;

        private static readonly Vector3 VortexTint = new(0.25f, 0.65f, 1f);
        private static readonly Vector3 DeepTint = new(0.08f, 0.3f, 0.85f);
        private static readonly Vector3 CausticTint = new(0.3f, 0.75f, 1f);

        private readonly ModelObject _portal;
        private readonly SourceQuadBatch _batch = new(96);

        private Texture2D? _spiral;
        private Texture2D? _ring;
        private Texture2D? _ripple;
        private Texture2D? _glow;

        private Vector3 _center;
        private Vector3 _axisX = Vector3.UnitX;
        private Vector3 _axisY = Vector3.UnitY;
        private Vector3 _normal = Vector3.UnitZ;
        private bool _framed;
        private float _time;

        public AtlansWaterPortalFx(ModelObject portal)
        {
            _portal = portal ?? throw new ArgumentNullException(nameof(portal));
            IsTransparent = true;
            AffectedByTransparency = false;
            LightEnabled = false;
            BlendState = Blendings.OneOneAdditive;
            DepthState = DepthStencilState.DepthRead;
            BoundingBoxLocal = new BoundingBox(new Vector3(-420f, -420f, -50f), new Vector3(420f, 420f, 650f));
        }

        /// <summary>Portal centre in world space once the model's bones are known.</summary>
        public bool TryGetCenter(out Vector3 center)
        {
            center = _center;
            return _framed;
        }

        public override async Task LoadContent()
        {
            await base.LoadContent();
            _batch.Initialize(GraphicsDevice);
            _spiral = await Load("Effect/twlight2.jpg");
            _ring = await Load("Effect/bluering0001_R.jpg");
            _ripple = await Load("Effect/Shockwave.jpg");
            _glow = await Load("Effect/flareBlue.jpg");
        }

        private static Task<Texture2D?> Load(string path) => TextureLoader.Instance.PrepareAndGetTexture(path);

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
            if (Status != GameControlStatus.Ready)
                return;

            float dt = MathF.Min((float)gameTime.ElapsedGameTime.TotalSeconds, 0.1f);
            _time += dt;
            UpdateFrame();
        }

        private void UpdateFrame()
        {
            Matrix[]? bones = _portal.GetBoneTransforms();
            Matrix frame = bones != null && bones.Length > 0 ? bones[0] * _portal.WorldPosition : _portal.WorldPosition;

            _center = Vector3.Transform(LocalCenter, frame);
            Vector3 ax = Vector3.TransformNormal(Vector3.UnitX, frame);
            Vector3 ay = Vector3.TransformNormal(Vector3.UnitY, frame);
            if (ax.LengthSquared() < 1e-6f || ay.LengthSquared() < 1e-6f)
                return;

            _axisX = ax;
            _axisY = ay;
            Vector3 n = Vector3.Cross(ax, ay);
            _normal = n.LengthSquared() > 1e-6f ? Vector3.Normalize(n) : Vector3.UnitZ;
            if (_normal.Z < 0f)
                _normal = -_normal;
            _framed = true;
        }

        public override void DrawAfter(GameTime gameTime)
        {
            base.DrawAfter(gameTime);
            if (!_framed || !Visible || !_portal.Visible || Status != GameControlStatus.Ready)
                return;

            float pulse = MathF.Sin(_time * 1.2f) * 0.5f + 0.5f;     // 0..1, ~5.2 s (matches the water)
            float breathe = MathF.Sin(_time * 0.5f) * 0.5f + 0.5f;   // ~12.6 s

            // Restrained on purpose: the water flipbook stays the main surface; these layers only add
            // depth and motion, so their summed brightness stays well below white.

            // 1) Vortex: two slow counter-rotating spirals in deep blue.
            if (_batch.Begin(_spiral, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                AddPlaneQuad(2f, 0.95f, -_time * 0.35f, DeepTint * (0.14f + 0.03f * breathe));
                AddPlaneQuad(4f, 0.6f, _time * 0.7f, VortexTint * (0.1f + 0.03f * pulse));
                _batch.End();
            }

            // 2) Faint ripples travelling from the centre to the rim.
            if (_batch.Begin(_ripple, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < RippleCount; i++)
                {
                    float t = ((_time / RipplePeriod) + i / (float)RippleCount) % 1f;
                    float fade = MathF.Sin(t * MathF.PI);
                    AddPlaneQuad(6f, 0.25f + t * 0.8f, i * 1.3f, CausticTint * (0.12f * fade));
                }
                _batch.End();
            }

            // 3) Thin rim glow that breathes with the vortex.
            if (_batch.Begin(_ring, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                AddPlaneQuad(8f, 1.04f, _time * 0.15f, VortexTint * (0.14f + 0.05f * pulse));
                _batch.End();
            }

            // 4) Small soft heart in the middle of the vortex.
            if (_batch.Begin(_glow, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                float size = 70f + 8f * pulse;
                _batch.AddBillboard(_center + _normal * 10f, size, size, _time * 0.3f, ToColor(DeepTint * (0.14f + 0.05f * pulse)));
                _batch.End();
            }
        }

        /// <summary>Quad in the portal plane, <paramref name="scale"/> of the ellipse, rotated in-plane.</summary>
        private void AddPlaneQuad(float lift, float scale, float rotation, Vector3 light)
        {
            float c = MathF.Cos(rotation);
            float s = MathF.Sin(rotation);
            // Rotate the UVs rather than the quad so the layer keeps the ellipse footprint.
            Vector3 ex = _axisX * (HalfX * scale);
            Vector3 ey = _axisY * (HalfY * scale);
            Vector3 o = _center + _normal * lift;
            Color color = ToColor(light);

            _batch.AddQuad(
                o - ex - ey, RotateUv(0f, 0f, c, s),
                o + ex - ey, RotateUv(1f, 0f, c, s),
                o + ex + ey, RotateUv(1f, 1f, c, s),
                o - ex + ey, RotateUv(0f, 1f, c, s),
                color);
        }

        private static Vector2 RotateUv(float u, float v, float c, float s)
        {
            float x = u - 0.5f;
            float y = v - 0.5f;
            return new Vector2(x * c - y * s + 0.5f, x * s + y * c + 0.5f);
        }

        private static Color ToColor(Vector3 light)
        {
            light = Vector3.Clamp(light, Vector3.Zero, Vector3.One);
            return new Color(light.X, light.Y, light.Z, 1f);
        }

        public override void Dispose()
        {
            _batch.Dispose();
            base.Dispose();
        }
    }
}
