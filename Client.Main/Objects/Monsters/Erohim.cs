using Client.Main.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(295, "Erohim")]
    public class Erohim : SoundMonsterObject
    {
        protected override string DeathSoundPath => "Sound/w31/mELOdeath.wav";

        protected override string GetAttackSoundPath(int attackType)
        {
            if (attackType == 2)
                return "Sound/w31/mELOeff1.wav";

            return MuGame.Random.Next(2) == 0 ? "Sound/w31/mELOattack1.wav" : "Sound/w31/mELOattack2.wav";
        }

        public Erohim()
        {
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster88.bmd");
            await base.Load();
        }
    }
}
