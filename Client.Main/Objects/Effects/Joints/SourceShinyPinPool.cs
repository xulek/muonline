#nullable enable
using Client.Main.Graphics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects.Joints
{
    /// <summary>
    /// BITMAP_SHINY + 6 sub 0 and BITMAP_PIN_LIGHT sub 0 particles (Weakness / Enervation cast and debuff), SourceMain5.2:
    /// shiny   ZzzEffectParticle.cpp:2719 (Alpha 1, Position += (-5 + rand%10)F, LifeTime 30, Gravity 1.3, Scale += rand%3/10*F),
    ///         7385 (Rotation += 5F, Scale -= 0.02F, Alpha -= 0.001F, Z -= Gravity*F, Light += (rand%10/100 - 0.05)F),
    ///         9257 (sprite + BITMAP_LIGHT sprite of the same size)
    /// pin     ZzzEffectParticle.cpp:2742 (same jitter, LifeTime 30, Gravity 10, Scale += rand%5/10*F), 7406 (as shiny without
    ///         rotation), default sprite render.
    /// </summary>
    internal sealed class SourceShinyPinPool
    {
        public const string ShinyTexturePath = "Effect/shiny05.jpg";  // BITMAP_SHINY + 6
        public const string PinTexturePath = "Effect/pin_lights.jpg"; // BITMAP_PIN_LIGHT
        public const string LightTexturePath = "Effect/flare01.jpg";  // BITMAP_LIGHT

        private struct Particle
        {
            public bool Live;
            public bool Pin;
            public Vector3 Position;
            public Vector3 Light;
            public float Scale;
            public float Rotation;
            public float Gravity;
            public float Alpha;
            public float LifeTime;
        }

        private readonly Particle[] _particles;
        private int _count;

        public SourceShinyPinPool(int capacity)
        {
            _particles = new Particle[capacity];
        }

        public bool HasLive => _count > 0;

        public void SpawnShiny(Vector3 position, Vector3 light, float scale, float factor) => Spawn(false, position, light, scale, factor);

        public void SpawnPin(Vector3 position, Vector3 light, float scale, float factor) => Spawn(true, position, light, scale, factor);

        private void Spawn(bool pin, Vector3 position, Vector3 light, float scale, float factor)
        {
            for (int i = 0; i < _particles.Length; i++)
            {
                ref Particle p = ref _particles[i];
                if (p.Live)
                    continue;
                p.Live = true;
                p.Pin = pin;
                p.Alpha = 1f;
                p.Position = position + new Vector3(
                    (-5f + MuGame.Random.Next(10)) * factor,
                    (-5f + MuGame.Random.Next(10)) * factor,
                    (-5f + MuGame.Random.Next(10)) * factor);
                p.Light = light;
                p.LifeTime = 30f;
                p.Rotation = 0f;
                p.Gravity = pin ? 10f : 1.3f;
                p.Scale = scale + MuGame.Random.Next(pin ? 5 : 3) / 10f * factor;
                _count++;
                return;
            }
        }

        public void Move(float factor)
        {
            if (_count == 0)
                return;
            for (int i = 0; i < _particles.Length; i++)
            {
                ref Particle p = ref _particles[i];
                if (!p.Live)
                    continue;
                p.LifeTime -= factor;
                p.Alpha -= factor * 0.001f;
                if (p.LifeTime <= 0f || p.Alpha <= 0f)
                {
                    p.Live = false;
                    _count--;
                    continue;
                }
                if (!p.Pin)
                    p.Rotation += 5f * factor;
                p.Scale -= factor * 0.02f;
                p.Position.Z -= p.Gravity * factor;
                float light = (MuGame.Random.Next(10) / 100f - 0.05f) * factor;
                p.Light += new Vector3(light);
            }
        }

        public void Draw(SourceQuadBatch batch, Texture2D? shiny, Texture2D? light, Texture2D? pin)
        {
            if (_count == 0)
                return;

            if (batch.Begin(shiny, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _particles.Length; i++)
                {
                    ref Particle p = ref _particles[i];
                    if (p.Live && !p.Pin)
                        batch.AddSprite(p.Position, p.Scale, p.Light, p.Rotation);
                }
                batch.End();
            }

            // RenderSprite(BITMAP_LIGHT, ..., Width, Height) with the shiny bitmap's size.
            if (shiny != null && batch.Begin(light, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _particles.Length; i++)
                {
                    ref Particle p = ref _particles[i];
                    if (p.Live && !p.Pin)
                        batch.AddSpriteRegion(p.Position, shiny.Width * p.Scale, shiny.Height * p.Scale, p.Light, p.Rotation, 0f, 0f, 1f, 1f);
                }
                batch.End();
            }

            if (batch.Begin(pin, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _particles.Length; i++)
                {
                    ref Particle p = ref _particles[i];
                    if (p.Live && p.Pin)
                        batch.AddSprite(p.Position, p.Scale, p.Light, 0f);
                }
                batch.End();
            }
        }
    }
}
