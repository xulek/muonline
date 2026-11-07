using Client.Main.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    public class TheDarkGiant : RaklionMonsterObject
    {
        protected override string AttackSoundPath => null;
        protected override string MoveSoundPath => "Sound/w58w59/IceGiant_move.wav";
        protected override string DeathSoundPath => "Sound/w58w59/IceGiant_death.wav";

        public TheDarkGiant()
        {
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster207.bmd");
            await base.Load();
        }
    }
}
