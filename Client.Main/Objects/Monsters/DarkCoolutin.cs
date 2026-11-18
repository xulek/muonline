using Client.Main.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(564, "Dark Coolutin")]
    public class DarkCoolutin : Coolutin
    {
        protected override string ModelPath => "Monster/Monster208.bmd";
    }
}
