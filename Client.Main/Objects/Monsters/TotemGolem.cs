using Client.Main.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(423, "Totem Golem")]
    public class TotemGolem : SoundMonsterObject
    {
        protected override string DeathSoundPath => "Sound/w52/SE_Mon_totemgolem_death01.wav";

        protected override string GetMoveSoundPath() =>
            MuGame.Random.Next(3) < 2
                ? "Sound/w52/SE_Mon_totemgolem_move01.wav"
                : "Sound/w52/SE_Mon_totemgolem_move02.wav";

        protected override string GetAttackSoundPath(int attackType) =>
            attackType == 2
                ? "Sound/w52/SE_Mon_totemgolem_attack02.wav"
                : "Sound/w52/SE_Mon_totemgolem_attack01.wav";

        public TotemGolem()
        {
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster134.bmd");
            await base.Load();
        }
    }
}
