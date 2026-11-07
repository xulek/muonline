using Client.Main.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(445, "Shadow Knight")]
    public class ShadowKnight : SoundMonsterObject
    {
        protected override string AttackSoundPath => "Sound/w57/ShadowKnight-Attack.wav";
        protected override string DeathSoundPath => "Sound/w57/Shadow-Death.wav";

        public ShadowKnight()
        {
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster141.bmd");
            await base.Load();
        }
    }
}
