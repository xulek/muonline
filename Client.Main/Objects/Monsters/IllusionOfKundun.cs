namespace Client.Main.Objects.Monsters
{
    // Kalima tiers 1–6 use the Schriker model with subtype 9's sound set.
    [NpcInfo(161, "Illusion of Kundun")]
    public class IllusionOfKundun : Schriker
    {
        public IllusionOfKundun() { Scale = 1.5f; }

        protected override string IdleSound1 => "Sound/mLvIdle1.wav";
        protected override string IdleSound2 => "Sound/mLvIdle2.wav";
        protected override string AttackSound1 => "Sound/mLvAttack1.wav";
        protected override string AttackSound2 => "Sound/mLvAttack2.wav";
        protected override string DeathSound => "Sound/mLvDeath.wav";
    }

    [NpcInfo(181, "Illusion of Kundun")]
    internal class IllusionOfKundun2 : IllusionOfKundun { public IllusionOfKundun2() { } }
    [NpcInfo(189, "Illusion of Kundun")]
    internal class IllusionOfKundun3 : IllusionOfKundun { public IllusionOfKundun3() { } }
    [NpcInfo(197, "Illusion of Kundun")]
    internal class IllusionOfKundun4 : IllusionOfKundun { public IllusionOfKundun4() { } }
    [NpcInfo(267, "Illusion of Kundun")]
    internal class IllusionOfKundun5 : IllusionOfKundun { public IllusionOfKundun5() { } }
    [NpcInfo(338, "Illusion of Kundun")]
    internal class IllusionOfKundun6 : IllusionOfKundun { public IllusionOfKundun6() { } }
}
