using Client.Main.Content;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(552, "Bloody Witch Queen")]
    public class BloodyWitchQueen : WitchQueen
    {
        protected override string ModelPath => "Monster/Monster197.bmd";

        public BloodyWitchQueen()
        {
            Scale = 1.75f;
        }
    }
}
