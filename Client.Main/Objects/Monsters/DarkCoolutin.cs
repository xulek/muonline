using Client.Main.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    public class DarkCoolutin : RaklionMonsterObject
    {
        protected override string AttackSoundPath => "Sound/w58w59/Coolertin_attack.wav";
        protected override string MoveSoundPath => "Sound/w58w59/Coolertin_move.wav";
        protected override string DeathSoundPath => "Sound/mHellSpiderDie.wav";

        public DarkCoolutin()
        {
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster208.bmd");
            await base.Load();
        }
    }
}
