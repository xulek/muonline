namespace Client.Main.Objects.Monsters.BloodCastle
{
    [NpcInfo(133, "Statue of Saint")]
    public class StatueOfSaint2 : BloodCastleStatueObject
    {
        public StatueOfSaint2()
            : base(0.8f) // SourceMain5.2 CreateMonster(MONSTER_STATUE_OF_SAINT_2): Scale = 0.8f
        {
            // SourceMain5.2 RenderCharacter: MODEL_DIVINE_SWORD_OF_ARCHANGEL (Item/Sword20.bmd),
            // link rendered at absolute scale 0.7 -> local 0.7 / 0.8.
            DivineWeaponModelPath = "Item/Sword20.bmd";
            DivineWeaponScale = 0.875f;
        }
    }
}
