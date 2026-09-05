namespace Client.Main.Objects.Monsters.BloodCastle
{
    [NpcInfo(134, "Statue of Saint")]
    public class StatueOfSaint3 : BloodCastleStatueObject
    {
        public StatueOfSaint3()
            : base(0.8f) // SourceMain5.2 CreateMonster(MONSTER_STATUE_OF_SAINT_3): Scale = 0.8f
        {
            // SourceMain5.2 RenderCharacter: MODEL_DIVINE_CB_OF_ARCHANGEL (Item/Bow19.bmd),
            // link rendered at absolute scale 0.9 -> local 0.9 / 0.8.
            DivineWeaponModelPath = "Item/Bow19.bmd";
            DivineWeaponScale = 1.125f;
        }
    }
}
