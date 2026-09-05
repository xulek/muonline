using Client.Main.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Client.Main.Objects.Player;
using Client.Main.Core.Utilities;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Models;
using Microsoft.Xna.Framework;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(64, "Orc Archer")]
    public class OrcArcher : MonsterObject
    {
        private WeaponObject _leftHandWeapon;
        public OrcArcher()
        {
            Scale = 1.2f;
            MoveSpeed = 250f;
            HiddenMesh = 1;
            _leftHandWeapon = new WeaponObject
            {
                LinkParentAnimation = false,
                ParentBoneLink = 39,
                ItemLevel = 3
            };
            Children.Add(_leftHandWeapon);
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster47.bmd");
            var item = ItemDatabase.GetItemDefinition(4, 3); // Battle Bow
            if (item != null)
                _leftHandWeapon.Model = await BMDLoader.Instance.Prepare(item.TexturePath);

            await base.Load();
            SetActionSpeed(MonsterActionType.Stop1, 0.25f);
            SetActionSpeed(MonsterActionType.Stop2, 0.20f);
            SetActionSpeed(MonsterActionType.Walk, 0.34f);
            SetActionSpeed(MonsterActionType.Attack1, 0.33f);
            SetActionSpeed(MonsterActionType.Attack2, 0.33f);
            SetActionSpeed(MonsterActionType.Shock, 0.50f);
            SetActionSpeed(MonsterActionType.Die, 0.55f);
        }

        public override void OnPerformAttack(int attackType = 1)
        {
            base.OnPerformAttack(attackType);
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mOrcArcherAttack1.wav", Position, listenerPosition);

            // SourceMain5.2 AttackEffect: MODEL_ORCARCHER fires CreateArrows on attack.
            if (World is WalkableWorldControl arrowWorld && LastAttackTargetId != 0)
            {
                var arrow = new Effects.MonsterArrowProjectileEffect(this, 39, LastAttackTargetId);
                arrowWorld.Objects.Add(arrow);
                _ = arrow.Load();
            }
        }

        public override void OnReceiveDamage()
        {
            base.OnReceiveDamage();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mOrcArcherAttack1.wav", Position, listenerPosition);
        }

        public override void OnDeathAnimationStart()
        {
            base.OnDeathAnimationStart();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mBullDie.wav", Position, listenerPosition);
        }
    }
}
