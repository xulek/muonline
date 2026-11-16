#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Objects.Effects.Joints;
using Client.Main.Objects.Player;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// Magic Gladiator Fire Slash (AT_SKILL_REDUCEDEFENSE 55).
    ///
    /// ZzzCharacter.cpp AttackStage (PLAYER_ATTACK_SKILL_WHEEL):
    ///   frames 1-2: CreateEffect(BITMAP_GATHERING, ..., 1, o) - 20 frames of sparks and small
    ///               JOINT_THUNDER sub 3 arcs converging on the left hand (bone 33);
    ///   frame &gt;= 3: PlayBuffer(SOUND_SKILL_SWORD3); CreateEffect(BITMAP_SWORD_FORCE, ..., 0, o)
    ///               which on its first frame creates BITMAP_JOINT_FORCE sub 0 at +100 Z with
    ///               HeadAngle yaw + 45 and scale 150.
    /// BITMAP_JOINT_FORCE sub 0 (Effect/motion_blur_r2.jpg): a crescent swept around the caster
    /// with radius 145 starting at yaw + 75, 8 tails per frame stepping -11 deg (18 tails), each
    /// tail spawning a BITMAP_FIRE particle (50%) and two JOINT_THUNDER sub 3 arcs. Lifetime 20,
    /// luminosity LifeTime / 30, then /1.5 per frame below 10.
    /// </summary>
    internal sealed class FireSlashEffect : SourceSkillEffectBase
    {
        private const string ForceTexturePath = "Effect/motion_blur_r2.jpg";
        private const string ThunderTexturePath = "Effect/JointThunder01.jpg";
        private const string FireTexturePath = "Effect/Fire01.jpg";
        private const string ShinyTexturePath = "Effect/Shiny02.jpg";
        private const string SparkTexturePath = "Effect/Spark03.jpg";
        private const string SwingSoundPath = "Sound/sKnightSkill3.wav";   // SOUND_SKILL_SWORD3

        private const int ForceTails = 18;
        private const float ForceScale = 150f;
        private const float ForceRadius = 145f;
        private const float GatheringLife = 20f;
        private const float SlashTriggerFrame = 3f;
        private const float SlashTriggerTimeout = 0.35f;

        private static readonly Vector3 ThunderLight = new(0.5f, 0.5f, 1f);

        private readonly int _skillId;
        private readonly SourceTrail _trail = new(200); // MAX_TAILS; Reset scales MaxTails 18 by the frame factor
        private readonly SourceThunderPool _thunders = new(192);

        private struct Particle
        {
            public bool Live;
            public Vector3 Position;
            public Vector3 Velocity;
            public Vector3 Light;
            public float Scale;
            public float Gravity;
            public float Rotation;
            public float LifeTime;
        }

        private readonly Particle[] _fires = new Particle[96];   // BITMAP_FIRE sub 0
        private readonly Particle[] _sparks = new Particle[64];  // BITMAP_SPARK+1 sub 2

        private Texture2D? _forceTexture;
        private Texture2D? _thunderTexture;
        private Texture2D? _fireTexture;
        private Texture2D? _shinyTexture;
        private Texture2D? _sparkTexture;

        private float _gatheringLife = GatheringLife;
        private Vector3 _handPosition;
        private float _shinyScale;
        private float _shinyRotation;

        private bool _slashStarted;
        private float _slashWait;
        private Vector3 _slashCenter;
        private Vector3 _slashAngle;
        private float _slashDirection;
        private Vector3 _slashPosition;
        private float _slashLight;
        private float _slashLife;
        private int _slashPushes;

        public FireSlashEffect(WalkerObject caster, int skillId)
            : base(caster, triggerFrame: 0f, triggerTimeoutSeconds: 0f, maxQuads: 512)
        {
            _skillId = skillId;
        }

        protected override async Task LoadEffectContent()
        {
            _forceTexture = await LoadTexture(ForceTexturePath);
            _thunderTexture = await LoadTexture(ThunderTexturePath);
            _fireTexture = await LoadTexture(FireTexturePath);
            _shinyTexture = await LoadTexture(ShinyTexturePath);
            _sparkTexture = await LoadTexture(SparkTexturePath);
        }

        protected override void OnStart()
        {
            _handPosition = GetCasterBonePosition(PlayerObject.LeftHandBoneIndex) + Vector3.UnitZ * 10f;
        }

        protected override void OnTick(float factor)
        {
            _thunders.Move(factor);
            MoveParticles(factor);

            TickGathering(factor);

            if (!_slashStarted)
            {
                _slashWait += factor / 25f;
                if (IsReleasedImmediately || Caster.CurrentFrame >= SlashTriggerFrame || _slashWait >= SlashTriggerTimeout)
                    StartSlash();
            }
            else
            {
                TickSlash(factor);
            }
        }

        private void TickGathering(float factor)
        {
            if (_gatheringLife <= 0f)
                return;

            // MoveEffect BITMAP_GATHERING sub 1: follow the left hand (bone 33) + 10 Z.
            _handPosition = GetCasterBonePosition(PlayerObject.LeftHandBoneIndex) + Vector3.UnitZ * 10f;
            _shinyScale = (MuGame.Random.Next(8) + 8) * 0.2f;
            _shinyRotation = MuGame.Random.Next(360);

            for (int j = 0; j < 3; j++)
            {
                if (!FpsCheck(1, factor))
                    break;

                Vector3 angle = new(MuGame.Random.Next(360), 0f, MuGame.Random.Next(360));
                Vector3 position = _handPosition + SourceJointMath.Rotate(new Vector3(0f, 120f, 0f), angle);

                if (((int)_gatheringLife % 2) == 0)
                {
                    // CreateJointFpsChecked(BITMAP_JOINT_THUNDER, Position, o->Position, Angle, 3, NULL, 10, PKKey 10, SkillIndex 10)
                    if (FpsCheck(1, factor))
                        _thunders.Spawn(position, _handPosition, angle, 3, 10f, 10f, 10f, 10, ThunderLight);
                }
                else if (FpsCheck(1, factor))
                {
                    // BITMAP_SPARK+1 sub 2: LifeTime 15, Velocity (target - position) / 10.
                    SpawnParticle(_sparks, position, (_handPosition - position) / 10f, (MuGame.Random.Next(50) + 10) / 100f, Vector3.One, 15f);
                }
            }

            _gatheringLife -= factor;
        }

        private void StartSlash()
        {
            _slashStarted = true;
            PlayUnmappedSound(_skillId, SwingSoundPath);

            // BITMAP_SWORD_FORCE: HeadAngle (0, 0, yaw + 45); joint at o->Position + 100 Z.
            // BITMAP_JOINT_FORCE sub 0: TargetPosition = Position, Angle[2] += 30,
            // Direction = Angle, start point = Position + Rotate((0,-180,0), Angle).
            float yaw = CasterYawDegrees + 45f + 30f;
            _slashCenter = CasterPosition + Vector3.UnitZ * 100f;
            _slashAngle = new Vector3(0f, 0f, yaw);
            _slashDirection = yaw;
            _slashPosition = _slashCenter + SourceJointMath.Rotate(new Vector3(0f, -180f, 0f), _slashAngle);
            _slashLife = 20f;
            _slashLight = 20f / 30f;
            _slashPushes = 0;
            // CreateJoint epilogue (ZzzEffectJoint.cpp:2711): MaxTails / FPS_ANIMATION_FACTOR, so the 11 * factor
            // step per tail still sweeps 18 * 11 degrees at any frame rate.
            _trail.Reset(SourceJointMath.ScaledMaxTails(ForceTails));
        }

        private void TickSlash(float factor)
        {
            if (_slashLife <= 0f)
                return;

            for (int j = 0; j < 8 && _slashPushes < _trail.MaxTails; j++)
            {
                Vector3 previous = _slashPosition;
                _slashPosition = _slashCenter + SourceJointMath.Rotate(new Vector3(0f, -ForceRadius, 0f), new Vector3(0f, 0f, _slashDirection));
                _trail.Push(_slashPosition, _slashAngle, ForceScale);
                _slashPushes++;
                _slashDirection -= 11f * factor;

                // BITMAP_FIRE sub 0 (rand_fps_check(2)), light = the joint light.
                if (FpsCheck(2, factor))
                {
                    var velocity = SourceJointMath.Rotate(new Vector3(0f, -(MuGame.Random.Next(16) + 32) * 0.1f, 0f), _slashAngle);
                    SpawnParticle(_fires, _slashPosition, velocity, (MuGame.Random.Next(64) + 128) * 0.01f, new Vector3(_slashLight), 24f);
                }

                // 2 x JOINT_THUNDER sub 3 from the previous to the new tail (PKKey 5 -> LifeTime 5, SkillIndex 10).
                if (FpsCheck(1, factor))
                    _thunders.Spawn(previous, _slashPosition, _slashAngle, 3, MuGame.Random.Next(10) + 5f, 5f, 10f, 10, ThunderLight);
                if (FpsCheck(1, factor))
                    _thunders.Spawn(previous, _slashPosition, _slashAngle, 3, MuGame.Random.Next(8) + 4f, 5f, 10f, 10, ThunderLight);
            }

            _slashLife -= factor;
            _slashLight = _slashLife < 10f ? _slashLight / MathF.Pow(1.5f, factor) : _slashLife / 30f;
        }

        private static void SpawnParticle(Particle[] pool, Vector3 position, Vector3 velocity, float scale, Vector3 light, float lifeTime)
        {
            for (int i = 0; i < pool.Length; i++)
            {
                ref Particle p = ref pool[i];
                if (p.Live)
                    continue;
                p = new Particle { Live = true, Position = position, Velocity = velocity, Scale = scale, Light = light,
                    LifeTime = lifeTime, Rotation = MuGame.Random.Next(360) };
                return;
            }
        }

        private void MoveParticles(float factor)
        {
            for (int i = 0; i < _fires.Length; i++)
            {
                ref Particle p = ref _fires[i];
                if (!p.Live)
                    continue;
                p.LifeTime -= factor;
                if (p.LifeTime <= 0f) { p.Live = false; continue; }
                p.Position += p.Velocity * factor;
                p.Gravity += 0.004f * factor;
                p.Scale -= 0.04f * factor;
                p.Position.Z += p.Gravity * 10f * factor;
            }

            for (int i = 0; i < _sparks.Length; i++)
            {
                ref Particle p = ref _sparks[i];
                if (!p.Live)
                    continue;
                p.LifeTime -= factor;
                if (p.LifeTime <= 0f) { p.Live = false; continue; }
                if (p.LifeTime > 5f && _gatheringLife <= 0f)
                    p.LifeTime = 5f;
                p.Light = new Vector3(p.LifeTime / 5f);
                p.Scale += factor * 0.05f;
                p.Position += p.Velocity * factor;
            }
        }

        private bool AnyParticle()
        {
            for (int i = 0; i < _fires.Length; i++) if (_fires[i].Live) return true;
            for (int i = 0; i < _sparks.Length; i++) if (_sparks[i].Live) return true;
            return false;
        }

        protected override bool HasActiveContent() =>
            !_slashStarted || _slashLife > 0f || _gatheringLife > 0f || _thunders.HasLive || AnyParticle();

        protected override void OnDraw()
        {
            if (_slashStarted && _slashLife > 0f &&
                Batch.Begin(_forceTexture, Graphics.Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearWrap))
            {
                // RenderJoints BITMAP_JOINT_FORCE sub 0: only the Tails[0]/[1] plane.
                Batch.AddTrail(_trail, SourceQuadBatch.TrailFaces.Two, new Vector3(_slashLight),
                    SourceQuadBatch.TrailShade.JointForce, _trail.MaxTails - 1);
                Batch.End();
            }

            _thunders.Draw(Batch, _thunderTexture);

            if (_fireTexture != null && Batch.Begin(_fireTexture, Graphics.Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _fires.Length; i++)
                {
                    ref Particle p = ref _fires[i];
                    if (!p.Live)
                        continue;
                    int frame = (int)((23f - p.LifeTime) / 6f);
                    Batch.AddSpriteRegion(p.Position, _fireTexture.Width * p.Scale * 0.25f, _fireTexture.Height * p.Scale, p.Light, 0f,
                        frame % 4 * 0.25f, 0f, 0.25f, 1f);
                }
                Batch.End();
            }

            if (Batch.Begin(_sparkTexture, Graphics.Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _sparks.Length; i++)
                {
                    ref Particle p = ref _sparks[i];
                    if (p.Live)
                        Batch.AddSprite(p.Position, p.Scale, p.Light);
                }
                Batch.End();
            }

            if (_gatheringLife > 0f &&
                Batch.Begin(_shinyTexture, Graphics.Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                Batch.AddSprite(_handPosition, _shinyScale, Vector3.One, _shinyRotation);
                Batch.End();
            }
        }
    }
}
