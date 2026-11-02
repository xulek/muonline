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
    [NpcInfo(7, "Giant")]
    public class Giant : MonsterObject
    {
        private WeaponObject _rightHandWeapon;
        private WeaponObject _leftHandWeapon;
        private readonly GiantDeathSandSmokeEffect _deathSmokeEffect;

        public Giant()
        {
            RenderShadow = true;
            AnimationSpeed = 12.5f; // SetActionSpeed doubles the source speed; 2 * 12.5 matches 25 FPS.
            Scale = 1.6f;
            MoveSpeed = 250f; // SourceMain5.2: default monster MoveSpeed (10 * 25 FPS)
            _rightHandWeapon = new WeaponObject
            {
                LinkParentAnimation = false,
                ParentBoneLink = 41
            };
            _leftHandWeapon = new WeaponObject
            {
                LinkParentAnimation = false,
                ParentBoneLink = 32
            };
            Children.Add(_rightHandWeapon);
            Children.Add(_leftHandWeapon);

            _deathSmokeEffect = new GiantDeathSandSmokeEffect();
            Children.Add(_deathSmokeEffect);
        }

        public override async Task Load()
        {
            // Model Loading Type: 5 -> File Number: 5 + 1 = 6
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster06.bmd");
            var item = ItemDatabase.GetItemDefinition(1, 2); // Double Axe
            if (item != null)
            {
                _rightHandWeapon.Model = await BMDLoader.Instance.Prepare(item.TexturePath);
                _leftHandWeapon.Model = await BMDLoader.Instance.Prepare(item.TexturePath);
            }
            await base.Load();

            // SourceMain5.2 ZzzOpenData.cpp: base monster action speeds.
            SetActionSpeed(MonsterActionType.Stop1, 0.25f * 0.7f);
            SetActionSpeed(MonsterActionType.Stop2, 0.20f * 0.7f);
            SetActionSpeed(MonsterActionType.Walk, 0.34f * 0.7f);
            SetActionSpeed(MonsterActionType.Attack1, 0.33f * 0.7f);
            SetActionSpeed(MonsterActionType.Attack2, 0.33f * 0.7f);
            SetActionSpeed(MonsterActionType.Shock, 0.50f * 0.7f);
            SetActionSpeed(MonsterActionType.Die, 0.55f);
        }

        // --- Sound handling methods (mapping from C++) ---

        // Sound mapping based on C++ SetMonsterSound(MODEL_MONSTER01 + Type, 25, 26, 27, 28, 29);
        // Sound index 0 -> Sound ID 25 (mGiant1.wav)
        // Sound index 1 -> Sound ID 26 (mGiant2.wav)
        // Sound index 2 -> Sound ID 27 (mGiantAttack1.wav)
        // Sound index 3 -> Sound ID 28 (mGiantAttack2.wav)
        // Sound index 4 -> Sound ID 29 (mGiantDie.wav)

        protected override void OnIdle()
        {
            base.OnIdle();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            string sound = MuGame.Random.Next(2) == 0
                ? "Sound/mGiant1.wav"
                : "Sound/mGiant2.wav";
            SoundController.Instance.PlayBufferWithAttenuation(sound, Position, listenerPosition);
        }

        public override void OnPerformAttack(int attackType = 1)
        {
            base.OnPerformAttack(attackType);
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            string sound = MuGame.Random.Next(2) == 0
                ? "Sound/mGiantAttack1.wav"
                : "Sound/mGiantAttack2.wav";
            SoundController.Instance.PlayBufferWithAttenuation(sound, Position, listenerPosition);
        }

        public override void OnReceiveDamage()
        {
            base.OnReceiveDamage();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            string sound = MuGame.Random.Next(2) == 0 ? "Sound/mGiantAttack1.wav" : "Sound/mGiantAttack2.wav";
            SoundController.Instance.PlayBufferWithAttenuation(sound, Position, listenerPosition);
        }

        public override void OnDeathAnimationStart()
        {
            base.OnDeathAnimationStart();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mGiantDie.wav", Position, listenerPosition);
        }
    }
}
