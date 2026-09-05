namespace Client.Main.Objects.Monsters.BloodCastle
{
    [NpcInfo(132, "Statue of Saint")]
    public class StatueOfSaint1 : BloodCastleStatueObject
    {
        public StatueOfSaint1()
            : base(0.8f) // SourceMain5.2 CreateMonster(MONSTER_STATUE_OF_SAINT_1): Scale = 0.8f
        {
            // SourceMain5.2 RenderCharacter: MODEL_DIVINE_STAFF_OF_ARCHANGEL (Item/Staff11.bmd),
            // link rendered at absolute scale 0.7 -> local 0.7 / 0.8.
            DivineWeaponModelPath = "Item/Staff11.bmd";
            DivineWeaponScale = 0.875f;
        }
    }
}
