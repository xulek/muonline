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
    /// Recovery (AT_SKILL_RECOVER = 234), SourceMain5.2 ReceiveMagic (WSclient.cpp:3812), all created at the packet:
    /// remote caster: BITMAP_IMPACT (ZzzEffect.cpp:1174 / 8589: Impack03 + Shiny02 + hikorora sprites before the caster)
    /// target: 19 x BITMAP_FLARE sub 47 joints (LevelUpFlare variant 47), MODEL_SUMMON sub 0 (2131 / 10546, nightmaresum),
    ///         BITMAP_TWLIGHT 0/1/2 decals (3624 / 13289), 2 x BITMAP_FLARE effect sub 3 on random bones
    ///         (3736 / 13482: BITMAP_LIGHT sub 5 particle per tick, static, LifeTime 50)
    /// </summary>
    internal sealed class RecoveryEffect : SourceSkillEffectBase
    {
        private const string SummonModelPath = "Skill/nightmaresum.bmd";     // MODEL_SUMMON
        private const string TwlightTexturePath = "Skill/twlighthik01.jpg";  // BITMAP_TWLIGHT
        private const string LightTexturePath = "Effect/flare01.jpg";        // BITMAP_LIGHT
        private const string ImpactTexturePath = "Effect/Impack03.jpg";      // BITMAP_IMPACT
        private const string ShinyTexturePath = "Effect/Shiny02.jpg";        // BITMAP_SHINY + 1
        private const string OroraTexturePath = "Effect/hikorora.jpg";       // BITMAP_ORORA
        private const float ModelVelocity = 0.3f;

        private static readonly Vector3 GroundLight = new(0.3f, 0.2f, 0.1f);
        private static readonly Vector3 EmberLight = new(0.9f, 0.4f, 0.1f);

        private struct Glow
        {
            public bool Live;
            public Vector3 Position;
            public float Scale;
            public float LifeTime;
        }

        private readonly WalkerObject _target;
        private readonly bool _remoteCaster;
        private readonly Glow[] _glows = new Glow[128];
        private readonly Vector3[] _embers = new Vector3[2];
        private readonly float[] _twlightAngle = new float[3];
        private SourceModelRenderer? _summonModel;
        private Texture2D? _twlightTexture;
        private Texture2D? _lightTexture;
        private Texture2D? _impactTexture;
        private Texture2D? _shinyTexture;
        private Texture2D? _ororaTexture;

        private Vector3 _center;
        private float _twlightScale;
        private float _twlightLifeTime;
        private float _summonLifeTime;
        private Vector3 _summonLight;
        private float _summonAngle;
        private float _summonFrame;
        private float _emberLifeTime;

        private float _impactLifeTime;
        private float _impactScale;
        private float _impactAlpha;

        public RecoveryEffect(WalkerObject caster, WalkerObject target, bool remoteCaster)
            : base(target, 0f, 0f, 256)
        {
            _target = target;
            _remoteCaster = remoteCaster;
            _casterForImpact = caster;
        }

        private readonly WalkerObject _casterForImpact;

        protected override async Task LoadEffectContent()
        {
            _twlightTexture = await LoadTexture(TwlightTexturePath);
            _lightTexture = await LoadTexture(LightTexturePath);
            _impactTexture = await LoadTexture(ImpactTexturePath);
            _shinyTexture = await LoadTexture(ShinyTexturePath);
            _ororaTexture = await LoadTexture(OroraTexturePath);
            _summonModel = await LoadModel(SummonModelPath, 0);
            if (_summonModel != null)
            {
                _summonModel.AnimationSpeed = 0f;
                _summonModel.UseNormalBlend();
            }
        }

        protected override void OnStart()
        {
            const float factor = 1f; // CreateEffect / CreateParticle init offsets: the 5.2 port scales them by FPS_ANIMATION_FACTOR, which shrinks them at high FPS; 25 Hz values
            _center = _target.WorldPosition.Translation;

            for (int i = 0; i < 19; i++)
            {
                var flare = new LevelUpFlare(_center, 47, i);
                World?.Objects.Add(flare);
                _ = flare.Load();
            }

            _summonLifeTime = 60f;
            _summonLight = GroundLight;
            _summonAngle = MathHelper.ToDegrees(_target.Angle.Z) + MuGame.Random.Next(360) * factor;

            _twlightLifeTime = 80f;
            _twlightScale = 8f;
            float targetYaw = MathHelper.ToDegrees(_target.Angle.Z);
            for (int i = 0; i < 3; i++)
                _twlightAngle[i] = targetYaw;

            _emberLifeTime = 60f;
            for (int i = 0; i < 2; i++)
                _embers[i] = RandomBonePosition();

            if (_remoteCaster)
            {
                _impactLifeTime = 80f;
                _impactScale = 0f;
                _impactAlpha = 1f;
            }
        }

        private Vector3 RandomBonePosition()
        {
            if (_target is PlayerObject player)
            {
                Matrix[]? bones = player.GetBoneTransforms();
                if (bones != null && bones.Length > 0)
                    return Vector3.Transform(bones[MuGame.Random.Next(bones.Length)].Translation, player.WorldPosition);
            }
            return _center;
        }

        protected override void OnTick(float factor)
        {
            if (_summonLifeTime > 0f)
            {
                _summonLight *= MathF.Pow(_summonLifeTime >= 30f ? 1.03f : 1f / 1.08f, factor);
                _summonAngle += 1f * factor;
                _summonFrame += ModelVelocity * factor;
                _summonLifeTime -= factor;
            }

            if (_twlightLifeTime > 0f)
            {
                _twlightScale = MathF.Max(0f, _twlightScale - 0.1f * factor);
                _twlightAngle[0] += 10f * factor;
                _twlightAngle[1] += 5f * factor;
                _twlightAngle[2] += 15f * factor;
                _twlightLifeTime -= factor;
            }

            if (_emberLifeTime > 0f)
            {
                for (int i = 0; i < 2; i++)
                    if (FpsCheck(1, factor))
                        SpawnGlow(_embers[i], 3.5f + (MuGame.Random.Next(20) - 10) * 0.1f);
                _emberLifeTime -= factor;
            }

            for (int i = 0; i < _glows.Length; i++)
            {
                ref Glow g = ref _glows[i];
                if (!g.Live)
                    continue;
                g.LifeTime -= factor;
                if (g.LifeTime <= 0f)
                    g.Live = false;
            }

            if (_impactLifeTime > 0f)
            {
                if (_impactScale < 2f)
                    _impactScale += 0.1f * factor;
                else if (_impactScale < 2.4f)
                    _impactScale += 0.02f * factor;
                else
                    _impactScale = 2f;
                if (_impactLifeTime <= 40f)
                    _impactAlpha -= 0.1f * factor;
                _impactLifeTime -= factor;
            }
        }

        private void SpawnGlow(Vector3 position, float scale)
        {
            for (int i = 0; i < _glows.Length; i++)
            {
                ref Glow g = ref _glows[i];
                if (g.Live)
                    continue;
                g = new Glow { Live = true, Position = position, Scale = scale, LifeTime = 50f };
                return;
            }
        }

        protected override bool HasActiveContent()
        {
            if (_summonLifeTime > 0f || _twlightLifeTime > 0f || _emberLifeTime > 0f || _impactLifeTime > 0f)
                return true;
            for (int i = 0; i < _glows.Length; i++) if (_glows[i].Live) return true;
            return false;
        }

        protected override void OnDraw()
        {
            var terrain = World?.Terrain;

            if (_twlightLifeTime > 0f && Batch.Begin(_twlightTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < 3; i++)
                    Batch.AddTerrainDecal(terrain, _center.X, _center.Y, _twlightScale, _twlightScale, GroundLight, -_twlightAngle[i]);
                Batch.End();
            }

            if (_summonLifeTime > 0f && _summonModel != null)
            {
                _summonModel.SetAnimationTime(_summonFrame);
                _summonModel.Update(LastGameTime);
                Vector3 light = (terrain?.EvaluateTerrainLight(_center.X, _center.Y) ?? Vector3.One) * _summonLight;
                _summonModel.DrawInstance(_center, new Vector3(0f, 0f, MathHelper.ToRadians(_summonAngle)), 0.7f, light, 1f);
            }

            if (Batch.Begin(_lightTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _glows.Length; i++)
                {
                    ref Glow g = ref _glows[i];
                    if (g.Live)
                        Batch.AddSprite(g.Position, g.Scale, EmberLight);
                }
                Batch.End();
            }

            if (_impactLifeTime > 0f)
                DrawImpact();
        }

        private void DrawImpact()
        {
            float scaleBk = _impactScale < 2f ? _impactScale
                : _impactScale >= 2.2f ? 2.2f - (_impactScale - 2.2f) : _impactScale;
            Vector3 light = new Vector3(0.7f, 0.6f, 0f) * MathF.Max(_impactAlpha, 0f);
            var angle = new Vector3(0f, 0f, MathHelper.ToDegrees(_casterForImpact.Angle.Z));
            Vector3 position = _casterForImpact.WorldPosition.Translation + SourceJointMath.Rotate(new Vector3(-10f, -30f, 0f), angle);
            position.Z += 130f * SourceJointMath.FrameFactor;
            float spin = (float)(FPSCounter.Instance.WorldTime * 0.08);

            if (Batch.Begin(_impactTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                Batch.AddSprite(position, scaleBk, light);
                Batch.End();
            }
            if (Batch.Begin(_shinyTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                Batch.AddSprite(position, scaleBk, light, -spin);
                Batch.End();
            }
            if (_impactScale > 1f && Batch.Begin(_ororaTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                Batch.AddSprite(position, scaleBk, light, -spin);
                if (_impactScale > 2f)
                    Batch.AddSprite(position, scaleBk, light, spin);
                Batch.End();
            }
        }

        protected override void DisposeEffect()
        {
            _summonModel?.Dispose();
        }
    }
}
