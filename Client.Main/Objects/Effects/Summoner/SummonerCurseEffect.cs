#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Client.Main.Objects.Effects.Joints;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects.Summoner
{
    /// <summary>
    /// Weakness (AT_SKILL_ALICE_WEAKNESS = 221) and Enervation / Innovation (222), SourceMain5.2 (hand + gate: base class):
    /// packet  WSclient.cpp:5193 PLAYER_SKILL_SLEEP (mount variants), AttackTime 1
    /// release ZzzCharacter.cpp:4582 / 4596 at the caster: BITMAP_MAGIC_ZIN sub 1 (7.0), sub 0 (2.0), sub 2 (1.0, 0.2, 0.1),
    ///         MODEL_SUMMONER_CASTING_EFFECT2 / 22 / 222 sub 1 (0.6), BITMAP_SHINY+6 sub 0 (0.5), BITMAP_PIN_LIGHT sub 0 (1.0),
    ///         SOUND_SKILL_WEAKNESS / SOUND_SKILL_ENERVATION. Weakness: first circle (2, 0.1, 0.1), the rest (2, 0.4, 0.3),
    ///         emitters (1.4, 0.2, 0.2); Enervation (0.25, 1, 0.7) throughout.
    /// zin     ZzzEffect.cpp:1276 (LifeTime 50 / 40 Alpha 0 / 30), 8268 (0: Alpha -0.05F below 20; 1: -0.03F below 20 else +0.06F
    ///         to 0.7; 2: Scale += 0.1F to 3.5, Alpha -0.05F below 20), 19327 (mzine_typer2 decal Scale, Light * Alpha * 2 /
    ///         / 2.5 / 1, rot HeadAngle[1] = 0)
    /// models  ZzzEffect.cpp:771 (LifeTime 40, "if (SubType = 0)" keeps Scale 0.6, BlendMesh 0, BlendMeshLight 0), 7861
    ///         (BlendMeshLight -0.03 below 20 else +0.05 to 0.5 per frame, Angle[2] +3F / -3F / +3F), RENDER_BRIGHT, terrain + Light
    /// shiny+6 ZzzEffect.cpp:1287 (LifeTime 24), 8294 (CreateParticleFpsChecked sub 0 in a 500 square, Z - rand%100 + 150)
    /// pin     ZzzEffect.cpp:1300 (LifeTime 40), 8360 (same position, rand_fps_check(2)); particles: SourceShinyPinPool.
    /// </summary>
    internal sealed class SummonerCurseEffect : AliceSkillEffectBase
    {
        private const string ZinTexturePath = "Effect/mzine_typer2.jpg"; // BITMAP_MAGIC_ZIN
        private const string WeaknessSound = "Sound/SE_Ch_summoner_weakness.wav";
        private const string EnervationSound = "Sound/SE_Ch_summoner_innovation.wav";
        private static readonly Vector3 WeaknessGlow = new(1f, 0.25f, 0.2f);
        private static readonly Vector3 EnervationGlow = new(0.25f, 1f, 0.7f);

        private static readonly string[] CircleModels =
        {
            "Effect/Suhwanzin2.bmd", "Effect/Suhwanzin22.bmd", "Effect/Suhwanzin222.bmd",
        };

        private struct Zin
        {
            public int SubType;
            public float Scale;
            public float Alpha;
            public float LifeTime;
            public Vector3 Light;
        }

        private readonly bool _weakness;
        private readonly Zin[] _zins = new Zin[5];
        private readonly SourceModelRenderer?[] _models = new SourceModelRenderer?[3];
        private readonly float[] _modelYaw = new float[3];
        private readonly SourceShinyPinPool _particles = new(96);
        private Texture2D? _zinTexture, _shiny6Texture, _lightTexture;
        private Vector3 _center;
        private Vector3 _modelLight, _emitterLight;
        private float _modelLife, _modelBlendLight;
        private float _shinyLife, _pinLife;

        public SummonerCurseEffect(WalkerObject caster, bool weakness)
            : base(caster, weakness ? Skill.Weakness : Skill.Enervation, 256)
        {
            _weakness = weakness;
        }

        protected override async Task LoadReleaseContent()
        {
            _zinTexture = await LoadTexture(ZinTexturePath);
            _shiny6Texture = await LoadTexture(SourceShinyPinPool.ShinyTexturePath);
            _lightTexture = await LoadTexture(SourceShinyPinPool.LightTexturePath);
            for (int i = 0; i < CircleModels.Length; i++)
            {
                SourceModelRenderer? r = await LoadModel(CircleModels[i], 0);
                if (r != null)
                    r.AnimationSpeed = 0f;
                _models[i] = r;
            }
        }

        protected override void OnRelease()
        {
            _center = CasterPosition;
            Vector3 first = _weakness ? new Vector3(2f, 0.1f, 0.1f) : new Vector3(0.25f, 1f, 0.7f);
            Vector3 rest = _weakness ? new Vector3(2f, 0.4f, 0.3f) : new Vector3(0.25f, 1f, 0.7f);
            _zins[0] = new Zin { SubType = 1, Scale = 7f, Alpha = 0f, LifeTime = 40f, Light = first };
            _zins[1] = new Zin { SubType = 0, Scale = 2f, Alpha = 1f, LifeTime = 50f, Light = rest };
            _zins[2] = new Zin { SubType = 2, Scale = 1f, Alpha = 1f, LifeTime = 30f, Light = rest };
            _zins[3] = new Zin { SubType = 2, Scale = 0.2f, Alpha = 1f, LifeTime = 30f, Light = rest };
            _zins[4] = new Zin { SubType = 2, Scale = 0.1f, Alpha = 1f, LifeTime = 30f, Light = rest };

            _modelLight = rest;
            _modelLife = 40f;
            _modelBlendLight = 0f;
            float yaw = CasterYawDegrees;
            _modelYaw[0] = _modelYaw[1] = _modelYaw[2] = yaw;

            _emitterLight = _weakness ? new Vector3(1.4f, 0.2f, 0.2f) : rest;
            _shinyLife = 24f;
            _pinLife = 40f;

            SoundController.Instance.PlayBuffer(_weakness ? WeaknessSound : EnervationSound);
        }

        protected override void TickRelease(float factor)
        {
            for (int i = 0; i < _zins.Length; i++)
            {
                ref Zin z = ref _zins[i];
                if (z.LifeTime <= 0f)
                    continue;
                switch (z.SubType)
                {
                    case 0:
                        if (z.LifeTime < 20f) z.Alpha -= 0.05f * factor;
                        else if (z.Alpha < 1f) z.Alpha += 0.05f * factor;
                        break;
                    case 1:
                        if (z.LifeTime < 20f) z.Alpha -= 0.03f * factor;
                        else if (z.Alpha < 0.7f) z.Alpha += 0.06f * factor;
                        break;
                    default:
                        if (z.Scale < 3.5f) z.Scale += 0.1f * factor;
                        if (z.LifeTime < 20f) z.Alpha -= 0.05f * factor;
                        else if (z.Alpha < 1f) z.Alpha += 0.05f * factor;
                        break;
                }
                z.LifeTime -= factor;
            }

            // Dynamic light: ground circle glow, follows the sub 0 circle alpha (50 ticks, fades below 20).
            ref Zin glow = ref _zins[1];
            if (glow.LifeTime > 0f)
            {
                float fadeIn = MathHelper.Clamp((50f - glow.LifeTime) / 4f, 0f, 1f);
                Lights.Set(0, _center + new Vector3(0f, 0f, 60f), _weakness ? WeaknessGlow : EnervationGlow, 380f,
                    1.1f * fadeIn * MathHelper.Clamp(glow.Alpha, 0f, 1f));
            }
            else
            {
                Lights.Hide(0);
            }

            if (_modelLife > 0f)
            {
                if (_modelLife < 20f) _modelBlendLight -= 0.03f;
                else if (_modelBlendLight < 0.5f) _modelBlendLight += 0.05f;
                _modelYaw[0] += 3f * factor;
                _modelYaw[1] -= 3f * factor;
                _modelYaw[2] += 3f * factor;
                _modelLife -= factor;
            }

            var fps = FPSCounter.Instance;
            if (_shinyLife > 0f)
            {
                Vector3 position = RandomEmitterPosition();
                if (fps.RandFPSCheck(1))
                    _particles.SpawnShiny(position, _emitterLight, 0.5f, factor);
                _shinyLife -= factor;
            }
            if (_pinLife > 0f)
            {
                Vector3 position = RandomEmitterPosition();
                if (fps.RandFPSCheck(2) && fps.RandFPSCheck(1))
                    _particles.SpawnPin(position, _emitterLight, 1f, factor);
                _pinLife -= factor;
            }

            _particles.Move(factor);
        }

        private Vector3 RandomEmitterPosition() => new(
            _center.X + (MuGame.Random.Next(500) - 250),
            _center.Y + (MuGame.Random.Next(500) - 250),
            _center.Z - MuGame.Random.Next(100) + 150f);

        protected override bool HasReleaseContent()
        {
            if (_modelLife > 0f || _shinyLife > 0f || _pinLife > 0f || _particles.HasLive)
                return true;
            for (int i = 0; i < _zins.Length; i++)
                if (_zins[i].LifeTime > 0f)
                    return true;
            return false;
        }

        protected override void DrawReleaseWorld()
        {
            var terrain = World?.Terrain;
            if (Batch.Begin(_zinTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _zins.Length; i++)
                {
                    ref Zin z = ref _zins[i];
                    if (z.LifeTime <= 0f)
                        continue;
                    Vector3 light = z.SubType switch
                    {
                        0 => z.Light * (z.Alpha * 2f),
                        1 => z.Light * (z.Alpha / 2.5f),
                        _ => z.Light * z.Alpha,
                    };
                    Batch.AddTerrainDecal(terrain, _center.X, _center.Y, z.Scale, z.Scale, light, 0f);
                }
                Batch.End();
            }

            if (_modelLife > 0f)
            {
                Vector3 light = (terrain?.EvaluateTerrainLight(_center.X, _center.Y) ?? Vector3.Zero) + _modelLight;
                float blendLight = MathF.Round(_modelBlendLight * 50f) / 50f;
                for (int i = 0; i < _models.Length; i++)
                    _models[i]?.DrawInstance(_center, new Vector3(0f, 0f, MathHelper.ToRadians(_modelYaw[i])), 0.6f, light, blendLight);
            }
        }

        protected override void DrawReleaseSprites()
        {
        }

        protected override void DrawReleaseParticles()
        {
            _particles.Draw(Batch, _shiny6Texture, _lightTexture, PinTexture);
        }
    }
}
