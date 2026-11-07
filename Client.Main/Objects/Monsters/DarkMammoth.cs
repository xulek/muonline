using Client.Main.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(562, "Dark Mammoth")]
    public class DarkMammoth : RaklionMonsterObject
    {
        protected override string AttackSoundPath => "Sound/w58w59/GiantMammoth_attack.wav";
        protected override string MoveSoundPath => "Sound/w58w59/GiantMammoth_move.wav";
        protected override string DeathSoundPath => "Sound/w58w59/GiantMammoth_death.wav";

        public DarkMammoth()
        {
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster206.bmd");
            await base.Load();
        }
    }
}
