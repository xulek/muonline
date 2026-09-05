using Client.Main.Content;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
namespace Client.Main.Objects.Monsters.BloodCastle
{
    [NpcInfo(130, "Magic Skeleton")]
    public class MagicSkeleton6 : MagicSkeleton1
    {
        public MagicSkeleton6()
        {
            // SourceMain5.2 RenderCharacter L9819: Blood Castle level tint (level = nCastle/3)
            Light = new Vector3(1.0f, 0.1f, 0.1f);
            Scale = 1.2f;
        }

        public override async Task Load()
        {
            // Visual setup and model loading are inherited from the source-matched base variant.
            await base.Load();
        }
    }
}
