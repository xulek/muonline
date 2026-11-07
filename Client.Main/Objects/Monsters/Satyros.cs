using Client.Main.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(353, "Satyros")]
    public class Satyros : SoundMonsterObject
    {
        protected override string AttackSoundPath => "Sound/w37/sati_attack-01.wav";
        protected override string DeathSoundPath => "Sound/w37/sati_death.wav";

        protected override string GetMoveSoundPath() =>
            MuGame.Random.Next(2) == 0 ? "Sound/w37/sati_idle-01.wav" : "Sound/w37/sati_idle-02.wav";

        protected override string GetAttackSoundPath(int attackType) =>
            MuGame.Random.Next(2) == 0 ? "Sound/w37/sati_attack-01.wav" : "Sound/w37/sati_attack-02.wav";

        public Satyros()
        {
            Scale = 1.3f;
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster110.bmd");
            await base.Load();
        }
    }
}
