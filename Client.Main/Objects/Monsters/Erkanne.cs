using Client.Main.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(507, "Erkanne")]
    public class Erkanne : SoundMonsterObject
    {
        private bool IsImperialGuardian => World?.MapId is >= 69 and <= 72;
        protected override string DeathSoundPath => IsImperialGuardian ? "Sound/w69w70w71w72/Jelint_death.wav" : null;
        protected override string GetMoveSoundPath() => !IsImperialGuardian ? null : MuGame.Random.Next(2) == 0
            ? "Sound/w69w70w71w72/Jelint_move01.wav"
            : "Sound/w69w70w71w72/Jelint_move02.wav";
        protected override string GetAttackSoundPath(int attackType) => !IsImperialGuardian ? null : attackType switch
        {
            1 => "Sound/w69w70w71w72/Jelint_attack1.wav",
            2 => "Sound/w69w70w71w72/Jelint_attack3.wav",
            3 => "Sound/w69w70w71w72/Ercanne_attack3.wav",
            _ => null,
        };

        public Erkanne()
        {
            Scale = 1.25f;
        }

        public override void OnAppearAnimationStart()
        {
            base.OnAppearAnimationStart();
            if (IsImperialGuardian)
                PlayMonsterSound("Sound/w69w70w71w72/Raymond_rage.wav");
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster168.bmd");
            await base.Load();
        }
    }
}
