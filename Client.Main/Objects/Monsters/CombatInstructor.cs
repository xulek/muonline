using Client.Main.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(513, "Combat Instructor")]
    public class CombatInstructor : SoundMonsterObject
    {
        private bool IsImperialGuardian => World?.MapId is >= 69 and <= 72;
        protected override bool UseImperialFootsteps => IsImperialGuardian;
        protected override string DeathSoundPath => IsImperialGuardian ? "Sound/w69w70w71w72/Jelint_death.wav" : null;
        protected override string GetMoveSoundPath() => !IsImperialGuardian ? null : MuGame.Random.Next(2) == 0
            ? "Sound/w69w70w71w72/Jelint_move01.wav"
            : "Sound/w69w70w71w72/Jelint_move02.wav";
        protected override string GetAttackSoundPath(int attackType) => !IsImperialGuardian ? null : attackType switch
        {
            1 => "Sound/w69w70w71w72/CombatMaster_attack1.wav",
            2 => "Sound/w69w70w71w72/CombatMaster_attack2.wav",
            3 => "Sound/w69w70w71w72/CombatMaster_attack3.wav",
            _ => null,
        };

        public CombatInstructor()
        {
            Scale = 1.25f;
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster174.bmd");
            await base.Load();
        }
    }
}
