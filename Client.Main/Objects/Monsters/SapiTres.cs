using Client.Main.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(443, "Sapi Tres")]
    public class SapiTres : SoundMonsterObject
    {
        protected override string AttackSoundPath => "Sound/w57/Sapi-Attack1.wav";
        protected override string DeathSoundPath => "Sound/w57/Sapi-Death.wav";

        public SapiTres()
        {
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster139.bmd");
            await base.Load();
        }
    }
}
