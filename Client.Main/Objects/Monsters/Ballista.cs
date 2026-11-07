using Client.Main.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(348, "Ballista")]
    public class Ballista : SoundMonsterObject
    {
        protected override string AttackSoundPath => "Sound/w35/tanker_attack.wav";
        protected override string DeathSoundPath => "Sound/w35/tanker_death.wav";

        public Ballista()
        {
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster100.bmd");
            await base.Load();
        }
    }
}