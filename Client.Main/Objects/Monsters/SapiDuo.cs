using Client.Main.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(442, "Sapi Duo")]
    public class SapiDuo : SoundMonsterObject
    {
        protected override string AttackSoundPath => "Sound/w57/Sapi-Attack.wav";
        protected override string DeathSoundPath => "Sound/w57/Sapi-Death.wav";

        public SapiDuo()
        {
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster138.bmd");
            await base.Load();
        }
    }
}
