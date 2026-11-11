using Client.Main.Models;

namespace Client.Main.Objects.Monsters
{
    /// <summary>
    /// SourceMain5.2 MONSTER_GOLDEN_LIZARD_KING: LizardKing visuals plus the golden metal/chrome body passes.
    /// </summary>
    [NpcInfo(80, "Golden Lizard King")]
    public class GoldenLizardKing : LizardKing
    {
        public GoldenLizardKing()
        {
            ApplyGoldenOverlay();
        }
    }
}
