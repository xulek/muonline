#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Graphics;
using Client.Main.Models;
using Client.Main.Objects.Effects.Joints;
using Client.Main.Objects.Player;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects.RageFighter
{
    /// <summary>
    /// Dragon Slasher (AT_SKILL_DRAGON_KICK = 265), SourceMain5.2:
    /// packet   WSclient.cpp:4766 / 13032  PLAYER_SKILL_DRAGONKICK, effect at the packet (hero: at cast, ZzzInterface.cpp:2758)
    /// create   MonkSystem.cpp:1188     MODEL_DRAGON_KICK_DUMMY on the owner + multishot ring set facing the target,
    ///          SOUND_RAGESKILL_DRAGONKICK (packet sound map)
    /// dummy    ZzzEffect.cpp:6252 (LifeTime 200, Velocity = PlaySpeed * 2), 17519 (owner frame 4..5: GROUND_WIND joints;
    ///          frame &gt; 5: 2 x 4 target bursts per tick; frame &gt; 8: SOUND_RAGESKILL_DRAGONKICK_ATTACK)
    /// wind     ZzzEffectJoint.cpp:2684 (FACE_TWO, Scale 150, LifeTime 10, MaxTails 8, Light (0.7,0.7,1)), 6835 (dummy
    ///          bone 2 at frame + sub * AlphaTarget, z += 20F, Light *= 0.7^F)
    /// bursts   ZzzEffectParticle.cpp:734 / 4970 (ENERGY sub 7), 1837 / 6102 (LIGHTNING_MEGA1), 2149 / 2187 / 6664 / 6710
    ///          (SPARK+1 sub 3 / 8), 2060 / 6589 (SPARK sub 13)
    /// rings    SourceMultiShotPool
    /// Not ported: RenderCharacter_AfterImage (character afterimages). The dummy waits for the caster to enter the
    /// action instead of dying at once.
    /// </summary>
    internal sealed class DragonSlasherEffect : SourceSkillEffectBase
    {
        private const string DummyPath = "Effect/dragon_kick_dummy.bmd";            // MODEL_DRAGON_KICK_DUMMY
        private const string WindTexturePath = "Effect/ground_wind.jpg";            // BITMAP_GROUND_WIND
        private const string EnergyTexturePath = "Effect/Thunder01.jpg";            // BITMAP_ENERGY
        private const string MegaTexturePath = "Effect/lighting_mega01.jpg";        // BITMAP_LIGHTNING_MEGA1
        private const string AttackSoundPath = "Sound/Ragefighter/Rage_Dragonkick_Att.wav"; // SOUND_RAGESKILL_DRAGONKICK_ATTACK
        private const float DummyFrozenFrame = 17.99f; // 19 keys, LockPositions
        private const float ActionWaitTicks = 14f;
        private const int MaxWindSlots = 16;
        private static readonly Vector3 KickLight = new(0.7f, 0.7f, 1f);

        private struct Wind
        {
            public bool Live;
            public int SubType;
            public Vector3 Right;
            public Vector3 Up;
            public float Light;
            public float LifeTime;
            public int MaxTails;
        }

        private struct Burst
        {
            public bool Live;
            public int Kind;           // 0 ENERGY 7, 1 MEGA1 0, 2 SPARK+1 3, 3 SPARK+1 8
            public Vector3 Position;
            public Vector3 Light;
            public float Scale;
            public float Rotation;
            public float Alpha;
            public float LifeTime;
        }

        private readonly ushort _targetId;
        private readonly Wind[] _winds = new Wind[32];
        private readonly SourceTrail[] _windTrails = new SourceTrail[32];
        private readonly Vector3[] _windPositions = new Vector3[MaxWindSlots];
        private readonly bool[] _windValid = new bool[MaxWindSlots];
        private readonly Burst[] _bursts = new Burst[256];
        private readonly SourceSparkPool _sparks = new(0, 192);
        private readonly SourceMultiShotPool _rings = new(1);
        private WalkerObject? _target;
        private SourceModelRenderer? _dummyRenderer;
        private Texture2D? _windTexture;
        private Texture2D? _energyTexture;
        private Texture2D? _megaTexture;
        private Texture2D? _spark1Texture;

        private float _playSpeed = 0.4f;
        private int _actionKeys = int.MaxValue;
        private bool _dummyLive = true;
        private bool _dummyAnimating = true;
        private bool _seenAction;
        private float _waitTicks;
        private float _dummyLife = 200f;
        private float _dummyVelocity;
        private float _dummyFrame;
        private float _alphaTarget;
        private Vector3 _dummyPosition;
        private Vector3 _dummyAngle;       // degrees
        private int _windCount;
        private int _burstCount;
        private static readonly Vector3 BurstGlow = new(0.6f, 0.65f, 1f);
        private static readonly Vector3 RingGlow = new(0.5f, 0.56f, 1f);
        private bool _bursting;
        private bool _ringsSpawned;
        private float _burstGlow;
        private float _lightAge;
        private Vector3 _burstPosition;

        public DragonSlasherEffect(WalkerObject caster, ushort targetId)
            : base(caster, triggerFrame: 0f, triggerTimeoutSeconds: 0f, maxQuads: 512)
        {
            _targetId = (ushort)(targetId & 0x7FFF);
        }

        protected override async Task LoadEffectContent()
        {
            if (await Client.Main.Content.BMDLoader.Instance.AssestExist(DummyPath))
            {
                var dummy = new SourceModelRenderer(DummyPath) { AnimationSpeed = 0f };
                if (await dummy.LoadInto(World))
                    _dummyRenderer = dummy;
            }

            await _rings.LoadAsync(World);
            _windTexture = await LoadTexture(WindTexturePath);
            _energyTexture = await LoadTexture(EnergyTexturePath);
            _megaTexture = await LoadTexture(MegaTexturePath);
            _spark1Texture = await LoadTexture(SourceSparkPool.Spark1TexturePath);
            await SoundController.Instance.PreloadSoundAsync(AttackSoundPath);
        }

        protected override void OnStart()
        {
            if (_targetId != 0 && World is WalkableWorldControl walkable && walkable.TryGetWalkerById(_targetId, out WalkerObject target))
                _target = target;

            if (Caster is ModelObject model && model.Model?.Actions is { } actions &&
                (int)PlayerAction.PlayerSkillDragonkick < actions.Length && actions[(int)PlayerAction.PlayerSkillDragonkick] is { } action)
            {
                if (action.PlaySpeed > 0f)
                    _playSpeed = action.PlaySpeed;
                _actionKeys = action.NumAnimationKeys;
            }

            _dummyVelocity = _playSpeed * 2f;
            _dummyPosition = CasterPosition;
            _dummyAngle = OwnerAngleDegrees;

            float factor = MathF.Max(SourceJointMath.FrameFactor, 0.001f);
            int tails = Math.Min(SourceJointMath.ScaledMaxTails(8), (int)(11f / factor) + 4);
            for (int i = 0; i < _windTrails.Length; i++)
                _windTrails[i] = new SourceTrail(tails);

            if (TargetAlive)
            {
                Vector3 angle = _dummyAngle;
                angle.Z = SourceJointMath.CreateAngle2D(CasterPosition, _target!.WorldPosition.Translation);
                _rings.SpawnSet(CasterPosition, angle, new Vector3(0.8f, 0.9f, 1.6f));
                _ringsSpawned = true;
            }
        }

        private Vector3 OwnerAngleDegrees => new(
            MathHelper.ToDegrees(Caster.Angle.X), MathHelper.ToDegrees(Caster.Angle.Y), MathHelper.ToDegrees(Caster.Angle.Z));

        private bool TargetAlive => _target != null && _target.Status != GameControlStatus.Disposed && _target.World != null;

        private bool InAction => Caster is PlayerObject player && player.CurrentAction == PlayerAction.PlayerSkillDragonkick;

        protected override void OnTick(float factor)
        {
            TickDummy(factor);
            TickWinds(factor);
            TickBursts(factor);
            _sparks.Move(factor, World?.Terrain);
            _rings.Move(factor);
            UpdateLights(factor);
        }

        private void UpdateLights(float factor)
        {
            // Slot 0: kick bursts on the target.
            _burstGlow = _bursting ? MathF.Min(1f, _burstGlow + 0.4f * factor) : MathF.Max(0f, _burstGlow - 0.15f * factor);
            if (_burstGlow > 0f)
                Lights.Set(0, _burstPosition, BurstGlow, 320f,
                    1.6f * _burstGlow * EffectLights.Flicker((float)(FPSCounter.Instance.WorldTime * 0.001), 1.4f, 0.35f));
            else
                Lights.Hide(0);

            // Slot 1: multishot ring flash at the caster.
            _lightAge += factor;
            float ring = _ringsSpawned ? EffectLights.Envelope(_lightAge, 14f, 1.5f, 10f) : 0f;
            if (ring > 0f)
                Lights.Set(1, CasterPosition + new Vector3(0f, 0f, 110f), RingGlow, 220f, 1.1f * ring);
            else
                Lights.Hide(1);
        }

        private void TickDummy(float factor)
        {
            _bursting = false;
            if (!_dummyLive)
                return;

            float frame = CasterFrame;
            if (!InAction)
            {
                _waitTicks += factor;
                if (_seenAction || _waitTicks > ActionWaitTicks)
                    KillDummy();
                return;
            }

            _seenAction = true;
            if (frame > _actionKeys)
            {
                KillDummy();
                return;
            }

            if (frame > 4f && frame < 5f && Caster is PlayerObject player)
            {
                float delay = _dummyVelocity * 10f;
                float actionSpeed = _playSpeed * factor;
                float perFrame = actionSpeed / delay;
                float animationFrame = frame - actionSpeed;
                _alphaTarget = perFrame * (_dummyVelocity / _playSpeed);
                Matrix dummyWorld = Matrix.CreateTranslation(_dummyPosition - player.WorldPosition.Translation);
                for (int i = 0; i < delay; i++)
                {
                    // Player bone 1 at the interpolated frame, placed at the dummy position.
                    if (player.TryComputeBoneAt((int)player.CurrentAction, animationFrame, 1, out Matrix boneModel) && FpsCheck(1, factor))
                        SpawnWind((boneModel * player.WorldPosition * dummyWorld).Translation, _dummyAngle, i);
                    animationFrame += perFrame;
                }
            }

            if (TargetAlive)
            {
                if (frame > 5f)
                {
                    SpawnBursts(_target!.WorldPosition.Translation, factor);
                    _bursting = true;
                    _burstPosition = _target!.WorldPosition.Translation + new Vector3(0f, 0f, 80f);
                }

                _dummyAngle = OwnerAngleDegrees;
                _dummyPosition = CasterPosition;

                if (frame > 8f)
                    _dummyLife = 150f;
                if ((int)_dummyLife == 150)
                    SoundController.Instance.PlayBufferSingleChannel(AttackSoundPath);
            }

            if (_dummyAnimating)
            {
                _dummyFrame += _dummyVelocity * factor;
                if (_dummyFrame >= 18f)
                {
                    _dummyFrame = 19f;
                    _dummyAnimating = false;
                }
            }

            _dummyLife -= factor;
            if (_dummyLife <= 0f)
                KillDummy();
        }

        private void KillDummy()
        {
            _dummyLive = false;
            for (int i = 0; i < _winds.Length; i++)
                _winds[i].Live = false;
            _windCount = 0;
        }

        private void SpawnWind(Vector3 position, Vector3 angleDeg, int subType)
        {
            if (subType >= MaxWindSlots)
                return;

            for (int i = 0; i < _winds.Length; i++)
            {
                ref Wind wind = ref _winds[i];
                if (wind.Live)
                    continue;

                wind.Live = true;
                wind.SubType = subType;
                wind.Light = 1f;
                wind.LifeTime = 10f;
                wind.MaxTails = SourceJointMath.ScaledMaxTails(8);
                SourceJointMath.AngleBasis(angleDeg, out wind.Right, out _, out wind.Up);
                SourceTrail trail = _windTrails[i];
                trail.Reset(wind.MaxTails);
                trail.Push(position, wind.Right, wind.Up, 150f);
                _windCount++;
                return;
            }
        }

        private void TickWinds(float factor)
        {
            if (_windCount == 0)
                return;

            Array.Clear(_windValid, 0, _windValid.Length);
            Vector3 angle = ToRadians(_dummyAngle);
            float decay = MathF.Pow(0.7f, factor);
            for (int i = 0; i < _winds.Length; i++)
            {
                ref Wind wind = ref _winds[i];
                if (!wind.Live)
                    continue;

                int slot = wind.SubType;
                if (!_windValid[slot])
                {
                    float targetFrame = MathF.Min(_dummyFrame + slot * _alphaTarget, DummyFrozenFrame);
                    if (_dummyRenderer == null ||
                        !_dummyRenderer.TryGetInstanceBoneAt(targetFrame, 2, _dummyPosition, angle, 1f, out _windPositions[slot]))
                    {
                        _windPositions[slot] = _dummyPosition;
                    }
                    _windPositions[slot].Z += 20f * factor;
                    _windValid[slot] = true;
                }

                wind.Light *= decay;
                _windTrails[i].Push(_windPositions[slot], wind.Right, wind.Up, 150f);
                wind.LifeTime -= factor;
                if (wind.LifeTime < 0f)
                {
                    wind.Live = false;
                    _windCount--;
                }
            }
        }

        private void SpawnBursts(Vector3 target, float factor)
        {
            for (int j = 0; j < 2; j++)
            {
                float luminosity = MathF.Sin((float)(FPSCounter.Instance.WorldTime * 0.002)) * 0.2f;
                Vector3 position = target;
                position.Z += 30f;
                float scale = (MuGame.Random.Next(40) + 50) * 0.01f * 1.5f * 1.8f;
                for (int i = 0; i < 4; i++)
                {
                    if (FpsCheck(1, factor))
                        SpawnBurst(0, position, scale * (MuGame.Random.Next(8) + 6) * 0.1f);
                    if (FpsCheck(1, factor))
                        SpawnBurst(1, position, scale * 0.9f);
                    if (FpsCheck(1, factor))
                        SpawnBurst(2, position, 1f + luminosity * 0.05f);
                    if (FpsCheck(1, factor))
                        SpawnBurst(3, position, 1f + luminosity * 0.05f);

                    if (i % 2 != 0)
                    {
                        if (FpsCheck(1, factor))
                            _sparks.SpawnSpark(position, _dummyAngle, 13);
                        position += new Vector3(MuGame.Random.Next(40) + 20f, MuGame.Random.Next(30) + 10f, MuGame.Random.Next(30) + 25f);
                    }
                    else
                    {
                        position += new Vector3(-MuGame.Random.Next(20) - 10f, MuGame.Random.Next(30) - 10f, MuGame.Random.Next(30) + 30f);
                    }
                }
            }
        }

        private void SpawnBurst(int kind, Vector3 position, float scale)
        {
            for (int i = 0; i < _bursts.Length; i++)
            {
                ref Burst b = ref _bursts[i];
                if (b.Live)
                    continue;

                b.Live = true;
                b.Kind = kind;
                b.Position = position;
                b.Scale = scale;
                b.Alpha = 1f;
                b.Light = kind == 1 ? Vector3.Zero : KickLight;
                b.Rotation = kind <= 1 ? MuGame.Random.Next(360) : 0f;
                b.LifeTime = kind == 3 ? 10f : 5f;
                _burstCount++;
                return;
            }
        }

        private void TickBursts(float factor)
        {
            if (_burstCount == 0)
                return;

            float shrink = MathF.Pow(0.8f, factor);
            for (int i = 0; i < _bursts.Length; i++)
            {
                ref Burst b = ref _bursts[i];
                if (!b.Live)
                    continue;

                b.LifeTime -= factor;
                bool dead = b.LifeTime <= 0f;
                if (!dead)
                {
                    switch (b.Kind)
                    {
                        case 0:
                            b.Rotation += 20f * factor;
                            b.Light *= 0.97f; // per frame
                            break;
                        case 1:
                            b.Alpha -= factor * 0.15f;
                            if (b.Alpha < 0.1f)
                                dead = true;
                            b.Light = KickLight * b.Alpha;
                            break;
                        case 2:
                            b.Scale *= shrink;
                            break;
                        default:
                            b.Scale -= factor * 0.5f;
                            if (b.Scale < 0.2f)
                                dead = true;
                            break;
                    }
                }

                if (dead)
                {
                    b.Live = false;
                    _burstCount--;
                }
            }
        }

        protected override bool HasActiveContent() =>
            _dummyLive || _windCount > 0 || _burstCount > 0 || _burstGlow > 0f || _sparks.HasLive || _rings.HasLive;

        protected override void OnDraw()
        {
            if (_windCount > 0 && Batch.Begin(_windTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _winds.Length; i++)
                {
                    ref Wind wind = ref _winds[i];
                    if (wind.Live)
                        Batch.AddTrail(_windTrails[i], SourceQuadBatch.TrailFaces.Two, KickLight * wind.Light, SourceQuadBatch.TrailShade.Flat, wind.MaxTails - 1);
                }
                Batch.End();
            }

            _rings.Draw(LastGameTime);

            if (_burstCount > 0)
            {
                DrawBursts(_energyTexture, 0, 0, SamplerState.LinearWrap);
                DrawBursts(_megaTexture, 1, 1, SamplerState.LinearWrap);
                DrawBursts(_spark1Texture, 2, 3, SamplerState.LinearClamp);
            }

            _sparks.DrawSparks(Batch, _spark1Texture);
        }

        private void DrawBursts(Texture2D? texture, int firstKind, int lastKind, SamplerState sampler)
        {
            if (texture == null || !Batch.Begin(texture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, sampler))
                return;

            for (int i = 0; i < _bursts.Length; i++)
            {
                ref Burst b = ref _bursts[i];
                if (b.Live && b.Kind >= firstKind && b.Kind <= lastKind)
                    Batch.AddSprite(b.Position, b.Scale, b.Light, b.Rotation);
            }
            Batch.End();
        }

        private static Vector3 ToRadians(Vector3 deg) =>
            new(MathHelper.ToRadians(deg.X), MathHelper.ToRadians(deg.Y), MathHelper.ToRadians(deg.Z));

        protected override void DisposeEffect()
        {
            _dummyRenderer?.Dispose();
            _rings.Dispose();
        }
    }
}
