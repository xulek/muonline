using Client.Main.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(368, "Elphis")]
    public class Elphis : MonsterObject
    {
        public Elphis()
        {
            // SourceMain5.2 CreateMonster(MONSTER_ELPHIS): c->Object.Scale = 2.5f,
            // EnableShadow = false, m_bRenderShadow = false
            Scale = 2.5f;
            RenderShadow = false;
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster126.bmd");
            await base.Load();
        }
    }
}