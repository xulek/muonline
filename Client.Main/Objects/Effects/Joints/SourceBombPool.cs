#nullable enable
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Graphics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects.Joints
{
    /// <summary>
    /// CreateBomb(p, Exp = true, SubType 0) (ZzzEffect.cpp:6393): Position + 80 Z, 20 x BITMAP_SPARK sub 2 at angle
    /// (rand%60 + 150, 0, rand%30), BITMAP_EXPLOTION sub 0 (Light 0.7, 4x4 sheet, LifeTime 20, Frame (20 - LifeTime) / 2,
    /// SOUND_EXPLOTION01 in CreateParticle).
    /// </summary>
    internal sealed class SourceBombPool
    {
        public const string ExplosionTexturePath = "Effect/Explotion01.jpg";
        private const string ExplosionSound = "Sound/eExplosion.wav";

        private struct Explosion
        {
            public bool Live;
            public Vector3 Position;
            public float LifeTime;
        }

        private readonly SourceSparkPool _sparks;
        private readonly Explosion[] _explosions;
        private readonly int _sparksPerBomb;

        public SourceBombPool(int maxBombs, int sparksPerBomb = 20)
        {
            _sparksPerBomb = sparksPerBomb;
            _sparks = new SourceSparkPool(1, maxBombs * sparksPerBomb);
            _explosions = new Explosion[maxBombs];
        }

        public bool HasLive
        {
            get
            {
                if (_sparks.HasLive) return true;
                for (int i = 0; i < _explosions.Length; i++) if (_explosions[i].Live) return true;
                return false;
            }
        }

        public void Spawn(Vector3 position, bool playSound = true)
        {
            position.Z += 80f;
            for (int j = 0; j < _sparksPerBomb; j++)
                _sparks.SpawnSpark(position, new Vector3(MuGame.Random.Next(60) + 60 + 90, 0f, MuGame.Random.Next(30)), 2);

            for (int i = 0; i < _explosions.Length; i++)
            {
                ref Explosion e = ref _explosions[i];
                if (e.Live)
                    continue;
                e = new Explosion { Live = true, Position = position, LifeTime = 20f };
                if (playSound)
                    SoundController.Instance.PlayBuffer(ExplosionSound);
                return;
            }
        }

        public void Move(float factor, TerrainControl? terrain)
        {
            _sparks.Move(factor, terrain);
            for (int i = 0; i < _explosions.Length; i++)
            {
                ref Explosion e = ref _explosions[i];
                if (!e.Live)
                    continue;
                e.LifeTime -= factor;
                if (e.LifeTime <= 0f)
                    e.Live = false;
            }
        }

        public void Draw(SourceQuadBatch batch, Texture2D? sparkTexture, Texture2D? explosionTexture)
        {
            if (sparkTexture != null)
                _sparks.DrawSparks(batch, sparkTexture);

            if (explosionTexture == null || !batch.Begin(explosionTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
                return;
            var light = new Vector3(0.7f);
            for (int i = 0; i < _explosions.Length; i++)
            {
                ref Explosion e = ref _explosions[i];
                if (!e.Live)
                    continue;
                int frame = (int)((20f - e.LifeTime) / 2f);
                batch.AddSpriteRegion(e.Position, explosionTexture.Width, explosionTexture.Height, light, 0f,
                    frame % 4 * 0.25f + 0.005f, frame / 4 * 0.25f + 0.005f, 0.25f - 0.01f, 0.25f - 0.01f);
            }
            batch.End();
        }
    }
}
