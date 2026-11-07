using Client.Main.Content;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(511, "Gallia")]
    public class Gallia : SoundMonsterObject
    {
        private bool IsImperialGuardian => World?.MapId is >= 69 and <= 72;
        protected override string MoveSoundPath => IsImperialGuardian ? "Sound/w69w70w71w72/3Cato_move.wav" : null;
        protected override string DeathSoundPath => IsImperialGuardian ? "Sound/w69w70w71w72/2Vermont_death.wav" : null;
        protected override string GetAttackSoundPath(int attackType) => !IsImperialGuardian ? null : attackType == 1
            ? "Sound/w69w70w71w72/Jelint_attack1.wav"
            : "Sound/w69w70w71w72/4Gallia_attack2.wav";

        public Gallia() => Scale = 1.35f;

        public override void OnAppearAnimationStart()
        {
            base.OnAppearAnimationStart();
            if (IsImperialGuardian)
                PlayMonsterSound("Sound/w69w70w71w72/Jelint_rage.wav");
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare("Monster/Monster172.bmd");
            await base.Load();
        }
    }
}
