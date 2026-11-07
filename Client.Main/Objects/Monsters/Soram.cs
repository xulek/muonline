using Client.Main.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(341, "Soram")]
    public class Soram : SoundMonsterObject
    {
        protected override string AttackSoundPath => "Sound/w35/soram_attack1.wav";
        protected override string DeathSoundPath => "Sound/w35/soram_death.wav";

        protected override string GetMoveSoundPath() =>
            MuGame.Random.Next(2) == 0 ? "Sound/w35/soram_idle1.wav" : "Sound/w35/soram_idle2.wav";

        protected override string GetAttackSoundPath(int attackType) =>
            attackType == 2 ? "Sound/w35/soram_attack2.wav" : AttackSoundPath;

        public Soram()
        {
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster95.bmd");
            await base.Load();
        }
    }
}