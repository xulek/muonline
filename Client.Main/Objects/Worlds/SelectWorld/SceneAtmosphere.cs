#nullable enable
using System;
using Client.Main.Objects.Effects.Particles;
using Client.Main.Objects.Effects.Scene;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Worlds.SelectWrold
{
    /// <summary>
    /// Living air of the character scene: fireflies drifting between the islands and sparks rising around the
    /// character's pedestal. Fireflies are kept at a steady count inside a box in front of the camera.
    /// </summary>
    internal sealed class SceneAtmosphere : SourceParticleSystem
    {
        private const int Fireflies = 0;
        private const int Sparks = 1;
        private const int FireflyCount = 110;

        private static readonly Vector3[] FireflyColors =
        {
            new(1f, 0.45f, 0.85f), new(0.45f, 0.85f, 1f), new(1f, 0.82f, 0.5f), new(0.75f, 0.55f, 1f)
        };

        private readonly Vector3 _pedestal;
        private readonly Vector3 _areaCenter;
        private readonly Vector3 _areaHalf;
        private float _time;
        private float _sparkCarry;
        private int _fireflies;

        public SceneAtmosphere(Vector3 pedestal, Vector3 areaCenter, Vector3 areaHalfSize) : base(FireflyCount + 120)
        {
            _pedestal = pedestal;
            _areaCenter = areaCenter;
            _areaHalf = areaHalfSize;
            Position = areaCenter;
            BlendState = SceneFxTextures.AddPremultiplied;
            MaxDistance = 9000f;
            ReferenceDistance = 800f;
            MinDistanceScale = 1f;
            ScaleGrowth = 0f;
        }

        protected override Texture2D? ParticleTexture => SceneFxTextures.Glow;
        protected override Vector2 ParticleTextureCenter => new(32f, 32f);

        protected override void OnBeforeParticlesUpdated(float dt)
        {
            _time += dt;

            _fireflies = 0;
            for (int i = 0; i < ActiveCount; i++)
                if (Particles[i].SubType == Fireflies)
                    _fireflies++;

            // Fill the scene at once on the first frame, then replace the ones that faded out.
            int missing = FireflyCount - _fireflies;
            for (int i = 0; i < missing && i < 6 + (_time < 0.2f ? FireflyCount : 0); i++)
            {
                Vector3 at = _areaCenter + new Vector3(
                    RandomRange(-_areaHalf.X, _areaHalf.X),
                    RandomRange(-_areaHalf.Y, _areaHalf.Y),
                    RandomRange(-_areaHalf.Z, _areaHalf.Z));
                CreateParticle(0, at, Vector3.Zero, FireflyColors[MuGame.Random.Next(FireflyColors.Length)], Fireflies);
            }

            _sparkCarry += 12f * dt;
            while (_sparkCarry >= 1f)
            {
                _sparkCarry -= 1f;
                float angle = RandomRange(0f, MathHelper.TwoPi);
                float radius = RandomRange(55f, 105f);
                Vector3 at = _pedestal + new Vector3(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius, RandomRange(0f, 20f));
                Vector3 color = MuGame.Random.Next(3) == 0 ? new Vector3(0.85f, 0.55f, 1f) : new Vector3(0.45f, 0.85f, 1f);
                CreateParticle(0, at, new Vector3(0f, 0f, angle), color, Sparks);
            }
        }

        protected override void OnParticleCreated(ref SourceParticle p)
        {
            if (p.SubType == Fireflies)
            {
                p.LifeTime = p.MaxLifeTime = RandomRange(7f, 15f);
                p.Velocity = new Vector3(RandomRange(-20f, 20f), RandomRange(-20f, 20f), RandomRange(-8f, 12f));
                p.Scale = RandomRange(0.55f, 1.05f);
                p.Alpha = RandomRange(0.55f, 1f);
                // Wander: frequency, phase, twinkle speed.
                p.TurningForce = new Vector3(RandomRange(0.3f, 0.9f), RandomRange(0f, 6.28f), RandomRange(1.5f, 4f));
            }
            else
            {
                p.LifeTime = p.MaxLifeTime = RandomRange(1.4f, 2.6f);
                p.Velocity = new Vector3(0f, 0f, RandomRange(45f, 95f));
                p.Scale = RandomRange(0.16f, 0.28f);
                p.Alpha = RandomRange(0.7f, 1f);
                p.TurningForce = new Vector3(RandomRange(0.6f, 1.2f), 0f, 0f);
            }
        }

        protected override void UpdateLiveParticle(ref SourceParticle p, float dt)
        {
            if (p.SubType == Fireflies)
            {
                float w = p.TurningForce.X;
                float phase = p.TurningForce.Y;
                Vector3 wander = new(MathF.Sin(_time * w + phase) * 18f, MathF.Cos(_time * w * 0.8f + phase) * 18f, MathF.Sin(_time * w * 1.3f + phase * 2f) * 9f);
                p.Position += (p.Velocity + wander) * dt;
                return;
            }

            // Sparks spiral slowly around the pedestal while rising.
            float angle = p.Angle.Z + p.TurningForce.X * dt;
            p.Angle.Z = angle;
            Vector2 offset = new(p.Position.X - _pedestal.X, p.Position.Y - _pedestal.Y);
            float radius = offset.Length() * (1f - 0.12f * dt);
            p.Position = new Vector3(_pedestal.X + MathF.Cos(angle) * radius, _pedestal.Y + MathF.Sin(angle) * radius, p.Position.Z + p.Velocity.Z * dt);
        }

        protected override Color GetParticleColor(in SourceParticle p, float lifeRatio)
        {
            float age = 1f - lifeRatio;
            float a;
            if (p.SubType == Fireflies)
            {
                float twinkle = 0.45f + 0.55f * (0.5f + 0.5f * MathF.Sin(_time * p.TurningForce.Z + p.TurningForce.Y * 3f));
                a = p.Alpha * twinkle * MathF.Min(age / 0.15f, 1f) * MathF.Min(lifeRatio / 0.2f, 1f);
            }
            else
            {
                a = p.Alpha * MathF.Min(age / 0.1f, 1f) * lifeRatio;
            }

            return new Color(new Vector4(p.Light * a, a));
        }
    }
}
