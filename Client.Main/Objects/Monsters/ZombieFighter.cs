using Client.Main.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(480, "Zombie Fighter")]
    public class ZombieFighter : SoundMonsterObject
    {
        protected override string AttackSoundPath => "Sound/w64/ZombieWarrior_attack.wav";
        protected override string DamageSoundPath => "Sound/w64/ZombieWarrior_damage01.wav";
        protected override string DeathSoundPath => "Sound/w64/ZombieWarrior_death.wav";
        protected override string MoveSoundPath => "Sound/w64/ZombieWarrior_move01.wav";

        public ZombieFighter()
        {
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster158.bmd");
            await base.Load();
        }
    }
}
