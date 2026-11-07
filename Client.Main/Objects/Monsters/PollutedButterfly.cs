using Client.Main.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(419, "Polluted Butterfly")]
    public class PollutedButterfly : SoundMonsterObject
    {
        protected override string AttackSoundPath => "Sound/w52/SE_Mon_rabbitugly_attack01.wav";
        protected override string DeathSoundPath => "Sound/w52/SE_Mon_rabbitugly_death01.wav";

        protected override string GetMoveSoundPath() =>
            MuGame.Random.Next(100) == 0 ? "Sound/w52/SE_Mon_rabbitugly_breath01.wav" : null;

        public PollutedButterfly()
        {
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster132.bmd");
            await base.Load();
        }
    }
}
