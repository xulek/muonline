using Client.Main.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(306, "Death Rider")]
    public class DeathRider : SoundMonsterObject
    {
        protected override string DeathSoundPath => "Sound/w34/dr_death.wav";

        protected override string GetMoveSoundPath() =>
            MuGame.Random.Next(2) == 0 ? "Sound/w34/dr_idle01.wav" : "Sound/w34/dr_idle2.wav";

        protected override string GetAttackSoundPath(int attackType) =>
            attackType == 2 ? "Sound/w34/dr_attack2.wav" : "Sound/w34/dr_attack1.wav";

        public DeathRider()
        {
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster103.bmd");
            await base.Load();
        }
    }
}
