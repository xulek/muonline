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
    /// Swell Mana (AT_SKILL_MANA = 69), SourceMain5.2:
    /// packet   WSclient.cpp:4337       mount action family / PLAYER_SKILL_VITALITY; SkillSuccess: MODEL_MANA_RUNE sub 0
    /// gate     ZzzCharacter.cpp:2754   case without a release: 14 ticks
    /// release  ZzzCharacter.cpp:4072   36 x BITMAP_JOINT_SPIRIT sub 21 at +100 Z, angle (-10, 0, i * 10), scale 60;
    ///          i % 20 == 0: BITMAP_MAGIC+1 sub 10; SOUND_BMS_MANA (loaded only in Battle Castle)
    /// rune     ZzzEffect.cpp:4325 (sub 0: LifeTime 50, Scale 0, Gravity 0.1, Alpha 0.3, HiddenMesh 0, Z += 300F, yaw 45;
    ///          sub 1: LifeTime 10, Scale 1.1, BlendMesh -2, BlendMeshLight 0.4), 14480 (sub 0: &gt; 43 hidden, 30..40 grow and
    ///          sub 1 FpsChecked at 1, &lt; 15 shrink + Alpha LifeTime / 20 + Z -= 10F; sub 1: Scale -= 0.02F, Alpha LifeTime / 20),
    ///          ZzzObject.cpp:1583 (sub 0: BodyLight (0.3,0.6,1), RENDER_TEXTURE + RENDER_BRIGHT | RENDER_CHROME)
    /// spirit   ZzzEffectJoint.cpp:621 (sub 21: additive, RENDER_FACE_TWO, Velocity 50, LifeTime 20, MaxTails 3, Light 0.5),
    ///          3847 (BITMAP_LIGHT sprite 4 + (20 - LifeTime) / 5 every frame, (0.1,0.5,1) or the fading joint light below 10,
    ///          Velocity += 5F), prologue flight (0,-Velocity,0)
    /// ground   ZzzEffect.cpp:1224 (sub 10: LifeTime 40, Scale (rand%50+50)/100*4), 19154 (sin((60 - LifeTime) * 0.05) + 0.5,
    ///          (0.1,0.5,1), Magic_Ground2 decal)
    /// </summary>
    internal sealed class SwellManaEffect : SourceSkillEffectBase
    {
        private const string RuneModelPath = "Skill/ManaRune.bmd";            // MODEL_MANA_RUNE
        private const string SpiritTexturePath = "Effect/JointSpirit01.jpg";  // BITMAP_JOINT_SPIRIT
        private const string LightTexturePath = "Effect/flare01.jpg";         // BITMAP_LIGHT
        private const string GroundTexturePath = "Effect/Magic_Ground2.jpg";  // BITMAP_MAGIC + 1
        private const string SoundPath = "Sound/BattleCastle/sDSwllMana.wav"; // SOUND_BMS_MANA
        private const short BattleCastleWorldIndex = 31;
        private const int SpiritCount = 36;
        private const float ModelVelocity = 0.3f;
        private static readonly Vector3 SpiritLight = new(0.1f, 0.5f, 1f);
        private static readonly Vector3 RuneBodyLight = new(0.3f, 0.6f, 1f);

        private struct Spirit
        {
            public bool Live;
            public Vector3 Position;
            public Vector3 Angle;
            public Vector3 Light;
            public Vector3 SpriteLight;
            public float Velocity;
            public float LifeTime;
            public SourceTrail Trail;
        }

        private struct Pulse
        {
            public bool Live;
            public float Scale;
            public float Frame;
            public float LifeTime;
        }

        private struct Ground
        {
            public bool Live;
            public Vector3 Position;
            public float Angle;
            public float Scale;
            public float LifeTime;
        }

        private readonly Spirit[] _spirits = new Spirit[SpiritCount];
        private readonly Pulse[] _pulses = new Pulse[16];
        private readonly Ground[] _grounds = new Ground[2];
        private SourceModelRenderer? _rune;
        private SourceModelRenderer? _runePulse;
        private Texture2D? _spiritTexture;
        private Texture2D? _lightTexture;
        private Texture2D? _groundTexture;

        private Vector3 _runePosition;
        private float _runeScale;
        private float _runeGravity;
        private float _runeAlpha;
        private float _runeFrame;
        private float _runeLife;
        private float _attackTime = 1f;
        private bool _released;
        private static readonly Vector3 ManaColor = new(0.2f, 0.55f, 1f);
        private Vector3 _releaseOrigin;
        private float _releaseAge = -1f;

        public SwellManaEffect(WalkerObject caster)
            : base(caster, triggerFrame: 0f, triggerTimeoutSeconds: 0f, maxQuads: SpiritCount * 8 + 64)
        {
        }

        protected override async Task LoadEffectContent()
        {
            _rune = await LoadModel(RuneModelPath, -1, 0);
            if (_rune != null)
            {
                _rune.AnimationSpeed = 0f;
                _rune.UseNormalBlend();
                _rune.ChromeBrightPass = true;
            }
            _runePulse = await LoadModel(RuneModelPath, -2);
            if (_runePulse != null)
                _runePulse.AnimationSpeed = 0f;
            _spiritTexture = await LoadTexture(SpiritTexturePath);
            _lightTexture = await LoadTexture(LightTexturePath);
            _groundTexture = await LoadTexture(GroundTexturePath);
        }

        protected override void OnStart()
        {
            // ReceiveMagic: MODEL_MANA_RUNE sub 0 at the packet.
            _runePosition = CasterPosition + Vector3.UnitZ * (300f * SourceJointMath.FrameFactor);
            _runeScale = 0f;
            _runeGravity = 0.1f;
            _runeAlpha = 0.3f;
            _runeLife = 50f;
        }

        private void Release()
        {
            _released = true;
            Vector3 origin = CasterPosition;
            _releaseOrigin = origin + Vector3.UnitZ * 100f;
            _releaseAge = 0f;
            int tails = SourceJointMath.ScaledMaxTails(3);
            for (int i = 0; i < SpiritCount; i++)
            {
                _spirits[i] = new Spirit
                {
                    Live = true,
                    Position = origin + Vector3.UnitZ * 100f,
                    Angle = new Vector3(-10f, 0f, i * 10f),
                    Light = new Vector3(0.5f),
                    Velocity = 50f,
                    LifeTime = 20f,
                    Trail = new SourceTrail(tails),
                };
                _spirits[i].Trail.Push(_spirits[i].Position, _spirits[i].Angle, 60f);
                if (i % 20 == 0)
                    _grounds[i / 20] = new Ground
                    {
                        Live = true,
                        Position = origin,
                        Angle = i * 10f,
                        Scale = (MuGame.Random.Next(50) + 50) / 100f * 4f,
                        LifeTime = 40f,
                    };
            }

            if (World?.WorldIndex == BattleCastleWorldIndex)
                SoundController.Instance.PlayBuffer(SoundPath);
        }

        protected override void OnTick(float factor)
        {
            if (!_released)
            {
                _attackTime += factor;
                if ((int)_attackTime >= 15)
                    Release();
            }

            MoveRune(factor);

            for (int i = 0; i < _spirits.Length; i++)
            {
                ref Spirit s = ref _spirits[i];
                if (!s.Live)
                    continue;
                s.Position += SourceJointMath.Rotate(new Vector3(0f, -s.Velocity, 0f), s.Angle) * factor;
                s.SpriteLight = SpiritLight;
                if (s.LifeTime < 10f)
                {
                    s.Light *= MathF.Pow(1f / 1.2f, factor);
                    s.SpriteLight = s.Light;
                }
                s.Velocity += factor * 5f;
                s.Trail.Push(s.Position, s.Angle, 60f);
                s.LifeTime -= factor;
                if (s.LifeTime < 0f)
                    s.Live = false;
            }

            for (int i = 0; i < _grounds.Length; i++)
            {
                ref Ground g = ref _grounds[i];
                if (!g.Live)
                    continue;
                g.LifeTime -= factor;
                if (g.LifeTime <= 0f)
                    g.Live = false;
            }

            UpdateLights(factor);
        }

        private void UpdateLights(float factor)
        {
            // slot 0: the floating rune (grows at 40..30, fades with its alpha below 15);
            // slot 1: burst when the spirits are released (20 frames).
            float rune = _runeLife > 0f && _runeLife <= 43f
                ? MathHelper.Clamp(_runeScale, 0f, 1f) * MathHelper.Clamp(_runeAlpha / 0.3f, 0f, 1f)
                : 0f;
            Lights.Set(0, _runePosition, RuneBodyLight, 260f, 1f * rune);

            if (_releaseAge >= 0f)
            {
                _releaseAge += factor;
                float spread = MathHelper.Clamp(_releaseAge / 20f, 0f, 1f);
                Lights.Set(1, _releaseOrigin, ManaColor, 280f + 180f * spread, 1.5f * EffectLights.Flash(_releaseAge, 22f, 1.3f));
            }
        }

        private void MoveRune(float factor)
        {
            if (_runeLife > 0f)
            {
                if (_runeLife <= 43f && _runeLife > 40f)
                {
                }
                else if (_runeLife <= 40f && _runeLife > 30f)
                {
                    _runeScale += _runeGravity * factor;
                    _runeGravity += 0.15f * factor;
                    if (_runeScale > 1f)
                    {
                        _runeScale = 1f;
                        _runeGravity = 0.01f;
                        if (FpsCheck(1, factor))
                            SpawnPulse();
                    }
                }
                else if (_runeLife < 15f)
                {
                    _runeScale = MathF.Max(0f, _runeScale - _runeGravity * factor);
                    _runeGravity += 0.2f * factor;
                    _runeAlpha = _runeLife / 20f;
                    _runePosition.Z -= 10f * factor;
                }
                _runeFrame += ModelVelocity * factor;
                _runeLife -= factor;
            }

            for (int i = 0; i < _pulses.Length; i++)
            {
                ref Pulse p = ref _pulses[i];
                if (!p.Live)
                    continue;
                p.Scale = MathF.Max(1f, p.Scale - 0.02f * factor);
                p.Frame += ModelVelocity * factor;
                p.LifeTime -= factor;
                if (p.LifeTime <= 0f)
                    p.Live = false;
            }
        }

        private void SpawnPulse()
        {
            for (int i = 0; i < _pulses.Length; i++)
            {
                if (_pulses[i].Live)
                    continue;
                _pulses[i] = new Pulse { Live = true, Scale = 1.1f, LifeTime = 10f };
                return;
            }
        }

        protected override bool HasActiveContent()
        {
            if (!_released || _runeLife > 0f)
                return true;
            for (int i = 0; i < _spirits.Length; i++) if (_spirits[i].Live) return true;
            for (int i = 0; i < _pulses.Length; i++) if (_pulses[i].Live) return true;
            for (int i = 0; i < _grounds.Length; i++) if (_grounds[i].Live) return true;
            return false;
        }

        protected override void OnDraw()
        {
            var terrain = World?.Terrain;

            if (Batch.Begin(_groundTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _grounds.Length; i++)
                {
                    ref Ground g = ref _grounds[i];
                    if (!g.Live)
                        continue;
                    float luminosity = g.LifeTime < 5f ? 1f - (5f - g.LifeTime) * 0.2f : MathF.Sin((60f - g.LifeTime) * 0.05f) + 0.5f;
                    Batch.AddTerrainDecal(terrain, g.Position.X, g.Position.Y, g.Scale, g.Scale, SpiritLight * luminosity, -g.Angle);
                }
                Batch.End();
            }

            if (Batch.Begin(_spiritTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _spirits.Length; i++)
                {
                    ref Spirit s = ref _spirits[i];
                    if (s.Live)
                        Batch.AddTrail(s.Trail, SourceQuadBatch.TrailFaces.Two, s.Light, SourceQuadBatch.TrailShade.Flat, s.Trail.MaxTails - 1);
                }
                Batch.End();
            }

            var runeAngle = new Vector3(0f, 0f, MathHelper.ToRadians(45f));
            if (_rune != null && _runeLife > 0f && _runeLife <= 43f && _runeScale > 0f)
            {
                _rune.SetAnimationTime(_runeFrame);
                _rune.Update(LastGameTime);
                _rune.DrawInstance(_runePosition, runeAngle, _runeScale, RuneBodyLight, 1f, MathF.Round(_runeAlpha * 50f) / 50f);
            }

            if (_runePulse != null)
            {
                Vector3 light = (terrain?.EvaluateTerrainLight(_runePosition.X, _runePosition.Y) ?? Vector3.Zero) + Vector3.One;
                for (int i = 0; i < _pulses.Length; i++)
                {
                    ref Pulse p = ref _pulses[i];
                    if (!p.Live)
                        continue;
                    _runePulse.SetAnimationTime(p.Frame);
                    _runePulse.Update(LastGameTime);
                    _runePulse.DrawInstance(_runePosition, runeAngle, p.Scale, light, 0.4f, MathF.Round(p.LifeTime / 20f * 50f) / 50f);
                }
            }

            if (Batch.Begin(_lightTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _spirits.Length; i++)
                {
                    ref Spirit s = ref _spirits[i];
                    if (s.Live)
                        Batch.AddSprite(s.Position, 4f + (20f - s.LifeTime) / 5f, s.SpriteLight, MuGame.Random.Next(360));
                }
                Batch.End();
            }
        }

        protected override void DisposeEffect()
        {
            _rune?.Dispose();
            _runePulse?.Dispose();
        }
    }
}
