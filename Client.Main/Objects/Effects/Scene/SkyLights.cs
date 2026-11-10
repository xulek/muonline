#nullable enable
using System;
using Client.Main.Graphics;
using Client.Main.Objects.Effects.Particles;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects.Scene
{
    /// <summary>Stars that flare up for a moment in front of the star field.</summary>
    internal sealed class SkyGlints : SourceParticleSystem
    {
        private const int Count = 26;
        private readonly Vector3 _min;
        private readonly Vector3 _max;

        private readonly float _size;

        public SkyGlints(Vector3 min, Vector3 max, float size = 1f) : base(Count)
        {
            _size = size;
            _min = min;
            _max = max;
            Position = (min + max) * 0.5f;
            BlendState = SceneFxTextures.AddPremultiplied;
            MaxDistance = 12000f;
            ReferenceDistance = 800f;
            MinDistanceScale = 1f;
            ScaleGrowth = 0f;
        }

        protected override Texture2D? ParticleTexture => SceneFxTextures.Glint;
        protected override Vector2 ParticleTextureCenter => new(32f, 32f);

        protected override void OnBeforeParticlesUpdated(float dt)
        {
            if (ActiveCount < Count && MuGame.Random.Next(4) == 0)
            {
                Vector3 at = new(RandomRange(_min.X, _max.X), RandomRange(_min.Y, _max.Y), RandomRange(_min.Z, _max.Z));
                Vector3 color = MuGame.Random.Next(3) == 0 ? new Vector3(1f, 0.7f, 0.95f) : new Vector3(0.8f, 0.9f, 1f);
                CreateParticle(0, at, Vector3.Zero, color);
            }
        }

        protected override void OnParticleCreated(ref SourceParticle p)
        {
            p.LifeTime = p.MaxLifeTime = RandomRange(1.6f, 4f);
            p.Scale = RandomRange(1.6f, 3.4f) * _size;
            p.Alpha = RandomRange(0.6f, 1f);
            p.Rotation = RandomRange(-0.3f, 0.3f);
            p.EnableMove = false;
        }

        protected override Color GetParticleColor(in SourceParticle p, float lifeRatio)
        {
            // Rises and falls once, like a star catching the eye.
            float a = p.Alpha * MathF.Pow(MathF.Sin(lifeRatio * MathF.PI), 2f);
            return new Color(new Vector4(p.Light * a, a));
        }

        protected override float GetParticleScale(in SourceParticle p, float lifeRatio, float distanceScale, float perspectiveScale) =>
            p.Scale * (0.6f + 0.4f * MathF.Sin(lifeRatio * MathF.PI)) * perspectiveScale;
    }

    /// <summary>A shooting star every few seconds across the sky behind the islands.</summary>
    internal sealed class ShootingStars : SourceParticleSystem
    {
        private readonly Vector3 _min;
        private readonly Vector3 _max;
        private readonly Vector3 _across;
        private float _wait = 2.5f;

        /// <param name="across">Horizontal direction the stars fly in (either way); across the view looks best.</param>
        public float Size { get; init; } = 1f;

        public ShootingStars(Vector3 min, Vector3 max, Vector3? across = null) : base(4)
        {
            _min = min;
            _max = max;
            _across = Vector3.Normalize(across ?? Vector3.UnitY);
            Position = (min + max) * 0.5f;
            BlendState = SceneFxTextures.AddPremultiplied;
            MaxDistance = 12000f;
            ReferenceDistance = 800f;
            MinDistanceScale = 1f;
            ScaleGrowth = 0f;
        }

        protected override Texture2D? ParticleTexture => SceneFxTextures.Streak;
        protected override Vector2 ParticleTextureCenter => new(118f, 8f);   // the head

        protected override void OnBeforeParticlesUpdated(float dt)
        {
            _wait -= dt;
            if (_wait > 0f)
                return;

            _wait = RandomRange(3.5f, 8f);
            Vector3 at = new(RandomRange(_min.X, _max.X), RandomRange(_min.Y, _max.Y), _max.Z);
            float side = MuGame.Random.Next(2) == 0 ? -1f : 1f;
            CreateParticle(0, at, new Vector3(side, 0f, 0f), new Vector3(0.9f, 0.85f, 1f));
        }

        protected override void OnParticleCreated(ref SourceParticle p)
        {
            p.LifeTime = p.MaxLifeTime = RandomRange(0.8f, 1.3f);
            p.Velocity = _across * (p.Angle.X * RandomRange(1400f, 2200f)) - new Vector3(0f, 0f, RandomRange(500f, 900f));
            p.Scale = RandomRange(2.6f, 3.6f) * Size;
            p.Alpha = 1f;
        }

        protected override Color GetParticleColor(in SourceParticle p, float lifeRatio)
        {
            float a = p.Alpha * MathF.Min((1f - lifeRatio) / 0.15f, 1f) * MathF.Min(lifeRatio / 0.4f, 1f);
            return new Color(new Vector4(p.Light * a, a));
        }

        // The streak is drawn along the star's motion on screen.
        protected override float GetParticleRotation(in SourceParticle p)
        {
            Matrix viewProjection = Camera.Instance.ViewProjection;
            Vector4 a = Vector4.Transform(p.Position, viewProjection);
            Vector4 b = Vector4.Transform(p.Position + p.Velocity * 0.05f, viewProjection);
            if (a.W <= 0.001f || b.W <= 0.001f)
                return 0f;
            float dx = b.X / b.W - a.X / a.W;
            float dy = -(b.Y / b.W - a.Y / a.W);
            return MathF.Atan2(dy, dx);
        }
    }
}
