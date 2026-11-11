using Client.Main.Models;

namespace Client.Main.Objects.Monsters
{
    /// <summary>
    /// SourceMain5.2 MONSTER_GOLDEN_DERKON: RedDragon visuals plus the golden metal/chrome body passes.
    /// </summary>
    [NpcInfo(79, "Golden Derkon")]
    public class GoldenDerkon : RedDragon
    {
        public GoldenDerkon()
        {
            Scale = 0.9f;
            ApplyGoldenOverlay();
        }
    }
}
