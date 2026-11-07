using Client.Main.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(517, "Cavalry Captain")]
    public class CavalryCaptain : SoundMonsterObject
    {
        private bool IsImperialGuardian => World?.MapId == 71;
        protected override bool UseImperialFootsteps => IsImperialGuardian;
        protected override string ImperialFootstepFrame1 => "Sound/w69w70w71w72/CavalryLeader_move02.wav";
        protected override string ImperialFootstepFrame7 => "Sound/w69w70w71w72/CavalryLeader_move01.wav";
        protected override string DeathSoundPath => IsImperialGuardian ? "Sound/w58w59/GiantMammoth_death.wav" : null;
        protected override string GetAttackSoundPath(int attackType) => !IsImperialGuardian ? null : attackType is 1 or 3
            ? "Sound/w69w70w71w72/CavalryLeader_attack1.wav"
            : "Sound/w69w70w71w72/CavalryLeader_attack2.wav";

        public CavalryCaptain()
        {
            Scale = 1.25f;
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster178.bmd");
            await base.Load();
        }
    }
}
