using Client.Main.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(357, "Genocider")]
    public class Genocider : SoundMonsterObject
    {
        protected override string AttackSoundPath => "Sound/w37/geno_attack-01.wav";
        protected override string DeathSoundPath => "Sound/w37/geno_death.wav";

        protected override string GetMoveSoundPath() =>
            MuGame.Random.Next(2) == 0 ? "Sound/w37/geno_idle-01.wav" : "Sound/w37/geno_idle-02.wav";

        protected override string GetAttackSoundPath(int attackType) =>
            MuGame.Random.Next(2) == 0 ? "Sound/w37/geno_attack-01.wav" : "Sound/w37/geno_attack-02.wav";

        public Genocider()
        {
            Scale = 1.2f;
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster114.bmd");
            await base.Load();
        }
    }
}
