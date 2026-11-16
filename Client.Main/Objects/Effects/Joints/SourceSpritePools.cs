#nullable enable
using System;
using Microsoft.Xna.Framework;

namespace Client.Main.Objects.Effects.Joints
{
    /// <summary>
    /// Fixed pool of short-lived jagged BITMAP_JOINT_THUNDER bolts (sub 3 / 16 style): a
    /// start/end pair re-jittered every frame, fading linearly over its lifetime.
    /// </summary>
    internal sealed class SourceBoltPool
    {
        private readonly Bolt[] _bolts;

        private struct Bolt
        {
            public bool Active;
            public Vector3 Start;
            public Vector3 End;
            public Vector3 Light;
            public float Width;
            public float Life;
            public float MaxLife;
            public int Segments;
            public float Jitter;
        }

        public SourceBoltPool(int capacity)
        {
            _bolts = new Bolt[Math.Max(1, capacity)];
        }

        public bool HasActive
        {
            get
            {
                for (int i = 0; i < _bolts.Length; i++)
                    if (_bolts[i].Active)
                        return true;
                return false;
            }
        }

        public void Spawn(Vector3 start, Vector3 end, float width, float lifeFrames, Vector3 light, int segments = 8, float jitter = -1f)
        {
            for (int i = 0; i < _bolts.Length; i++)
            {
                if (_bolts[i].Active)
                    continue;

                float length = Vector3.Distance(start, end);
                _bolts[i] = new Bolt
                {
                    Active = true,
                    Start = start,
                    End = end,
                    Light = light,
                    Width = width,
                    Life = lifeFrames,
                    MaxLife = MathF.Max(lifeFrames, 0.01f),
                    Segments = segments,
                    Jitter = jitter >= 0f ? jitter : length / segments * 0.6f
                };
                return;
            }
        }

        public void Tick(float factor)
        {
            for (int i = 0; i < _bolts.Length; i++)
            {
                ref Bolt bolt = ref _bolts[i];
                if (!bolt.Active)
                    continue;
                bolt.Life -= factor;
                if (bolt.Life <= 0f)
                    bolt.Active = false;
            }
        }

        public void Draw(SourceQuadBatch batch)
        {
            int frameSeed = Environment.TickCount / 40;
            for (int i = 0; i < _bolts.Length; i++)
            {
                ref Bolt bolt = ref _bolts[i];
                if (!bolt.Active)
                    continue;

                float fade = MathHelper.Clamp(bolt.Life / bolt.MaxLife * 1.5f, 0f, 1f);
                batch.AddBolt(bolt.Start, bolt.End, bolt.Segments, bolt.Jitter, bolt.Width,
                    SourceQuadBatch.ToColor(bolt.Light * fade), frameSeed * 31 + i * 7919);
            }
        }
    }

    /// <summary>
    /// Fixed pool of camera-facing sprites / particles with velocity, gravity, growth and a
    /// per-frame light fade (covers BITMAP_FIRE, BITMAP_SPARK, BITMAP_SMOKE, BITMAP_SHINY style
    /// particles used by the skill ports).
    /// </summary>
    internal sealed class SourceParticlePool
    {
        private readonly Particle[] _particles;

        private struct Particle
        {
            public bool Active;
            public Vector3 Position;
            public Vector3 Velocity;
            public Vector3 Light;
            public float Scale;
            public float ScaleGrowth;
            public float Rotation;
            public float Gravity;
            public float LightFade;
            public float Life;
            public float MaxLife;
            public bool LifeFade;
        }

        public SourceParticlePool(int capacity)
        {
            _particles = new Particle[Math.Max(1, capacity)];
        }

        public bool HasActive
        {
            get
            {
                for (int i = 0; i < _particles.Length; i++)
                    if (_particles[i].Active)
                        return true;
                return false;
            }
        }

        public void Spawn(
            Vector3 position,
            Vector3 velocity,
            float scale,
            Vector3 light,
            float lifeFrames,
            float gravity = 0f,
            float scaleGrowth = 0f,
            float lightFade = 1f,
            bool lifeFade = true)
        {
            for (int i = 0; i < _particles.Length; i++)
            {
                if (_particles[i].Active)
                    continue;

                _particles[i] = new Particle
                {
                    Active = true,
                    Position = position,
                    Velocity = velocity,
                    Light = light,
                    Scale = scale,
                    ScaleGrowth = scaleGrowth,
                    Rotation = MuGame.Random.Next(360),
                    Gravity = gravity,
                    LightFade = lightFade,
                    Life = lifeFrames,
                    MaxLife = MathF.Max(lifeFrames, 0.01f),
                    LifeFade = lifeFade
                };
                return;
            }
        }

        public void Tick(float factor)
        {
            for (int i = 0; i < _particles.Length; i++)
            {
                ref Particle p = ref _particles[i];
                if (!p.Active)
                    continue;

                p.Position += p.Velocity * factor;
                p.Velocity.Z += p.Gravity * factor;
                p.Scale = MathF.Max(0f, p.Scale + p.ScaleGrowth * factor);
                if (p.LightFade != 1f)
                    p.Light *= MathF.Pow(p.LightFade, factor);
                p.Life -= factor;
                if (p.Life <= 0f)
                    p.Active = false;
            }
        }

        public void Draw(SourceQuadBatch batch)
        {
            for (int i = 0; i < _particles.Length; i++)
            {
                ref Particle p = ref _particles[i];
                if (!p.Active)
                    continue;

                Vector3 light = p.LifeFade ? p.Light * MathHelper.Clamp(p.Life / p.MaxLife * 2f, 0f, 1f) : p.Light;
                batch.AddSprite(p.Position, p.Scale, light, p.Rotation);
            }
        }
    }
}
