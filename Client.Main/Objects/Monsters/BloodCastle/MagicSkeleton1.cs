using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Core.Utilities;
using Client.Main.Models;
using Client.Main.Objects.Player;
using Microsoft.Xna.Framework;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters.BloodCastle
{
    [NpcInfo(89, "Magic Skeleton")]
    public class MagicSkeleton1 : MonsterObject
    {
        private readonly WeaponObject _staff;
        private static readonly int[] StaffBones = { 33 };

        public MagicSkeleton1()
        {
            Scale = 1.2f;
            MoveSpeed = 250f;
            _staff = new WeaponObject { LinkParentAnimation = false, ParentBoneLink = 33, ItemLevel = 11 };
            Children.Add(_staff);
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster63.bmd");
            var staff = ItemDatabase.GetItemDefinition(5, 0); // Staff
            if (staff != null)
                _staff.Model = await BMDLoader.Instance.Prepare(staff.TexturePath);
            await base.Load();
            SetActionSpeed(MonsterActionType.Stop1, 0.25f);
            SetActionSpeed(MonsterActionType.Stop2, 0.20f);
            SetActionSpeed(MonsterActionType.Walk, 0.34f);
            SetActionSpeed(MonsterActionType.Attack1, 0.33f);
            SetActionSpeed(MonsterActionType.Attack2, 0.33f);
            SetActionSpeed(MonsterActionType.Shock, 0.50f);
            SetActionSpeed(MonsterActionType.Die, 0.55f);
        }

        protected override void OnIdle()
        {
            base.OnIdle();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mMagicSkull.wav", Position, listenerPosition);
        }

        protected override void OnAttackProgress(in AttackProgress progress)
        {
            if (!progress.CrossedTick(14) || World == null ||
                !TryGetAttackTargetPosition(out Vector3 target))
                return;

            SpawnMagicAttackEffect(StaffBones, progress.Action);
            Matrix[] bones = GetBoneTransforms();
            if (bones == null || bones.Length <= 33)
                return;

            var meteor = new Effects.MonsterArrowProjectileEffect((bones[33] * WorldPosition).Translation,
                target + Vector3.UnitZ * 55f, "Skill/Piercing.bmd", 0.75f);
            World.Objects.Add(meteor);
            _ = meteor.Load();
        }

        public override void OnDeathAnimationStart()
        {
            base.OnDeathAnimationStart();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mMagicSkull.wav", Position, listenerPosition);
        }
    }
}
