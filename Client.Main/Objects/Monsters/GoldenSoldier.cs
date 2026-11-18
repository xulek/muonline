namespace Client.Main.Objects.Monsters
{
    [NpcInfo(54, "Golden Soldier")]
    public class GoldenSoldier : Soldier
    {
        public GoldenSoldier()
        {
            Scale = 1.1f;
            ApplyGoldenOverlay(metalOnly: true);
        }
    }
}
