using Client.Main.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(504, "Gaion Kharein")]
    public class GaionKharein : SoundMonsterObject
    {
        private bool IsImperialGuardian => World?.MapId == 72;
        protected override string MoveSoundPath => IsImperialGuardian ? "Sound/w69w70w71w72/GaionKalein_move.wav" : null;
        protected override string DeathSoundPath => IsImperialGuardian ? "Sound/w69w70w71w72/GrandWizard_death.wav" : null;

        public GaionKharein()
        {
            Scale = 1.4f;
        }

        public override void OnAppearAnimationStart()
        {
            base.OnAppearAnimationStart();
            if (IsImperialGuardian)
                PlayMonsterSound("Sound/w69w70w71w72/GaionKalein_rage.wav");
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster165.bmd");
            await base.Load();
        }
    }
}
