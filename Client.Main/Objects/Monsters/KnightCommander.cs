using Client.Main.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(514, "Knight Commander")]
    public class KnightCommander : SoundMonsterObject
    {
        protected override string DeathSoundPath => World?.MapId == 70 ? "Sound/w69w70w71w72/Jelint_death.wav" : null;
        protected override string GetMoveSoundPath() => World?.MapId != 70 ? null : MuGame.Random.Next(2) == 0
            ? "Sound/w69w70w71w72/Jelint_move01.wav"
            : "Sound/w69w70w71w72/Jelint_move02.wav";
        protected override string GetAttackSoundPath(int attackType) => World?.MapId == 70
            ? "Sound/w69w70w71w72/CombatMaster_attack3.wav"
            : null;

        public KnightCommander()
        {
            Scale = 1.35f;
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster175.bmd");
            await base.Load();
        }
    }
}
