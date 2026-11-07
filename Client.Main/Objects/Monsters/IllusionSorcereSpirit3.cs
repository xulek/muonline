using Client.Main.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(388, "Illusion Spirit (Poison)")]
    public class IllusionSorcereSpirit3 : IllusionTempleSpirit
    {
        protected override bool IsPoison => true;
        public IllusionSorcereSpirit3()
        {
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster126.bmd");
            await base.Load();
        }
    }
}
