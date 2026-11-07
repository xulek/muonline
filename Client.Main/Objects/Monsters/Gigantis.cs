using Client.Main.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(356, "Gigantis")]
    public class Gigantis : SoundMonsterObject
    {
        protected override string MoveSoundPath => "Sound/w37/gigan_idle-01.wav";
        protected override string AttackSoundPath => "Sound/w37/gigan_attack-01.wav";
        protected override string DeathSoundPath => "Sound/w37/gigan_death.wav";

        protected override string GetAttackSoundPath(int attackType) =>
            MuGame.Random.Next(2) == 0 ? "Sound/w37/gigan_attack-01.wav" : "Sound/w37/gigan_attack-02.wav";

        public Gigantis()
        {
            Scale = 1.2f;
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster113.bmd");
            await base.Load();
        }
    }
}
