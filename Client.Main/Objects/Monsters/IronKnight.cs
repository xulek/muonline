using Client.Main.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(458, "Iron Knight")]
    public class IronKnight : RaklionMonsterObject
    {
        protected override string AttackSoundPath => "Sound/w58w59/IronKnight_attack.wav";
        protected override string MoveSoundPath => "Sound/w58w59/IronKnight_move.wav";
        protected override string DeathSoundPath => "Sound/death1.wav";
        protected override bool PlayMoveSoundOnIdle => true;

        public IronKnight()
        {
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster150.bmd");
            await base.Load();
        }
    }
}
