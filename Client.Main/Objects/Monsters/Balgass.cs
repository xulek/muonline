using Client.Main.Content;
using Client.Main.Objects.Effects;
using Microsoft.Xna.Framework;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(349, "Balgass")]
    public class Balgass : SoundMonsterObject
    {
        private GlowingEyesEffect _eyeGlow;

        protected override string AttackSoundPath => "Sound/w35/balga_at1.wav";
        protected override string DeathSoundPath => "Sound/w35/balga_death.wav";

        protected override string GetMoveSoundPath() =>
            MuGame.Random.Next(2) == 0 ? "Sound/w35/balga_idle1.wav" : "Sound/w35/balga_idle2.wav";

        protected override string GetAttackSoundPath(int attackType) => attackType switch
        {
            2 => "Sound/w35/balga_at2.wav",
            3 => "Sound/w35/balga_skill1.wav",
            4 => "Sound/w35/balga_skill2.wav",
            _ => AttackSoundPath
        };

        public Balgass()
        {
            _eyeGlow = new GlowingEyesEffect { LeftEyeBone = 10, RightEyeBone = 9, GlowColor = new Color(50, 130, 255) };
            Children.Add(_eyeGlow);
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster90.bmd");
            await base.Load();
        }
    }
}