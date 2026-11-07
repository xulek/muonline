using Client.Main.Content;
using Client.Main.Objects.Effects;
using Microsoft.Xna.Framework;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(290, "Lizard Warrior")]
    public class LizardWarrior : SoundMonsterObject
    {
        private GlowingEyesEffect _eyeGlow;

        protected override string DeathSoundPath => "Sound/w31/mLWdeath.wav";

        protected override string GetMoveSoundPath() =>
            MuGame.Random.Next(2) == 0 ? "Sound/w31/mLWidle1.wav" : "Sound/w31/mLWidle2.wav";

        protected override string GetAttackSoundPath(int attackType) =>
            MuGame.Random.Next(2) == 0 ? "Sound/w31/mLWattack1.wav" : "Sound/w31/mLWattack2.wav";

        public LizardWarrior()
        {
            // Eyes: bones 42 (L), 43 (R) — same model as LizardKing
            _eyeGlow = new GlowingEyesEffect { LeftEyeBone = 42, RightEyeBone = 43, GlowColor = new Color(70, 180, 255) };
            Children.Add(_eyeGlow);
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster82.bmd");
            await base.Load();
        }
    }
}
