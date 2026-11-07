using Client.Main.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(487, "Cruel Blood Assassin")]
    public class CruelBloodAssassin : SoundMonsterObject
    {
        protected override string AttackSoundPath => "Sound/w64/BloodAssassin_attack.wav";
        protected override string DamageSoundPath => "Sound/w64/BloodAssassin_damage01.wav";
        protected override string DeathSoundPath => "Sound/w64/BloodAssassin_death.wav";
        protected override string MoveSoundPath => "Sound/w64/BloodAssassin_move01.wav";

        public CruelBloodAssassin()
        {
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster162.bmd");
            await base.Load();
        }
    }
}
