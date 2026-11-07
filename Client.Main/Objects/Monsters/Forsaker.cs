using Client.Main.Content;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(519, "Guardian Priest")]
    public class Forsaker : SoundMonsterObject
    {
        private bool IsImperialGuardian => World?.MapId is >= 69 and <= 72;
        protected override string DeathSoundPath => IsImperialGuardian ? "Sound/pDarkDeath.wav" : null;

        protected override void OnIdle()
        {
            base.OnIdle();
            if (IsImperialGuardian)
                PlayMonsterSound("Sound/w69w70w71w72/Priest_stay.wav");
        }

        protected override string GetAttackSoundPath(int attackType) => !IsImperialGuardian ? null : attackType switch
        {
            1 => "Sound/aThunder01.wav",
            2 => "Sound/w58w59/Selupan_cure.wav",
            _ => null,
        };

        public Forsaker()
        {
            Scale = 0.9f;
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare("Monster/Monster180.bmd");
            await base.Load();
        }
    }
}
