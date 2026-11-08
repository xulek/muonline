using Client.Main.Content;
using Client.Main.Models;
using Client.Main.Objects.Effects;
using Client.Main.Objects.Player;
using System.Threading.Tasks;

namespace Client.Main.Objects.NPCS
{
    [NpcInfo(258, "Luke the Helper")]
    public class LukeTheHelper : NPCObject
    {
        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare("Player/Player.bmd");
            await SetBodyPartsAsync("Player/", "HelmMale", "ArmorMale", "PantMale", "GloveMale", "BootMale", 10); // Plate Set
            // PC-room helper: same glow as Leo the Helper (o->m_bpcroom).
            Helm.PcRoomEffect = true;
            Armor.PcRoomEffect = true;
            Pants.PcRoomEffect = true;
            Gloves.PcRoomEffect = true;
            Boots.PcRoomEffect = true;
            AddItemEffect(new ItemLinkEffect
            {
                CustomEmitter = effect => ItemLinkEffectEmitter.EmitPcRoomPlate(effect),
            });

            await base.Load();
            CurrentAction = (int)PlayerAction.PlayerStopMale;
        }
        protected override void HandleClick() { }
    }
}
