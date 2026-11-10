#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Content;
using Client.Main.Objects.Effects.Particles;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects.Scene
{
    /// <summary>
    /// Moonlight glitter on open water: specks flashing on the sea surface along a band from near the camera towards
    /// a light source on the horizon, densest in the middle of the band.
    /// </summary>
    internal sealed class SeaGlitter : SourceParticleSystem
    {
        private readonly Vector3 _near;
        private readonly Vector3 _far;
        private readonly float _nearWidth;
        private readonly float _farWidth;
        private readonly float _rate;
        private readonly Vector3 _side;
        private float _carry;

        /// <param name="near">Start of the band on the water (its Z is the sea level).</param>
        /// <param name="far">End of the band, towards the light.</param>
        public SeaGlitter(Vector3 near, Vector3 far, float nearWidth, float farWidth, float rate) : base(Math.Clamp((int)(rate * 1.2f) + 16, 32, 600))
        {
            _near = near;
            _far = new Vector3(far.X, far.Y, near.Z);
            _nearWidth = nearWidth;
            _farWidth = farWidth;
            _rate = rate;
            Vector3 along = _far - _near;
            _side = Vector3.Normalize(new Vector3(-along.Y, along.X, 0f));
            Position = (_near + _far) * 0.5f;
            BlendState = SceneFxTextures.AddPremultiplied;
            MaxDistance = 40000f;
            ReferenceDistance = 800f;
            MinDistanceScale = 1f;
            ScaleGrowth = 0f;
        }

        protected override Texture2D? ParticleTexture => SceneFxTextures.Glow;
        protected override Vector2 ParticleTextureCenter => new(32f, 32f);

        protected override void OnBeforeParticlesUpdated(float dt)
        {
            _carry += _rate * dt;
            while (_carry >= 1f)
            {
                _carry -= 1f;
                float t = MathF.Pow(RandomRange(0f, 1f), 0.8f);
                // Roughly gaussian across the band: the reflection is brightest in its middle.
                float across = (RandomRange(-1f, 1f) + RandomRange(-1f, 1f) + RandomRange(-1f, 1f)) / 3f;
                Vector3 at = Vector3.Lerp(_near, _far, t) + _side * across * MathHelper.Lerp(_nearWidth, _farWidth, t);
                CreateParticle(0, at, new Vector3(t, 0f, 0f), new Vector3(0.85f, 0.92f, 1f));
            }
        }

        protected override void OnParticleCreated(ref SourceParticle p)
        {
            p.LifeTime = p.MaxLifeTime = RandomRange(0.18f, 0.6f);
            // Far specks are bigger in the world so they stay visible on screen.
            float distance = p.Angle.X;
            p.Scale = RandomRange(0.1f, 0.24f) * (1f + distance * 7f);
            p.Alpha = RandomRange(0.45f, 1f);
            p.EnableMove = false;
        }

        protected override void UpdateLiveParticle(ref SourceParticle p, float dt)
        {
        }

        protected override Color GetParticleColor(in SourceParticle p, float lifeRatio)
        {
            float a = p.Alpha * MathF.Sin(lifeRatio * MathF.PI);
            return new Color(new Vector4(p.Light * a, a));
        }

        protected override float GetParticleScale(in SourceParticle p, float lifeRatio, float distanceScale, float perspectiveScale) =>
            p.Scale * (0.5f + 0.5f * MathF.Sin(lifeRatio * MathF.PI)) * perspectiveScale;
    }

    /// <summary>Glowing motes rising around a point (a magic light, a shrine): they spiral up and fade.</summary>
    internal sealed class RisingSparks : SourceParticleSystem
    {
        private readonly float _radius;
        private readonly float _rate;
        private readonly Vector2 _speed;
        private readonly float _size;
        private readonly Vector3[] _colors;
        private float _carry;

        public RisingSparks(Vector3 center, float radius, float rate, Vector2 riseSpeed, float size, params Vector3[] colors) : base(Math.Clamp((int)(rate * 4f) + 16, 32, 400))
        {
            Position = center;
            _radius = radius;
            _rate = rate;
            _speed = riseSpeed;
            _size = size;
            _colors = colors.Length > 0 ? colors : new[] { Vector3.One };
            BlendState = SceneFxTextures.AddPremultiplied;
            MaxDistance = 40000f;
            ReferenceDistance = 800f;
            MinDistanceScale = 1f;
            ScaleGrowth = 0f;
        }

        protected override Texture2D? ParticleTexture => SceneFxTextures.Glow;
        protected override Vector2 ParticleTextureCenter => new(32f, 32f);

        protected override void OnBeforeParticlesUpdated(float dt)
        {
            _carry += _rate * dt;
            while (_carry >= 1f)
            {
                _carry -= 1f;
                float angle = RandomRange(0f, MathHelper.TwoPi);
                float r = _radius * MathF.Sqrt(RandomRange(0f, 1f));
                Vector3 at = Position + new Vector3(MathF.Cos(angle) * r, MathF.Sin(angle) * r, RandomRange(0f, _radius * 0.3f));
                CreateParticle(0, at, new Vector3(0f, 0f, angle), _colors[MuGame.Random.Next(_colors.Length)]);
            }
        }

        protected override void OnParticleCreated(ref SourceParticle p)
        {
            p.LifeTime = p.MaxLifeTime = RandomRange(2f, 4f);
            p.Velocity = new Vector3(0f, 0f, RandomRange(_speed.X, _speed.Y));
            p.Scale = _size * RandomRange(0.6f, 1.2f);
            p.Alpha = RandomRange(0.6f, 1f);
            p.TurningForce = new Vector3(RandomRange(0.4f, 1f) * (MuGame.Random.Next(2) == 0 ? -1f : 1f), RandomRange(0f, 6.28f), 0f);
        }

        protected override void UpdateLiveParticle(ref SourceParticle p, float dt)
        {
            float angle = p.Angle.Z + p.TurningForce.X * dt;
            p.Angle.Z = angle;
            Vector2 offset = new(p.Position.X - Position.X, p.Position.Y - Position.Y);
            float r = offset.Length() * (1f + 0.08f * dt);
            p.Position = new Vector3(Position.X + MathF.Cos(angle) * r, Position.Y + MathF.Sin(angle) * r, p.Position.Z + p.Velocity.Z * dt);
        }

        protected override Color GetParticleColor(in SourceParticle p, float lifeRatio)
        {
            float twinkle = 0.75f + 0.25f * MathF.Sin(p.LifeTime * 9f + p.TurningForce.Y);
            float a = p.Alpha * twinkle * MathF.Min((1f - lifeRatio) / 0.15f, 1f) * lifeRatio;
            return new Color(new Vector4(p.Light * a, a));
        }
    }

    /// <summary>Banks of low mist drifting slowly through a box (sea fog, valley haze).</summary>
    internal sealed class DriftingMist : SourceParticleSystem
    {
        private readonly Vector3 _min;
        private readonly Vector3 _max;
        private readonly int _count;
        private readonly Vector3 _wind;
        private readonly float _alpha;
        private readonly Vector2 _size;
        private readonly Vector3 _color;
        private bool _seeded;

        public DriftingMist(Vector3 min, Vector3 max, int count, Vector3 wind, float alpha, Vector2 size, Vector3 color) : base(count + 4)
        {
            _min = min;
            _max = max;
            _count = count;
            _wind = wind;
            _alpha = alpha;
            _size = size;
            _color = color;
            Position = (min + max) * 0.5f;
            BlendState = SceneFxTextures.AddPremultiplied;
            DepthTowardCamera = 1200f;
            MaxDistance = 40000f;
            ReferenceDistance = 800f;
            MinDistanceScale = 1f;
            ScaleGrowth = 0f;
        }

        protected override Texture2D? ParticleTexture => SceneFxTextures.Puff;
        protected override Vector2 ParticleTextureCenter => new(64f, 64f);

        protected override void OnBeforeParticlesUpdated(float dt)
        {
            // The whole bank at once on the first frame (already in mid-life), then one by one as they fade.
            for (int i = 0; ActiveCount < _count && i < (_seeded ? 1 : _count); i++)
            {
                Vector3 at = new(RandomRange(_min.X, _max.X), RandomRange(_min.Y, _max.Y), RandomRange(_min.Z, _max.Z));
                int index = CreateParticle(0, at, Vector3.Zero, _color);
                if (!_seeded && index >= 0)
                    Particles[index].LifeTime = RandomRange(0.2f, 1f) * Particles[index].MaxLifeTime;
            }
            _seeded = true;
        }

        protected override void OnParticleCreated(ref SourceParticle p)
        {
            p.LifeTime = p.MaxLifeTime = RandomRange(12f, 22f);
            p.Velocity = _wind * RandomRange(0.6f, 1.3f);
            p.Scale = RandomRange(_size.X, _size.Y);
            p.Rotation = RandomRange(0f, MathHelper.TwoPi);
            p.Alpha = _alpha * RandomRange(0.6f, 1.2f);
            p.TurningForce = new Vector3(RandomRange(-0.05f, 0.05f), 0f, 0f);
        }

        protected override void UpdateLiveParticle(ref SourceParticle p, float dt)
        {
            p.Position += p.Velocity * dt;
            p.Rotation += p.TurningForce.X * dt;
        }

        protected override Color GetParticleColor(in SourceParticle p, float lifeRatio)
        {
            float a = p.Alpha * MathF.Pow(MathF.Sin(lifeRatio * MathF.PI), 1.5f);
            return new Color(new Vector4(p.Light * a, a));
        }
    }
}
