using Client.Main.Content;
using Client.Main.Objects.Effects;
using Microsoft.Xna.Framework;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(515, "Grand Wizard")]
    public class GrandWizard : SoundMonsterObject
    {
        protected override string MoveSoundPath => World?.MapId == 70 ? "Sound/w69w70w71w72/GaionKalein_move.wav" : null;
        protected override string DeathSoundPath => World?.MapId == 70 ? "Sound/w69w70w71w72/GrandWizard_death.wav" : null;
        private GlowingEyesEffect _eyeGlow;

        public GrandWizard()
        {
            Scale = 1.3f;
            // Eyes: bones 79,33 or 80,34 (two MoveEye calls in original)
            _eyeGlow = new GlowingEyesEffect { LeftEyeBone = 33, RightEyeBone = 79, GlowColor = new Color(60, 140, 255) };
            Children.Add(_eyeGlow);
        }

        public override void OnPerformAttack(int attackType = 1)
        {
            base.OnPerformAttack(attackType);
            if (World?.MapId != 70)
                return;

            if (attackType == 1)
            {
                PlayMonsterSound("Sound/eMeteorite.wav");
                PlayMonsterSound("Sound/eExplosion.wav");
            }
            else if (attackType == 2)
                PlayMonsterSound("Sound/w42/firepillar.wav");
            else if (attackType == 3)
                PlayMonsterSound("Sound/eExplosion.wav");
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster176.bmd");
            await base.Load();
        }
    }
}
