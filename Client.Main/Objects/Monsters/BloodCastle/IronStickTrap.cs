using Client.Main.Content;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters.BloodCastle
{
    [NpcInfo(101, "Iron Stick Trap")]
    public class IronStickTrap : SoundMonsterObject
    {
        protected override string AttackSoundPath => "Sound/aGrate.wav";
        public IronStickTrap()
        {
            RenderShadow = false;
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Object/Object40.bmd");
            await base.Load();
        }
    }
}
