using Client.Main.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(456, "Ice Giant")]
    public class IceGiant : RaklionMonsterObject
    {
        protected virtual string ModelPath => "Monster/Monster148.bmd";
        protected override string AttackSoundPath => null;
        protected override string MoveSoundPath => "Sound/w58w59/IceGiant_move.wav";
        protected override string DeathSoundPath => "Sound/w58w59/IceGiant_death.wav";

        public IceGiant()
        {
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare(ModelPath);
            await base.Load();
        }
    }

    [NpcInfo(563, "Dark Giant")]
    public class DarkGiant : IceGiant
    {
        protected override string ModelPath => "Monster/Monster207.bmd";
    }
}
