using Client.Main.Content;
using Client.Main.Objects.Effects;
using Microsoft.Xna.Framework;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(508, "Destler")]
    public class Destler : SoundMonsterObject
    {
        private GlowingEyesEffect _eyeGlow;

        protected override string GetAttackSoundPath(int attackType) => attackType switch
        {
            2 => "Sound/w69w70w71w72/1Deasuler_attack2.wav",
            3 => "Sound/w69w70w71w72/1Deasuler_attack3.wav",
            _ => null,
        };

        public Destler()
        {
            // Eyes: bones 14 (R), 15 (L). Secondary positions 71-74 are blade nodes, not eyes.
            _eyeGlow = new GlowingEyesEffect { LeftEyeBone = 15, RightEyeBone = 14, GlowColor = new Color(40, 100, 255) };
            Children.Add(_eyeGlow);
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster169.bmd");
            await base.Load();
        }
    }
}
