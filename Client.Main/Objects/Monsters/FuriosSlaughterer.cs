using Client.Main.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(529, "Terrible Butcher")]
    public class FuriosSlaughterer : SoundMonsterObject
    {
        protected override string AttackSoundPath => "Sound/Doppelganger/Angerbutcher_attack.wav";
        protected override string DeathSoundPath => "Sound/Doppelganger/Angerbutcher_death.wav";

        public FuriosSlaughterer()
        {
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster191.bmd");
            await base.Load();
        }
    }
}
