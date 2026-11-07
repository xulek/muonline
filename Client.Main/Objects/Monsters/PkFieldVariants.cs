namespace Client.Main.Objects.Monsters
{
    // These server types use the same models and sounds as their PK Field counterparts.
    [NpcInfo(481, "Zombier")]
    internal class Zombier : ZombieFighter { public Zombier() { } }

    [NpcInfo(483, "Hell Gladiator")]
    internal class HellGladiator : ResurrectedGladiator { public HellGladiator() { } }

    [NpcInfo(484, "Slaughterer")]
    internal class PkFieldSlaughterer : AshSlaughter { public PkFieldSlaughterer() { } }

    [NpcInfo(488, "Cold Blooded Assassin")]
    internal class ColdBloodedAssassin : CruelBloodAssassin { public ColdBloodedAssassin() { } }

    [NpcInfo(490, "Lava Giant")]
    internal class LavaGiant : RuthlessLavaGiant { public LavaGiant() { } }
}
