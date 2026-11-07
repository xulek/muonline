using Client.Main.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(518, "Shield Bearer")]
    public class ShieldBearer : SoundMonsterObject
    {
        private bool IsImperialGuardian => World?.MapId is >= 69 and <= 72;
        protected override bool UseImperialFootsteps => IsImperialGuardian;
        protected override string DeathSoundPath => IsImperialGuardian ? "Sound/w69w70w71w72/GrandWizard_death.wav" : null;
        protected override string GetMoveSoundPath() => !IsImperialGuardian ? null : MuGame.Random.Next(2) == 0
            ? "Sound/w69w70w71w72/Jelint_move01.wav"
            : "Sound/w69w70w71w72/Jelint_move02.wav";
        protected override string GetAttackSoundPath(int attackType) => !IsImperialGuardian ? null : attackType == 1
            ? "Sound/w69w70w71w72/CombatMaster_attack1.wav"
            : "Sound/w69w70w71w72/CombatMaster_attack2.wav";

        public ShieldBearer()
        {
            Scale = 1.2f;
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster179.bmd");
            await base.Load();
        }
    }
}
