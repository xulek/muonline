using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Objects.Effects;
using Client.Main.Objects.Player;
using Client.Main.Core.Utilities;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{

    [NpcInfo(57, "IronWheel")]
    public class IronWheel : MonsterObject
    {
        private readonly WeaponObject _weapon;
        private GlowingEyesEffect _eyeGlow;

        public IronWheel()
        {
            Scale = 1.4f;
            MoveSpeed = 250f;

            _weapon = new WeaponObject
            {
                LinkParentAnimation = false,
                ParentBoneLink = 23
            };
            Children.Add(_weapon);

            _eyeGlow = new GlowingEyesEffect { LeftEyeBone = 9, RightEyeBone = 8, GlowColor = new Color(60, 150, 255) };
            Children.Add(_eyeGlow);
            Children.Add(new SourceMonsterSandSmokeEffect());
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster42.bmd");
            var weapon = ItemDatabase.GetItemDefinition(4, 14); // Aquagold Crossbow
            if (weapon != null)
                _weapon.Model = await BMDLoader.Instance.Prepare(weapon.TexturePath);

            await base.Load();
            SetActionSpeed(MonsterActionType.Stop1, 0.25f);
            SetActionSpeed(MonsterActionType.Stop2, 0.20f);
            SetActionSpeed(MonsterActionType.Walk, 0.18f);
            SetActionSpeed(MonsterActionType.Attack1, 0.33f);
            SetActionSpeed(MonsterActionType.Attack2, 0.33f);
            SetActionSpeed(MonsterActionType.Shock, 0.50f);
            SetActionSpeed(MonsterActionType.Die, 0.55f);
            // C++: Models[MODEL_MONSTER01+Type].BoneHead = 3;
        }

        // Sound mapping based on C++ SetMonsterSound(MODEL_MONSTER01 + Type, 143, 143, 144, 144, 144);
        protected override void OnIdle()
        {
            base.OnIdle();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/iron1.wav", Position, listenerPosition); // Sound 143
        }

        public override void OnPerformAttack(int attackType = 1)
        {
            base.OnPerformAttack(attackType);
            PlayAttackSound("Sound/iron_attack1.wav"); // Sound 144
        }

        protected override void OnAttackProgress(in AttackProgress progress)
        {
            if (progress.CrossedTick(1))
                ArrowProjectileSpawner.SpawnMonsterBomb(this, spread: true);
        }

        public override void OnReceiveDamage()
        {
            base.OnReceiveDamage();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/iron_attack1.wav", Position, listenerPosition);
        }

        public override void OnDeathAnimationStart()
        {
            base.OnDeathAnimationStart();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/iron_attack1.wav", Position, listenerPosition); // Sound 144
        }
    }
}
