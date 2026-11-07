using Client.Main.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(448, "Ghost Napin")]
    public class GhostNapin : SoundMonsterObject
    {
        protected override string AttackSoundPath => "Sound/w57/Naipin-Attack.wav";
        protected override string DeathSoundPath => "Sound/w57/Naipin-Death.wav";

        protected override string GetMoveSoundPath() =>
            MuGame.Random.Next(100) == 0 ? "Sound/w57/Naipin-Ghost.wav" : null;

        public GhostNapin()
        {
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster144.bmd");
            await base.Load();
        }
    }
}
