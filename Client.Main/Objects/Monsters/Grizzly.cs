using Client.Main.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(424, "Grizzly")]
    public class Grizzly : SoundMonsterObject
    {
        protected override string AttackSoundPath => "Sound/w52/SE_Mon_beastwoo_attack01.wav";
        protected override string DeathSoundPath => "Sound/w52/SE_Mon_beastwoo_death01.wav";

        protected override string GetMoveSoundPath() =>
            MuGame.Random.Next(100) == 0 ? "Sound/w52/SE_Mon_beastwoo_move01.wav" : null;

        public Grizzly()
        {
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster135.bmd");
            await base.Load();
        }
    }
}
