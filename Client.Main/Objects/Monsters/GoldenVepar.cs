using Client.Main.Models;

namespace Client.Main.Objects.Monsters
{
    /// <summary>
    /// SourceMain5.2 MONSTER_GOLDEN_VEPAR: Vepar visuals plus the golden metal/chrome body passes.
    /// </summary>
    [NpcInfo(81, "Golden Vepar")]
    public class GoldenVepar : Vepar
    {
        public GoldenVepar()
        {
            ApplyGoldenOverlay();
        }
    }
}
