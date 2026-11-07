using Client.Main.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(509, "Vermont")]
    public class Vermont : SoundMonsterObject
    {
        private bool IsImperialGuardian => World?.MapId is >= 69 and <= 72;
        protected override string DeathSoundPath => IsImperialGuardian ? "Sound/w69w70w71w72/2Vermont_death.wav" : null;
        protected override string GetMoveSoundPath() => !IsImperialGuardian ? null : MuGame.Random.Next(2) == 0
            ? "Sound/w69w70w71w72/Jelint_move01.wav"
            : "Sound/w69w70w71w72/Jelint_move02.wav";
        protected override string GetAttackSoundPath(int attackType) => !IsImperialGuardian ? null : attackType == 2
            ? "Sound/w69w70w71w72/2Vermont_attack2.wav"
            : "Sound/w69w70w71w72/2Vermont_attack1.wav";

        public Vermont()
        {
            Scale = 1.3f;
        }

        public override void OnAppearAnimationStart()
        {
            base.OnAppearAnimationStart();
            if (IsImperialGuardian)
                PlayMonsterSound("Sound/w69w70w71w72/Jelint_rage.wav");
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster170.bmd");
            await base.Load();
        }
    }
}
