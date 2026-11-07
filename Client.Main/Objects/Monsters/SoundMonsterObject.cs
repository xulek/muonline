using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Models;
using Microsoft.Xna.Framework;

namespace Client.Main.Objects.Monsters
{
    /// <summary>
    /// Monster base for map-specific sounds selected by the original GM event code
    /// (movement, attack variants and death).
    /// </summary>
    public abstract class SoundMonsterObject : MonsterObject
    {
        protected virtual string MoveSoundPath => null;
        protected virtual string AttackSoundPath => null;
        protected virtual string DamageSoundPath => null;
        protected virtual string DeathSoundPath => null;
        protected virtual bool PlayMoveSoundOnIdle => false;
        protected virtual bool UseImperialFootsteps => false;
        protected virtual string ImperialFootstepFrame1 => "Sound/w69w70w71w72/Jelint_move02.wav";
        protected virtual string ImperialFootstepFrame7 => "Sound/w69w70w71w72/Jelint_move01.wav";
        private double _previousWalkFrame = -1;

        protected virtual string GetMoveSoundPath() => MoveSoundPath;

        protected virtual string GetAttackSoundPath(int attackType) => AttackSoundPath;

        protected virtual string GetDamageSoundPath() => DamageSoundPath;

        protected void PlayMonsterSound(string soundPath)
        {
            if (string.IsNullOrEmpty(soundPath) || World is not WalkableWorldControl world || world.Walker == null)
                return;

            Vector3 listenerPosition = world.Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation(soundPath, Position, listenerPosition);
        }

        protected override void OnStartWalk()
        {
            base.OnStartWalk();
            if (!UseImperialFootsteps)
                PlayMonsterSound(GetMoveSoundPath());
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
            if (!UseImperialFootsteps || !IsMoving || CurrentAction != (int)MonsterActionType.Walk ||
                World is not WalkableWorldControl world || world.Walker == null ||
                Model?.Actions == null || CurrentAction >= Model.Actions.Length || Model.Actions[CurrentAction] == null)
            {
                _previousWalkFrame = -1;
                return;
            }

            var action = Model.Actions[CurrentAction];
            int totalFrames = System.Math.Max(action.LockPositions ? action.NumAnimationKeys - 1 : action.NumAnimationKeys, 1);
            double frame = GetAnimationTime() % totalFrames;
            bool crossed1 = CrossedWalkFrame(_previousWalkFrame, frame, 1);
            bool crossed7 = CrossedWalkFrame(_previousWalkFrame, frame, 7);
            _previousWalkFrame = frame;

            Vector2 delta = new(Position.X - world.Walker.Position.X, Position.Y - world.Walker.Position.Y);
            if (delta.LengthSquared() > 500f * 500f)
                return;
            if (crossed1)
                SoundController.Instance.PlayBuffer(ImperialFootstepFrame1);
            if (crossed7)
                SoundController.Instance.PlayBuffer(ImperialFootstepFrame7);
        }

        private static bool CrossedWalkFrame(double previous, double current, double trigger)
        {
            if (previous < 0)
                return current >= trigger && current < trigger + 1;
            if (current < previous)
                return previous < trigger || current >= trigger;
            return previous < trigger && current >= trigger;
        }

        protected override void OnIdle()
        {
            base.OnIdle();
            if (PlayMoveSoundOnIdle)
                PlayMonsterSound(GetMoveSoundPath());
        }

        public override void OnPerformAttack(int attackType = 1)
        {
            base.OnPerformAttack(attackType);
            PlayMonsterSound(GetAttackSoundPath(attackType));
        }

        public override void OnReceiveDamage()
        {
            base.OnReceiveDamage();
            PlayMonsterSound(GetDamageSoundPath());
        }

        public override void OnDeathAnimationStart()
        {
            base.OnDeathAnimationStart();
            PlayMonsterSound(DeathSoundPath);
        }
    }
}
