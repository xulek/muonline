using Client.Main.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(516, "Master Assassin")]
    internal class MasterAssassin : SoundMonsterObject
    {
        protected override string MoveSoundPath => World?.MapId == 71 ? "Sound/w69w70w71w72/GaionKalein_move.wav" : null;
        protected override string DeathSoundPath => World?.MapId == 71 ? "Sound/w69w70w71w72/AssassinMaster_Death.wav" : null;
        protected override string GetAttackSoundPath(int attackType) => World?.MapId != 71 ? null : attackType == 1
            ? "Sound/w69w70w71w72/CombatMaster_attack1.wav"
            : "Sound/w69w70w71w72/CombatMaster_attack2.wav";
        public MasterAssassin()
        {
            Scale = 1.55f;
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster177.bmd");
            await base.Load();
        }
    }
}
