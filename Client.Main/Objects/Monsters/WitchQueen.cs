using Client.Main.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(304, "Witch Queen")]
    public class WitchQueen : SoundMonsterObject
    {
        protected override string DeathSoundPath => "Sound/w34/wq_death.wav";

        protected override string GetMoveSoundPath() =>
            MuGame.Random.Next(2) == 0 ? "Sound/w34/wq_idle1.wav" : "Sound/w34/wq_idle2.wav";

        protected override string GetAttackSoundPath(int attackType) =>
            attackType == 2 ? "Sound/w34/wq_attack1.wav" : "Sound/w34/wq_attack2.wav";

        public WitchQueen()
        {
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster101.bmd");
            await base.Load();
        }
    }
}
