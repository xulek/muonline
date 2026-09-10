using Client.Main.Content;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(65, "Elite Orc")]
    public class EliteOrc : MonsterObject
    {
        public EliteOrc()
        {
            // SourceMain5.2 CreateMonster (MONSTER_ELITE_ORC): MODEL_ORC, Scale = 1.3f, HiddenMesh = 2
            Scale = 1.3f;
            HiddenMesh = 2;
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster48.bmd");
            await base.Load();
        }
    }
}
