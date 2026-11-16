#nullable enable
using System;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Graphics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects.Joints
{
    /// <summary>
    /// CreateBomb3(vPos, iSubType, fScale) (ZzzEffect.cpp:6494): angle (rand%60+150, 0, rand%30);
    /// up to 2 BITMAP_SUMMON_SAHAMUTT_EXPLOSION (sub 0: 1/3 chance; 1: one unless rand_fps_check(5); 2: two unless
    /// rand_fps_check(10); 3: two), BITMAP_MAGIC + 1 (1, 0.5, 0.2), 3 x MODEL_STONE sub 13 / 14, 5 (sub 3: 20) x BITMAP_SPARK 2.
    /// explosion ZzzEffectParticle.cpp:2498 (LifeTime 16), 4303 (Frame = 16 - LifeTime), 9000 (4x4 sheet, quad = sheet x Scale)
    /// magic+1   ZzzEffectParticle.cpp:939 (LifeTime 10, Scale += (rand%32+48)*0.001F, Rotation WorldTime%360),
    ///           4169 (Light *= 0.9^F, Scale += 0.1F), 9189 default sprite
    /// Stones sub 13 are invisible in the original (Scale *= the default Scale argument 0) and their move case breaks before any
    /// child, so they are not spawned. Sub 14 stones (fScale != 1, Light (1, 0.6, 0.2), Scale 2.2 * fScale) go to <see cref="Stones"/>,
    /// which the owning effect loads, moves and draws.
    /// When the explosion loop breaks before writing vBombPos the original reads stale stack data: the previous bomb position is used.
    /// </summary>
    internal sealed class SourceBomb3Pool
    {
        public const string ExplosionTexturePath = "Effect/loungexflow.jpg"; // BITMAP_SUMMON_SAHAMUTT_EXPLOSION
        public const string Magic1TexturePath = "Effect/Magic_Ground2.jpg";  // BITMAP_MAGIC + 1

        private struct Particle
        {
            public bool Live;
            public bool IsExplosion;
            public Vector3 Position;
            public Vector3 Light;
            public float Scale;
            public float Rotation;
            public float LifeTime;
        }

        private static Vector3 s_lastBombPosition;

        private readonly Particle[] _particles;
        private readonly SourceSparkPool _sparks;

        public SourceBomb3Pool(int maxParticles, int maxSparks)
        {
            _particles = new Particle[maxParticles];
            _sparks = new SourceSparkPool(0, maxSparks);
        }

        /// <summary>Receives the MODEL_STONE sub 14 debris of bombs with fScale != 1 (null: not spawned).</summary>
        public SourceStonePool? Stones { get; set; }

        public bool HasLive
        {
            get
            {
                for (int i = 0; i < _particles.Length; i++) if (_particles[i].Live) return true;
                return _sparks.HasLive;
            }
        }

        public void Spawn(Vector3 position, int subType, float factor, float scale = 1f)
        {
            var angle = new Vector3(MuGame.Random.Next(60) + 60 + 90, 0f, MuGame.Random.Next(30));
            Vector3 bombPosition = s_lastBombPosition;
            for (int i = 0; i < 2; ++i)
            {
                if (subType == 2)
                {
                    if (FPSCounter.Instance.RandFPSCheck(10)) break;
                    bombPosition = position + new Vector3(MuGame.Random.Next(150) - 75, MuGame.Random.Next(150) - 75, MuGame.Random.Next(130) + 30);
                }
                else if (subType == 1)
                {
                    if (i == 1) break;
                    if (FPSCounter.Instance.RandFPSCheck(5)) break;
                    bombPosition = position + new Vector3(MuGame.Random.Next(80) - 40, MuGame.Random.Next(80) - 40, MuGame.Random.Next(120) + 30);
                }
                else if (subType == 3)
                {
                    bombPosition = position + new Vector3(MuGame.Random.Next(90) - 40, MuGame.Random.Next(90) - 40, MuGame.Random.Next(100) + 70);
                }
                else
                {
                    if (i == 1) break;
                    if (MuGame.Random.Next(3) != 0) break;
                    bombPosition = position + new Vector3(MuGame.Random.Next(30) - 15, MuGame.Random.Next(30) - 15, MuGame.Random.Next(80) + 30);
                }

                float explosionScale = scale != 1f ? scale : 0.15f * MuGame.Random.Next(10);
                Add(true, bombPosition, Vector3.One, explosionScale, 0f, 16f);
            }
            s_lastBombPosition = bombPosition;

            float magicScale = (scale != 1f ? 2.2f * scale : 1f) + (MuGame.Random.Next(32) + 48) * 0.001f * factor;
            Add(false, bombPosition, new Vector3(1f, 0.5f, 0.2f), magicScale, (long)FPSCounter.Instance.WorldTime % 360, 10f);

            if (scale != 1f && Stones != null)
            {
                for (int i = 0; i < 3; ++i)
                    Stones.SpawnSub14(bombPosition, angle, new Vector3(1f, 0.6f, 0.2f), 2.2f * scale, factor);
            }

            int sparks = subType == 3 ? 20 : 5;
            for (int i = 0; i < sparks; ++i)
                _sparks.SpawnSpark(bombPosition, angle, 2);
        }

        private void Add(bool explosion, Vector3 position, Vector3 light, float scale, float rotation, float lifeTime)
        {
            for (int i = 0; i < _particles.Length; i++)
            {
                ref Particle p = ref _particles[i];
                if (p.Live)
                    continue;
                p = new Particle { Live = true, IsExplosion = explosion, Position = position, Light = light, Scale = scale, Rotation = rotation, LifeTime = lifeTime };
                return;
            }
        }

        public void Move(float factor, TerrainControl? terrain)
        {
            float decay = MathF.Pow(0.9f, factor);
            for (int i = 0; i < _particles.Length; i++)
            {
                ref Particle p = ref _particles[i];
                if (!p.Live)
                    continue;
                p.LifeTime -= factor;
                if (p.LifeTime <= 0f)
                {
                    p.Live = false;
                    continue;
                }
                if (!p.IsExplosion)
                {
                    p.Light *= decay;
                    p.Scale += factor * 0.1f;
                }
            }
            _sparks.Move(factor, terrain);
        }

        public void Draw(SourceQuadBatch batch, Texture2D? explosionTexture, Texture2D? magic1Texture, Texture2D? sparkTexture)
        {
            if (explosionTexture != null && batch.Begin(explosionTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _particles.Length; i++)
                {
                    ref Particle p = ref _particles[i];
                    if (!p.Live || !p.IsExplosion)
                        continue;
                    int frame = (int)(16f - p.LifeTime);
                    batch.AddSpriteRegion(p.Position, explosionTexture.Width * p.Scale, explosionTexture.Height * p.Scale, p.Light, 0f,
                        frame % 4 * 0.25f + 0.005f, frame / 4 * 0.25f + 0.005f, 0.25f - 0.01f, 0.25f - 0.01f);
                }
                batch.End();
            }

            if (batch.Begin(magic1Texture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _particles.Length; i++)
                {
                    ref Particle p = ref _particles[i];
                    if (p.Live && !p.IsExplosion)
                        batch.AddSprite(p.Position, p.Scale, p.Light, p.Rotation);
                }
                batch.End();
            }

            _sparks.DrawSparks(batch, sparkTexture);
        }
    }
}
