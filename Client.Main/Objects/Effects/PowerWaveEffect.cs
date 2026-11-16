#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Graphics;
using Client.Main.Models;
using Client.Main.Objects.Effects.Joints;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// Power Wave (AT_SKILL_POWERWAVE = 11), SourceMain5.2:
    /// gate    ZzzCharacter.cpp:2876   AttackStage default: magic action, AnimationFrame >= 5 (else 14 ticks)
    /// release ZzzCharacter.cpp:4906   CreateEffect(MODEL_MAGIC2, o->Position, (0, 0, angle to target), o->Light), SOUND_MAGIC
    /// wave    ZzzEffect.cpp:2068 (init: BlendMesh 0, LifeTime 20, Direction (0,-60,0)), 10424 (move), Skill/Magic02.bmd,
    ///         epilogue MoveParticle(o, true); smoke BITMAP_SMOKE sub 3 (ZzzEffectParticle.cpp:1261/5361)
    /// </summary>
    internal sealed class PowerWaveEffect : SourceSkillEffectBase
    {
        private const string ModelPath = "Skill/Magic02.bmd";          // MODEL_MAGIC2
        private const string SmokeTexturePath = "Effect/smoke01.jpg";  // BITMAP_SMOKE
        private const string SoundPath = "Sound/sMagic.wav";           // SOUND_MAGIC

        private const int SkillId = 11;
        private const float ReleaseDelaySeconds = 14f * 0.04f;
        private const float ModelVelocity = 0.3f;

        private struct Smoke
        {
            public bool Live;
            public Vector3 Position;
            public Vector3 Angle;
            public Vector3 Velocity;
            public Vector3 Light;
            public float Scale;
            public float LifeTime;
        }

        private readonly WalkerObject? _target;
        private readonly Smoke[] _smokes = new Smoke[96];
        private SourceModelRenderer? _model;
        private Texture2D? _smokeTexture;

        private bool _waveLive;
        private Vector3 _wavePosition;
        private Vector3 _waveAngle;
        private Vector3 _waveStep;
        private float _waveLifeTime;
        private float _waveFrame;

        public PowerWaveEffect(WalkerObject caster, WalkerObject? target)
            : base(caster, 5f, ReleaseDelaySeconds, 256)
        {
            _target = target;
        }

        protected override async Task LoadEffectContent()
        {
            _smokeTexture = await LoadTexture(SmokeTexturePath);
            _model = await LoadModel(ModelPath, 0);
            if (_model != null)
            {
                _model.AnimationSpeed = 0f;
                _model.UseNormalBlend();
            }
        }

        protected override void OnStart()
        {
            Vector3 origin = CasterPosition;
            float yaw = _target != null && _target.Status != GameControlStatus.Disposed && _target.World != null
                ? SourceJointMath.CreateAngle2D(origin, _target.WorldPosition.Translation)
                : CasterYawDegrees;

            _waveLive = true;
            _wavePosition = origin;
            _waveAngle = new Vector3(0f, 0f, yaw);
            _waveStep = SourceJointMath.Rotate(new Vector3(0f, -60f, 0f), _waveAngle);
            _waveLifeTime = 20f;
            PlayUnmappedSound(SkillId, SoundPath);
        }

        protected override void OnTick(float factor)
        {
            if (_waveLive)
            {
                for (int j = 0; j < 4; j++)
                    if (FpsCheck(1, factor) && FpsCheck(1, factor))
                        SpawnSmoke();

                _waveFrame += ModelVelocity * factor;
                _wavePosition += _waveStep * factor;
                _waveLifeTime -= factor;
                if (_waveLifeTime <= 0f)
                    _waveLive = false;
            }

            for (int i = 0; i < _smokes.Length; i++)
            {
                ref Smoke p = ref _smokes[i];
                if (!p.Live)
                    continue;

                p.LifeTime -= factor;
                if (p.LifeTime <= 0f)
                {
                    p.Live = false;
                    continue;
                }

                p.Position += SourceJointMath.Rotate(p.Velocity, p.Angle) * factor;
                float luminosity = p.LifeTime / 8f;
                p.Light = new Vector3(luminosity * 0.8f, luminosity * 0.8f, luminosity);
                p.Velocity *= 0.4f; // per frame
                p.Scale += factor * 0.1f;
            }
        }

        private void SpawnSmoke()
        {
            for (int i = 0; i < _smokes.Length; i++)
            {
                ref Smoke p = ref _smokes[i];
                if (p.Live)
                    continue;

                p.Live = true;
                p.Position = _wavePosition;
                p.LifeTime = 10f;
                p.Light = Vector3.One;
                p.Scale = (MuGame.Random.Next(32) + 80) * 0.01f;
                p.Angle = new Vector3(MuGame.Random.Next(90) - 45, 0f, MuGame.Random.Next(360));
                p.Velocity = new Vector3(0f, -(MuGame.Random.Next(8) + 40), 0f);
                return;
            }
        }

        protected override bool HasActiveContent()
        {
            if (_waveLive)
                return true;
            for (int i = 0; i < _smokes.Length; i++) if (_smokes[i].Live) return true;
            return false;
        }

        protected override void OnDraw()
        {
            if (_waveLive && _model != null)
            {
                // Values of the move case, evaluated before LifeTime -= factor.
                float lifeTime = _waveLifeTime + SourceJointMath.FrameFactor;
                _model.ScrollU = -lifeTime * 0.2f;
                _model.SetAnimationTime(_waveFrame);
                _model.Update(LastGameTime);
                Vector3 light = World?.Terrain?.EvaluateTerrainLight(_wavePosition.X, _wavePosition.Y) ?? Vector3.One;
                _model.DrawInstance(_wavePosition, new Vector3(0f, 0f, MathHelper.ToRadians(_waveAngle.Z)), 0.9f, light,
                    MathF.Max(MathF.Round(lifeTime * 0.1f * 50f) / 50f, 0.003f));
            }

            if (Batch.Begin(_smokeTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _smokes.Length; i++)
                {
                    ref Smoke p = ref _smokes[i];
                    if (p.Live)
                        Batch.AddSprite(p.Position, p.Scale, p.Light);
                }
                Batch.End();
            }
        }

        protected override void DisposeEffect()
        {
            _model?.Dispose();
        }
    }
}
