using Client.Main.Content;
using Client.Main.Core.Utilities;
using Client.Main.Objects.Player;
using Microsoft.Xna.Framework;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(54, "Golden Soldier")]
    public class GoldenSoldier : MonsterObject
    {
        private readonly WeaponObject _leftHandWeapon;

        public GoldenSoldier()
        {
            // SourceMain5.2 CreateMonster(MONSTER_GOLDEN_SOLDIER): MODEL_SOLDIER,
            // Scale = 1.1f, Weapon[1] = Aquagold Crossbow (LinkBone 33, see
            // SettingMonsterLinkBone MODEL_SOLDIER case). RenderCharacter: METAL|
            // RENDER_BRIGHT pass with BITMAP_SHINY+1 (= Effect/Shiny02.jpg),
            // tinted (1.0, 0.5, 0.0).
            Scale = 1.1f;
            BrightOverlay = 1f;
            BrightOverlayTexturePath = "Effect/Shiny02.jpg";
            BrightOverlayTint = new Vector3(1f, 0.5f, 0f);

            _leftHandWeapon = new WeaponObject
            {
                LinkParentAnimation = false,
                ParentBoneLink = 33
            };
            Children.Add(_leftHandWeapon);
        }

        public override async Task Load()
        {
            var weapon = ItemDatabase.GetItemDefinition(4, 14); // Aquagold Crossbow
            if (weapon != null)
                _leftHandWeapon.Model = await BMDLoader.Instance.Prepare(weapon.TexturePath);

            Model = await BMDLoader.Instance.Prepare($"Monster/Monster41.bmd");
            await base.Load();
        }
    }
}
