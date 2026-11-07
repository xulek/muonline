using Client.Main.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(444, "Shadow Pawn")]
    public class ShadowPawn : SoundMonsterObject
    {
        protected override string AttackSoundPath => "Sound/w57/ShadowPawn-Attack.wav";
        protected override string DeathSoundPath => "Sound/w57/Shadow-Death.wav";

        public ShadowPawn()
        {
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster140.bmd");
            await base.Load();
        }
    }
}
