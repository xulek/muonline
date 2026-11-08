using Client.Main.Content;
using Client.Main.Models;
using Client.Main.Objects;
using Microsoft.Xna.Framework;
using System.Threading.Tasks;

namespace Client.Main.Objects.NPCS
{
    [NpcInfo(371, "Leo the Helper")]
    public class Leo : NPCObject
    {
        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare("Player/Player.bmd");
            await SetBodyPartsAsync("Player/", "HelmMale", "ArmorMale", "PantMale", "GloveMale", "BootMale", 10);

            // ZzzCharacter.cpp (MONSTER_LEO_THE_HELPER): plain Plate set, no weapon, level 0, and the
            // PC-room (m_bpcroom) glow on every plate part; the root-bone sprites come from the armor.
            Helm.PcRoomEffect = true;
            Armor.PcRoomEffect = true;
            Pants.PcRoomEffect = true;
            Gloves.PcRoomEffect = true;
            Boots.PcRoomEffect = true;

            AddItemEffect(new Client.Main.Objects.Effects.ItemLinkEffect
            {
                CustomEmitter = effect => Client.Main.Objects.Effects.ItemLinkEffectEmitter.EmitPcRoomPlate(effect),
            });

            await base.Load();
            AnimationSpeed = 25f;
            CurrentAction = (int)PlayerAction.PlayerStopMale;
        }
        protected override void HandleClick() { }
    }
}
