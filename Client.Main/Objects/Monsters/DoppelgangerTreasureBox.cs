using System.Threading.Tasks;
using Client.Main.Content;

namespace Client.Main.Objects.Monsters
{
    public abstract class DoppelgangerTreasureBox : SoundMonsterObject
    {
        protected override string DeathSoundPath => "Sound/Doppelganger/treasurebox_open.wav";

        protected DoppelgangerTreasureBox()
        {
            Scale = 2.3f;
            Blood = false;
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare("NPC/DoppelgangerBox.bmd");
            await base.Load();
        }
    }

    [NpcInfo(541, "Compensation Box")]
    public class DoppelgangerBox : DoppelgangerTreasureBox { }

    [NpcInfo(542, "Golden Compensation Box")]
    public class DoppelgangerGoldenBox : DoppelgangerTreasureBox { }
}
