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
    /// Strike of Destruction (AT_SKILL_BLOW_OF_DESTRUCTION = 232), SourceMain5.2:
    /// gate    ZzzCharacter.cpp:2854   AttackTime = 15 at once
    /// release ZzzCharacter.cpp:3993   MODEL_BLOW_OF_DESTRUCTION sub 0, target = tile centre in o->Light
    /// blow    ZzzEffect.cpp:4887 (init sub 0 / 1), 15011 (move), 18581 (sprites), 19425 (flareBlue decals)
    /// models  MODEL_NIGHTWATER_01 4925/15147, MODEL_KNIGHT_PLANCRACK_A/B 4932/4950/15152/15166,
    ///         MODEL_RAKLION_BOSS_CRACKEFFECT 1136/8726 (RenderObject)
    /// particles BITMAP_WATERFALL_5/3 sub 8 (ZzzEffectParticle.cpp:3507/3696, 8468/8628), BITMAP_SMOKE 55 (1551/5767)
    /// </summary>
    internal sealed class StrikeOfDestructionEffect : SourceSkillEffectBase
    {
        private const string DecalTexturePath = "Effect/flareBlue.jpg";        // BITMAP_FLARE_BLUE
        private const string SwordTexturePath = "Effect/SwordEff_mono.jpg";    // BITMAP_SWORD_EFFECT_MONO
        private const string LightTexturePath = "Effect/flare01.jpg";          // BITMAP_LIGHT
        private const string Waterfall3TexturePath = "Effect/WATERFALL3.jpg"; // BITMAP_WATERFALL_3
        private const string Waterfall5TexturePath = "Effect/WATERFALL5.jpg"; // BITMAP_WATERFALL_5
        private const string SmokeTexturePath = "Effect/smoke01.jpg";          // BITMAP_SMOKE

        private const float ModelVelocity = 0.3f;

        private enum Kind { NightWater, CrackA, CrackB, CrackGrand, Count }

        private static readonly string[] ModelPaths =
        {
            "Effect/nightwater01.bmd", "Effect/knight_plancrack_a.bmd", "Effect/knight_plancrack_b.bmd", "Effect/knight_plancrack_grand.bmd",
        };

        private struct ModelFx
        {
            public bool Live;
            public Kind Kind;
            public Vector3 Position;
            public Vector3 Angle;
            public Vector3 Light;
            public float Scale;
            public float Alpha;
            public float LifeTime;
        }

        private struct Particle
        {
            public bool Live;
            public bool Waterfall5;
            public Vector3 Position;
            public Vector3 Light;
            public float VelocityZ;
            public float Scale;
            public float Gravity;
            public float Rotation;
            public float LifeTime;
        }

        private struct Blow
        {
            public bool Live;
            public Vector3 Position;
            public Vector3 Light;
            public float LifeTime;
        }

        private readonly Vector3? _target;
        private readonly ModelFx[] _models = new ModelFx[48];
        private readonly Particle[] _waterfalls = new Particle[192];
        private readonly Particle[] _smokes = new Particle[128];
        private readonly SourceModelRenderer?[] _renderers = new SourceModelRenderer?[(int)Kind.Count];
        private readonly float[] _kindFrame = new float[(int)Kind.Count];

        private Texture2D? _decalTexture;
        private Texture2D? _swordTexture;
        private Texture2D? _lightTexture;
        private Texture2D? _waterfall3Texture;
        private Texture2D? _waterfall5Texture;
        private Texture2D? _smokeTexture;

        private Blow _blow0;   // at the caster
        private Blow _blow1;   // at the target tile
        private Vector3 _startPosition;
        private Vector3 _ownerAngle;

        public StrikeOfDestructionEffect(WalkerObject caster, Vector3? target)
            : base(caster, 0f, 0f, 1024)
        {
            _target = target;
        }

        protected override async Task LoadEffectContent()
        {
            _decalTexture = await LoadTexture(DecalTexturePath);
            _swordTexture = await LoadTexture(SwordTexturePath);
            _lightTexture = await LoadTexture(LightTexturePath);
            _waterfall3Texture = await LoadTexture(Waterfall3TexturePath);
            _waterfall5Texture = await LoadTexture(Waterfall5TexturePath);
            _smokeTexture = await LoadTexture(SmokeTexturePath);

            for (int k = 0; k < (int)Kind.Count; k++)
            {
                // ZzzObject.cpp:578-607: every model is RenderBody(RENDER_TEXTURE | RENDER_BRIGHT): additive, BodyLight = Light * Alpha.
                var renderer = await LoadModel(ModelPaths[k], -2);
                if (renderer == null)
                    continue;
                renderer.AnimationSpeed = 0f;
                _renderers[k] = renderer;
            }
        }

        protected override void OnStart()
        {
            Vector3 caster = CasterPosition;
            _ownerAngle = new Vector3(0f, 0f, CasterYawDegrees);

            Vector3 target = _target ?? caster;
            _startPosition = new Vector3(target.X, target.Y, caster.Z);

            _blow0 = new Blow { Live = true, LifeTime = 40f, Light = new Vector3(1.2f),
                Position = caster + SourceJointMath.Rotate(new Vector3(-20f, -100f, 0f), _ownerAngle) };
            _blow1 = new Blow { Live = true, LifeTime = 40f, Light = new Vector3(1.2f),
                Position = new Vector3(_startPosition.X, _startPosition.Y, 150f) };
        }

        protected override void OnTick(float factor)
        {
            float lightDecay = MathF.Pow(1f / 1.05f, factor);

            if (_blow0.Live)
            {
                if (_blow0.LifeTime <= 24f)
                {
                    // ZzzEffect.cpp:15018
                    Client.Main.Graphics.Camera.Instance.SetEarthQuake(_blow0.LifeTime >= 15f ? (MuGame.Random.Next(8) - 4) * 0.1f : 0f);
                    if ((int)_blow0.LifeTime == 23)
                        SpawnCasterCracks(factor);
                    _blow0.Light *= lightDecay;
                }
                Age(ref _blow0, factor);
            }

            if (_blow1.Live)
            {
                if (_blow1.LifeTime <= 24f)
                {
                    if (_blow1.LifeTime >= 15f)
                        SpawnSplash(factor);

                    if ((int)_blow1.LifeTime == 23)
                    {
                        var light = new Vector3(0.3f, 0.3f, 1f);
                        if (FpsCheck(1, factor)) SpawnModel(Kind.NightWater, _blow1.Position, _ownerAngle, light, 2f);
                        if (FpsCheck(1, factor)) SpawnModel(Kind.NightWater, _blow1.Position, _ownerAngle, light, 1f);
                        if (FpsCheck(1, factor)) SpawnModel(Kind.CrackGrand, _blow1.Position, _ownerAngle, light, 0.2f);
                        // 5 + rand % 3 x MODEL_STONE sub 13: Scale *= 0 (no Scale argument) -> invisible, no children.
                    }
                    _blow1.Light *= lightDecay;
                }
                Age(ref _blow1, factor);
            }

            MoveModels(factor);
            MoveParticles(factor);
        }

        private static void Age(ref Blow blow, float factor)
        {
            blow.LifeTime -= factor;
            if (blow.LifeTime <= 0f)
                blow.Live = false;
        }

        private void SpawnCasterCracks(float factor)
        {
            var light = new Vector3(0.3f, 0.3f, 1f);
            Vector3 position = _blow0.Position;
            if (FpsCheck(1, factor)) SpawnModel(Kind.NightWater, position, _ownerAngle, light, 0.9f);
            if (FpsCheck(1, factor)) SpawnModel(Kind.NightWater, position, _ownerAngle, light, 0.9f);
            if (FpsCheck(1, factor)) SpawnModel(Kind.CrackA, position, _ownerAngle, light, 1.2f);

            Vector3 dir = _startPosition - position;
            float length = dir.Length();
            if (length > 0f)
                dir /= length;
            int count = (int)(length / 100f) + 1;
            for (int i = 0; i < count; i++)
            {
                Vector3 pos = position + dir * (55f * i);
                Vector3 angle = _ownerAngle;
                if (i % 2 == 0)
                    angle.Z += MuGame.Random.Next(20) + 10;
                else
                    angle.Z -= MuGame.Random.Next(20) + 10;
                if (FpsCheck(1, factor))
                    SpawnModel(Kind.CrackB, pos, angle, light, 1f);
            }
        }

        private void SpawnSplash(float factor)
        {
            for (int i = 0; i < 15; i++)
            {
                Vector3 pos = _blow1.Position + new Vector3(
                    MuGame.Random.Next(300) - 150, MuGame.Random.Next(300) - 150, MuGame.Random.Next(300) - 150);
                float scale = 1.6f + MuGame.Random.Next(10) * 0.1f;
                bool waterfall5 = FpsCheck(2, factor);
                if (FpsCheck(1, factor))
                    SpawnWaterfall(pos, scale, waterfall5, factor);
                if (FpsCheck(2, factor) && FpsCheck(1, factor))
                    SpawnSmoke(pos);
            }
        }

        // ------------------------------------------------------------------ models

        private void SpawnModel(Kind kind, Vector3 position, Vector3 angle, Vector3 light, float scale)
        {
            for (int i = 0; i < _models.Length; i++)
            {
                ref ModelFx m = ref _models[i];
                if (m.Live)
                    continue;

                if (!AnyLive(kind))
                    _kindFrame[(int)kind] = 0f;

                const float factor = 1f; // CreateEffect / CreateParticle init offsets: the 5.2 port scales them by FPS_ANIMATION_FACTOR, which shrinks them at high FPS; 25 Hz values
                m = default;
                m.Live = true;
                m.Kind = kind;
                m.Position = position;
                m.Angle = angle;
                m.Light = light;
                m.Alpha = 1f;
                m.Scale = scale;
                switch (kind)
                {
                    case Kind.NightWater:
                        m.LifeTime = 25f;
                        m.Angle.Z = MuGame.Random.Next(360);
                        break;
                    case Kind.CrackA:
                        m.LifeTime = 25f;
                        m.Angle.Z = MuGame.Random.Next(360);
                        m.Scale = scale + MuGame.Random.Next(10) * 0.05f;
                        m.Position.Z += 10f * factor;
                        break;
                    case Kind.CrackB:
                        m.LifeTime = 25f;
                        m.Position.Z += 15f * factor;
                        m.Angle.Z += 90f * factor;
                        break;
                    case Kind.CrackGrand:
                        m.LifeTime = 40f;
                        m.Scale = scale + 1f;
                        m.Position.Z += 30f * factor;
                        m.Angle.Z = MuGame.Random.Next(360);
                        break;
                }
                return;
            }
        }

        private bool AnyLive(Kind kind)
        {
            for (int i = 0; i < _models.Length; i++)
                if (_models[i].Live && _models[i].Kind == kind) return true;
            return false;
        }

        private void MoveModels(float factor)
        {
            for (int k = 0; k < (int)Kind.Count; k++)
                _kindFrame[k] += ModelVelocity * factor;

            for (int i = 0; i < _models.Length; i++)
            {
                ref ModelFx m = ref _models[i];
                if (!m.Live)
                    continue;

                m.Alpha -= (m.Kind == Kind.CrackGrand ? 0.03f : 0.04f) * factor;
                m.LifeTime -= factor;
                if (m.LifeTime <= 0f)
                    m.Live = false;
            }
        }

        // ------------------------------------------------------------------ particles

        private void SpawnWaterfall(Vector3 position, float scale, bool waterfall5, float factor)
        {
            for (int i = 0; i < _waterfalls.Length; i++)
            {
                ref Particle p = ref _waterfalls[i];
                if (p.Live)
                    continue;

                p = default;
                p.Live = true;
                p.Waterfall5 = waterfall5;
                p.Position = position;
                p.Light = new Vector3(0.5f, 0.5f, 1f);
                p.LifeTime = 30f;
                if (waterfall5)
                {
                    p.VelocityZ = MuGame.Random.Next(3) + 1;
                    p.Scale = scale + (MuGame.Random.Next(5) + 5) * 0.05f * factor;
                }
                else
                {
                    p.VelocityZ = MuGame.Random.Next(5) + 5;
                    p.Scale = (MuGame.Random.Next(10) + 10) * 0.05f;
                }
                return;
            }
        }

        private void SpawnSmoke(Vector3 position)
        {
            for (int i = 0; i < _smokes.Length; i++)
            {
                ref Particle p = ref _smokes[i];
                if (p.Live)
                    continue;

                p = default;
                p.Live = true;
                p.Position = position;
                p.Light = Vector3.One;
                p.LifeTime = 30f;
                p.Scale = 1f + (MuGame.Random.Next(32) + 48) * 0.01f;
                p.Rotation = (long)FPSCounter.Instance.WorldTime % 360;
                return;
            }
        }

        private void MoveParticles(float factor)
        {
            float waterDecay = MathF.Pow(1f / 1.1f, factor);
            float smokeDecay = MathF.Pow(1f / 1.08f, factor);

            for (int i = 0; i < _waterfalls.Length; i++)
            {
                ref Particle p = ref _waterfalls[i];
                if (!p.Live)
                    continue;

                p.LifeTime -= factor;
                if (p.LifeTime <= 0f)
                {
                    p.Live = false;
                    continue;
                }

                p.Position.Z += p.VelocityZ * factor; // MovePosition: (0, 0, Vz) rotated by a yaw-only angle
                p.Scale += factor * 0.05f;
                p.VelocityZ -= 0.6f * factor;
                p.Light *= waterDecay;
            }

            for (int i = 0; i < _smokes.Length; i++)
            {
                ref Particle p = ref _smokes[i];
                if (!p.Live)
                    continue;

                p.LifeTime -= factor;
                if (p.LifeTime <= 0f)
                {
                    p.Live = false;
                    continue;
                }

                p.Gravity += 0.1f * factor;
                p.Position.Z += p.Gravity * factor;
                p.Scale += factor * 0.05f;
                p.Light *= smokeDecay;
            }
        }

        // ------------------------------------------------------------------ lifecycle / draw

        protected override bool HasActiveContent()
        {
            if (_blow0.Live || _blow1.Live)
                return true;
            for (int i = 0; i < _models.Length; i++) if (_models[i].Live) return true;
            for (int i = 0; i < _waterfalls.Length; i++) if (_waterfalls[i].Live) return true;
            for (int i = 0; i < _smokes.Length; i++) if (_smokes[i].Live) return true;
            return false;
        }

        protected override void OnDraw()
        {
            var terrain = World?.Terrain;

            // RenderEffectShadows: flareBlue decals of BLOW sub 0 / 1 and of the sub 0 water / crack models.
            if (Batch.Begin(_decalTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                if (_blow0.Live && _blow0.LifeTime <= 24f)
                    Batch.AddTerrainDecal(terrain, _blow0.Position.X, _blow0.Position.Y, 4f, 4f, _blow0.Light, -_ownerAngle.Z);
                if (_blow1.Live && _blow1.LifeTime <= 24f)
                    Batch.AddTerrainDecal(terrain, _blow1.Position.X, _blow1.Position.Y, 6f, 6f, _blow1.Light, -_ownerAngle.Z);

                for (int i = 0; i < _models.Length; i++)
                {
                    ref ModelFx m = ref _models[i];
                    if (m.Live && m.Kind != Kind.CrackGrand && m.LifeTime <= 24f)
                        Batch.AddTerrainDecal(terrain, m.Position.X, m.Position.Y, 4f, 4f, m.Light, -m.Angle.Z);
                }
                Batch.End();
            }

            for (int k = 0; k < (int)Kind.Count; k++)
            {
                SourceModelRenderer? renderer = _renderers[k];
                if (renderer == null || !AnyLive((Kind)k))
                    continue;

                // PLANCRACK_A / B: BlendMesh 1 with BlendMeshTexCoordV = -(int)WorldTime % 2000 * 0.0001.
                if ((Kind)k is Kind.CrackA or Kind.CrackB)
                    renderer.SetMeshScrollV(1, -((long)FPSCounter.Instance.WorldTime % 2000) * 0.0001f);
                renderer.SetAnimationTime(_kindFrame[k]);
                renderer.Update(LastGameTime);
                for (int i = 0; i < _models.Length; i++)
                {
                    ref ModelFx m = ref _models[i];
                    if (!m.Live || (int)m.Kind != k || m.Alpha <= 0f)
                        continue;

                    Vector3 light = (terrain?.EvaluateTerrainLight(m.Position.X, m.Position.Y) ?? Vector3.One) * m.Light;
                    renderer.DrawInstance(m.Position, Deg(m.Angle), m.Scale, light * m.Alpha, 1f);
                }
            }

            if (_blow0.Live && _blow0.LifeTime <= 24f && Batch.Begin(_swordTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                Batch.AddSprite(_blow0.Position + new Vector3(0f, 0f, 65f), MuGame.Random.Next(10) * 0.011f + 3f, new Vector3(0.5f, 0.5f, 1f));
                Batch.End();
            }

            if (_blow1.Live && _blow1.LifeTime <= 24f && Batch.Begin(_lightTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                var light = new Vector3(_blow1.Light.X * 0.5f, _blow1.Light.Y * 0.5f, _blow1.Light.Z);
                Batch.AddSprite(_blow1.Position + new Vector3(0f, 0f, 100f), 5f, light);
                Batch.End();
            }

            DrawWaterfalls(_waterfall5Texture, true);
            DrawWaterfalls(_waterfall3Texture, false);

            if (Batch.Begin(_smokeTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _smokes.Length; i++)
                {
                    ref Particle p = ref _smokes[i];
                    if (p.Live)
                        Batch.AddSprite(p.Position, p.Scale, p.Light, p.Rotation);
                }
                Batch.End();
            }
        }

        private void DrawWaterfalls(Texture2D? texture, bool waterfall5)
        {
            if (!Batch.Begin(texture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
                return;

            for (int i = 0; i < _waterfalls.Length; i++)
            {
                ref Particle p = ref _waterfalls[i];
                if (p.Live && p.Waterfall5 == waterfall5)
                    Batch.AddSprite(p.Position, p.Scale, p.Light);
            }
            Batch.End();
        }

        private static Vector3 Deg(Vector3 degrees) =>
            new(MathHelper.ToRadians(degrees.X), MathHelper.ToRadians(degrees.Y), MathHelper.ToRadians(degrees.Z));

        protected override void DisposeEffect()
        {
            for (int k = 0; k < _renderers.Length; k++)
                _renderers[k]?.Dispose();
        }
    }
}
