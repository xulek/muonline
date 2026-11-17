#nullable enable
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
    /// Beast Uppercut (AT_SKILL_STAMP = 261), SourceMain5.2:
    /// packet   WSclient.cpp:12994      ReceiveStraightAttack: remote casters create the effect at the packet;
    ///          hero ZzzInterface.cpp:2991 once PLAYER_SKILL_STAMP reaches AnimationFrame 2
    /// create   MonkSystem.cpp:1086     MODEL_DOWN_ATTACK_DUMMY_R on the owner, SOUND_RAGESKILL_STAMP
    /// dummy    ZzzEffect.cpp:5980 (Velocity = PlaySpeed(STAMP) * 2, LifeTime 100, Scale 0.9), 17011 (R: bomb at frame 3,
    ///          dead at 6 -> DUMMY_L), 16973 (L: bomb at 5, dead at 8), FORCEPILLAR joint + BITMAP_FLARE sprite on bone 0,
    ///          17899 (PlayAnimation), ZzzObject.cpp:2522 (RENDER_TEXTURE | RENDER_BRIGHT, BodyLight 204)
    /// pillar   ZzzEffectJoint.cpp:2662 (FACE_TWO, Scale 50, LifeTime 7, MaxTails 5, Light (0.95, 0.72, 0.48)),
    ///          6809 (follows bone 0 of the dummy, dies with it)
    /// bomb     ZzzEffect.cpp:6494 CreateBomb3(target, 3, 0.9) + SOUND_RAGESKILL_STAMP_ATTACK
    /// DUMMY_L is created FpsChecked after DUMMY_R is already dead; the 25 Hz behaviour (always created) is used.
    /// Joints of a dead dummy die (the original keeps them when DUMMY_L reuses the effect slot).
    /// </summary>
    internal sealed class BeastUppercutEffect : SourceSkillEffectBase
    {
        private const string RightDummyPath = "Effect/down_left_punch.bmd";   // MODEL_DOWN_ATTACK_DUMMY_R
        private const string LeftDummyPath = "Effect/down_right_punch.bmd";   // MODEL_DOWN_ATTACK_DUMMY_L
        private const string PillarTexturePath = "Effect/force_Pillar.jpg";   // BITMAP_FORCEPILLAR
        private const string FlareTexturePath = "Effect/Flare.jpg";           // BITMAP_FLARE
        private const string CastSoundPath = "Sound/Ragefighter/Rage_Stamp.wav";       // SOUND_RAGESKILL_STAMP
        private const string AttackSoundPath = "Sound/Ragefighter/Rage_Stamp_Att.wav"; // SOUND_RAGESKILL_STAMP_ATTACK
        private const float DummyScale = 0.9f;
        private static readonly Vector3 PillarLight = new(0.95f, 0.72f, 0.48f);
        private static readonly Vector3 FlareLight = new(0.47f, 0.36f, 0.24f);

        private struct Pillar
        {
            public bool Live;
            public Vector3 Right;
            public Vector3 Up;
            public float LifeTime;
        }

        private readonly ushort _targetId;
        private readonly Pillar[] _pillars = new Pillar[12];
        private readonly SourceTrail[] _pillarTrails = new SourceTrail[12];
        private readonly SourceBomb3Pool _bombs = new(6, 40);
        private readonly SourceStonePool _stones = new(6, 0);
        private readonly SourceModelRenderer?[] _dummyRenderers = new SourceModelRenderer?[2];
        private readonly int[] _dummyKeys = new int[2];
        private WalkerObject? _target;
        private Texture2D? _pillarTexture;
        private Texture2D? _flareTexture;
        private Texture2D? _explosionTexture;
        private Texture2D? _magic1Texture;
        private Texture2D? _sparkTexture;

        private int _dummy = -1;          // 0 = DUMMY_R, 1 = DUMMY_L, -1 = none
        private bool _dummyAnimating;
        private float _dummyFrame;
        private float _dummyLife;
        private float _dummyVelocity;
        private Vector3 _dummyPosition;
        private Vector3 _dummyAngle;      // degrees
        private Vector3 _boneOffset;
        private Vector3 _flarePosition;
        private bool _flareVisible;
        private int _pillarCount;
        private static readonly Vector3 BombGlow = new(1f, 0.6f, 0.3f);
        private float _flareGlow;
        private float _bombAge = -1f;
        private Vector3 _bombPosition;

        public BeastUppercutEffect(WalkerObject caster, ushort targetId)
            : base(caster, triggerFrame: caster is PlayerObject { IsMainWalker: true } ? 2f : 0f,
                triggerTimeoutSeconds: 14f * 0.04f, maxQuads: 512)
        {
            _targetId = (ushort)(targetId & 0x7FFF);
            _bombs.Stones = _stones;
        }

        protected override async Task LoadEffectContent()
        {
            _dummyRenderers[0] = await LoadModel(RightDummyPath);
            _dummyRenderers[1] = await LoadModel(LeftDummyPath);
            for (int i = 0; i < 2; i++)
            {
                SourceModelRenderer? renderer = _dummyRenderers[i];
                if (renderer == null)
                    continue;
                renderer.AnimationSpeed = 0f;
                _dummyKeys[i] = renderer.Model?.Actions is { Length: > 0 } actions ? actions[0].NumAnimationKeys : 1;
            }

            await _stones.LoadAsync(World);
            _pillarTexture = await LoadTexture(PillarTexturePath);
            _flareTexture = await LoadTexture(FlareTexturePath);
            _explosionTexture = await LoadTexture(SourceBomb3Pool.ExplosionTexturePath);
            _magic1Texture = await LoadTexture(SourceBomb3Pool.Magic1TexturePath);
            _sparkTexture = await LoadTexture(SourceSparkPool.ParticleTexturePath);
            await SoundController.Instance.PreloadSoundAsync(AttackSoundPath);
        }

        protected override void OnStart()
        {
            if (_targetId != 0 && World is WalkableWorldControl walkable && walkable.TryGetWalkerById(_targetId, out WalkerObject target))
                _target = target;

            float playSpeed = 0.4f;
            if (Caster is ModelObject model && model.Model?.Actions is { } actions &&
                (int)PlayerAction.PlayerSkillStamp < actions.Length && actions[(int)PlayerAction.PlayerSkillStamp]?.PlaySpeed > 0f)
            {
                playSpeed = actions[(int)PlayerAction.PlayerSkillStamp].PlaySpeed;
            }
            _dummyVelocity = playSpeed * 2f;

            int tails = SourceJointMath.ScaledMaxTails(5);
            for (int i = 0; i < _pillarTrails.Length; i++)
                _pillarTrails[i] = new SourceTrail(tails);

            StartDummy(0);
            SoundController.Instance.PlayBuffer(CastSoundPath);
        }

        private Vector3 OwnerAngleDegrees => new(
            MathHelper.ToDegrees(Caster.Angle.X), MathHelper.ToDegrees(Caster.Angle.Y), MathHelper.ToDegrees(Caster.Angle.Z));

        private bool TargetAlive => _target != null && _target.Status != GameControlStatus.Disposed && _target.World != null;

        private void StartDummy(int kind)
        {
            _dummy = kind;
            _dummyAnimating = true;
            _dummyFrame = 0f;
            _dummyLife = 100f;
            _dummyPosition = CasterPosition;
            _dummyAngle = OwnerAngleDegrees;
            _flareVisible = false;
            UpdateBone();
        }

        // Animation(BoneTransform, AnimationFrame, ..., o->Angle) + TransformByObjectBone(result, o, 0).
        private void UpdateBone()
        {
            _boneOffset = Vector3.Zero;
            SourceModelRenderer? renderer = _dummy >= 0 ? _dummyRenderers[_dummy] : null;
            if (renderer == null)
                return;

            int keys = _dummyKeys[_dummy];
            renderer.SetAnimationTime(System.MathF.Min(_dummyFrame, keys - 0.01f));
            renderer.Update(LastGameTime);
            if (renderer.TryGetInstanceBone(0, _dummyPosition, ToRadians(_dummyAngle), DummyScale, out Vector3 bone))
                _boneOffset = bone - _dummyPosition;
        }

        protected override void OnTick(float factor)
        {
            TickDummy(factor);
            TickPillars(factor);
            _bombs.Move(factor, World?.Terrain);
            _stones.Move(factor, World?.Terrain);
            UpdateLights(factor);
        }

        private void UpdateLights(float factor)
        {
            // Slot 0: force pillar glow on the punching fist.
            _flareGlow = _flareVisible && _dummy >= 0
                ? System.MathF.Min(1f, _flareGlow + 0.35f * factor)
                : System.MathF.Max(0f, _flareGlow - 0.25f * factor);
            if (_flareGlow > 0f)
                Lights.Set(0, _flarePosition, PillarLight, 200f, _flareGlow);
            else
                Lights.Hide(0);

            // Slot 1: bomb flash on the target (restarted by each uppercut).
            if (_bombAge >= 0f)
            {
                _bombAge += factor;
                float flash = EffectLights.Flash(_bombAge, 14f, 1.4f);
                if (flash > 0f)
                    Lights.Set(1, _bombPosition, BombGlow, 300f + 120f * flash,
                        2f * flash * EffectLights.Flicker((float)(FPSCounter.Instance.WorldTime * 0.001), 2.3f, 0.2f));
                else
                    Lights.Hide(1);
            }
        }

        private void TickDummy(float factor)
        {
            if (_dummy < 0)
                return;

            _flareVisible = false;
            bool right = _dummy == 0;
            if (_dummyFrame >= (right ? 6f : 8f))
            {
                KillPillars();
                if (right)
                    StartDummy(1);
                else
                    _dummy = -1;
                return;
            }

            bool interrupted = false;
            if (_dummyFrame >= (right ? 3f : 5f) && _dummyLife > 50f)
            {
                if (!TargetAlive)
                {
                    interrupted = true;
                }
                else
                {
                    _bombs.Spawn(_target!.WorldPosition.Translation, 3, factor, 0.9f);
                    _bombAge = 0f;
                    _bombPosition = _target!.WorldPosition.Translation + new Vector3(0f, 0f, 60f);
                    SoundController.Instance.PlayBuffer(AttackSoundPath);
                    _dummyLife = 40f;
                }
            }

            if (!interrupted)
            {
                if (FpsCheck(1, factor))
                    SpawnPillar(_dummyPosition + _boneOffset, _dummyAngle);

                _dummyAngle = OwnerAngleDegrees;
                _dummyPosition = CasterPosition;
                _flarePosition = _dummyPosition + _boneOffset;
                _flareVisible = true;
            }

            if (_dummyAnimating)
            {
                _dummyFrame += _dummyVelocity * factor;
                int keys = _dummyKeys[_dummy];
                if (keys > 1 && _dummyFrame >= keys)
                {
                    _dummyFrame = keys;
                    _dummyAnimating = false;
                }
            }

            _dummyLife -= factor;
            if (_dummyLife <= 0f)
            {
                KillPillars();
                _dummy = -1;
                return;
            }

            UpdateBone();
        }

        private void SpawnPillar(Vector3 position, Vector3 angleDeg)
        {
            for (int i = 0; i < _pillars.Length; i++)
            {
                ref Pillar pillar = ref _pillars[i];
                if (pillar.Live)
                    continue;

                pillar.Live = true;
                pillar.LifeTime = 7f;
                SourceJointMath.AngleBasis(angleDeg, out pillar.Right, out _, out pillar.Up);
                SourceTrail trail = _pillarTrails[i];
                trail.Reset(SourceJointMath.ScaledMaxTails(5));
                trail.Push(position, pillar.Right, pillar.Up, 50f);
                _pillarCount++;
                return;
            }
        }

        private void KillPillars()
        {
            for (int i = 0; i < _pillars.Length; i++)
                _pillars[i].Live = false;
            _pillarCount = 0;
        }

        private void TickPillars(float factor)
        {
            if (_pillarCount == 0 || _dummy < 0)
                return;

            // MoveJoint: Position = bone 0 of the target dummy; CreateTail with the joint's own angle matrix.
            Vector3 position = _dummyPosition + _boneOffset;
            for (int i = 0; i < _pillars.Length; i++)
            {
                ref Pillar pillar = ref _pillars[i];
                if (!pillar.Live)
                    continue;

                _pillarTrails[i].Push(position, pillar.Right, pillar.Up, 50f);
                pillar.LifeTime -= factor;
                if (pillar.LifeTime < 0f)
                {
                    pillar.Live = false;
                    _pillarCount--;
                }
            }
        }

        protected override bool HasActiveContent() => _dummy >= 0 || _pillarCount > 0 || _flareGlow > 0f || _bombs.HasLive || _stones.HasLive;

        protected override void OnDraw()
        {
            if (_pillarCount > 0 && Batch.Begin(_pillarTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearWrap))
            {
                for (int i = 0; i < _pillars.Length; i++)
                {
                    if (!_pillars[i].Live)
                        continue;
                    SourceTrail trail = _pillarTrails[i];
                    Batch.AddTrail(trail, SourceQuadBatch.TrailFaces.Two, PillarLight, SourceQuadBatch.TrailShade.Flat, trail.MaxTails - 1);
                }
                Batch.End();
            }

            SourceModelRenderer? renderer = _dummy >= 0 ? _dummyRenderers[_dummy] : null;
            if (renderer != null)
            {
                // BodyLight = RequestTerrainLight + o->Light (the caster's light, (1,1,1)).
                Vector3 light = (World?.Terrain?.EvaluateTerrainLight(_dummyPosition.X, _dummyPosition.Y) ?? Vector3.Zero) + Vector3.One;
                renderer.DrawInstance(_dummyPosition, ToRadians(_dummyAngle), DummyScale, light, 1f);
            }

            _stones.DrawModels(LastGameTime, World?.Terrain);

            if (_flareVisible && _dummy >= 0 && Batch.Begin(_flareTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                Batch.AddSprite(_flarePosition, 5f, FlareLight);
                Batch.End();
            }

            _bombs.Draw(Batch, _explosionTexture, _magic1Texture, _sparkTexture);
        }

        private static Vector3 ToRadians(Vector3 deg) =>
            new(MathHelper.ToRadians(deg.X), MathHelper.ToRadians(deg.Y), MathHelper.ToRadians(deg.Z));

        protected override void DisposeEffect()
        {
            _dummyRenderers[0]?.Dispose();
            _dummyRenderers[1]?.Dispose();
            _stones.Dispose();
        }
    }
}
