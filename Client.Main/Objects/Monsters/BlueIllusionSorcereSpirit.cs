using Client.Main.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(387, "Illusion Spirit (Ice)")]
    public class BlueIllusionSorcereSpirit : IllusionTempleSpirit
    {
        public BlueIllusionSorcereSpirit()
        {
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster125.bmd");
            await base.Load();
        }
    }
}
