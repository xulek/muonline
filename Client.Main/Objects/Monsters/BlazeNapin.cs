using Client.Main.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(449, "Blaze Napin")]
    public class BlazeNapin : SoundMonsterObject
    {
        protected override string AttackSoundPath => "Sound/w57/Naipin-Attack.wav";
        protected override string DeathSoundPath => "Sound/w57/Naipin-Death.wav";

        protected override string GetMoveSoundPath() =>
            MuGame.Random.Next(100) == 0 ? "Sound/w57/Naipin-Blaze.wav" : null;

        public BlazeNapin()
        {
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster145.bmd");
            await base.Load();
        }
    }
}
