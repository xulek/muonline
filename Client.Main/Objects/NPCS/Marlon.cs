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

        public Marlon()
        {
            Children.Add(_weapon);
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare("Player/Player.bmd");
            await SetBodyPartsAsync("Player/", "HelmMale", "ArmorMale", "PantMale", "GloveMale", "BootMale", 10);
            
            // ZzzCharacter.cpp (MONSTER_MARLON): Plate set +7 and a +8 Berdysh.
            Helm.ItemLevel = 7;
            Armor.ItemLevel = 7;
            Pants.ItemLevel = 7;
            Gloves.ItemLevel = 7;
            Boots.ItemLevel = 7;

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
