using Client.Main.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(135, "White Wizard")]
    public class WhiteWizard : MonsterObject
    {
        public WhiteWizard()
        {
            Scale = 1.7f;

            // SourceMain5.2 RenderCharacter L8783: MONSTER_WHITE_WIZARD gets the
            // RENDER_BRIGHT | RENDER_EXTRA body pass (no chrome texture).
            BrightOverlay = 1f;
        }

        public override async Task Load()
        {
            // SourceMain5.2 CreateMonster: MONSTER_WHITE_WIZARD shares the MONSTER_CURSED_KING
            // case (MODEL_CURSED_KING = MODEL_MONSTER01 + 48 -> Monster49.bmd)
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster49.bmd");
            await base.Load();
        }
    }
}
