using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Core.Utilities;
using Client.Main.Models;
using Client.Main.Objects.Effects;
using Client.Main.Objects.Player;
using Microsoft.Xna.Framework;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(6, "Lich")]
    public class Lich : MonsterObject
    {
        private WeaponObject _rightHandWeapon;
        public Lich()
        {
            RenderShadow = true;
            Scale = 0.85f; // Set according to C++ Setting_Monster
            _rightHandWeapon = new WeaponObject
            {
                LinkParentAnimation = false,
                ParentBoneLink = 41
            };
            Children.Add(_rightHandWeapon);
        }

        public override async Task Load()
        {
            // Model Loading Type: 4 -> File Number: 4 + 1 = 5
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster05.bmd");
            var item = ItemDatabase.GetItemDefinition(5, 2); // Serpent Staff
            if (item != null)
                _rightHandWeapon.Model = await BMDLoader.Instance.Prepare(item.TexturePath);
            await base.Load();
        }

        // Sound mapping based on C++ SetMonsterSound(MODEL_MONSTER01 + Type, 20, 21, 22, 23, 24);
        protected override void OnIdle()
        {
            base.OnIdle();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            string sound = MuGame.Random.Next(2) == 0 ? "Sound/mWizard1.wav" : "Sound/mWizard2.wav";
            SoundController.Instance.PlayBufferWithAttenuation(sound, Position, listenerPosition);
        }

        public override void OnPerformAttack(int attackType = 1)
        {
            base.OnPerformAttack(attackType);
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            string sound = MuGame.Random.Next(2) == 0 ? "Sound/mWizardAttack1.wav" : "Sound/mWizardAttack2.wav";
            SoundController.Instance.PlayBufferWithAttenuation(sound, Position, listenerPosition);

            if (attackType != 2 || World is not WalkableWorldControl world)
                return;

            ushort targetId = LastAttackTargetId;
            if (targetId == 0 || !world.TryGetWalkerById(targetId, out _))
                return;

            PerformSkillAttack(world, targetId);
        }

        protected virtual void PerformSkillAttack(WalkableWorldControl world, ushort targetId)
        {
            if (!world.TryGetWalkerById(targetId, out var target))
                return;

            var effect = new ScrollOfMeteoriteEffect(target.WorldPosition.Translation);
            world.Objects.Add(effect);
            _ = effect.Load();
        }

        public override void OnReceiveDamage()
        {
            base.OnReceiveDamage();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            string sound = MuGame.Random.Next(2) == 0 ? "Sound/mWizardAttack1.wav" : "Sound/mWizardAttack2.wav";
            SoundController.Instance.PlayBufferWithAttenuation(sound, Position, listenerPosition);
        }

        public override void OnDeathAnimationStart()
        {
            base.OnDeathAnimationStart();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mWizardDie.wav", Position, listenerPosition); // Index 4 -> Sound 24
        }
    }
}
