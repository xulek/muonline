using Client.Main.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(466, "Cursed Goblin")]
    public class CursedGoblin : SoundMonsterObject
    {
        protected override string DeathSoundPath => "Sound/mGoblinDie.wav";
        protected override void OnIdle()
        {
            base.OnIdle();
            PlayMonsterSound(MuGame.Random.Next(2) == 0 ? "Sound/mGoblin1.wav" : "Sound/mGoblin2.wav");
        }

        protected override string GetAttackSoundPath(int attackType) =>
            MuGame.Random.Next(2) == 0 ? "Sound/mGoblinAttack1.wav" : "Sound/mGoblinAttack2.wav";

        protected override string GetDamageSoundPath() => GetAttackSoundPath(1);

        public CursedGoblin()
        {
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster157.bmd");
            await base.Load();
        }
    }
}
