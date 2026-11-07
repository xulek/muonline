using Client.Main.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(489, "Burning Lava Giant")]
    public class BurningLavaGiant : SoundMonsterObject
    {
        protected override string DeathSoundPath => "Sound/w64/BurningLavaGolem_death.wav";
        protected override string DamageSoundPath => "Sound/w64/BurningLavaGolem_damage01.wav";
        protected override string MoveSoundPath => "Sound/w64/BurningLavaGolem_move01.wav";

        protected override string GetAttackSoundPath(int attackType) =>
            attackType == 2 ? "Sound/w64/BurningLavaGolem_attack02.wav" : "Sound/w64/BurningLavaGolem_attack01.wav";

        public BurningLavaGiant()
        {
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster164.bmd");
            await base.Load();
        }
    }
}
