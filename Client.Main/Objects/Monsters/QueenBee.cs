using Client.Main.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(292, "Queen Bee")]
    public class QueenBee : SoundMonsterObject
    {
        protected override string DeathSoundPath => "Sound/w31/mAWdeath.wav";

        protected override string GetMoveSoundPath() =>
            MuGame.Random.Next(2) == 0 ? "Sound/w31/mQBidle1.wav" : "Sound/w31/mQBidle2.wav";

        protected override string GetAttackSoundPath(int attackType) =>
            MuGame.Random.Next(2) == 0 ? "Sound/w31/mQBattack1.wav" : "Sound/w31/mQBattack2.wav";

        public QueenBee()
        {
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster84.bmd");
            await base.Load();
        }
    }
}
