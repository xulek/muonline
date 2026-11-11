using Client.Main.Models;

namespace Client.Main.Objects.Monsters
{
    /// <summary>
    /// SourceMain5.2 MONSTER_GOLDEN_WHEEL: IronWheel visuals plus the golden metal/chrome body passes.
    /// </summary>
    [NpcInfo(83, "Golden Wheel")]
    public class GoldenWheel : IronWheel
    {
        public GoldenWheel()
        {
            ApplyGoldenOverlay();
        }
    }
}
