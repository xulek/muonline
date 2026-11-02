using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Models;
using Client.Main.Core.Utilities;
using Client.Main.Objects.Effects;
using Microsoft.Xna.Framework;
using System.Threading.Tasks;
using Client.Main.Objects.Player;

namespace Client.Main.Objects.Monsters
{
    // Renamed to avoid conflict with Player class
    [NpcInfo(10, "Dark Knight")]
    public class DarkKnight : MonsterObject
    {
        private WeaponObject _rightHandWeapon;
        private readonly SourceMonsterSmokeEffect _smokeEffect;

        public DarkKnight()
        {
            RenderShadow = true;
            Scale = 0.8f; // Set according to C++ Setting_Monster
            MoveSpeed = 250f; // SourceMain5.2: default monster MoveSpeed (10 * 25 FPS)
            _rightHandWeapon = new WeaponObject
            {
                LinkParentAnimation = false,
                ParentBoneLink = 26
            };
            Children.Add(_rightHandWeapon);

            _smokeEffect = new SourceMonsterSmokeEffect();
            Children.Add(_smokeEffect);
        }

        public override async Task Load()
        {
            // Model Loading Type: 3 -> File Number: 3 + 1 = 4
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster04.bmd");
            var item = ItemDatabase.GetItemDefinition(0, 13); // Double Blade
            if (item != null)
            {
                _rightHandWeapon.ItemLevel = 1;
                _rightHandWeapon.Model = await BMDLoader.Instance.Prepare(item.TexturePath);
            }
            await base.Load();

            // SourceMain5.2 ZzzOpenData.cpp: model type 3 uses the 1.2x multiplier.
            SetActionSpeed(MonsterActionType.Stop1, 0.25f * 1.2f);
            SetActionSpeed(MonsterActionType.Stop2, 0.20f * 1.2f);
            SetActionSpeed(MonsterActionType.Walk, 0.34f * 1.2f);
            SetActionSpeed(MonsterActionType.Attack1, 0.33f * 1.2f);
            SetActionSpeed(MonsterActionType.Attack2, 0.33f * 1.2f);
            SetActionSpeed(MonsterActionType.Shock, 0.50f * 1.2f);
            SetActionSpeed(MonsterActionType.Die, 0.55f);
        }

        // Sound mapping based on C++ SetMonsterSound(MODEL_MONSTER01 + Type, 15, 16, 17, 18, 19);
        protected override void OnIdle()
        {
            base.OnIdle();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            string sound = MuGame.Random.Next(2) == 0
                ? "Sound/mDarkKnight1.wav"
                : "Sound/mDarkKnight2.wav";
            SoundController.Instance.PlayBufferWithAttenuation(sound, Position, listenerPosition);
        }

        public override void OnPerformAttack(int attackType = 1)
        {
            base.OnPerformAttack(attackType);
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            string sound = MuGame.Random.Next(2) == 0
                ? "Sound/mDarkKnightAttack1.wav"
                : "Sound/mDarkKnightAttack2.wav";
            SoundController.Instance.PlayBufferWithAttenuation(sound, Position, listenerPosition);
        }

        public override void OnReceiveDamage()
        {
            base.OnReceiveDamage();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            string sound = MuGame.Random.Next(2) == 0
                ? "Sound/mDarkKnightAttack1.wav"
                : "Sound/mDarkKnightAttack2.wav";
            SoundController.Instance.PlayBufferWithAttenuation(sound, Position, listenerPosition);
        }

        public override void OnDeathAnimationStart()
        {
            base.OnDeathAnimationStart();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mDarkKnightDie.wav", Position, listenerPosition);
        }
    }
}
