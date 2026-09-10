namespace Client.Main.Objects.Monsters
{
    /// <summary>
    /// SourceMain5.2 CreateMonster(MONSTER_OMEGA_WING): shares the MODEL_CRUST case with
    /// ALPHA_CRUST — Scale = 1.3f, BlendMesh = 1, Thunder Blade +9 / Legendary Shield +9.
    /// No own constructor on purpose: AlphaCrust's constructor carries the full config.
    /// </summary>
    [NpcInfo(301, "Omega Wing")]
    public class OmegaWing : AlphaCrust
    {
    }
}
