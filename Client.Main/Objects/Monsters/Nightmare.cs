using Client.Main.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(361, "Nightmare")]
    public class Nightmare : SoundMonsterObject
    {
        protected override string AttackSoundPath => "Sound/w39/nightmare_attack-01.wav";
        protected override string DeathSoundPath => "Sound/w39/nightmare_death.wav";

        protected override string GetMoveSoundPath() =>
            MuGame.Random.Next(2) == 0 ? "Sound/w39/nightmare_idle-01.wav" : "Sound/w39/nightmare_idle-02.wav";

        protected override string GetAttackSoundPath(int attackType) => attackType switch
        {
            2 => "Sound/w39/nightmare_skill-01.wav",
            3 => "Sound/w39/nightmare_skill-02.wav",
            4 => "Sound/w39/nightmare_skill-03.wav",
            _ => AttackSoundPath
        };

        public Nightmare()
        {
            Scale = 1.6f;
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare("Monster/Monster122.bmd");
            await base.Load();
        }
    }
}
