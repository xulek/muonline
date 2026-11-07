using Client.Main.Content;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(68, "Molt")]
    public class Molt : SoundMonsterObject
    {
        protected override string AttackSoundPath => "Sound/mMoltAttack1.wav";
        protected override string DamageSoundPath => "Sound/mMoltAttack1.wav";
        protected override string DeathSoundPath => "Sound/mMoltDie.wav";

        protected override void OnIdle()
        {
            base.OnIdle();
            PlayMonsterSound("Sound/mMolt1.wav");
        }

        public Molt()
        {
            // SourceMain5.2 CreateMonster(MONSTER_MOLT): MODEL_MOLT, Scale = 1.4f
            Scale = 1.4f;
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster50.bmd");
            await base.Load();
        }
    }
}
