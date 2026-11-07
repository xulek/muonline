using Client.Main.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(313, "Werewolf")]
    public class Werewolf : SoundMonsterObject
    {
        protected override string AttackSoundPath => "Sound/w35/ww_attack1.wav";
        protected override string DeathSoundPath => "Sound/w35/ww_death.wav";

        protected override string GetMoveSoundPath() =>
            MuGame.Random.Next(2) == 0 ? "Sound/w35/ww_idle1.wav" : "Sound/w35/ww_idle2.wav";

        protected override string GetAttackSoundPath(int attackType) =>
            attackType == 2 ? "Sound/w35/ww_attack2.wav" : AttackSoundPath;

        public Werewolf()
        {
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster96.bmd");
            await base.Load();
        }
    }
}
