#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Client.Main.Objects.Effects.Joints;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects.RageFighter
{
    /// <summary>
    /// Ignore Defense / Increase Health / Increase Block (AT_SKILL_ATT / HP / DEF_UP_OURFORCES = 266 / 267 / 268), cast part:
    /// packet   WSclient.cpp:4702 (remote: RageCreateEffect), hero ZzzInterface.cpp:2803 (at cast, random buff action + sound)
    /// create   MonkSystem.cpp:1104 / 1133 / 1160: BITMAP_SWORD_EFFECT_MONO sub 0 / 1 / 2, 3 x MODEL_SHOCKWAVE_GROUND01 sub 0
    ///          (Scale 1 - i * 0.15) + 3 x BITMAP_EVENT_CLOUD sub 0, MODEL_WINDFOCE sub 0 / 4 / 5, BITMAP_MAGIC sub 13
    /// mono     ZzzEffect.cpp:6189 (LifeTime 15), 17412 (particle unless (int)LifeTime % 3 == 0; (int)LifeTime 14: SHOCKWAVE01 sub 2);
    ///          ZzzEffectParticle.cpp:3851 (LifeTime 20), 8892 (Scale *= 1.2^F to 6, then *= 0.8^F to 1, Light *= 0.94), 9280
    /// ground   ZzzEffect.cpp:6232 (LifeTime 20), 17512 (Scale *= 1.2^F, Light *= 0.8), ZzzObject.cpp:2547 (BodyLight * 6)
    /// cloud    ZzzEffect.cpp:6114 (LifeTime 30), 17225 (Scale *= 1.08^F max 3, Light *= 0.9), 18910
    /// wind     ZzzEffect.cpp:6125 (LifeTime 50; sub 3: 70), 17241 (Scale += 0.1F max 3, Light *= 0.9; sub 3 at LifeTime 40 / 30
    ///          with Light *= 0.85), ZzzObject.cpp:2527 (BodyLight * 6)
    /// magic    ZzzEffect.cpp:1216, 8166, 19148      burst  ZzzEffect.cpp:6010, 17118, ZzzObject.cpp:2486 (BodyLight * 15, x2)
    /// Not ported: the caster body passes of CMonkSystem::RageFighterEffect (MonkSystem.cpp:497).
    /// </summary>
    internal sealed class RageBuffEffect : SourceSkillEffectBase
    {
        private const string ShockWavePath = "Effect/shockwave01.bmd";          // MODEL_SHOCKWAVE01
        private const string GroundWavePath = "Effect/shockwave_ground01.bmd";  // MODEL_SHOCKWAVE_GROUND01
        private const string WindPath = "Effect/wind_foce.bmd";                 // MODEL_WINDFOCE
        private const string MonoTexturePath = "Effect/SwordEff_mono.jpg";      // BITMAP_SWORD_EFFECT_MONO
        private const string CloudTexturePath = "Effect/clouds2.jpg";           // BITMAP_EVENT_CLOUD
        private const string MagicTexturePath = "Effect/Magic_Ground1.jpg";     // BITMAP_MAGIC

        private struct Mono
        {
            public bool Live;
            public bool Shrinking;
            public Vector3 Light;
            public float Scale;
            public float LifeTime;
        }

        private struct Wind
        {
            public bool Live;
            public bool Copy;      // sub 3
            public Vector3 Light;
            public float Scale;
            public float LifeTime;
            public int LastMark;
        }

        private readonly Vector3 _monoLight;
        private readonly Vector3 _groundLight;
        private readonly Vector3 _cloudLight;
        private readonly Vector3 _windLight;
        private readonly Vector3 _magicLight;
        private readonly Vector3 _burstLight;
        private readonly Mono[] _monos = new Mono[24];
        private readonly Wind[] _winds = new Wind[3];
        private readonly float[] _groundScales = new float[3];
        private SourceModelRenderer? _shockRenderer;
        private SourceModelRenderer? _groundRenderer;
        private SourceModelRenderer? _windRenderer;
        private Texture2D? _monoTexture;
        private Texture2D? _cloudTexture;
        private Texture2D? _magicTexture;

        private Vector3 _origin;
        private Vector3 _ownerAngle;       // degrees
        private float _monoLife = 15f;
        private int _monoMark = -1;
        private int _monoCount;
        private float _groundLife = 20f;
        private Vector3 _groundColor;
        private float _cloudLife = 30f;
        private float _cloudScale = 1f;
        private Vector3 _cloudColor;
        private float _magicLife = 30f;
        private float _magicScale = 1f;
        private Vector3 _magicColor;
        private bool _burstLive;
        private Vector3 _burstPosition;
        private Vector3 _burstColor;
        private float _burstScale;
        private float _burstLife;
        private float _lightAge;

        public RageBuffEffect(WalkerObject caster, ushort skillId)
            : base(caster, triggerFrame: 0f, triggerTimeoutSeconds: 0f, maxQuads: 256)
        {
            switch (skillId)
            {
                case 267: // AT_SKILL_HP_UP_OURFORCES
                    _monoLight = new Vector3(0.5f, 0.28f, 1f);
                    _groundLight = new Vector3(1f, 0.35f, 1f);
                    _cloudLight = new Vector3(0.8f, 0.4f, 1f);
                    _windLight = new Vector3(0.85f, 0.2f, 1f);
                    _magicLight = new Vector3(0.78f, 0.2f, 1f);
                    _burstLight = new Vector3(0.7f, 0.5f, 1f);
                    break;
                case 268: // AT_SKILL_DEF_UP_OURFORCES
                    _monoLight = new Vector3(1f, 0.15f, 0f);
                    _groundLight = new Vector3(1f, 0.1f, 0f);
                    _cloudLight = new Vector3(1f, 0.3f, 0.2f);
                    _windLight = new Vector3(1f, 0.2f, 0f);
                    _magicLight = new Vector3(1f, 0.15f, 0f);
                    _burstLight = new Vector3(1f, 0.12f, 0f);
                    break;
                default: // AT_SKILL_ATT_UP_OURFORCES
                    _monoLight = new Vector3(0.2f, 0.4f, 1f);
                    _groundLight = new Vector3(0.6f, 0.65f, 1f);
                    _cloudLight = new Vector3(0.65f, 0.8f, 1f);
                    _windLight = new Vector3(0.5f, 0.55f, 1f);
                    _magicLight = new Vector3(0.5f, 0.55f, 0.9f);
                    _burstLight = new Vector3(0.4f, 0.4f, 1f);
                    break;
            }
        }

        protected override async Task LoadEffectContent()
        {
            _shockRenderer = await LoadModel(ShockWavePath);
            _groundRenderer = await LoadModel(GroundWavePath);
            _windRenderer = await LoadModel(WindPath);
            if (_shockRenderer != null) _shockRenderer.AnimationSpeed = 0f;
            if (_groundRenderer != null) _groundRenderer.AnimationSpeed = 0f;
            if (_windRenderer != null) _windRenderer.AnimationSpeed = 0f;
            _monoTexture = await LoadTexture(MonoTexturePath);
            _cloudTexture = await LoadTexture(CloudTexturePath);
            _magicTexture = await LoadTexture(MagicTexturePath);
        }

        protected override void OnStart()
        {
            _origin = CasterPosition;
            _ownerAngle = new Vector3(
                MathHelper.ToDegrees(Caster.Angle.X), MathHelper.ToDegrees(Caster.Angle.Y), MathHelper.ToDegrees(Caster.Angle.Z));
            for (int i = 0; i < 3; i++)
                _groundScales[i] = 1f - i * 0.15f;
            _groundColor = _groundLight;
            _cloudColor = _cloudLight;
            _magicColor = _magicLight;
            _winds[0] = new Wind { Live = true, Light = _windLight, Scale = 1f, LifeTime = 50f, LastMark = -1 };
        }

        protected override void OnTick(float factor)
        {
            TickMonoEffect(factor);
            TickMonos(factor);

            if (_groundLife > 0f)
            {
                float grow = MathF.Pow(1.2f, factor);
                for (int i = 0; i < 3; i++)
                    _groundScales[i] *= grow;
                _groundColor *= 0.8f; // per frame
                _groundLife -= factor;
            }

            if (_cloudLife > 0f)
            {
                _cloudScale = MathF.Min(3f, _cloudScale * MathF.Pow(1.08f, factor));
                _cloudColor *= 0.9f; // per frame
                _cloudLife -= factor;
            }

            if (_magicLife > 0f)
            {
                _magicScale *= MathF.Pow(1.1f, factor);
                _magicColor *= 0.95f; // per frame
                if (_magicScale > 4f)
                    _magicColor *= 0.5f; // per frame
                _magicLife -= factor;
                if (_magicScale > 8f)
                    _magicLife = 0f;
            }

            TickWinds(factor);

            if (_burstLive)
            {
                _burstScale *= MathF.Pow(1.24f, factor);
                _burstPosition.Z *= MathF.Pow(1.19f, factor);
                _burstColor *= 0.45f; // per frame
                if (_burstColor.X < 0.001f)
                    _burstColor = Vector3.Zero;
                _burstLife -= factor;
                if (_burstLife <= 0f)
                    _burstLive = false;
            }

            // Dynamic light: buff burst glow at the caster, tinted like the shockwave.
            _lightAge += factor;
            float glow = EffectLights.Envelope(_lightAge, 40f, 2f, 30f);
            if (glow > 0f)
                Lights.Set(0, _origin + new Vector3(0f, 0f, 70f), _burstLight, 360f, 1.4f * glow);
            else
                Lights.Hide(0);
        }

        private void TickMonoEffect(float factor)
        {
            if (_monoLife <= 0f)
                return;

            int mark = (int)_monoLife;
            if (mark % 3 != 0 && FpsCheck(1, factor))
                SpawnMono();

            if (mark == 14 && _monoMark != 14)
            {
                _burstLive = true;
                _burstPosition = _origin;
                _burstColor = _burstLight;
                _burstScale = 1f;
                _burstLife = 5f / 0.3f;
            }
            _monoMark = mark;
            _monoLife -= factor;
        }

        private void SpawnMono()
        {
            for (int i = 0; i < _monos.Length; i++)
            {
                ref Mono m = ref _monos[i];
                if (m.Live)
                    continue;

                m = new Mono { Live = true, Light = _monoLight, Scale = 1f, LifeTime = 20f };
                _monoCount++;
                return;
            }
        }

        private void TickMonos(float factor)
        {
            if (_monoCount == 0)
                return;

            float grow = MathF.Pow(1.2f, factor);
            float shrink = MathF.Pow(0.8f, factor);
            for (int i = 0; i < _monos.Length; i++)
            {
                ref Mono m = ref _monos[i];
                if (!m.Live)
                    continue;

                m.LifeTime -= factor;
                bool dead = m.LifeTime <= 0f;
                if (!dead)
                {
                    m.Scale *= m.Shrinking ? shrink : grow;
                    if (m.Scale > 6f)
                    {
                        m.Shrinking = true;
                        continue;
                    }

                    if (m.Shrinking && m.Scale < 1f)
                        dead = true;
                    else
                        m.Light *= 0.94f; // per frame
                }

                if (dead)
                {
                    m.Live = false;
                    _monoCount--;
                }
            }
        }

        private void TickWinds(float factor)
        {
            for (int i = 0; i < _winds.Length; i++)
            {
                ref Wind wind = ref _winds[i];
                if (!wind.Live)
                    continue;

                wind.Scale = MathF.Min(3f, wind.Scale + 0.1f * factor);
                wind.Light *= wind.Copy ? 0.85f : 0.9f; // per frame
                if (!wind.Copy)
                {
                    int mark = (int)wind.LifeTime;
                    if (mark != wind.LastMark && (mark == 40 || mark == 30))
                        SpawnWindCopy();
                    wind.LastMark = mark;
                }

                wind.LifeTime -= factor;
                if (wind.LifeTime <= 0f)
                    wind.Live = false;
            }
        }

        private void SpawnWindCopy()
        {
            for (int i = 0; i < _winds.Length; i++)
            {
                if (_winds[i].Live)
                    continue;
                _winds[i] = new Wind { Live = true, Copy = true, Light = _windLight, Scale = 1f, LifeTime = 70f, LastMark = -1 };
                return;
            }
        }

        protected override bool HasActiveContent()
        {
            if (_monoLife > 0f || _monoCount > 0 || _groundLife > 0f || _cloudLife > 0f || _magicLife > 0f || _burstLive)
                return true;
            for (int i = 0; i < _winds.Length; i++) if (_winds[i].Live) return true;
            return false;
        }

        protected override void OnDraw()
        {
            var terrain = World?.Terrain;
            double worldTime = FPSCounter.Instance.WorldTime;
            float factor = SourceJointMath.FrameFactor;

            if (_magicLife > 0f && Batch.Begin(_magicTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                Batch.AddTerrainDecal(terrain, _origin.X, _origin.Y, _magicScale, _magicScale, _magicColor, -_ownerAngle.Z);
                Batch.AddTerrainDecal(terrain, _origin.X, _origin.Y, _magicScale, _magicScale, _magicColor, -_ownerAngle.Z);
                Batch.End();
            }

            if (_cloudLife > 0f && Batch.Begin(_cloudTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                float rotation = (float)(-(long)worldTime * 0.5 % 360.0);
                for (int i = 0; i < 3; i++)
                    Batch.AddTerrainDecal(terrain, _origin.X, _origin.Y, _cloudScale, _cloudScale, _cloudColor, rotation);
                Batch.End();
            }

            Vector3 ownerAngle = ToRadians(_ownerAngle);
            if (_groundLife > 0f && _groundRenderer != null)
            {
                for (int i = 0; i < 3; i++)
                    _groundRenderer.DrawInstance(_origin, ownerAngle, _groundScales[i], _groundColor * 6f, 1f);
            }

            if (_windRenderer != null)
            {
                for (int i = 0; i < _winds.Length; i++)
                {
                    ref Wind wind = ref _winds[i];
                    if (!wind.Live)
                        continue;
                    float spin = (float)(-(long)worldTime * (wind.Copy ? 0.9 : 0.3) % 360.0);
                    var angle = new Vector3(ownerAngle.X, ownerAngle.Y, MathHelper.ToRadians(spin));
                    _windRenderer.DrawInstance(_origin, angle, wind.Scale, wind.Light * 6f, 1f);
                }
            }

            if (_burstLive && _shockRenderer != null)
            {
                _shockRenderer.DrawInstance(_burstPosition, ownerAngle, _burstScale, _burstColor * 15f, 1f);
                _shockRenderer.DrawInstance(_burstPosition, ownerAngle, _burstScale, _burstColor * 15f, 1f);
            }

            if (_monoCount > 0 && _monoTexture != null &&
                Batch.Begin(_monoTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _monos.Length; i++)
                {
                    ref Mono m = ref _monos[i];
                    if (!m.Live)
                        continue;
                    Vector3 position = _origin;
                    position.Z += 31f * m.Scale * factor;
                    Batch.AddSpriteRegion(position, _monoTexture.Width * m.Scale * 0.9f, _monoTexture.Height * m.Scale * 1.1f, m.Light, 0f, 0f, 0f, 1f, 1f);
                }
                Batch.End();
            }
        }

        private static Vector3 ToRadians(Vector3 deg) =>
            new(MathHelper.ToRadians(deg.X), MathHelper.ToRadians(deg.Y), MathHelper.ToRadians(deg.Z));

        protected override void DisposeEffect()
        {
            _shockRenderer?.Dispose();
            _groundRenderer?.Dispose();
            _windRenderer?.Dispose();
        }
    }
}
