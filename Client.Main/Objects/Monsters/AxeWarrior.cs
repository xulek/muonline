using Client.Main.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(294, "Axe Warrior")]
    public class AxeWarrior : SoundMonsterObject
    {
        protected override string DeathSoundPath => "Sound/w31/mAWdeath.wav";

        protected override string GetMoveSoundPath() =>
            MuGame.Random.Next(2) == 0 ? "Sound/w31/mAWidle1.wav" : "Sound/w31/mAWidle2.wav";

        protected override string GetAttackSoundPath(int attackType) =>
            MuGame.Random.Next(2) == 0 ? "Sound/w31/mAWattack1.wav" : "Sound/w31/mAWattack2.wav";

        public AxeWarrior()
        {
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster86.bmd");
            await base.Load();
        }
    }
}
