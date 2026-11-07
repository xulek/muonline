using Client.Main.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(420, "Hideous Rabbit")]
    public class HideousRabbit : SoundMonsterObject
    {
        protected override string AttackSoundPath => "Sound/w52/SE_Mon_wolfhuman_attack01.wav";
        protected override string DeathSoundPath => "Sound/w52/SE_Mon_wolfhuman_death01.wav";

        protected override string GetMoveSoundPath() =>
            MuGame.Random.Next(30) == 0 ? "Sound/w52/SE_Mon_wolfhuman_move02.wav" : null;

        public HideousRabbit()
        {
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster130.bmd");
            await base.Load();
        }
    }
}
