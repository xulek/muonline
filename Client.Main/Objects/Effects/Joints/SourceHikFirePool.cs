#nullable enable
using Client.Main.Graphics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects.Joints
{
    /// <summary>
    /// The three flame particles the Rage Fighter fire effects pick with rand() % 3:
    /// BITMAP_FIRE_HIK1 sub 0 / 5 / 6 (ZzzEffectParticle.cpp:1856 init, 6123 move, firehik01.jpg),
    /// BITMAP_FIRE_CURSEDLICH sub 4 / 9 (293, 4385, 9094, firehik02.jpg) and
    /// BITMAP_FIRE_HIK3 sub 0 / 6 (1933, 6352, firehik03.jpg).
    /// Shared init: Scale (rand%72+72) * 0.01 * Scale, Rotation rand%360, Gravity (rand%24+64) * 0.1, Alpha 0, Light = Alpha * Light.
    /// Per kind: LifeTime rand%5 + 27 / 12 / 17, fade below LifeTime 15 / 10 / 10, shrink (rand%3 + 5 / 6 / 7) * 0.01 per tick.
    /// The rising subs (HIK1 6, CURSEDLICH 9, HIK3 6) keep Rotation 0 and climb an extra Gravity * 1.2 per tick.
    /// </summary>
    internal sealed class SourceHikFirePool
    {
        public const string Hik1TexturePath = "Effect/firehik01.jpg";
        public const string CursedLichTexturePath = "Effect/firehik02.jpg";
        public const string Hik3TexturePath = "Effect/firehik03.jpg";

        public const int Hik1 = 0;
        public const int CursedLich = 1;
        public const int Hik3 = 2;

        private struct Fire
        {
            public bool Live;
            public bool Rising;
            public int Kind;
            public Vector3 Position;
            public Vector3 TurningForce;
            public float Alpha;
            public float Scale;
            public float Rotation;
            public float Gravity;
            public float LifeTime;
        }

        private readonly Fire[] _fires;
        private int _live;

        public SourceHikFirePool(int capacity)
        {
            _fires = new Fire[capacity];
        }

        public bool HasLive => _live > 0;

        /// <summary>switch (rand() % 3): HIK1 / CURSEDLICH / HIK3 with the plain (0 / 4 / 0) or rising (6 / 9 / 6) subs.</summary>
        public void SpawnRandom(Vector3 position, Vector3 light, float scale, bool rising, float factor)
        {
            int kind = MuGame.Random.Next(3);
            if (FpsCheck(factor))
                Spawn(kind, position, light, scale, rising);
        }

        public void Spawn(int kind, Vector3 position, Vector3 light, float scale, bool rising)
        {
            for (int i = 0; i < _fires.Length; i++)
            {
                ref Fire f = ref _fires[i];
                if (f.Live)
                    continue;

                f.Live = true;
                f.Rising = rising;
                f.Kind = kind;
                f.Position = position;
                f.TurningForce = light;
                f.LifeTime = MuGame.Random.Next(5) + (kind == Hik1 ? 27 : kind == CursedLich ? 12 : 17);
                f.Scale = (MuGame.Random.Next(72) + 72) * 0.01f * scale;
                f.Rotation = MuGame.Random.Next(360);
                f.Gravity = (MuGame.Random.Next(24) + 64) * 0.1f;
                f.Alpha = 0f;
                _live++;
                return;
            }
        }

        public void Move(float factor)
        {
            if (_live == 0)
                return;

            for (int i = 0; i < _fires.Length; i++)
            {
                ref Fire f = ref _fires[i];
                if (!f.Live)
                    continue;

                f.LifeTime -= factor;
                bool dead = f.LifeTime <= 0f;
                if (!dead)
                {
                    if (f.LifeTime < (f.Kind == Hik1 ? 15f : 10f))
                        f.Alpha -= factor * 0.2f;
                    else if (f.Alpha < 1f)
                        f.Alpha += factor * (MuGame.Random.Next(2) + 2) * 0.1f;
                    else
                        f.Alpha = 1f;

                    if (f.Alpha < 0.1f)
                        dead = true;

                    if (f.Scale > 0f)
                        f.Scale -= factor * (MuGame.Random.Next(3) + (f.Kind == Hik1 ? 5 : f.Kind == CursedLich ? 6 : 7)) * 0.01f;
                    else
                        dead = true;

                    f.Position.Z += f.Gravity * factor;
                    f.Rotation += 3f * factor;
                    if (f.Rising)
                    {
                        f.Rotation = 0f;
                        f.Position.Z += f.Gravity * 1.2f * factor;
                    }
                }

                if (dead)
                {
                    f.Live = false;
                    _live--;
                }
            }
        }

        public void Draw(SourceQuadBatch batch, Texture2D? hik1, Texture2D? cursedLich, Texture2D? hik3)
        {
            if (_live == 0)
                return;

            DrawKind(batch, hik1, Hik1);
            DrawKind(batch, cursedLich, CursedLich);
            DrawKind(batch, hik3, Hik3);
        }

        private void DrawKind(SourceQuadBatch batch, Texture2D? texture, int kind)
        {
            if (texture == null || !batch.Begin(texture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
                return;

            for (int i = 0; i < _fires.Length; i++)
            {
                ref Fire f = ref _fires[i];
                if (f.Live && f.Kind == kind)
                    batch.AddSprite(f.Position, f.Scale, f.TurningForce * f.Alpha, f.Rotation);
            }
            batch.End();
        }

        private static bool FpsCheck(float factor) => MuGame.Random.NextDouble() < MathHelper.Clamp(factor, 0f, 1f);
    }
}
