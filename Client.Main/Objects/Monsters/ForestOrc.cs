using Client.Main.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(307, "Forest Orc")]
    public class ForestOrc : SoundMonsterObject
    {
        protected override string DeathSoundPath => "Sound/w34/fo_death.wav";

        protected override string GetMoveSoundPath() =>
            MuGame.Random.Next(2) == 0 ? "Sound/w34/fo_idle1.wav" : "Sound/w34/fo_idle2.wav";

        protected override string GetAttackSoundPath(int attackType) =>
            attackType == 2 ? "Sound/w34/fo_attack2.wav" : "Sound/w34/fo_attack1.wav";

        public ForestOrc()
        {
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster104.bmd");
            await base.Load();
        }
    }
}
