using Client.Main.Content;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters.BloodCastle
{
    [NpcInfo(136, "Orc Soldier of Doom")]
    public class OrcSoldierOfDoom : MonsterObject
    {
        public OrcSoldierOfDoom()
        {
            Scale = 1.3f; // SourceMain5.2 CreateMonster: c->Object.Scale = 1.3f
            HiddenMesh = 2; // SourceMain5.2 CreateMonster: o->HiddenMesh = 2
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster48.bmd");
            await base.Load();
        }
    }
}
