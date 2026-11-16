#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Client.Main.Objects.Effects.Joints;
using Client.Main.Objects.Player;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// Power Slash (AT_SKILL_ICE_BLADE = 56), SourceMain5.2:
    /// stage   ZzzCharacter.cpp:2647   PLAYER_ATTACK_TWO_HAND_SWORD_TWO && rand_fps_check(1): 5 x MODEL_MAGIC2 sub 2 at
    ///         yaw -40 .. +40 (step 20), SOUND_SKILL_SWORD3, AttackTime = 15
    /// wave    ZzzEffect.cpp:2068 (LifeTime 20, Direction (0,-60,0)), 10424 + 10437 (sub 2: HiddenMesh 0, 4 x BITMAP_SMOKE 3,
    ///         BITMAP_SMOKE 11, 2 x Shiny02 1.5, flare01 3.5 at +50), epilogue MoveParticle(o, true)
    /// </summary>
    internal sealed class PowerSlashEffect : SourceSkillEffectBase
    {
        private const string ShinyTexturePath = "Effect/Shiny02.jpg";  // BITMAP_SHINY + 1
        private const string LightTexturePath = "Effect/flare01.jpg";  // BITMAP_LIGHT
        private const string SmokeTexturePath = "Effect/smoke01.jpg";  // BITMAP_SMOKE
        private const string SoundPath = "Sound/sKnightSkill3.wav";    // SOUND_SKILL_SWORD3
        private const int SkillId = 56;
        private const int WaveCount = 5;
        private const float StageTimeoutSeconds = 14f * 0.04f;

        private struct Smoke
        {
            public bool Live;
            public bool Sub11;
            public Vector3 Position;
            public Vector3 Angle;
            public Vector3 Velocity;
            public Vector3 Light;
            public Vector3 BaseLight;
            public float Scale;
            public float Rotation;
            public float LifeTime;
        }

        private readonly Vector3[] _positions = new Vector3[WaveCount];
        private readonly Vector3[] _steps = new Vector3[WaveCount];
        private readonly float[] _yaws = new float[WaveCount];
        private readonly Smoke[] _smokes = new Smoke[400];
        private Texture2D? _shinyTexture;
        private Texture2D? _lightTexture;
        private Texture2D? _smokeTexture;
        private bool _released;
        private float _lifeTime;
        private float _waitSeconds;
        private Vector3 _glowLight;

        public PowerSlashEffect(WalkerObject caster)
            : base(caster, 0f, 0f, 512)
        {
        }

        protected override async Task LoadEffectContent()
        {
            _shinyTexture = await LoadTexture(ShinyTexturePath);
            _lightTexture = await LoadTexture(LightTexturePath);
            _smokeTexture = await LoadTexture(SmokeTexturePath);
        }

        protected override void OnStart()
        {
        }

        protected override void OnTick(float factor)
        {
            if (!_released)
            {
                // AttackStage: the waves fire on the first frame of the action that passes rand_fps_check(1).
                _waitSeconds += factor * 0.04f;
                bool inAction = Caster is PlayerObject p && p.CurrentAction == Client.Main.Models.PlayerAction.PlayerAttackTwoHandSwordTwo;
                if ((inAction && FpsCheck(1, factor)) || _waitSeconds >= StageTimeoutSeconds)
                    Release();
                else
                    return;
            }

            if (_lifeTime > 0f)
            {
                float luminosity = (MuGame.Random.Next(4) + 7) * 0.1f;
                if (_lifeTime < 5f)
                    luminosity = MathF.Max(0f, luminosity - (5f - _lifeTime) * 0.2f);
                _glowLight = new Vector3(luminosity * 0.3f, luminosity * 0.6f, luminosity);

                for (int i = 0; i < WaveCount; i++)
                {
                    var angle = new Vector3(0f, 0f, _yaws[i]);
                    for (int j = 0; j < 4; j++)
                        if (FpsCheck(1, factor) && FpsCheck(1, factor))
                            SpawnSmoke(_positions[i], angle, false, Vector3.One, (MuGame.Random.Next(32) + 80) * 0.01f);
                    if (FpsCheck(1, factor) && FpsCheck(1, factor))
                        SpawnSmoke(_positions[i], angle, true, _glowLight, (MuGame.Random.Next(32) + 80) * 0.015f);

                    _positions[i] += _steps[i] * factor;
                }
                _lifeTime -= factor;
            }

            MoveSmokes(factor);
        }

        private void Release()
        {
            _released = true;
            Vector3 origin = CasterPosition;
            float yaw = CasterYawDegrees - 40f;
            for (int i = 0; i < WaveCount; i++)
            {
                _yaws[i] = yaw;
                _positions[i] = origin;
                _steps[i] = SourceJointMath.Rotate(new Vector3(0f, -60f, 0f), new Vector3(0f, 0f, yaw));
                yaw += 20f;
            }
            _lifeTime = 20f;
            SoundController.Instance.PlayBuffer(SoundPath); // the packet already played SOUND_SKILL_SWORD4
        }

        private void SpawnSmoke(Vector3 position, Vector3 angle, bool sub11, Vector3 light, float scale)
        {
            const float factor = 1f; // CreateEffect / CreateParticle init offsets: the 5.2 port scales them by FPS_ANIMATION_FACTOR, which shrinks them at high FPS; 25 Hz values
            for (int i = 0; i < _smokes.Length; i++)
            {
                ref Smoke s = ref _smokes[i];
                if (s.Live)
                    continue;

                s.Live = true;
                s.Sub11 = sub11;
                s.Position = position;
                s.Scale = scale;
                s.BaseLight = light;
                s.Light = light;
                s.Angle = new Vector3(MuGame.Random.Next(90) - 45, angle.Y, MuGame.Random.Next(360));
                s.Velocity = new Vector3(0f, -(MuGame.Random.Next(8) + 40), 0f);
                if (sub11)
                {
                    s.LifeTime = 50f;
                    s.Position += new Vector3((MuGame.Random.Next(64) - 32) * factor, (MuGame.Random.Next(64) - 32) * factor, (MuGame.Random.Next(64) + 32) * factor);
                    s.Rotation = MuGame.Random.Next(360);
                }
                else
                {
                    s.LifeTime = 10f;
                    s.Scale = (MuGame.Random.Next(32) + 80) * 0.01f;
                    s.Rotation = 0f;
                }
                return;
            }
        }

        private void MoveSmokes(float factor)
        {
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
                s.Velocity *= 0.4f; // per frame
                if (s.Sub11)
                {
                    s.Light = s.BaseLight * (s.LifeTime / 50f);
                    s.Scale += factor * 0.05f;
                }
                else
                {
                    float l = s.LifeTime / 8f;
                    s.Light = new Vector3(l * 0.8f, l * 0.8f, l);
                    s.Scale += factor * 0.1f;
                }
            }
        }

        protected override bool HasActiveContent()
        {
            if (!_released || _lifeTime > 0f)
                return true;
            for (int i = 0; i < _smokes.Length; i++) if (_smokes[i].Live) return true;
            return false;
        }

        protected override void OnDraw()
        {
            if (_lifeTime > 0f && Batch.Begin(_shinyTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < WaveCount; i++)
                {
                    Vector3 p = _positions[i] + Vector3.UnitZ * 50f;
                    Batch.AddSprite(p, 1.5f, Vector3.One, MuGame.Random.Next(360));
                    Batch.AddSprite(p, 1.5f, Vector3.One, MuGame.Random.Next(360));
                }
                Batch.End();
            }

            if (_lifeTime > 0f && Batch.Begin(_lightTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < WaveCount; i++)
                    Batch.AddSprite(_positions[i] + Vector3.UnitZ * 50f, 3.5f, _glowLight, MuGame.Random.Next(360));
                Batch.End();
            }

            if (Batch.Begin(_smokeTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _smokes.Length; i++)
                {
                    ref Smoke s = ref _smokes[i];
                    if (s.Live)
                        Batch.AddSprite(s.Position, s.Scale, s.Light, s.Rotation);
                }
                Batch.End();
            }
        }
    }
}
