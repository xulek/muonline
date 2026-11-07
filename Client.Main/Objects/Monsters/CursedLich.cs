using Client.Main.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(422, "Cursed Lich")]
    public class CursedLich : SoundMonsterObject
    {
        protected override string AttackSoundPath => "Sound/w52/SE_Mon_curserich_attack01.wav";
        protected override string DeathSoundPath => "Sound/w52/SE_Mon_curserich_death01.wav";

        protected override string GetMoveSoundPath() =>
            MuGame.Random.Next(100) == 0 ? "Sound/w52/SE_Mon_curserich_move01.wav" : null;

        public CursedLich()
        {
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster133.bmd");
            await base.Load();
        }
    }
}
