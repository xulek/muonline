#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Graphics;
using Client.Main.Models;
using Client.Main.Objects.Effects.Joints;
using Client.Main.Objects.Player;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// Poison (AT_SKILL_POISON = 1), SourceMain5.2:
    /// gate    ZzzCharacter.cpp:2876   AttackStage default: magic action, AnimationFrame >= 5 (else 14 ticks)
    /// release ZzzCharacter.cpp:4857   MODEL_POISON at the target (players only), 10 x BITMAP_SMOKE sub 1, SOUND_HEART
    /// poison  ZzzEffect.cpp:2990 (init), 9745 (move), Skill/Poison01.bmd, BlendMesh 1
    /// smoke   ZzzEffectParticle.cpp:1287 (init), 5393 (move), default smoke render
    /// </summary>
    internal sealed class PoisonEffect : SourceSkillEffectBase
    {
        private const string ModelPath = "Skill/Poison01.bmd";        // MODEL_POISON
        private const string SmokeTexturePath = "Effect/smoke01.jpg"; // BITMAP_SMOKE
        private const string SoundPath = "Sound/pHeartBeat.wav";      // SOUND_HEART

        private const int SkillId = 1;
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
        private readonly Smoke[] _smokes = new Smoke[10];
        private SourceModelRenderer? _model;
        private Texture2D? _smokeTexture;

        private bool _poisonLive;
        private Vector3 _poisonPosition;
        private Vector3 _poisonAngle;
        private float _poisonLifeTime;
        private float _poisonFrame;

        public PoisonEffect(WalkerObject caster, WalkerObject? target)
            : base(caster, 5f, ReleaseDelaySeconds, 64)
        {
            _target = target;
        }

        protected override async Task LoadEffectContent()
        {
            _smokeTexture = await LoadTexture(SmokeTexturePath);
            _model = await LoadModel(ModelPath, 1);
            if (_model != null)
            {
                _model.AnimationSpeed = 0f;
                _model.UseNormalBlend();
            }
        }

        protected override void OnStart()
        {
            if (!(_target != null && _target.Status != GameControlStatus.Disposed && _target.World != null))
                return;

            Vector3 position = _target.WorldPosition.Translation;
            const float factor = 1f; // CreateEffect / CreateParticle init offsets: the 5.2 port scales them by FPS_ANIMATION_FACTOR, which shrinks them at high FPS; 25 Hz values
            _poisonAngle = new Vector3(0f, 0f, CasterYawDegrees);

            if (Caster is PlayerObject)
            {
                _poisonLive = true;
                _poisonPosition = position;
                _poisonLifeTime = 40f;
            }

            for (int i = 0; i < _smokes.Length; i++)
            {
                ref Smoke s = ref _smokes[i];
                s.Live = true;
                s.LifeTime = 50f;
                s.Light = new Vector3(0.4f, 0.6f, 1f);
                s.Scale = (MuGame.Random.Next(32) + 80) * 0.01f;
                s.Position = position + new Vector3(
                    (MuGame.Random.Next(64) - 32) * factor,
                    (MuGame.Random.Next(64) - 32) * factor,
                    (MuGame.Random.Next(64) + 32) * factor);
                s.Angle = new Vector3(MuGame.Random.Next(90) - 45, 0f, MuGame.Random.Next(360));
                s.Velocity = new Vector3(0f, -(MuGame.Random.Next(8) + 40), 0f);
            }

            PlayUnmappedSound(SkillId, SoundPath);
        }

        protected override void OnTick(float factor)
        {
            if (_poisonLive)
            {
                _poisonFrame += ModelVelocity * factor;
                _poisonLifeTime -= factor;
                if (_poisonLifeTime <= 0f)
                    _poisonLive = false;
            }

            for (int i = 0; i < _smokes.Length; i++)
            {
                ref Smoke s = ref _smokes[i];
                if (!s.Live)
                    continue;

                s.LifeTime -= factor;
                if (s.LifeTime <= 0f)
                {
                    s.Live = false;
                    continue;
                }

                s.Position += SourceJointMath.Rotate(s.Velocity, s.Angle) * factor;
                float luminosity = s.LifeTime / 50f;
                s.Light = new Vector3(luminosity * 0.5f, luminosity, luminosity * 0.8f);
                s.Velocity *= 0.4f; // per frame
                s.Scale += factor * 0.05f;
            }
        }

        protected override bool HasActiveContent()
        {
            if (_poisonLive)
                return true;
            for (int i = 0; i < _smokes.Length; i++) if (_smokes[i].Live) return true;
            return false;
        }

        protected override void OnDraw()
        {
            if (_poisonLive && _model != null)
            {
                // BlendMeshLight = Alpha = LifeTime * 0.1 (set in the move case, before LifeTime -= factor).
                float value = MathF.Round((_poisonLifeTime + SourceJointMath.FrameFactor) * 0.1f * 50f) / 50f;
                _model.SetAnimationTime(_poisonFrame);
                _model.Update(LastGameTime);
                Vector3 light = World?.Terrain?.EvaluateTerrainLight(_poisonPosition.X, _poisonPosition.Y) ?? Vector3.One;
                _model.DrawInstance(_poisonPosition, new Vector3(0f, 0f, MathHelper.ToRadians(_poisonAngle.Z)), 1f, light,
                    MathF.Max(value, 0.003f), MathF.Min(1f, MathF.Max(value, 0.003f)));
            }

            if (Batch.Begin(_smokeTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _smokes.Length; i++)
                {
                    ref Smoke s = ref _smokes[i];
                    if (s.Live)
                        Batch.AddSprite(s.Position, s.Scale, s.Light);
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
