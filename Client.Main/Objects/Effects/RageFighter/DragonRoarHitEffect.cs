#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Graphics;
using Client.Main.Models;
using Client.Main.Objects.Effects.Joints;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects.RageFighter
{
    /// <summary>
    /// Dragon Roar (AT_SKILL_DRAGON_LOWER = 264), per-target hit, SourceMain5.2:
    /// packet   WSclient.cpp:4780       SetLowerEffEct (MonkSystem.cpp:1339, first 4 hits of a cast): MODEL_VOLCANO_OF_MONK
    ///          sub = hit count, Scale 0.8, on the target; MODEL_TARGETMON_EFFECT; SOUND_RAGESKILL_DRAGONLOWER_ATTACK (handler)
    /// volcano  ZzzEffect.cpp:6293 (sub 1: LifeTime 100, Velocity 0.6, 4 stones, LIGHT_RED sub 3; sub 2 / 3: delay
    ///          20..29 / 30..49), 17768 (rising fire, (int)LifeTime 10: 4 stones + SHOCKWAVE_GROUND01 sub 2, Alpha /
    ///          BlendMeshLight fades; sub 2 / 3 erupt as sub 1 with Scale 1 at LifeTime 2), ZzzObject.cpp:2569
    /// stone    ZzzEffect.cpp:6324, 17840, ZzzObject.cpp:2579      wave  ZzzEffect.cpp:6238, 17507, ZzzObject.cpp:2547
    /// target   ZzzEffect.cpp:6286, 17729 (3 fires on a random bone every tick, LifeTime 100)
    /// light    ZzzEffect.cpp:6162, 18952 (BITMAP_LIGHT_RED sub 3 decal, Light = LifeTime * 0.01)
    /// Sub 4 volcanoes have no init branch and never show. Not ported: AddTerrainLight.
    /// </summary>
    internal sealed class DragonRoarHitEffect : SourceSkillEffectBase
    {
        private const string VolcanoPath = "Effect/volcano_of_monk.bmd";            // MODEL_VOLCANO_OF_MONK
        private const string StonePath = "Effect/volcano_stone.bmd";                // MODEL_VOLCANO_STONE
        private const string GroundWavePath = "Effect/shockwave_ground01.bmd";      // MODEL_SHOCKWAVE_GROUND01
        private const string CoreTexturePath = "Effect/volcano_core.jpg";           // BITMAP_VOLCANO_CORE
        private const string RedLightTexturePath = "Effect/flare01_red.jpg";        // BITMAP_LIGHT_RED
        private const int VolcanoKeys = 9;

        /// <summary>CMonkSystem::m_nLowerEffCnt per caster; reset when a new cast starts.</summary>
        private static readonly Dictionary<ushort, int> HitCounters = new();

        private struct Stone
        {
            public bool Live;
            public Vector3 Position;
            public Vector3 Angle;
            public Vector3 HeadAngle;
            public float Scale;
            public float Gravity;
            public float Alpha;
            public float LifeTime;
        }

        private readonly ushort _targetId;
        private readonly int _volcanoSubType;
        private readonly Stone[] _stones = new Stone[8];
        private readonly SourceHikFirePool _fires = new(384);
        private WalkerObject? _target;
        private SourceModelRenderer? _volcanoRenderer;
        private SourceModelRenderer? _coreRenderer;
        private SourceModelRenderer? _stoneRenderer;
        private SourceModelRenderer? _waveRenderer;
        private Texture2D? _redLightTexture;
        private Texture2D? _hik1Texture;
        private Texture2D? _lichTexture;
        private Texture2D? _hik3Texture;

        private float _targetFireLife = 100f;
        private float _delayLife;
        private Vector3 _hitPosition;
        private Vector3 _hitAngle;         // degrees

        private bool _volcanoLive;
        private bool _volcanoAnimating;
        private Vector3 _volcanoPosition;
        private float _volcanoScale;
        private float _volcanoLife;
        private float _volcanoFrame;
        private float _volcanoAlpha;
        private float _volcanoBlendLight;
        private int _volcanoMark = -1;

        private float _redLightLife;
        private static readonly Vector3 VolcanoGlow = new(1f, 0.35f, 0.1f);

        private bool _waveLive;
        private Vector3 _waveLight;
        private float _waveScale;
        private float _waveLife;

        public DragonRoarHitEffect(WalkerObject caster, ushort targetId, int volcanoSubType)
            : base(caster, triggerFrame: 0f, triggerTimeoutSeconds: 0f, maxQuads: 512)
        {
            _targetId = (ushort)(targetId & 0x7FFF);
            _volcanoSubType = volcanoSubType;
        }

        /// <summary>Called when a new Dragon Roar cast starts (CMonkSystem::InitLower).</summary>
        public static void ResetHitCounter(ushort casterId) => HitCounters[casterId] = 0;

        /// <summary>SetLowerEffEct + GetLowerEffCnt: the volcano subtype for the next hit (0 = none).</summary>
        public static int NextVolcanoSubType(ushort casterId)
        {
            HitCounters.TryGetValue(casterId, out int count);
            if (count > 3)
                return 0;

            HitCounters[casterId] = ++count;
            return count;
        }

        protected override async Task LoadEffectContent()
        {
            _volcanoRenderer = await LoadModel(VolcanoPath, -1);
            _stoneRenderer = await LoadModel(StonePath, -1);
            _waveRenderer = await LoadModel(GroundWavePath);
            if (_volcanoRenderer != null)
            {
                _volcanoRenderer.AnimationSpeed = 0f;
                _volcanoRenderer.UseNormalBlend();
            }
            if (_stoneRenderer != null)
            {
                _stoneRenderer.AnimationSpeed = 0f;
                _stoneRenderer.UseNormalBlend();
            }
            if (_waveRenderer != null)
                _waveRenderer.AnimationSpeed = 0f;

            if (await Client.Main.Content.BMDLoader.Instance.AssestExist(VolcanoPath))
            {
                var core = new SourceModelRenderer(VolcanoPath) { VisibleMeshMask = 0b1, AnimationSpeed = 0f };
                if (await core.LoadInto(World))
                {
                    core.OverrideAllTextures(await LoadTexture(CoreTexturePath));
                    _coreRenderer = core;
                }
            }

            _redLightTexture = await LoadTexture(RedLightTexturePath);
            _hik1Texture = await LoadTexture(SourceHikFirePool.Hik1TexturePath);
            _lichTexture = await LoadTexture(SourceHikFirePool.CursedLichTexturePath);
            _hik3Texture = await LoadTexture(SourceHikFirePool.Hik3TexturePath);
        }

        protected override void OnStart()
        {
            if (_targetId != 0 && World is WalkableWorldControl walkable && walkable.TryGetWalkerById(_targetId, out WalkerObject target))
                _target = target;

            if (!TargetAlive)
            {
                _targetFireLife = 0f;
                return;
            }

            _hitPosition = _target!.WorldPosition.Translation;
            _hitAngle = new Vector3(
                MathHelper.ToDegrees(_target.Angle.X), MathHelper.ToDegrees(_target.Angle.Y), MathHelper.ToDegrees(_target.Angle.Z));

            if (_volcanoSubType == 1)
                Erupt(0.8f);
            else if (_volcanoSubType == 2)
                _delayLife = 20 + MuGame.Random.Next(10);
            else if (_volcanoSubType == 3)
                _delayLife = 30 + MuGame.Random.Next(20);
        }

        private bool TargetAlive => _target != null && _target.Status != GameControlStatus.Disposed && _target.World != null;

        private void Erupt(float scale)
        {
            _volcanoLive = true;
            _volcanoAnimating = true;
            _volcanoScale = scale;
            _volcanoLife = 100f;
            _volcanoFrame = 0f;
            _volcanoAlpha = 1f;
            _volcanoBlendLight = 1f;
            _volcanoMark = -1;
            _volcanoPosition = new Vector3(_hitPosition.X, _hitPosition.Y, TerrainHeight(_hitPosition));
            for (int i = 0; i < 4; i++)
                SpawnStone();
            _redLightLife = 100f;
        }

        private void SpawnStone()
        {
            for (int i = 0; i < _stones.Length; i++)
            {
                ref Stone s = ref _stones[i];
                if (s.Live)
                    continue;

                s.Live = true;
                s.Position = _volcanoPosition;
                s.Scale = 1f + (MuGame.Random.Next(20) + 5) * 0.06f;
                s.LifeTime = MuGame.Random.Next(10) + 30;
                s.Gravity = MuGame.Random.Next(2) + 2;
                s.Alpha = 1f;
                s.Angle = new Vector3(_hitAngle.X, _hitAngle.Y, MuGame.Random.Next(360));
                s.HeadAngle = SourceJointMath.Rotate(new Vector3(0f, (MuGame.Random.Next(128) + 64) * 0.1f, 0f), s.Angle);
                s.HeadAngle.Z += 25f * SourceJointMath.FrameFactor;
                return;
            }
        }

        protected override void OnTick(float factor)
        {
            TickTargetFire(factor);
            TickDelay(factor);
            TickVolcano(factor);
            TickStones(factor);

            if (_waveLive)
            {
                _waveScale *= MathF.Pow(1.2f, factor);
                _waveLight *= 0.7f; // per frame
                _waveLife -= factor;
                if (_waveLife <= 0f)
                    _waveLive = false;
            }

            if (_redLightLife > 0f)
                _redLightLife -= factor;

            _fires.Move(factor);

            // Dynamic light: one eruption glow per hit (none for hits without a volcano).
            if (_volcanoLive)
            {
                float glow = MathF.Min(1f, (100f - _volcanoLife) / 4f) * MathHelper.Clamp(_volcanoLife / 20f, 0f, 1f);
                Lights.Set(0, _volcanoPosition + new Vector3(0f, 0f, 80f), VolcanoGlow, 320f,
                    1.5f * glow * EffectLights.Flicker((float)(FPSCounter.Instance.WorldTime * 0.001), _targetId * 0.37f, 0.2f));
            }
            else
            {
                Lights.Hide(0);
            }
        }

        private void TickTargetFire(float factor)
        {
            if (_targetFireLife <= 0f)
                return;

            if (!TargetAlive)
            {
                _targetFireLife = 0f;
                return;
            }

            Matrix[]? bones = _target!.GetBoneTransforms();
            if (bones != null && bones.Length > 1)
            {
                Vector3 position = Vector3.Transform(bones[MuGame.Random.Next(bones.Length - 1)].Translation, _target.WorldPosition);
                float scale = (MuGame.Random.Next(5) + 5) * 0.1f;
                for (int i = 0; i < 3; i++)
                    _fires.SpawnRandom(position, Vector3.One, scale, rising: false, factor);
            }

            _targetFireLife -= factor;
        }

        private void TickDelay(float factor)
        {
            if (_delayLife <= 0f)
                return;

            if (!TargetAlive)
            {
                _delayLife = 0f;
                return;
            }

            if ((int)_delayLife == 2)
            {
                _delayLife = 0f;
                Erupt(1f);
                return;
            }

            _delayLife -= factor;
        }

        private void TickVolcano(float factor)
        {
            if (!_volcanoLive)
                return;

            Vector3 firePosition = _volcanoPosition;
            firePosition.Z += (80f - _volcanoLife) * (0.9f + MuGame.Random.Next(5) * 0.1f);
            firePosition.Z += 20f;
            if (firePosition.Z <= 400f)
            {
                float scale = _volcanoScale * (MuGame.Random.Next(5) + 8) * 0.19f;
                _fires.SpawnRandom(firePosition, Vector3.One, scale, rising: true, factor);

                int mark = (int)_volcanoLife;
                if (mark == 10 && _volcanoMark != 10)
                {
                    for (int i = 0; i < 4; i++)
                        SpawnStone();

                    _waveLive = true;
                    _waveLight = new Vector3(1f, 0.1f, 0f);
                    _waveScale = 1f;
                    _waveLife = 10f;
                }
                _volcanoMark = mark;

                if (_volcanoLife < 20f)
                    _volcanoAlpha = _volcanoLife * 0.1f;
                if (_volcanoLife < 30f)
                    _volcanoBlendLight = _volcanoLife * 0.03f;
            }

            if (_volcanoAnimating)
            {
                _volcanoFrame += 0.6f * factor;
                if (_volcanoFrame >= VolcanoKeys)
                {
                    _volcanoFrame = VolcanoKeys;
                    _volcanoAnimating = false;
                }
            }

            _volcanoLife -= factor;
            if (_volcanoLife <= 0f)
                _volcanoLive = false;
        }

        private void TickStones(float factor)
        {
            var terrain = World?.Terrain;
            float drag = MathF.Pow(0.6f, factor);
            for (int i = 0; i < _stones.Length; i++)
            {
                ref Stone s = ref _stones[i];
                if (!s.Live)
                    continue;

                s.HeadAngle.Z -= s.Gravity * factor;
                s.Position += s.HeadAngle; // per frame
                float height = terrain?.RequestTerrainHeight(s.Position.X, s.Position.Y) ?? s.Position.Z;
                s.Angle.X += 0.3f * s.LifeTime * factor;
                s.Angle.Y += 0.3f * s.LifeTime * factor;
                if (s.Position.Z <= height)
                {
                    s.Position.Z = height;
                    s.HeadAngle.X *= drag;
                    s.HeadAngle.Y *= drag;
                    s.HeadAngle.Z += s.LifeTime * factor;
                    if (s.HeadAngle.Z < 0.5f)
                        s.HeadAngle.Z = 0f;
                    s.Alpha -= 0.05f * factor;
                }
                s.Scale -= 0.03f * factor;

                float fireScale = s.Scale * (MuGame.Random.Next(5) + 5) * 0.1f;
                _fires.SpawnRandom(s.Position, Vector3.One, fireScale, rising: false, factor);

                s.LifeTime -= factor;
                if (s.LifeTime <= 0f)
                    s.Live = false;
            }
        }

        protected override bool HasActiveContent()
        {
            if (_targetFireLife > 0f || _delayLife > 0f || _volcanoLive || _waveLive || _redLightLife > 0f || _fires.HasLive)
                return true;
            for (int i = 0; i < _stones.Length; i++) if (_stones[i].Live) return true;
            return false;
        }

        protected override void OnDraw()
        {
            if (_redLightLife > 0f && Batch.Begin(_redLightTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                Batch.AddTerrainDecal(World?.Terrain, _volcanoPosition.X, _volcanoPosition.Y, 3f, 3f, new Vector3(_redLightLife * 0.01f), _hitAngle.X);
                Batch.End();
            }

            Vector3 angle = ToRadians(_hitAngle);
            if (_volcanoLive)
            {
                float frame = MathF.Min(_volcanoFrame, VolcanoKeys - 0.01f);
                if (_volcanoRenderer != null)
                {
                    _volcanoRenderer.SetAnimationTime(frame);
                    _volcanoRenderer.Update(LastGameTime);
                    _volcanoRenderer.DrawInstance(_volcanoPosition, angle, _volcanoScale, Vector3.One, 1f, _volcanoAlpha);
                }
                if (_coreRenderer != null)
                {
                    _coreRenderer.SetAnimationTime(frame);
                    _coreRenderer.Update(LastGameTime);
                    _coreRenderer.DrawInstance(_volcanoPosition, angle, _volcanoScale, new Vector3(_volcanoBlendLight), 1f);
                }
            }

            if (_stoneRenderer != null)
            {
                for (int i = 0; i < _stones.Length; i++)
                {
                    ref Stone s = ref _stones[i];
                    if (s.Live && s.Scale > 0f)
                        _stoneRenderer.DrawInstance(s.Position, ToRadians(s.Angle), s.Scale, Vector3.One, 1f, s.Alpha);
                }
            }

            if (_waveLive)
                _waveRenderer?.DrawInstance(_volcanoPosition, angle, _waveScale, _waveLight * 6f, 1f);

            _fires.Draw(Batch, _hik1Texture, _lichTexture, _hik3Texture);
        }

        private static Vector3 ToRadians(Vector3 deg) =>
            new(MathHelper.ToRadians(deg.X), MathHelper.ToRadians(deg.Y), MathHelper.ToRadians(deg.Z));

        protected override void DisposeEffect()
        {
            _volcanoRenderer?.Dispose();
            _coreRenderer?.Dispose();
            _stoneRenderer?.Dispose();
            _waveRenderer?.Dispose();
        }
    }
}
