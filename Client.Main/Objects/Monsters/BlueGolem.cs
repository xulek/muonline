using Client.Main.Content;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(305, "Blue Golem")]
    public class BlueGolem : SoundMonsterObject
    {
        protected override string DeathSoundPath => "Sound/w34/bg_death.wav";

        protected override string GetMoveSoundPath() =>
            MuGame.Random.Next(2) == 0 ? "Sound/w34/bg_idle1.wav" : "Sound/w34/bg_idle2.wav";

        protected override string GetAttackSoundPath(int attackType) =>
            attackType == 2 ? "Sound/w34/bg_attack2.wav" : "Sound/w34/bg_attack1.wav";

        public BlueGolem()
        {
            RenderShadow = true;
            Scale = 1.8f;
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster102.bmd");
            await base.Load();
        }
    }
}
