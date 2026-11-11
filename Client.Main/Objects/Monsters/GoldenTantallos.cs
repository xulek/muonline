using Client.Main.Models;

namespace Client.Main.Objects.Monsters
{
    /// <summary>
    /// SourceMain5.2 MONSTER_GOLDEN_TANTALLOS: Tantalos visuals plus the golden metal/chrome body passes.
    /// </summary>
    [NpcInfo(82, "Golden Tantallos")]
    public class GoldenTantallos : Tantalos
    {
        public GoldenTantallos()
        {
            ApplyGoldenOverlay();
        }
    }
}
