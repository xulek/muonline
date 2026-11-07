using Client.Main.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(454, "Ice Walker")]
    public class IceWalker : RaklionMonsterObject
    {
        protected override string AttackSoundPath => "Sound/w58w59/IceWalker_attack.wav";
        protected override string MoveSoundPath => "Sound/w58w59/IceWalker_move.wav";
        protected override string DeathSoundPath => "Sound/w52/SE_Mon_wolfhuman_death01.wav";

        public IceWalker()
        {
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster146.bmd");
            await base.Load();
        }
    }
}
