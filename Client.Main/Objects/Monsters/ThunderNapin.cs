using Client.Main.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(447, "Thunder Napin")]
    public class ThunderNapin : SoundMonsterObject
    {
        protected override string AttackSoundPath => "Sound/w57/Naipin-Attack.wav";
        protected override string DeathSoundPath => "Sound/w57/Naipin-Death.wav";

        protected override string GetMoveSoundPath() =>
            MuGame.Random.Next(100) == 0 ? "Sound/w57/Naipin-Thunder.wav" : null;

        public ThunderNapin()
        {
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster143.bmd");
            await base.Load();
        }
    }
}
