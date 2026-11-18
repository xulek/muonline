using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Models;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(44, "Golden Dragon")]
    public class GoldenDragon : RedDragon // Inherits from RedDragon
    {
        protected override bool HasBossBreath => false;

        public GoldenDragon()
        {
            Scale = 0.9f; // Set according to C++ Setting_Monster
            ApplyGoldenOverlay();
        }
    }
}
