using Client.Main.Content;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
namespace Client.Main.Objects.Monsters.BloodCastle
{
    [NpcInfo(127, "Dark Skull Soldier")]
    public class DarkSkullSoldier6 : DarkSkullSoldier1
    {
        public DarkSkullSoldier6()
        {
            // SourceMain5.2 RenderCharacter L9819: Blood Castle level tint (level = nCastle/3)
            Light = new Vector3(1.0f, 0.1f, 0.1f);
            Scale = 1.0f;
        }

        public override async Task Load()
        {
            // Visual setup and model loading are inherited from the source-matched base variant.
            await base.Load();
        }
    }
}
