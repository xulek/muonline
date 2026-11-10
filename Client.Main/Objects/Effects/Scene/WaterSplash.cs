#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Content;
using Client.Main.Graphics;
using Client.Main.Objects.Effects.Particles;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects.Scene
{
    /// <summary>How a <see cref="WaterDroplets"/> emitter throws its drops.</summary>
    internal readonly record struct DropletSpec(
        float Rate,                  // drops per second
        float Jitter,                // random offset of the start point
        Vector2 OutSpeed,            // horizontal speed range
        Vector2 UpSpeed,             // vertical start speed range (negative = thrown down)
        float Gravity,               // units / s²
        Vector2 Life,                // seconds
        Vector2 Size,                // sprite scale
        float FallDepth);            // drops vanish this far below the start (they hit water or leave the spray)

    /// <summary>
    /// Water drops flying on ballistic arcs: thin streaks turned along their motion on screen (motion blur),
    /// glinting while they fly and fading as they fall back.
    /// </summary>
    internal sealed class WaterDroplets : SourceParticleSystem
    {
        private readonly Vector3 _from;
        private readonly Vector3 _to;
        private readonly DropletSpec _spec;
        private float _carry;
        private float _time;

        public WaterDroplets(Vector3 from, Vector3 to, DropletSpec spec) : base(Math.Clamp((int)(spec.Rate * spec.Life.Y * 1.3f) + 8, 16, 400))
        {
            _from = from;
            _to = to;
            _spec = spec;
            Position = (from + to) * 0.5f;
            BlendState = SceneFxTextures.AddPremultiplied;
            MaxDistance = 9000f;
            ReferenceDistance = 800f;
            MinDistanceScale = 1f;
            ScaleGrowth = 0f;
        }

        protected override Texture2D? ParticleTexture => SceneFxTextures.Drop;
        protected override Vector2 ParticleTextureCenter => new(SceneFxTextures.Drop.Width * 0.5f, SceneFxTextures.Drop.Height * 0.5f);

        protected override void OnBeforeParticlesUpdated(float dt)
        {
            _time += dt;
            // Spray comes in bursts, not as a steady trickle.
            float gust = 0.65f + 0.35f * MathF.Sin(_time * 2.7f + Position.X * 0.01f) * MathF.Sin(_time * 1.3f);
            _carry += _spec.Rate * gust * dt;
            while (_carry >= 1f)
            {
                _carry -= 1f;
                Vector3 at = Vector3.Lerp(_from, _to, RandomRange(0f, 1f)) +
                             new Vector3(RandomRange(-_spec.Jitter, _spec.Jitter), RandomRange(-_spec.Jitter, _spec.Jitter), RandomRange(0f, _spec.Jitter * 0.5f));
                CreateParticle(0, at, Vector3.Zero, new Vector3(0.78f, 0.92f, 1f));
            }
        }

        protected override void OnParticleCreated(ref SourceParticle p)
        {
            p.LifeTime = p.MaxLifeTime = RandomRange(_spec.Life.X, _spec.Life.Y);
            float angle = RandomRange(0f, MathHelper.TwoPi);
            // Most drops fly low and fast, a few high and slow.
            float speed = MathHelper.Lerp(_spec.OutSpeed.X, _spec.OutSpeed.Y, MathF.Pow(RandomRange(0f, 1f), 0.7f));
            p.Velocity = new Vector3(MathF.Cos(angle) * speed, MathF.Sin(angle) * speed, RandomRange(_spec.UpSpeed.X, _spec.UpSpeed.Y));
            p.Gravity = -_spec.Gravity;
            p.Scale = RandomRange(_spec.Size.X, _spec.Size.Y);
            p.Alpha = RandomRange(0.55f, 1f);
            p.TurningForce = new Vector3(RandomRange(0f, 6.28f), RandomRange(14f, 26f), 0f);
        }

        protected override void UpdateLiveParticle(ref SourceParticle p, float dt)
        {
            p.Velocity *= 1f - 0.6f * dt;             // air drag
            p.Position += p.Velocity * dt;
            p.Velocity.Z += p.Gravity * dt;
            if (p.Position.Z < p.StartPosition.Z - _spec.FallDepth)
                p.LifeTime = 0f;
        }

        protected override Color GetParticleColor(in SourceParticle p, float lifeRatio)
        {
            float glint = 0.7f + 0.3f * MathF.Sin(_time * p.TurningForce.Y + p.TurningForce.X);
            float a = p.Alpha * glint * MathF.Min(lifeRatio / 0.35f, 1f) * MathF.Min((1f - lifeRatio) / 0.05f, 1f);
            return new Color(new Vector4(p.Light * a, a));
        }

        protected override float GetParticleScale(in SourceParticle p, float lifeRatio, float distanceScale, float perspectiveScale) =>
            p.Scale * perspectiveScale;

        protected override float GetParticleRotation(in SourceParticle p)
        {
            Matrix viewProjection = Camera.Instance.ViewProjection;
            Vector4 a = Vector4.Transform(p.Position, viewProjection);
            Vector4 b = Vector4.Transform(p.Position + p.Velocity * 0.02f, viewProjection);
            if (a.W <= 0.001f || b.W <= 0.001f)
                return 0f;
            return MathF.Atan2(-(b.Y / b.W - a.Y / a.W), b.X / b.W - a.X / a.W);
        }
    }

    /// <summary>Soft spray cloud where water lands: puffs that swell, drift and thin out.</summary>
    internal sealed class SplashMist : SourceParticleSystem
    {
        private readonly float _rate;
        private readonly float _radius;
        private readonly float _alpha;
        private readonly float _size;
        private readonly float _rise;
        private float _carry;

        public SplashMist(Vector3 position, float rate, float radius, float alpha, float size, float rise) : base(64)
        {
            Position = position;
            _rate = rate;
            _radius = radius;
            _alpha = alpha;
            _size = size;
            _rise = rise;
            BlendState = SceneFxTextures.AddPremultiplied;
            DepthTowardCamera = 150f;
            MaxDistance = 9000f;
            ReferenceDistance = 800f;
            MinDistanceScale = 1f;
            ScaleGrowth = 0f;
        }

        protected override Texture2D? ParticleTexture => SceneFxTextures.Puff;
        protected override Vector2 ParticleTextureCenter => new(64f, 64f);

        protected override void OnBeforeParticlesUpdated(float dt)
        {
            _carry += _rate * dt;
            while (_carry >= 1f)
            {
                _carry -= 1f;
                float angle = RandomRange(0f, MathHelper.TwoPi);
                float r = RandomRange(0f, _radius);
                CreateParticle(0, Position + new Vector3(MathF.Cos(angle) * r, MathF.Sin(angle) * r, RandomRange(-10f, 15f)), Vector3.Zero, new Vector3(0.6f, 0.78f, 0.95f));
            }
        }

        protected override void OnParticleCreated(ref SourceParticle p)
        {
            p.LifeTime = p.MaxLifeTime = RandomRange(1.3f, 2.4f);
            p.Velocity = new Vector3(RandomRange(-25f, 25f), RandomRange(-25f, 25f), _rise * RandomRange(0.5f, 1.2f));
            p.Scale = _size * RandomRange(0.5f, 0.9f);
            p.Rotation = RandomRange(0f, MathHelper.TwoPi);
            p.Alpha = _alpha * RandomRange(0.7f, 1.2f);
            p.TurningForce = new Vector3(RandomRange(-0.4f, 0.4f), 0f, 0f);
        }

        protected override void UpdateLiveParticle(ref SourceParticle p, float dt)
        {
            p.Position += p.Velocity * dt;
            p.Velocity *= 1f - 0.8f * dt;
            p.Scale += _size * 0.6f * dt;
            p.Rotation += p.TurningForce.X * dt;
        }

        protected override Color GetParticleColor(in SourceParticle p, float lifeRatio)
        {
            float age = 1f - lifeRatio;
            float a = p.Alpha * MathF.Min(age / 0.2f, 1f) * MathF.Pow(lifeRatio, 1.3f);
            return new Color(new Vector4(p.Light * a, a));
        }
    }
}
