using Client.Main.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(418, "Strange Rabbit")]
    public class StrangeRabbit : SoundMonsterObject
    {
        protected override string AttackSoundPath => "Sound/w52/SE_Mon_rabbitstrange_attack01.wav";
        protected override string DeathSoundPath => "Sound/w52/SE_Mon_rabbitstrange_death01.wav";

        public StrangeRabbit()
        {
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster129.bmd");
            await base.Load();
        }
    }
}
