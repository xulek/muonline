using Client.Main.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(312, "Bow Scout")]
    public class BowScout : SoundMonsterObject
    {
        protected override string DeathSoundPath => "Sound/w35/ww_s2_death.wav";

        protected override string GetMoveSoundPath() =>
            MuGame.Random.Next(2) == 0 ? "Sound/w35/ww_s2_idle1.wav" : "Sound/w35/ww_s2_idle2.wav";

        protected override string GetAttackSoundPath(int attackType) =>
            attackType == 2 ? "Sound/w35/ww_s2_attack2.wav" : "Sound/w35/ww_s2_attack1.wav";

        public BowScout()
        {
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster97.bmd");
            await base.Load();
        }
    }
}
