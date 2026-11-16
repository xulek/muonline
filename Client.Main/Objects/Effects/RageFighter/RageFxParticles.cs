#nullable enable
using System;
using Microsoft.Xna.Framework;

namespace Client.Main.Objects.Effects.RageFighter
{
    /// <summary>
    /// One SourceMain5.2 particle (ZzzEffectParticle.cpp) reduced to the parameters the
    /// Rage Fighter skills need. All rates are per legacy 25 FPS frame and are scaled by
    /// FPS_ANIMATION_FACTOR in <see cref="RageParticlePool.Update"/>.
    /// </summary>
    internal struct RageParticle
    {
        public Vector3 Position;
        public Vector3 Velocity;
        public float Gravity;        // subtracted from Velocity.Z every frame
        public float Scale;
        public float ScaleAdd;       // Scale += ScaleAdd
        public float ScaleMul;       // Scale *= ScaleMul (1 = none)
        public float Rotation;       // degrees
        public float RotationSpeed;
        public Vector3 Light;
        public float LightMul;       // Light *= LightMul (1 = none)
        public float Life;
        public float MaxLife;
        public float FadeIn;         // alpha gained per frame until 1 (0 = starts opaque)
        public float FadeOutBelow;   // alpha drops 0.2/frame once Life is under this value
        public float Alpha;
        public float LinearFade;     // 1 = light scaled by Life / MaxLife
        public byte StripFrames;     // >0: horizontal sprite strip, frame = elapsed frames
        public float WidthFactor;
        public float HeightFactor;
        public float ZOffsetPerScale; // drawn ZOffsetPerScale * Scale above Position
    }

    /// <summary>
    /// Fixed-size particle pool for one texture. No allocation after construction.
    /// </summary>
    internal sealed class RageParticlePool
    {
        private readonly RageParticle[] _particles;
        private int _count;

        public RageParticlePool(int capacity, int textureSlot)
        {
            _particles = new RageParticle[Math.Max(1, capacity)];
            TextureSlot = textureSlot;
        }

        public int TextureSlot { get; }
        public int Count => _count;

        public ref RageParticle Spawn(Vector3 position, float scale, float life, Vector3 light)
        {
            int index = _count < _particles.Length ? _count++ : MuGame.Random.Next(_particles.Length);
            ref RageParticle p = ref _particles[index];
            p = default;
            p.Position = position;
            p.Scale = scale;
            p.ScaleMul = 1f;
            p.LightMul = 1f;
            p.Life = life;
            p.MaxLife = MathF.Max(life, 0.001f);
            p.Light = light;
            p.Alpha = 1f;
            p.WidthFactor = 1f;
            p.HeightFactor = 1f;
            return ref p;
        }

        public void Clear() => _count = 0;

        public void Update(float factor)
        {
            for (int i = 0; i < _count;)
            {
                ref RageParticle p = ref _particles[i];
                p.Life -= factor;

                if (p.FadeIn > 0f && (p.FadeOutBelow <= 0f || p.Life >= p.FadeOutBelow))
                    p.Alpha = MathF.Min(1f, p.Alpha + p.FadeIn * factor);
                if (p.FadeOutBelow > 0f && p.Life < p.FadeOutBelow)
                    p.Alpha -= 0.2f * factor;

                p.Velocity.Z -= p.Gravity * factor;
                p.Position += p.Velocity * factor;
                p.Scale += p.ScaleAdd * factor;
                if (p.ScaleMul != 1f)
                    p.Scale *= MathF.Pow(p.ScaleMul, factor);
                if (p.LightMul != 1f)
                    p.Light *= MathF.Pow(p.LightMul, factor);
                p.Rotation += p.RotationSpeed * factor;

                if (p.Life <= 0f || p.Scale <= 0f || (p.FadeOutBelow > 0f && p.Alpha <= 0.05f && p.Life < p.FadeOutBelow))
                {
                    _particles[i] = _particles[--_count];
                    continue;
                }

                i++;
            }
        }

        public void Write(RageFxBatch batch, float textureWidth, float textureHeight)
        {
            for (int i = 0; i < _count && !batch.IsFull; i++)
            {
                ref RageParticle p = ref _particles[i];
                float alpha = MathHelper.Clamp(p.Alpha, 0f, 1f);
                if (p.LinearFade > 0f)
                    alpha *= MathHelper.Clamp(p.Life / p.MaxLife, 0f, 1f);
                if (alpha <= 0f)
                    continue;

                float halfW = textureWidth * p.Scale * p.WidthFactor * 0.5f;
                float halfH = textureHeight * p.Scale * p.HeightFactor * 0.5f;
                Vector3 center = p.Position;
                if (p.ZOffsetPerScale != 0f)
                    center.Z += p.ZOffsetPerScale * p.Scale;

                if (p.StripFrames > 0)
                {
                    int frame = Math.Clamp((int)(p.MaxLife - p.Life), 0, p.StripFrames - 1);
                    float w = 1f / p.StripFrames;
                    batch.Billboard(center, halfW, halfH, p.Rotation, p.Light * alpha,
                        frame * w + 0.005f, 0f, (frame + 1) * w - 0.005f, 1f);
                }
                else
                {
                    batch.Billboard(center, halfW, halfH, p.Rotation, p.Light * alpha);
                }
            }
        }
    }
}
