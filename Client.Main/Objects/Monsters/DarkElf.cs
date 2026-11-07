using Client.Main.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(340, "Dark Elf")]
    public class DarkElf : SoundMonsterObject
    {
        protected override string AttackSoundPath => "Sound/w35/darkelf_at1.wav";
        protected override string DeathSoundPath => "Sound/w35/darkelf_death.wav";

        protected override string GetMoveSoundPath() =>
            MuGame.Random.Next(2) == 0 ? "Sound/w35/darkelf_idle1.wav" : "Sound/w35/darkelf_idle2.wav";

        protected override string GetAttackSoundPath(int attackType) => attackType switch
        {
            2 => "Sound/w35/darkelf_at2.wav",
            3 => "Sound/w35/darkelf_skill1.wav",
            4 => "Sound/w35/darkelf_skill2.wav",
            _ => AttackSoundPath
        };

        public DarkElf()
        {
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster127.bmd");
            await base.Load();
        }
    }
}
