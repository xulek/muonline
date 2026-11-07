using Client.Main.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(355, "Kentauros")]
    public class Kentauros : SoundMonsterObject
    {
        protected override string AttackSoundPath => "Sound/w37/kenta_attack-01.wav";
        protected override string DeathSoundPath => "Sound/w37/kenta_death.wav";

        protected override string GetMoveSoundPath() =>
            MuGame.Random.Next(2) == 0 ? "Sound/w37/kenta_idle-01.wav" : "Sound/w37/kenta_idle-02.wav";

        protected override string GetAttackSoundPath(int attackType) =>
            attackType == 2 ? "Sound/w37/kenta_skill-01.wav" : AttackSoundPath;

        public Kentauros()
        {
            Scale = 1.1f;
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster112.bmd");
            await base.Load();
        }
    }
}
