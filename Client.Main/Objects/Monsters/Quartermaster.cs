using Client.Main.Content;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(512, "Quarter Master")]
    public class Quartermaster : SoundMonsterObject
    {
        private bool IsImperialGuardian => World?.MapId is >= 69 and <= 72;
        protected override bool UseImperialFootsteps => IsImperialGuardian;
        protected override string DeathSoundPath => IsImperialGuardian ? "Sound/w69w70w71w72/Jelint_death.wav" : null;
        protected override string GetAttackSoundPath(int attackType) => !IsImperialGuardian ? null : attackType == 1
            ? "Sound/w69w70w71w72/Jelint_attack1.wav"
            : "Sound/w69w70w71w72/QuaterMaster_attack2.wav";

        public Quartermaster() => Scale = 1.27f;

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare("Monster/Monster173.bmd");
            await base.Load();
        }
    }
}
