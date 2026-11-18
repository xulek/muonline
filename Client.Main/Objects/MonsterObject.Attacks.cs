using Client.Main.Controls;
using Client.Main.Controllers;
using Client.Main.Models;
using Microsoft.Xna.Framework;

namespace Client.Main.Objects
{
    public abstract partial class MonsterObject
    {
        // OpenMU sends 0x18, optionally followed by 0x19 for the same attack and target.
        // Neither packet carries an attack sequence number. Pair only complementary packets.
        private const double AttackCompanionSeconds = 0.2;
        private const double AttackTicksPerSecond = 25.0;
        protected const ushort IceAttackSkill = 7;
        protected const ushort EnergyBallAttackSkill = 17;
        protected const ushort BossAttackSkill = 50;

        private enum AttackSource : byte
        {
            None,
            Animation,
            TargetedSkill,
            Paired,
            AreaSkill,
            MonsterSkill,
        }

        private AttackSource _attackSource;
        private MonsterActionType _attackAction;
        private ushort _attackSkill;
        private ushort _observedAttackSkill;
        private int _nextAttackVariation;
        private double _attackStartedAt;
        private double _attackTick;
        private double _attackFrame;
        private Vector3? _attackPoint;
        protected virtual bool VaryBasicAttack => true;

        protected readonly record struct AttackProgress(
            MonsterActionType Action, ushort SkillId, bool SkillStarted,
            double PreviousTick, double Tick, double PreviousFrame, double Frame)
        {
            public bool CrossedTick(int tick)
            {
                double threshold = tick - 1e-8;
                return PreviousTick < threshold && Tick >= threshold;
            }

            public bool CrossedFrame(double frame) => PreviousFrame < frame && Frame >= frame;
        }

        internal bool ApplyNormalAttack(ushort targetId, double now, Direction direction)
        {
            targetId &= 0x7FFF;
            if (_attackSource == AttackSource.TargetedSkill && MatchesAttackCompanion(targetId, now))
            {
                _attackSource = AttackSource.Paired;
                return true;
            }

            return StartBasicAttack(targetId, 0, now, AttackSource.Animation, direction);
        }

        internal bool ApplyTargetedAttack(ushort skillId, ushort targetId, double now, WalkerObject target)
        {
            targetId &= 0x7FFF;
            if (skillId == 0)
                return false;

            if (_attackSource == AttackSource.Animation && MatchesAttackCompanion(targetId, now))
            {
                _attackSkill = skillId;
                _attackSource = AttackSource.Paired;
                return true;
            }

            // ReceiveMagic starts the monster's attack for Poison, Flame, Ice, Power Wave and Energy Ball.
            return skillId is 1 or 5 or IceAttackSkill or 11 or EnergyBallAttackSkill or BossAttackSkill &&
                   StartBasicAttack(targetId, skillId, now, AttackSource.TargetedSkill, target: target);
        }

        internal bool ApplyMonsterSkill(MonsterActionType action, ushort skillId, ushort targetId, double now, WalkerObject target)
        {
            return StartAttack(action, (ushort)(targetId & 0x7FFF), skillId, now, AttackSource.MonsterSkill, target: target);
        }

        internal bool ApplyAreaAttack(ushort skillId, Vector3 point, double now, float yawDegrees) =>
            skillId != 0 && StartBasicAttack(0, skillId, now, AttackSource.AreaSkill, point: point, yawDegrees: yawDegrees);

        private bool MatchesAttackCompanion(ushort targetId, double now) =>
            targetId != 0 && targetId == LastAttackTargetId && !IsDead && Status != GameControlStatus.Disposed &&
            CurrentAction == (int)_attackAction && now >= _attackStartedAt && now - _attackStartedAt <= AttackCompanionSeconds;

        private bool StartBasicAttack(ushort targetId, ushort skillId, double now, AttackSource source,
            Direction? direction = null, WalkerObject target = null, Vector3? point = null, float? yawDegrees = null)
        {
            var action = _nextAttackVariation == 0 || !VaryBasicAttack ? MonsterActionType.Attack1 : MonsterActionType.Attack2;
            if (!CanUseAttackAction(action))
                action = action == MonsterActionType.Attack1 ? MonsterActionType.Attack2 : MonsterActionType.Attack1;

            if (!StartAttack(action, targetId, skillId, now, source, direction, target, point, yawDegrees))
                return false;

            _nextAttackVariation = (_nextAttackVariation + 1) % 3;
            return true;
        }

        private bool StartAttack(MonsterActionType action, ushort targetId, ushort skillId, double now,
            AttackSource source, Direction? direction = null, WalkerObject target = null,
            Vector3? point = null, float? yawDegrees = null)
        {
            if (IsDead || Status == GameControlStatus.Disposed || !CanUseAttackAction(action) ||
                !TryPlayAction((ushort)action, fromServer: true))
                return false;

            if (direction.HasValue)
            {
                Direction = direction.Value;
                SnapFacingToDirection();
            }
            else if (target?.Visible == true)
            {
                FaceTowardsObject(target, immediate: true);
            }
            else if (yawDegrees.HasValue)
            {
                SetFacingYawDegrees(yawDegrees.Value);
            }

            LastAttackTargetId = targetId;
            _attackPoint = point;
            if (action == MonsterActionType.Appear)
            {
                _attackSource = AttackSource.None;
                OnAppearAnimationStart();
                return true;
            }

            _attackAction = action;
            _attackSource = source;
            _attackSkill = skillId;
            _observedAttackSkill = 0;
            _attackStartedAt = now;
            _attackTick = 0;
            _attackFrame = -1;
            OnPerformAttack(action switch
            {
                MonsterActionType.Attack1 => 1,
                MonsterActionType.Attack3 => 3,
                MonsterActionType.Attack4 => 4,
                _ => 2,
            });
            return true;
        }

        private bool CanUseAttackAction(MonsterActionType action) =>
            Model == null || IsValidAction((int)action) && Model.Actions[(int)action].NumAnimationKeys > 0;

        private void UpdateAttackAfterAnimation(GameTime gameTime, bool attackWasPlaying)
        {
            if (!attackWasPlaying || !Visible || World == null || IsDead)
            {
                _attackSource = AttackSource.None;
                return;
            }

            // The animation controller may have returned to idle on this step. BMD still staged
            // the last attack pose, so the final step can release markers crossed on that pose.
            bool attackFinished = CurrentAction != (int)_attackAction;
            double frame = _priorActionIndex == (int)_attackAction ? AnimationFramePosition : 0;
            AdvanceAttack(gameTime.TotalGameTime.TotalSeconds, frame, attackFinished);

            if (attackFinished)
                _attackSource = AttackSource.None;
        }

        private void AdvanceAttack(double now, double frame, bool attackFinished)
        {
            if (_attackSource == AttackSource.None || IsDead || Status == GameControlStatus.Disposed ||
                (!attackFinished && CurrentAction != (int)_attackAction))
            {
                _attackSource = AttackSource.None;
                return;
            }

            double tick = System.Math.Max(_attackTick, 1 + (now - _attackStartedAt) * AttackTicksPerSecond);
            if (attackFinished && _animationController.TryGetActionDurationSeconds((ushort)_attackAction, out float duration))
                tick = System.Math.Clamp(tick, _attackTick, System.Math.Max(_attackTick, 1 + duration * AttackTicksPerSecond));

            frame = System.Math.Max(_attackFrame, frame);
            bool skillStarted = _attackSkill != _observedAttackSkill;
            var progress = new AttackProgress(_attackAction, _attackSkill, skillStarted, _attackTick, tick, _attackFrame, frame);
            _observedAttackSkill = _attackSkill;
            _attackTick = tick;
            _attackFrame = frame;
            OnAttackProgress(in progress);
        }

        protected virtual void OnAttackProgress(in AttackProgress progress) { }

        internal void PlayAttackSound(string path, Vector3? origin = null)
        {
            if (World is WalkableWorldControl { Walker: { } listener } world)
                SoundController.Instance.PlayCombatSound(path, world, origin ?? Position, listener.Position);
        }

        protected void CancelAttack()
        {
            _attackSource = AttackSource.None;
            _attackPoint = null;
        }

        protected override void OnWorldChanged(WorldControl newWorld, WorldControl prevWorld)
        {
            CancelAttack();
            base.OnWorldChanged(newWorld, prevWorld);
        }
    }
}
