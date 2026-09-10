using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Models;
using Client.Main.Objects.Effects;
using Microsoft.Xna.Framework;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(51, "Great Bahamut")]
    public class GreatBahamut : Bahamut // Inherits from Bahamut
    {
        public GreatBahamut() : base(true)
        {
            Scale = 1.0f; // Set according to C++ Setting_Monster (same as Bahamut?)
            Children.Add(new SourceMonsterSandSmokeEffect());
        }
        // Load() and sounds inherited
    }
}
