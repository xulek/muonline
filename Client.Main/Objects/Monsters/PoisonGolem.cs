using Client.Main.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(293, "Poison Golem")]
    public class PoisonGolem : SoundMonsterObject
    {
        protected override string DeathSoundPath => "Sound/w31/mPGdeath.wav";

        protected override string GetMoveSoundPath() =>
            MuGame.Random.Next(2) == 0 ? "Sound/w31/mPGidle1.wav" : "Sound/w31/mPGidle2.wav";

        protected override string GetAttackSoundPath(int attackType)
        {
            if (attackType == 1)
                return "Sound/w31/mPGeff1.wav";

            return MuGame.Random.Next(2) == 0 ? "Sound/w31/mPGattack1.wav" : "Sound/w31/mPGattack2.wav";
        }

        public PoisonGolem()
        {
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster85.bmd");
            await base.Load();
        }
    }
}
