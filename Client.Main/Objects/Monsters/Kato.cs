using Client.Main.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(510, "Kato")]
    internal class Kato : SoundMonsterObject
    {
        protected override string MoveSoundPath => World?.MapId == 71 ? "Sound/w69w70w71w72/3Cato_move.wav" : null;
        protected override string DeathSoundPath => World?.MapId == 71 ? "Sound/w69w70w71w72/2Vermont_death.wav" : null;
        protected override string GetAttackSoundPath(int attackType) => World?.MapId != 71 ? null : attackType == 1
            ? "Sound/w69w70w71w72/CombatMaster_attack1.wav"
            : "Sound/w69w70w71w72/3Cato_attack2.wav";

        public Kato()
        {
            Scale = 1.35f;
        }

        public override void OnAppearAnimationStart()
        {
            base.OnAppearAnimationStart();
            if (World?.MapId == 71)
                PlayMonsterSound("Sound/w69w70w71w72/Raymond_rage.wav");
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster171.bmd");
            await base.Load();
        }
    }
}
