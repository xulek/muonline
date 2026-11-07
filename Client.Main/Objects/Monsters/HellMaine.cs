using Client.Main.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(309, "Hell Maine")]
    public class HellMaine : SoundMonsterObject
    {
        protected override string DeathSoundPath => "Sound/w34/hm_death.wav";

        protected override string GetMoveSoundPath() =>
            MuGame.Random.Next(2) == 0 ? "Sound/w34/hm_idle1.wav" : "Sound/w34/hm_idle2.wav";

        protected override string GetAttackSoundPath(int attackType) => attackType switch
        {
            1 => "Sound/w34/hm_bloodywind.wav",
            2 => "Sound/w34/hm_firelay.wav",
            _ => "Sound/w34/hm_attack1.wav"
        };

        public HellMaine()
        {
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster106.bmd");
            await base.Load();
        }
    }
}
