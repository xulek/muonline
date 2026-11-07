using Client.Main.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(291, "Fire Golem")]
    public class FireGolem : SoundMonsterObject
    {
        protected override string DeathSoundPath => "Sound/w31/mFGdeath.wav";

        protected override string GetMoveSoundPath() =>
            MuGame.Random.Next(2) == 0 ? "Sound/w31/mFGidle1.wav" : "Sound/w31/mFGidle2.wav";

        protected override string GetAttackSoundPath(int attackType) =>
            attackType == 2 ? "Sound/w31/mFGattack2.wav" : "Sound/w31/mFGattack1.wav";

        public FireGolem()
        {
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster83.bmd");
            await base.Load();
        }
    }
}
