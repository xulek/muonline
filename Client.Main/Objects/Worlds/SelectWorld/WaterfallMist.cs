#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Content;
using Client.Main.Objects.Effects.Particles;
using Client.Main.Objects.Effects.Scene;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Worlds.SelectWrold
{
    /// <summary>Slow clouds of mist around the falling water, plus droplets jumping off the lip.</summary>
    internal sealed class WaterfallMist : SourceParticleSystem
    {
        private readonly Vector3 _edgeStart;
        private readonly Vector3 _edgeEnd;
        private readonly Vector3 _lipStart;
        private readonly Vector3 _lipEnd;
        private readonly float _rate;
        private float _carry;
        private WaterDroplets? _spray;

        public WaterfallMist(Vector3 min, Vector3 max, Vector3 lipMin, Vector3 lipMax, float fallHeight) : base(160)
        {
            _edgeStart = min;
            _edgeEnd = new Vector3(max.X, max.Y, min.Z);
            _lipStart = lipMin;
            _lipEnd = new Vector3(lipMax.X, lipMax.Y, lipMin.Z);
            float width = Vector2.Distance(new Vector2(min.X, min.Y), new Vector2(max.X, max.Y));
            _rate = Math.Clamp(width / 45f, 4f, 22f) * Math.Clamp(fallHeight / 500f, 0.6f, 1.5f);
            Position = (min + max) * 0.5f;
            BlendState = SceneFxTextures.AddPremultiplied;
            DepthTowardCamera = 150f;
            MaxDistance = 9000f;
            ReferenceDistance = 800f;
            MinDistanceScale = 1f;
            ScaleGrowth = 0f;
        }

        protected override Texture2D? ParticleTexture => SceneFxTextures.Puff;
        protected override Vector2 ParticleTextureCenter => new(64f, 64f);

        public override async Task LoadContent()
        {
            if (World != null && _spray == null)
            {
                // Drops jumping off the lip and falling along the sheet.
                _spray = new WaterDroplets(_lipStart, _lipEnd, new DropletSpec(
                    Rate: _rate * 1.5f, Jitter: 8f, OutSpeed: new Vector2(15f, 70f), UpSpeed: new Vector2(10f, 80f),
                    Gravity: 650f, Life: new Vector2(0.5f, 1.0f), Size: new Vector2(0.32f, 0.6f), FallDepth: 300f)) { World = World };
                World.Objects.Add(_spray);
                await _spray.Load();
            }
            await base.LoadContent();
        }

        protected override void OnBeforeParticlesUpdated(float dt)
        {
            _carry += _rate * dt;
            while (_carry >= 1f)
            {
                _carry -= 1f;
                Vector3 at = Vector3.Lerp(_edgeStart, _edgeEnd, RandomRange(0f, 1f));
                at.Z += RandomRange(-120f, 60f);
                CreateParticle(0, at, Vector3.Zero, new Vector3(0.55f, 0.72f, 0.95f));
            }
        }

        protected override void OnParticleCreated(ref SourceParticle p)
        {
            p.LifeTime = p.MaxLifeTime = RandomRange(2.6f, 4.4f);
            p.Velocity = new Vector3(RandomRange(-22f, 22f), RandomRange(-22f, 22f), RandomRange(12f, 40f));
            p.Scale = RandomRange(0.9f, 1.6f);
            p.Rotation = RandomRange(0f, MathHelper.TwoPi);
            p.Alpha = RandomRange(0.3f, 0.45f);
            p.TurningForce = new Vector3(RandomRange(-0.25f, 0.25f), 0f, 0f);
        }

        protected override void UpdateLiveParticle(ref SourceParticle p, float dt)
        {
            p.Position += p.Velocity * dt;
            p.Velocity *= 1f - 0.25f * dt;
            p.Scale += 0.45f * dt;
            p.Rotation += p.TurningForce.X * dt;
        }

        protected override Color GetParticleColor(in SourceParticle p, float lifeRatio)
        {
            float age = 1f - lifeRatio;
            float a = p.Alpha * MathF.Min(age / 0.25f, 1f) * MathF.Min(lifeRatio / 0.55f, 1f);
            return new Color(new Vector4(p.Light * a, a));
        }

        public override void Dispose()
        {
            if (_spray != null)
            {
                World?.Objects.Remove(_spray);
                _spray.Dispose();
                _spray = null;
            }
            base.Dispose();
        }
    }
}
