#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Client.Main.Objects.Effects.Joints;
using Client.Main.Objects.Player;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects.Summoner
{
    /// <summary>
    /// Book summons (Explosion 223, Requiem 224, Pollution 225): CSummonSystem::CastSummonSkill (SummonSystem.cpp:65) faces the
    /// target tile corner (X * TERRAIN_SCALE), CreateCastingEffect (190) at the caster, then the creature (CreateSummonObject 116).
    /// cast    BITMAP_MAGIC 10 (1,1,1) / 9 (palette 0), CASTING_EFFECT1/11/111 (palette 1), 2/22/222 (palette 2), 4 (palette 3)
    /// magic   ZzzEffect.cpp:1181 (9: LifeTime 40, Scale 2.4; 10: LifeTime 44, Scale 12, Alpha 0), 8091 (9: Alpha -0.05 below 20
    ///         else +0.05 to 1 per frame, HeadAngle += (4, -8, 4)F; 10: Alpha -0.03 below 20 else +0.05 per frame),
    ///         19097 (9: SUMMON_IMPACT decals 2.4 rot HeadAngle[1] and 2.88 rot HeadAngle[2], Light*Alpha, additive;
    ///         10: minus, BITMAP_LIGHT decal 12 x up to 5 by LifeTime, rot -Angle[2])
    /// models  ZzzEffect.cpp:768 (LifeTime 40 / 4: 25; "if (SubType = 0)" keeps Scale 0.9, 4: Scale 1; BlendMesh 0, BlendMeshLight 0),
    ///         7858 (BlendMeshLight -0.03 below 20 else +0.05 to 0.5 per frame; Angle[2] -/+3F; 4: Scale += 0.6F),
    ///         epilogue PlayAnimation 0.3 (one key: static); ZzzObject.cpp:1712 RenderBody(RENDER_TEXTURE | RENDER_BRIGHT), BodyLight terrain + Light
    /// </summary>
    internal abstract class SummonBookEffectBase : SourceSkillEffectBase
    {
        private const string ImpactTexturePath = "Effect/empact01.jpg"; // BITMAP_SUMMON_IMPACT
        private const string LightTexturePath = "Effect/flare01.jpg";   // BITMAP_LIGHT
        private const int CastLightSlot = 5;

        private static readonly string[] CastingModelPaths =
        {
            "Effect/Suhwanzin1.bmd", "Effect/Suhwanzin11.bmd", "Effect/Suhwanzin111.bmd",
            "Effect/Suhwanzin2.bmd", "Effect/Suhwanzin22.bmd", "Effect/Suhwanzin222.bmd",
            "Effect/Suhwanzin4.bmd",
        };

        // Angle[2] per tick: 1 -, 11 +, 111 -, 2 +, 22 -, 222 +, 4 none.
        private static readonly float[] CastingSpin = { -3f, 3f, -3f, 3f, -3f, 3f, 0f };

        private struct CastingModel
        {
            public bool Live;
            public float LifeTime;
            public float Scale;
            public float Yaw;
            public float BlendMeshLight;
            public Vector3 Light;
        }

        private readonly SourceModelRenderer?[] _castingRenderers = new SourceModelRenderer?[CastingModelPaths.Length];
        private readonly CastingModel[] _castingModels = new CastingModel[CastingModelPaths.Length];
        private Texture2D? _impactTexture;
        private Texture2D? _lightTexture;

        private Vector3 _origin;
        private Vector3 _magic9Light;
        private float _magic9Life, _magic9Alpha = 1f;
        private Vector3 _magic9HeadAngle;
        private float _magic10Life, _magic10Alpha;

        protected SummonBookEffectBase(WalkerObject caster, Vector3 targetTileCorner, int maxQuads)
            : base(caster, 0f, 0f, maxQuads)
        {
            TargetCorner = targetTileCorner;
            CastYaw = SourceJointMath.CreateAngle(caster.WorldPosition.Translation.X, caster.WorldPosition.Translation.Y,
                targetTileCorner.X, targetTileCorner.Y);
            SummonLevel = ResolveSummonLevel(caster);
        }

        /// <summary>(iTargetPos_X * TERRAIN_SCALE, iTargetPos_Y * TERRAIN_SCALE, RequestTerrainHeight).</summary>
        protected Vector3 TargetCorner { get; private set; }

        /// <summary>pObject->Angle[2] after CastSummonSkill (degrees).</summary>
        protected float CastYaw { get; }

        /// <summary>Weapon[1] (book) level: 0 below +7, 1 for +7..+10, 2 from +11.</summary>
        protected int SummonLevel { get; }

        protected Vector3 CastAngle => new(0f, 0f, CastYaw);

        protected abstract Vector3[] CastingPalette { get; }

        private static int ResolveSummonLevel(WalkerObject caster)
        {
            int level = caster is PlayerObject p ? p.Weapon1?.ItemLevel ?? 0 : 0;
            return level >= 11 ? 2 : level >= 7 ? 1 : 0;
        }

        protected override async Task LoadEffectContent()
        {
            _impactTexture = await LoadTexture(ImpactTexturePath);
            _lightTexture = await LoadTexture(LightTexturePath);
            for (int i = 0; i < CastingModelPaths.Length; i++)
            {
                SourceModelRenderer? r = await LoadModel(CastingModelPaths[i], 0);
                if (r != null)
                    r.AnimationSpeed = 0f;
                _castingRenderers[i] = r;
            }
            await LoadCreatureContent();
        }

        protected abstract Task LoadCreatureContent();

        protected sealed override void OnStart()
        {
            _origin = CasterPosition;
            TargetCorner = new Vector3(TargetCorner.X, TargetCorner.Y, TerrainHeight(TargetCorner));
            Vector3[] palette = CastingPalette;

            _magic10Life = 44f;
            _magic10Alpha = 0f;
            _magic9Life = 40f;
            _magic9Alpha = 1f;
            _magic9Light = palette[0];
            _magic9HeadAngle = Vector3.Zero;

            for (int i = 0; i < _castingModels.Length; i++)
            {
                _castingModels[i] = new CastingModel
                {
                    Live = true,
                    LifeTime = i == 6 ? 25f : 40f,
                    Scale = i == 6 ? 1f : 0.9f,
                    Yaw = CastYaw,
                    BlendMeshLight = 0f,
                    Light = i < 3 ? palette[1] : i < 6 ? palette[2] : palette[3],
                };
            }

            OnSummon();
        }

        /// <summary>CreateSummonObject: spawn the creature (and its sound).</summary>
        protected abstract void OnSummon();

        protected sealed override void OnTick(float factor)
        {
            TickCasting(factor);
            TickCreature(factor);
        }

        protected abstract void TickCreature(float factor);

        protected abstract bool HasCreatureContent();

        protected abstract void DrawCreature();

        private void TickCasting(float factor)
        {
            if (_magic10Life > 0f)
            {
                if (_magic10Life < 20f) _magic10Alpha -= 0.03f;
                else if (_magic10Alpha < 1f) _magic10Alpha += 0.05f;
                _magic10Life -= factor;
            }

            if (_magic9Life > 0f)
            {
                if (_magic9Life < 20f) _magic9Alpha -= 0.05f;
                else if (_magic9Alpha < 1f) _magic9Alpha += 0.05f;
                _magic9HeadAngle += new Vector3(4f, -8f, 4f) * factor;
                _magic9Life -= factor;
            }

            for (int i = 0; i < _castingModels.Length; i++)
            {
                ref CastingModel m = ref _castingModels[i];
                if (!m.Live)
                    continue;

                if (m.LifeTime < 20f) m.BlendMeshLight -= 0.03f;
                else if (m.BlendMeshLight < 0.5f) m.BlendMeshLight += 0.05f;
                m.Yaw += CastingSpin[i] * factor;
                if (i == 6)
                    m.Scale += 0.6f * factor;
                m.LifeTime -= factor;
                if (m.LifeTime <= 0f)
                    m.Live = false;
            }

            // Dynamic light: casting circle glow at the caster (slot 5; creatures use 0..4).
            if (_magic9Life > 0f)
                Lights.Set(CastLightSlot, _origin + new Vector3(0f, 0f, 50f), _magic9Light, 260f,
                    0.9f * EffectLights.Envelope(40f - _magic9Life, 40f, 4f, 16f));
            else
                Lights.Hide(CastLightSlot);
        }

        protected sealed override bool HasActiveContent()
        {
            if (_magic9Life > 0f || _magic10Life > 0f)
                return true;
            for (int i = 0; i < _castingModels.Length; i++)
                if (_castingModels[i].Live)
                    return true;
            return HasCreatureContent();
        }

        protected sealed override void OnDraw()
        {
            var terrain = World?.Terrain;

            // RenderEffectShadows: decals first.
            if (_magic10Life > 0f && Batch.Begin(_lightTexture, Blendings.Negative, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                Vector3 light = Vector3.One * _magic10Alpha;
                int layers = _magic10Life < 3f ? 1 : _magic10Life < 6f ? 2 : _magic10Life < 9f ? 3 : _magic10Life < 12f ? 4 : 5;
                for (int i = 0; i < layers; i++)
                    Batch.AddTerrainDecal(terrain, _origin.X, _origin.Y, 12f, 12f, light, -CastYaw);
                Batch.End();
            }

            if (_magic9Life > 0f && Batch.Begin(_impactTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                Vector3 light = _magic9Light * _magic9Alpha;
                Batch.AddTerrainDecal(terrain, _origin.X, _origin.Y, 2.4f, 2.4f, light, _magic9HeadAngle.Y);
                Batch.AddTerrainDecal(terrain, _origin.X, _origin.Y, 2.4f * 1.2f, 2.4f * 1.2f, light, _magic9HeadAngle.Z);
                Batch.End();
            }

            for (int i = 0; i < _castingModels.Length; i++)
            {
                ref CastingModel m = ref _castingModels[i];
                SourceModelRenderer? r = _castingRenderers[i];
                if (!m.Live || r == null)
                    continue;

                Vector3 light = (terrain?.EvaluateTerrainLight(_origin.X, _origin.Y) ?? Vector3.Zero) + m.Light;
                r.DrawInstance(_origin, new Vector3(0f, 0f, MathHelper.ToRadians(m.Yaw)), m.Scale, light,
                    MathF.Round(m.BlendMeshLight * 50f) / 50f);
            }

            DrawCreature();
        }
    }
}
