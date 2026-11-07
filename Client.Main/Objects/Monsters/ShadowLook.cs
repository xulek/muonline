using Client.Main.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(446, "Shadow Look")]
    public class ShadowLook : SoundMonsterObject
    {
        protected override string AttackSoundPath => "Sound/w57/ShadowRook-Attack.wav";
        protected override string DeathSoundPath => "Sound/w57/Shadow-Death.wav";

        public ShadowLook()
        {
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster142.bmd");
            await base.Load();
        }
    }
}
