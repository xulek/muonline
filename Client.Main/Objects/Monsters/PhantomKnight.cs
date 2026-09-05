using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Objects.Player;
using Client.Main.Core.Utilities;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Client.Main.Models;

using Joints = Client.Main.Objects.Effects.Joints;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(72, "Phantom Knight")]
    public class PhantomKnight : MonsterObject
    {
        private WeaponObject _rightHandWeapon;
        public PhantomKnight()
        {
            Scale = 1.45f;
            MoveSpeed = 250f;
            _rightHandWeapon = new WeaponObject
            {
                LinkParentAnimation = false,
                ParentBoneLink = 30,
                ItemLevel = 5
            };
            Children.Add(_rightHandWeapon);
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster54.bmd");
            var item = ItemDatabase.GetItemDefinition(0, 17); // Dark Breaker
            if (item != null)
                _rightHandWeapon.Model = await BMDLoader.Instance.Prepare(item.TexturePath);

            await base.Load();
            SetActionSpeed(MonsterActionType.Stop1, 0.25f);
            SetActionSpeed(MonsterActionType.Stop2, 0.20f);
            SetActionSpeed(MonsterActionType.Walk, 0.34f);
            SetActionSpeed(MonsterActionType.Attack1, 0.33f);
            SetActionSpeed(MonsterActionType.Attack2, 0.33f);
            SetActionSpeed(MonsterActionType.Shock, 0.50f);
            SetActionSpeed(MonsterActionType.Die, 0.22f);
        }

        // Sound mapping based on C++ SetMonsterSound(MODEL_MONSTER01 + Type, 168, 168, 169, 169, 170);
        protected override void OnIdle()
        {
            base.OnIdle();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mPhantom1.wav", Position, listenerPosition); // Sound 168
        }

        public override void OnPerformAttack(int attackType = 1)
        {
            base.OnPerformAttack(attackType);
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mPhantomAttack1.wav", Position, listenerPosition); // Sound 169

            // SourceMain5.2 AttackEffect MONSTER_PHANTOM_KNIGHT boss branch
            // (Skill == AT_SKILL_BOSS, CheckAttackTime(14)): 36x CreateJoint
            // (JOINT_SPIRIT, sub1, scale 60, random angles) from +100 Z —
            // radial spirit streaks. No effect on normal attacks.
            if (attackType != 2)
                return;

            if (World != null && TryConsumeAttackEffectWindow())
            {
                Vector3 origin = Position + Vector3.UnitZ * 100f;
                for (int i = 0; i < 36; i++)
                {
                    var spirit = Joints.SourceJointEffect.SpiritBurst(
                        origin,
                        MuGame.Random.Next(360),
                        MuGame.Random.Next(360),
                        scale: 60f);
                    World.Objects.Add(spirit);
                    _ = spirit.Load();
                }
            }
        }

        public override void OnReceiveDamage()
        {
            base.OnReceiveDamage();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mPhantomAttack1.wav", Position, listenerPosition);
        }

        public override void OnDeathAnimationStart()
        {
            base.OnDeathAnimationStart();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mPhantomDie.wav", Position, listenerPosition); // Sound 170
        }
    }
}
