using Client.Main.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(360, "Dreadfear")]
    public class Dreadfear : SoundMonsterObject
    {
        protected override string AttackSoundPath => "Sound/w38/dred_attack-01.wav";
        protected override string DeathSoundPath => "Sound/w38/dred_death.wav";

        protected override string GetMoveSoundPath() =>
            MuGame.Random.Next(2) == 0 ? "Sound/w38/dred_idle-01.wav" : "Sound/w38/dred_idle-02.wav";

        protected override string GetAttackSoundPath(int attackType) =>
            attackType == 2 ? "Sound/w38/dred_attack-02.wav" : AttackSoundPath;

        public Dreadfear()
        {
            Scale = 1.3f;
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster117.bmd");
            await base.Load();
        }
    }
}
