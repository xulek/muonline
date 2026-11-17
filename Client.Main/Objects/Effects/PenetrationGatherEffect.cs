#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Client.Main.Objects.Effects.Joints;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// Penetration (AT_SKILL_PIERCING = 52) charge, SourceMain5.2:
    /// stage   ZzzCharacter.cpp:2573   CheckAttackTime(3): CreateEffect(BITMAP_GATHERING, o->Position, o->Angle, o->Light, 0, o),
    ///         SOUND_PIERCING (the arrow itself is ArrowProjectileEffect, SubType 2)
    /// effect  ZzzEffect.cpp:1334 (LifeTime 10, rot(0,-100,0) * F + 150 * F), 8462 (3 x: Shiny02 sprite 0.3 scale,
    ///         BITMAP_SPARK+1 sub 2 gathering inward; breaks when rand_fps_check(1) fails)
    /// particle ZzzEffectParticle.cpp:2132 (sub 2: LifeTime 15, Velocity (target - pos) / 10), 6652 (move)
    /// </summary>
    internal sealed class PenetrationGatherEffect : SourceSkillEffectBase
    {
        private const string ShinyTexturePath = "Effect/Shiny02.jpg"; // BITMAP_SHINY + 1
        private const string SparkTexturePath = "Effect/Spark03.jpg"; // BITMAP_SPARK + 1
        private const string SoundPath = "Sound/ePiercing.wav";       // SOUND_PIERCING
        private const float StartDelaySeconds = 2f * 0.04f;           // AttackTime 1 -> 3

        private struct Spark
        {
            public bool Live;
            public Vector3 Position;
            public Vector3 Velocity;
            public float Scale;
            public float LifeTime;
        }

        private readonly Spark[] _sparks = new Spark[64];
        private Texture2D? _shinyTexture;
        private Texture2D? _sparkTexture;
        private Vector3 _position;
        private float _lifeTime;
        private int _sprites;
        private static readonly Vector3 GatherColor = new(0.85f, 0.9f, 1f);
        private float _age;
        private readonly Vector3[] _spriteScales = new Vector3[3];

        public PenetrationGatherEffect(WalkerObject caster)
            : base(caster, float.MaxValue, StartDelaySeconds, 128)
        {
        }

        protected override async Task LoadEffectContent()
        {
            _shinyTexture = await LoadTexture(ShinyTexturePath);
            _sparkTexture = await LoadTexture(SparkTexturePath);
        }

        protected override void OnStart()
        {
            const float factor = 1f; // CreateEffect / CreateParticle init offsets: the 5.2 port scales them by FPS_ANIMATION_FACTOR, which shrinks them at high FPS; 25 Hz values
            var angle = new Vector3(0f, 0f, CasterYawDegrees);
            _position = CasterPosition + SourceJointMath.Rotate(new Vector3(0f, -100f, 0f), angle) * factor;
            _position.Z += 150f * factor;
            _lifeTime = 10f;
            SoundController.Instance.PlayBuffer(SoundPath);
        }

        protected override void OnTick(float factor)
        {
            _sprites = 0;
            // Charge glow at the gathering point (10 frames + spark tail).
            _age += factor;
            Lights.Set(0, _position, GatherColor, 180f,
                1f * EffectLights.Envelope(_age, 15f, 3f, 6f) * EffectLights.Flicker(_age * 0.04f, 5.3f, 0.15f));
            if (_lifeTime > 0f)
            {
                for (int j = 0; j < 3; j++)
                {
                    if (!FpsCheck(1, factor))
                        break;

                    var angle = new Vector3(MuGame.Random.Next(360), 0f, MuGame.Random.Next(360));
                    Vector3 start = _position + SourceJointMath.Rotate(new Vector3(0f, 120f, 0f), angle);
                    _spriteScales[_sprites++] = new Vector3((MuGame.Random.Next(8) + 8) * 0.3f, MuGame.Random.Next(360), 0f);
                    if (FpsCheck(1, factor))
                        SpawnSpark(start, (MuGame.Random.Next(50) + 10) / 100f);
                }
                _lifeTime -= factor;
            }

            for (int i = 0; i < _sparks.Length; i++)
            {
                ref Spark s = ref _sparks[i];
                if (!s.Live)
                    continue;

                s.LifeTime -= factor;
                if (s.LifeTime <= 0f)
                {
                    s.Live = false;
                    continue;
                }
                if (s.LifeTime > 5f && _lifeTime <= 0f)
                    s.LifeTime = 5f; // Target (the gathering effect) died
                s.Scale += factor * 0.05f;
                s.Position += s.Velocity * factor;
            }
        }

        private void SpawnSpark(Vector3 position, float scale)
        {
            for (int i = 0; i < _sparks.Length; i++)
            {
                ref Spark s = ref _sparks[i];
                if (s.Live)
                    continue;

                s.Live = true;
                s.Position = position;
                s.Scale = scale;
                s.LifeTime = 15f;
                s.Velocity = (_position - position) / 10f;
                return;
            }
        }

        protected override bool HasActiveContent()
        {
            if (_lifeTime > 0f)
                return true;
            for (int i = 0; i < _sparks.Length; i++) if (_sparks[i].Live) return true;
            return false;
        }

        protected override void OnDraw()
        {
            if (_sprites > 0 && Batch.Begin(_shinyTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _sprites; i++)
                    Batch.AddSprite(_position, _spriteScales[i].X, Vector3.One, _spriteScales[i].Y);
                Batch.End();
            }

            if (Batch.Begin(_sparkTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _sparks.Length; i++)
                {
                    ref Spark s = ref _sparks[i];
                    if (s.Live)
                        Batch.AddSprite(s.Position, s.Scale, new Vector3(s.LifeTime / 5f));
                }
                Batch.End();
            }
        }
    }
}
