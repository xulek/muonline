#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Client.Main.Models;
using Client.Main.Objects.Effects.Joints;
using Client.Main.Objects.Player;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects.RageFighter
{
    /// <summary>
    /// Dragon Roar (AT_SKILL_DRAGON_LOWER = 264), caster part, SourceMain5.2:
    /// cast     ZzzInterface.cpp:2788 (hero) / WSclient.cpp:5234 (0x1E), PLAYER_SKILL_DRAGONLORE
    /// create   MonkSystem.cpp:1236     BITMAP_LAVA effect + MODEL_BLOW_OF_DESTRUCTION sub 2, SOUND_RAGESKILL_DRAGONLOWER (packet sound map)
    /// lava     ZzzEffect.cpp:6262, 17611 (frame 2..8: BITMAP_LAVA joints on bones 36 / 28 at interpolated frames; frame &gt; 5:
    ///          SHOCKWAVE01 sub 1, SHOCKWAVE_GROUND01 sub 1, fire column + 4 x HIK1 sub 5 + SPARK sub 12)
    /// joint    ZzzEffectJoint.cpp:2695 (FACE_TWO, Scale 20, LifeTime 10, MaxTails 10), 6852 (bone at frame - (PS*10 - sub%7) * 0.1, Light *= 0.8^F)
    /// waves    ZzzEffect.cpp:6005 / 17118 / ZzzObject.cpp:2480 (SHOCKWAVE01 sub 1), 6230 / 17500 / 2539 (GROUND01 sub 1)
    /// blow     ZzzEffect.cpp:4909, 15116 ((int)LifeTime 30: MODEL_DRAGON_LOWER_DUMMY + BITMAP_LIGHT_RED sub 4)
    /// dummy    ZzzEffect.cpp:6270, 17705, 18922, ZzzObject.cpp:2554      red light  ZzzEffect.cpp:6151, 18930
    /// Not ported: AddTerrainLight, BITMAP_LIGHT_RED sub 1 of the dummy (dies in its first
    /// tick and has no render branch). The lava effect waits for the caster to enter the action instead of dying at once.
    /// </summary>
    internal sealed class DragonRoarEffect : SourceSkillEffectBase
    {
        private const string ShockWavePath = "Effect/shockwave01.bmd";              // MODEL_SHOCKWAVE01
        private const string GroundWavePath = "Effect/shockwave_ground01.bmd";      // MODEL_SHOCKWAVE_GROUND01
        private const string CrackPath = "Effect/knight_plancrack_dragon.bmd";      // MODEL_DRAGON_LOWER_DUMMY
        private const string LavaTexturePath = "Effect/Lava.jpg";                   // BITMAP_LAVA
        private const string Damage2TexturePath = "Effect/Damage2.jpg";             // BITMAP_DAMAGE2
        private const string KWave2TexturePath = "Effect/Kwave2.jpg";               // BITMAP_KWAVE2
        private const string RedLightTexturePath = "Effect/flare01_red.jpg";        // BITMAP_LIGHT_RED
        private const int LeftHandBone = 36;
        private const int RightHandBone = 28;
        private const int MaxLagSlots = 14;
        private const float ActionWaitTicks = 14f;

        private struct Lava
        {
            public bool Live;
            public int SubType;
            public Vector3 Right;
            public Vector3 Up;
            public float Light;
            public float Velocity;
            public float LifeTime;
            public int MaxTails;
        }

        private readonly Vector3? _targetPosition;
        private readonly Lava[] _lavas = new Lava[72];
        private readonly SourceTrail[] _lavaTrails = new SourceTrail[72];
        private readonly Vector3[] _lagPositions = new Vector3[MaxLagSlots];
        private readonly bool[] _lagValid = new bool[MaxLagSlots];
        private readonly SourceHikFirePool _fires = new(192);
        private readonly SourceSparkPool _sparks = new(0, 224);
        private SourceModelRenderer? _shockRenderer;
        private SourceModelRenderer? _groundRenderer;
        private SourceModelRenderer? _crackMarkRenderer;   // meshes 0 + 2
        private SourceModelRenderer? _crackLineRenderer;   // mesh 1
        private Texture2D? _lavaTexture;
        private Texture2D? _redLightTexture;
        private Texture2D? _hik1Texture;
        private Texture2D? _lichTexture;
        private Texture2D? _hik3Texture;
        private Texture2D? _sparkTexture;

        private Vector3 _origin;
        private Vector3 _originAngle;      // degrees
        private float _playSpeed = 0.3f;
        private int _actionKeys = int.MaxValue;
        private bool _lavaLive = true;
        private bool _seenAction;
        private float _waitTicks;
        private float _lavaLife = 999f;
        private int _lavaCount;

        private bool _shockLive;
        private Vector3 _shockPosition;
        private Vector3 _shockLight;
        private float _shockScale;
        private float _shockLife;

        private bool _groundLive;
        private Vector3 _groundLight;
        private float _groundScale;
        private float _groundLife;

        private float _blowLife = 50f;
        private int _blowMark = -1;

        private bool _crackLive;
        private bool _crackAnimating;
        private Vector3 _crackPosition;
        private Vector3 _crackAngle;       // degrees
        private float _crackFrame;
        private float _crackLife;
        private float _crackAlpha;

        private Vector3 _redLightPosition;
        private float _redLightLife;
        private float _redLightRotation;

        public DragonRoarEffect(WalkerObject caster, Vector3? targetPosition)
            : base(caster, triggerFrame: 0f, triggerTimeoutSeconds: 0f, maxQuads: 512)
        {
            _targetPosition = targetPosition;
        }

        protected override async Task LoadEffectContent()
        {
            _shockRenderer = await LoadModel(ShockWavePath);
            _groundRenderer = await LoadModel(GroundWavePath);
            _crackMarkRenderer = await LoadMasked(CrackPath, 0b101);
            _crackLineRenderer = await LoadMasked(CrackPath, 0b010);
            if (_shockRenderer != null)
            {
                _shockRenderer.AnimationSpeed = 0f;
                _shockRenderer.OverrideAllTextures(await LoadTexture(Damage2TexturePath));
            }
            if (_groundRenderer != null)
            {
                _groundRenderer.AnimationSpeed = 0f;
                _groundRenderer.OverrideAllTextures(await LoadTexture(KWave2TexturePath));
            }

            _lavaTexture = await LoadTexture(LavaTexturePath);
            _redLightTexture = await LoadTexture(RedLightTexturePath);
            _hik1Texture = await LoadTexture(SourceHikFirePool.Hik1TexturePath);
            _lichTexture = await LoadTexture(SourceHikFirePool.CursedLichTexturePath);
            _hik3Texture = await LoadTexture(SourceHikFirePool.Hik3TexturePath);
            _sparkTexture = await LoadTexture(SourceSparkPool.ParticleTexturePath);
        }

        private async Task<SourceModelRenderer?> LoadMasked(string path, int mask)
        {
            if (!await Client.Main.Content.BMDLoader.Instance.AssestExist(path))
                return null;

            var renderer = new SourceModelRenderer(path) { VisibleMeshMask = mask, AnimationSpeed = 0f };
            return await renderer.LoadInto(World) ? renderer : null;
        }

        protected override void OnStart()
        {
            _origin = CasterPosition;
            _originAngle = OwnerAngleDegrees;

            if (Caster is ModelObject model && model.Model?.Actions is { } actions &&
                (int)PlayerAction.PlayerSkillDragonlore < actions.Length && actions[(int)PlayerAction.PlayerSkillDragonlore] is { } action)
            {
                if (action.PlaySpeed > 0f)
                    _playSpeed = action.PlaySpeed;
                _actionKeys = action.NumAnimationKeys;
            }

            // A lava ribbon lives 11 ticks; MaxTails (10 / F) is never exceeded.
            float factor = MathF.Max(SourceJointMath.FrameFactor, 0.001f);
            int tails = Math.Min(SourceJointMath.ScaledMaxTails(10), (int)(11f / factor) + 4);
            for (int i = 0; i < _lavaTrails.Length; i++)
                _lavaTrails[i] = new SourceTrail(tails);
        }

        private Vector3 OwnerAngleDegrees => new(
            MathHelper.ToDegrees(Caster.Angle.X), MathHelper.ToDegrees(Caster.Angle.Y), MathHelper.ToDegrees(Caster.Angle.Z));

        private bool InAction => Caster is PlayerObject player && player.CurrentAction == PlayerAction.PlayerSkillDragonlore;

        // Animation(BoneTransform, frame, ...) + TransformByObjectBone(result, owner, bone).
        private bool TryOwnerBoneAt(float frame, int bone, out Vector3 position)
        {
            if (Caster is PlayerObject player && player.TryComputeBoneAt((int)player.CurrentAction, frame, bone, out Matrix boneModel))
            {
                position = (boneModel * player.WorldPosition).Translation;
                return true;
            }

            position = default;
            return false;
        }

        protected override void OnTick(float factor)
        {
            TickLavaEffect(factor);
            TickBlow(factor);
            TickCrack(factor);
            TickWaves(factor);
            TickLavaJoints(factor);
            _fires.Move(factor);
            _sparks.Move(factor, World?.Terrain);
            if (_redLightLife > 0f)
                _redLightLife -= factor;
        }

        private void TickLavaEffect(float factor)
        {
            if (!_lavaLive)
                return;

            float frame = CasterFrame;
            if (!InAction)
            {
                _waitTicks += factor;
                if (_seenAction || _waitTicks > ActionWaitTicks)
                    _lavaLive = false;
                return;
            }

            _seenAction = true;
            if (frame > _actionKeys)
            {
                _lavaLive = false;
                return;
            }

            Vector3 ownerAngle = OwnerAngleDegrees;
            if (frame > 2f && frame < 8f && FpsCheck(1, factor))
            {
                float delay = _playSpeed * 10f * factor;
                float perFrame = _playSpeed / delay;
                float animationFrame = frame - _playSpeed;
                for (int i = 0; i < delay; i++)
                {
                    if (TryOwnerBoneAt(animationFrame, LeftHandBone, out Vector3 start) && FpsCheck(1, factor))
                        SpawnLava(start, ownerAngle, i);
                    if (TryOwnerBoneAt(animationFrame, RightHandBone, out Vector3 end) && FpsCheck(1, factor))
                        SpawnLava(end, ownerAngle, 7 + i);
                    animationFrame += perFrame;
                }
            }

            if (frame > 5f && _lavaLife > 100f)
            {
                _shockLive = true;
                _shockPosition = CasterPosition;
                _shockLight = new Vector3(1f, 0.12f, 0f);
                _shockScale = 1f;
                _shockLife = 5f / _playSpeed;
                _lavaLife = 95f;
            }

            if (frame > 5f && _lavaLife > 80f)
            {
                _groundLive = true;
                _groundLight = new Vector3(1f, 0.1f, 0f);
                _groundScale = 1f;
                _groundLife = 50f;
                _lavaLife = 80f;
            }

            if (frame > 5f)
            {
                float scale = (MuGame.Random.Next(5) + 18) * 0.1f;
                Vector3 position = _origin;
                position.Z += (80f - _lavaLife) * 10f - (80f - _lavaLife) * 3f;
                _fires.SpawnRandom(position, Vector3.One, scale, rising: false, factor);

                float columnZ = position.Z;
                var half = new Vector3(0.5f);
                for (int i = 1; i < 8; i += 2)
                {
                    var local = new Vector3(MuGame.Random.Next(100) - 50, MuGame.Random.Next(100) - 50, 0f);
                    var angle = new Vector3(MuGame.Random.Next(90), 0f, 45f * i);
                    position += SourceJointMath.Rotate(local, angle);
                    position.Z = columnZ;
                    if (FpsCheck(1, factor))
                        _fires.Spawn(SourceHikFirePool.Hik1, position, half, scale, rising: false);

                    if (FpsCheck(1, factor))
                        _sparks.SpawnSpark(new Vector3(position.X, position.Y, 250f), _originAngle, 12, 1.5f);
                }
            }

            _lavaLife -= factor;
            if (_lavaLife <= 0f)
                _lavaLive = false;
        }

        private void SpawnLava(Vector3 position, Vector3 angleDeg, int subType)
        {
            if (subType >= MaxLagSlots)
                return;

            for (int i = 0; i < _lavas.Length; i++)
            {
                ref Lava lava = ref _lavas[i];
                if (lava.Live)
                    continue;

                lava.Live = true;
                lava.SubType = subType;
                lava.Light = 1f;
                lava.Velocity = _playSpeed;
                lava.LifeTime = 10f;
                lava.MaxTails = SourceJointMath.ScaledMaxTails(10);
                SourceJointMath.AngleBasis(angleDeg, out lava.Right, out _, out lava.Up);
                SourceTrail trail = _lavaTrails[i];
                trail.Reset(lava.MaxTails);
                trail.Push(position, lava.Right, lava.Up, 20f);
                _lavaCount++;
                return;
            }
        }

        private void TickLavaJoints(float factor)
        {
            if (_lavaCount == 0)
                return;

            Array.Clear(_lagValid, 0, _lagValid.Length);
            float frame = CasterFrame;
            float decay = MathF.Pow(0.8f, factor);
            for (int i = 0; i < _lavas.Length; i++)
            {
                ref Lava lava = ref _lavas[i];
                if (!lava.Live)
                    continue;

                int slot = lava.SubType;
                if (!_lagValid[slot])
                {
                    int tempFrame = slot >= 7 ? slot - 7 : slot;
                    float targetFrame = frame - (lava.Velocity * 10f - tempFrame) * 0.1f;
                    if (!TryOwnerBoneAt(targetFrame, slot >= 7 ? RightHandBone : LeftHandBone, out _lagPositions[slot]))
                        _lagPositions[slot] = GetCasterBonePosition(slot >= 7 ? RightHandBone : LeftHandBone);
                    _lagValid[slot] = true;
                }

                lava.Light *= decay;
                _lavaTrails[i].Push(_lagPositions[slot], lava.Right, lava.Up, 20f);
                lava.LifeTime -= factor;
                if (lava.LifeTime < 0f)
                {
                    lava.Live = false;
                    _lavaCount--;
                }
            }
        }

        private void TickBlow(float factor)
        {
            if (_blowLife <= 0f)
                return;

            // ZzzEffect.cpp:15119
            Client.Main.Graphics.Camera.Instance.SetEarthQuake(
                _blowLife <= 35f && _blowLife >= 15f ? (MuGame.Random.Next(8) - 4) * 0.1f : 0f);

            int mark = (int)_blowLife;
            if (mark == 30 && _blowMark != 30 && _targetPosition.HasValue)
            {
                Vector3 direction = _targetPosition.Value - _origin;
                if (direction.LengthSquared() > 0f)
                    direction.Normalize();
                Vector3 position = _origin + direction * 55f;

                _crackLive = true;
                _crackAnimating = true;
                _crackFrame = 0f;
                _crackLife = 300f;
                _crackAlpha = 1f;
                _crackPosition = new Vector3(position.X, position.Y, TerrainHeight(position) + 10f);
                _crackAngle = new Vector3(_originAngle.X, _originAngle.Y, 45f + MuGame.Random.Next(180));

                _redLightPosition = position;
                _redLightLife = 50f;
                _redLightRotation = OwnerAngleDegrees.X;
            }
            _blowMark = mark;
            _blowLife -= factor;
        }

        private void AdvanceCrack(float amount, bool stopAtEnd)
        {
            if (!_crackAnimating)
                return;

            _crackFrame += amount;
            if (_crackFrame >= 10f)
            {
                if (stopAtEnd)
                {
                    _crackFrame = 10f;
                    _crackAnimating = false;
                }
                else
                {
                    int whole = (int)_crackFrame;
                    _crackFrame = whole % 10 + (_crackFrame - whole);
                }
            }
        }

        private void TickCrack(float factor)
        {
            if (!_crackLive)
                return;

            AdvanceCrack(0.3f * factor, stopAtEnd: false);       // move case
            if (_crackFrame > 1f && _crackFrame < 6f && FpsCheck(1, factor))
                _sparks.SpawnSpark(new Vector3(_crackPosition.X, _crackPosition.Y, 250f), _crackAngle, 12, 1.5f);
            if (_crackFrame > 1f && _crackLife > 80f)
                _crackLife = 80f;
            _crackAlpha *= MathF.Pow(0.96f, factor);

            AdvanceCrack(0.3f * factor, stopAtEnd: true);        // MoveEffect epilogue
            _crackLife -= factor;
            if (_crackLife <= 0f)
            {
                _crackLive = false;
                return;
            }

            AdvanceCrack(0.3f / 6f * factor, stopAtEnd: false);  // RenderEffects
        }

        private void TickWaves(float factor)
        {
            if (_shockLive)
            {
                _shockScale *= MathF.Pow(1.24f, factor);
                _shockPosition.Z *= MathF.Pow(1.19f, factor);
                _shockLight *= 0.45f; // per frame
                if (_shockLight.X < 0.001f)
                    _shockLight = Vector3.Zero;
                _shockLife -= factor;
                if (_shockLife <= 0f)
                    _shockLive = false;
            }

            if (_groundLive)
            {
                _groundScale *= MathF.Pow(1.2f, factor);
                _groundLight *= 0.9f; // per frame
                _groundLife -= factor;
                if (_groundLife <= 0f)
                    _groundLive = false;
            }
        }

        protected override bool HasActiveContent() =>
            _lavaLive || _blowLife > 0f || _crackLive || _shockLive || _groundLive || _lavaCount > 0 ||
            _redLightLife > 0f || _fires.HasLive || _sparks.HasLive;

        protected override void OnDraw()
        {
            if (_redLightLife > 0f && Batch.Begin(_redLightTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                var light = new Vector3(_redLightLife * 0.01f);
                Batch.AddTerrainDecal(World?.Terrain, _redLightPosition.X, _redLightPosition.Y, 3f, 3f, light, _redLightRotation);
                Batch.AddTerrainDecal(World?.Terrain, _redLightPosition.X, _redLightPosition.Y, 3f, 3f, light, _redLightRotation);
                Batch.End();
            }

            if (_lavaCount > 0 && Batch.Begin(_lavaTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _lavas.Length; i++)
                {
                    ref Lava lava = ref _lavas[i];
                    if (lava.Live)
                        Batch.AddTrail(_lavaTrails[i], SourceQuadBatch.TrailFaces.Two, new Vector3(lava.Light), SourceQuadBatch.TrailShade.Flat, lava.MaxTails - 1);
                }
                Batch.End();
            }

            Vector3 originAngle = ToRadians(_originAngle);
            if (_shockLive)
                _shockRenderer?.DrawInstance(_shockPosition, originAngle, _shockScale, _shockLight * 5f, 1f);
            if (_groundLive)
                _groundRenderer?.DrawInstance(_origin, originAngle, _groundScale, _groundLight * 6f, 1f);

            if (_crackLive && _crackMarkRenderer != null && _crackLineRenderer != null)
            {
                float frame = MathF.Min(_crackFrame, 9.99f);
                float scroll = -((long)FPSCounter.Instance.WorldTime % 2000) * 0.0001f;
                Vector3 angle = ToRadians(_crackAngle);

                _crackMarkRenderer.ScrollV = scroll;
                _crackMarkRenderer.SetAnimationTime(frame);
                _crackMarkRenderer.Update(LastGameTime);
                _crackMarkRenderer.DrawInstance(_crackPosition, angle, 0.8f, new Vector3(1f, 0.8f, 0.2f) * _crackAlpha, 1f);

                _crackLineRenderer.ScrollV = scroll;
                _crackLineRenderer.SetAnimationTime(frame);
                _crackLineRenderer.Update(LastGameTime);
                _crackLineRenderer.DrawInstance(_crackPosition, angle, 0.8f, new Vector3(1f, 0.2f, 0.1f) * _crackAlpha, 1f);
            }

            _fires.Draw(Batch, _hik1Texture, _lichTexture, _hik3Texture);
            _sparks.DrawSparks(Batch, _sparkTexture);
        }

        private static Vector3 ToRadians(Vector3 deg) =>
            new(MathHelper.ToRadians(deg.X), MathHelper.ToRadians(deg.Y), MathHelper.ToRadians(deg.Z));

        protected override void DisposeEffect()
        {
            _shockRenderer?.Dispose();
            _groundRenderer?.Dispose();
            _crackMarkRenderer?.Dispose();
            _crackLineRenderer?.Dispose();
        }
    }
}
