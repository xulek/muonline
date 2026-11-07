using Client.Main.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(354, "Blade Hunter")]
    public class BladeHunter : SoundMonsterObject
    {
        protected override string AttackSoundPath => "Sound/w37/blade_attack-01.wav";
        protected override string DeathSoundPath => "Sound/w37/blade_death.wav";

        protected override string GetMoveSoundPath() =>
            MuGame.Random.Next(2) == 0 ? "Sound/w37/blade_idle-01.wav" : "Sound/w37/blade_idle-02.wav";

        protected override string GetAttackSoundPath(int attackType) =>
            MuGame.Random.Next(2) == 0 ? "Sound/w37/blade_attack-01.wav" : "Sound/w37/blade_attack-02.wav";

        public BladeHunter()
        {
            Scale = 1.3f;
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster111.bmd");
            await base.Load();
        }
    }
}
