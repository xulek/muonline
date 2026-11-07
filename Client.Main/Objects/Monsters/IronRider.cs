using Client.Main.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(352, "Iron Rider")]
    public class IronRider : SoundMonsterObject
    {
        protected override string AttackSoundPath => "Sound/w37/ir_attack-01.wav";
        protected override string DeathSoundPath => "Sound/w37/ir_death.wav";

        protected override string GetMoveSoundPath() =>
            MuGame.Random.Next(2) == 0 ? "Sound/w37/ir_idle-01.wav" : "Sound/w37/ir_idle-02.wav";

        protected override string GetAttackSoundPath(int attackType) =>
            MuGame.Random.Next(2) == 0 ? "Sound/w37/ir_attack-01.wav" : "Sound/w37/ir_attack-02.wav";

        public IronRider()
        {
            Scale = 1.3f;
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster109.bmd");
            await base.Load();
        }
    }
}
