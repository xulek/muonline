#nullable enable
using System;
using Client.Main.Objects.Effects.Particles;
using Client.Main.Objects.Effects.Scene;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Worlds.SelectWrold
{
    /// <summary>Petals of the blossoming trees drifting down through the scene, swaying and turning.</summary>
    internal sealed class FallingPetals : SourceParticleSystem
    {
        private const int Count = 55;

        private static readonly Vector3[] Colors =
        {
            new(1f, 0.62f, 0.9f), new(0.92f, 0.55f, 1f), new(1f, 0.78f, 0.95f)
        };

        private readonly Vector3 _center;
        private readonly Vector3 _half;
        private float _time;

        public FallingPetals(Vector3 center, Vector3 halfSize) : base(Count)
        {
            _center = center;
            _half = halfSize;
            Position = center;
            BlendState = BlendState.AlphaBlend;   // the sprite is premultiplied white
            MaxDistance = 9000f;
            ReferenceDistance = 800f;
            MinDistanceScale = 1f;
            ScaleGrowth = 0f;
        }

        protected override Texture2D? ParticleTexture => SceneFxTextures.Petal;
        protected override Vector2 ParticleTextureCenter => new(16f, 16f);

        protected override void OnBeforeParticlesUpdated(float dt)
        {
            _time += dt;
            // Spread through the whole height on the first frame, then fall in from the top.
            bool first = _time < 0.2f;
            for (int i = 0; ActiveCount < Count && i < (first ? Count : 1); i++)
            {
                if (!first && MuGame.Random.Next(3) != 0)
                    break;
                float z = first ? RandomRange(-_half.Z, _half.Z) : _half.Z;
                Vector3 at = _center + new Vector3(RandomRange(-_half.X, _half.X), RandomRange(-_half.Y, _half.Y), z);
                CreateParticle(0, at, Vector3.Zero, Colors[MuGame.Random.Next(Colors.Length)]);
            }
        }

        protected override void OnParticleCreated(ref SourceParticle p)
        {
            p.LifeTime = p.MaxLifeTime = 40f;
            p.Velocity = new Vector3(RandomRange(-12f, 12f), RandomRange(-12f, 12f), -RandomRange(28f, 55f));
            p.Scale = RandomRange(0.5f, 0.85f);
            p.Rotation = RandomRange(0f, MathHelper.TwoPi);
            p.Alpha = RandomRange(0.65f, 0.95f);
            p.TurningForce = new Vector3(RandomRange(0.6f, 1.4f), RandomRange(0f, 6.28f), RandomRange(-2.2f, 2.2f));
        }

        protected override void UpdateLiveParticle(ref SourceParticle p, float dt)
        {
            float sway = MathF.Sin(_time * p.TurningForce.X + p.TurningForce.Y);
            p.Position += (p.Velocity + new Vector3(sway * 30f, MathF.Cos(_time * p.TurningForce.X * 0.7f + p.TurningForce.Y) * 20f, 0f)) * dt;
            p.Rotation += p.TurningForce.Z * dt;
            if (p.Position.Z < _center.Z - _half.Z)
                p.LifeTime = MathF.Min(p.LifeTime, 0f);
        }

        protected override Color GetParticleColor(in SourceParticle p, float lifeRatio)
        {
            // Fade in under the top of the area and out near its floor.
            float fromTop = (_center.Z + _half.Z - p.Position.Z) / 120f;
            float fromBottom = (p.Position.Z - (_center.Z - _half.Z)) / 160f;
            float a = p.Alpha * Math.Clamp(MathF.Min(fromTop, fromBottom), 0f, 1f);
            // Turning petals show their edge: dim the flat moments.
            a *= 0.65f + 0.35f * MathF.Abs(MathF.Sin(p.Rotation));
            return new Color(new Vector4(p.Light * a, a));
        }
    }
}
