using Client.Main.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(345, "Death Spirit")]
    public class DeathSpirit : SoundMonsterObject
    {
        protected override string AttackSoundPath => "Sound/w35/dths_at1.wav";
        protected override string DeathSoundPath => "Sound/w35/dths_deat.wav";

        protected override string GetMoveSoundPath() =>
            MuGame.Random.Next(2) == 0 ? "Sound/w35/dths_idle1.wav" : "Sound/w35/dths_idle2.wav";

        protected override string GetAttackSoundPath(int attackType) =>
            attackType == 2 ? "Sound/w35/dths_at2.wav" : AttackSoundPath;

        public DeathSpirit()
        {
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster94.bmd");
            await base.Load();
        }
    }
}
