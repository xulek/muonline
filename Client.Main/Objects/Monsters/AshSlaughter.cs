using Client.Main.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(485, "Ash Slaughterer")]
    public class AshSlaughter : SoundMonsterObject
    {
        protected override string AttackSoundPath => "Sound/w64/AshesButcher_attack.wav";
        protected override string DamageSoundPath => "Sound/w64/AshesButcher_damage01.wav";
        protected override string DeathSoundPath => "Sound/w64/AshesButcher_death.wav";
        protected override string MoveSoundPath => "Sound/w64/AshesButcher_move01.wav";

        public AshSlaughter()
        {
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster160.bmd");
            await base.Load();
        }
    }
}
