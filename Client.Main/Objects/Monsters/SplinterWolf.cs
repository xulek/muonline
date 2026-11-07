using Client.Main.Content;
using Client.Main.Objects.Effects;
using Microsoft.Xna.Framework;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(351, "Splinter Wolf")]
    public class SplinterWolf : SoundMonsterObject
    {
        private GlowingEyesEffect _eyeGlow;

        protected override string AttackSoundPath => "Sound/w37/swolf_attack-01.wav";
        protected override string DeathSoundPath => "Sound/w37/swolf_death.wav";

        protected override string GetMoveSoundPath() =>
            MuGame.Random.Next(2) == 0 ? "Sound/w37/swolf_idle-01.wav" : "Sound/w37/swolf_idle-02.wav";

        protected override string GetAttackSoundPath(int attackType) =>
            MuGame.Random.Next(2) == 0 ? "Sound/w37/swolf_attack-01.wav" : "Sound/w37/swolf_attack-02.wav";

        public SplinterWolf()
        {
            Scale = 0.8f;

            _eyeGlow = new GlowingEyesEffect { LeftEyeBone = 17, RightEyeBone = 16, GlowColor = new Color(45, 120, 255) };
            Children.Add(_eyeGlow);
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster108.bmd");
            await base.Load();
        }
    }
}
