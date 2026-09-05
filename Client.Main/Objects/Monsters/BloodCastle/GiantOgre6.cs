using Client.Main.Content;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
namespace Client.Main.Objects.Monsters.BloodCastle
{
    [NpcInfo(128, "Giant Ogre")]
    public class GiantOgre6 : GiantOgre1
    {
        public GiantOgre6()
        {
            // SourceMain5.2 RenderCharacter L9819: Blood Castle level tint (level = nCastle/3)
            Light = new Vector3(1.0f, 0.1f, 0.1f);
            Scale = 0.8f;
        }

        public override async Task Load()
        {
            // Visual setup and model loading are inherited from the source-matched base variant.
            await base.Load();
        }
    }
}
