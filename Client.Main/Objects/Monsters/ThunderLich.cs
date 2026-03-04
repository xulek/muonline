using Client.Main.Content;
using Client.Main.Controls;
using Client.Main.Core.Utilities;
using Client.Main.Models;
using Client.Main.Objects.Effects;
using Client.Main.Objects.Player;
using Microsoft.Xna.Framework;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(9, "Thunder Lich")]
    public class ThunderLich : Lich // Inherits from Lich
    {
        private WeaponObject _rightHandWeapon;
        public ThunderLich()
        {
            Scale = 1.1f; // Set according to C++ Setting_Monster
            _rightHandWeapon = new WeaponObject
            {
                LinkParentAnimation = false,
                ParentBoneLink = 41,
                ItemLevel = 1
            };
            Children.Add(_rightHandWeapon);
        }
        public override async Task Load()
        {
            var item = ItemDatabase.GetItemDefinition(5, 3); // Thunder Staff
            if (item != null)
                _rightHandWeapon.Model = await BMDLoader.Instance.Prepare(item.TexturePath);

            await base.Load();
        }
        // Load() and sound methods inherited
        // Sounds are inherited from Lich

        protected override void PerformSkillAttack(WalkableWorldControl world, ushort targetId)
        {
            if (targetId == 0 || !world.TryGetWalkerById(targetId, out _))
                return;

            int boneIndex = _rightHandWeapon?.ParentBoneLink ?? 41;
            Vector3 localOffset = new Vector3(0f, -130f, 0f);

            Vector3 SourceProvider()
            {
                var bones = GetBoneTransforms();
                if (bones != null && boneIndex >= 0 && boneIndex < bones.Length)
                {
                    Matrix boneWorld = bones[boneIndex] * WorldPosition;
                    return Vector3.Transform(localOffset, boneWorld);
                }

                return WorldPosition.Translation;
            }

            Vector3 TargetProvider()
            {
                if (world.TryGetWalkerById(targetId, out var target))
                    return target.WorldPosition.Translation + Vector3.UnitZ * 80f;

                return WorldPosition.Translation + Vector3.UnitZ * 80f;
            }

            var effect = new ScrollOfLightningEffect(SourceProvider, TargetProvider);
            world.Objects.Add(effect);
            _ = effect.Load();
        }
    }
}
