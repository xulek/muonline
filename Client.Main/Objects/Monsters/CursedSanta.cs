using Client.Main.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(476, "Cursed Santa")]
    internal class CursedSanta : SoundMonsterObject
    {
        protected override string AttackSoundPath => "Sound/xmas/DarkSanta_Attack01.wav";
        protected override string DeathSoundPath => "Sound/xmas/DarkSanta_Death01.wav";

        protected override string GetDamageSoundPath() =>
            MuGame.Random.Next(2) == 0 ? "Sound/xmas/DarkSanta_Damage01.wav" : "Sound/xmas/DarkSanta_Damage02.wav";

        protected override string GetMoveSoundPath() =>
            MuGame.Random.Next(2) == 0 ? "Sound/xmas/DarkSanta_Walk01.wav" : "Sound/xmas/DarkSanta_Walk02.wav";

        protected override void OnIdle()
        {
            base.OnIdle();
            PlayMonsterSound(MuGame.Random.Next(2) == 0
                ? "Sound/xmas/DarkSanta_Idle01.wav"
                : "Sound/xmas/DarkSanta_Idle02.wav");
        }

        public CursedSanta()
        {
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster156.bmd");
            await base.Load();
        }
    }
}
