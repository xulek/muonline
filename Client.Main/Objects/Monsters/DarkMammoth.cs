using Client.Main.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(562, "Dark Mammoth")]
    public class DarkMammoth : GiantMammoth
    {
        protected override string ModelPath => "Monster/Monster206.bmd";
    }
}
