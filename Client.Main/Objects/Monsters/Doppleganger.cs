using Client.Main.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(533, "Doppelganger")]
    public class Doppleganger : SoundMonsterObject
    {
        protected override string DeathSoundPath => "Sound/Doppelganger/Doppelganger_death.wav";

        public override void OnAppearAnimationStart()
        {
            base.OnAppearAnimationStart();
            PlayMonsterSound("Sound/Doppelganger/Doppelganger_attack.wav");
        }

        public Doppleganger()
        {
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster192.bmd");
            await base.Load();
        }
    }
}
