using Client.Main.Models;

namespace Client.Main.Objects.Monsters
{
    /// <summary>
    /// SourceMain5.2 MONSTER_GOLDEN_GOBLIN: Goblin visuals plus the golden metal/chrome body passes.
    /// </summary>
    [NpcInfo(78, "Golden Goblin")]
    public class GoldenGoblin : Goblin
    {
        public GoldenGoblin()
        {
            ApplyGoldenOverlay();
        }
    }
}
