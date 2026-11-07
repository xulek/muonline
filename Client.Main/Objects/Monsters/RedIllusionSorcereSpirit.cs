using Client.Main.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(386, "Illusion Spirit (Lightning)")]
    public class RedIllusionSorcereSpirit : IllusionTempleSpirit
    {
        public RedIllusionSorcereSpirit()
        {
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster124.bmd");
            await base.Load();
        }
    }
}
