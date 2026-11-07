using Client.Main.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(482, "Gladiator")]
    public class ResurrectedGladiator : SoundMonsterObject
    {
        protected override string AttackSoundPath => "Sound/w64/RaisedGladiator_attack.wav";
        protected override string DamageSoundPath => "Sound/w64/RaisedGladiator_damage01.wav";
        protected override string DeathSoundPath => "Sound/w64/RaisedGladiator_death.wav";
        protected override string MoveSoundPath => "Sound/w64/RaisedGladiator_move01.wav";

        public ResurrectedGladiator()
        {
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster159.bmd");
            await base.Load();
        }
    }
}
