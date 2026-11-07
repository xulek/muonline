using Client.Main.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(421, "Werewolf")]
    public class Werewolf2 : SoundMonsterObject
    {
        protected override string DeathSoundPath => "Sound/w52/SE_Mon_butterflypollution_death01.wav";

        protected override string GetMoveSoundPath() =>
            MuGame.Random.Next(100) == 0 ? "Sound/w52/SE_Mon_butterflypollution_move01.wav" : null;

        public Werewolf2()
        {
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster131.bmd");
            await base.Load();
        }
    }
}
