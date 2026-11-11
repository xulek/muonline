using Client.Main.Content;
using Client.Main.Core.Utilities;
using Client.Main.Models;
using Client.Main.Objects.Player;
using Client.Main.Networking;
using System.Threading.Tasks;

namespace Client.Main.Objects.NPCS
{
    [NpcInfo(229, "Marlon")]
    public class Marlon : NPCObject
    {
        private readonly WeaponObject _weapon = new() { LinkParentAnimation = false, ParentBoneLink = 33 };

        private static void ConfigureSetPart(ModelObject part, int group)
        {
            part.ItemLevel = 7;
            part.MaterialItemGroup = group;
            part.MaterialItemIndex = 9;
        }

        public Marlon()
        {
            Children.Add(_weapon);
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare("Player/Player.bmd");
            await SetBodyPartsAsync("Player/", "HelmMale", "ArmorMale", "PantMale", "GloveMale", "BootMale", 10);
            
            // ZzzCharacter.cpp (MONSTER_MARLON): Plate set +7 and a +8 Berdysh.
            // Plate set = item index 9; the group/index drive the per-item material colours.
            ConfigureSetPart(Helm, 7);
            ConfigureSetPart(Armor, 8);
            ConfigureSetPart(Pants, 9);
            ConfigureSetPart(Gloves, 10);
            ConfigureSetPart(Boots, 11);

            var berdysh = ItemDatabase.GetItemDefinition(3, 7);
            if (berdysh != null)
            {
                _weapon.Model = await BMDLoader.Instance.Prepare(berdysh.TexturePath);
                _weapon.ItemLevel = 8;
                _weapon.MaterialItemGroup = 3;
                _weapon.MaterialItemIndex = 7;
            }
            await base.Load();
            AnimationSpeed = 25f;
            CurrentAction = (int)PlayerAction.PlayerStopMale;
        }
        protected override void HandleClick()
        {
            var svc = MuGame.Network?.GetCharacterService();
            if (svc != null)
            {
                _ = svc.SendTalkToNpcRequestAsync(NetworkId);
            }
        }
    }
}
