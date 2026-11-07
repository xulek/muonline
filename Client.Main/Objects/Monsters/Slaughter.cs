using Client.Main.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(530, "Mad Butcher")]
    public class Slaughter : SoundMonsterObject
    {
        protected override string AttackSoundPath => "Sound/Doppelganger/Butcher_attack.wav";
        protected override string DeathSoundPath => "Sound/Doppelganger/Butcher_death.wav";

        public Slaughter()
        {
            Scale = 0.8f;
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster190.bmd");
            await base.Load();
        }
    }
}
