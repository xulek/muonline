using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Core.Utilities;
using Client.Main.Models;
using Client.Main.Objects.Player;
using Microsoft.Xna.Framework;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(25, "Ice Queen")]
    public class IceQueen : MonsterObject
    {
        private readonly WeaponObject _rightHandWeapon;

        public IceQueen()
        {
            RenderShadow = false; // SourceMain5.2 RenderCharacter: MONSTER_ICE_QUEEN excluded from blob shadow pass
            BlendMesh = 2;
            BlendMeshLight = 1f;
            LightEnabled = false;
            Scale = 1.1f; // Set according to C++ Setting_Monster
            MoveSpeed = 250f; // SourceMain5.2: default monster MoveSpeed (10 * 25 FPS)
            Blood = false; // SourceMain5.2 CreateBlood: MODEL_ICE_QUEEN exempt from death blood decals
            _rightHandWeapon = new WeaponObject
            {
                LinkParentAnimation = false,
                ParentBoneLink = 26
            };
            Children.Add(_rightHandWeapon);
        }

        public override async Task Load()
        {
            // Model Loading Type: 18 -> File Number: 18 + 1 = 19
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster19.bmd");
            var staff = ItemDatabase.GetItemDefinition(5, 1); // Angelic Staff
            if (staff != null)
                _rightHandWeapon.Model = await BMDLoader.Instance.Prepare(staff.TexturePath);
            await base.Load();
            SetActionSpeed(MonsterActionType.Stop1, 0.25f);
            SetActionSpeed(MonsterActionType.Stop2, 0.20f);
            SetActionSpeed(MonsterActionType.Walk, 0.34f);
            SetActionSpeed(MonsterActionType.Attack1, 0.33f);
            SetActionSpeed(MonsterActionType.Attack2, 0.33f);
            SetActionSpeed(MonsterActionType.Shock, 0.50f);
            SetActionSpeed(MonsterActionType.Die, 0.55f);
            // C++: Models[MODEL_MONSTER01+Type].BoneHead = 16;
        }

        // Sound mapping based on C++ SetMonsterSound(MODEL_MONSTER01 + Type, 60, 61, 62, 63, 64)
        protected override void OnIdle()
        {
            base.OnIdle();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            string sound = MuGame.Random.Next(2) == 0
                ? "Sound/mIceQueen1.wav"
                : "Sound/mIceQueen2.wav";
            SoundController.Instance.PlayBufferWithAttenuation(sound, Position, listenerPosition);
        }

        public override void OnPerformAttack(int attackType = 1)
        {
            base.OnPerformAttack(attackType);
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            string sound = MuGame.Random.Next(2) == 0
                ? "Sound/mIceQueenAttack1.wav"
                : "Sound/mIceQueenAttack2.wav";
            SoundController.Instance.PlayBufferWithAttenuation(sound, Position, listenerPosition);
        }

        public override void OnReceiveDamage()
        {
            base.OnReceiveDamage();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            string sound = MuGame.Random.Next(2) == 0
                ? "Sound/mIceQueenAttack1.wav"
                : "Sound/mIceQueenAttack2.wav";
            SoundController.Instance.PlayBufferWithAttenuation(sound, Position, listenerPosition);
        }

        public override void OnDeathAnimationStart()
        {
            base.OnDeathAnimationStart();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mIceQueenDie.wav", Position, listenerPosition);
        }
    }
}
