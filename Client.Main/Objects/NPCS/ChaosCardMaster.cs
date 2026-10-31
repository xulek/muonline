using Client.Main.Content;
using Client.Main.Models;
using Client.Main.Networking;
using System.Threading.Tasks;

namespace Client.Main.Objects.NPCS
{
    /// <summary>
    /// MONSTER_CHAOS_CARD_MASTER (ZzzCharacter.cpp): Venom Mist set +9 and Wings of Heaven, no weapon.
    /// </summary>
    [NpcInfo(375, "Chaos Card Master")]
    public class ChaosCardMaster : NPCObject
    {
        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare("Player/Player.bmd");
            // Venom Mist is item index 30 of every armor group (file number 31).
            await SetBodyPartsAsync("Player/", "HelmMale", "ArmorMale", "PantMale", "GloveMale", "BootMale", 31);

            ConfigureSetPart(Helm, 7);
            ConfigureSetPart(Armor, 8);
            ConfigureSetPart(Pants, 9);
            ConfigureSetPart(Gloves, 10);
            ConfigureSetPart(Boots, 11);

            // Wings of Heaven = Wing02.bmd, drawn with the additive mesh 0 like in RenderLinkObject.
            await ConfigureNpcWingsAsync(
                "Item/Wing02.bmd",
                blendMesh: 0,
                blendState: Microsoft.Xna.Framework.Graphics.BlendState.Additive);

            await base.Load();
            AnimationSpeed = 25f;
            CurrentAction = (int)PlayerAction.PlayerStopMale;
        }

        private static void ConfigureSetPart(ModelObject part, int group)
        {
            part.ItemLevel = 9;
            part.MaterialItemGroup = group;
            part.MaterialItemIndex = 30;
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
