using Client.Main.Content;
using Client.Main.Objects.Effects;
using Microsoft.Xna.Framework;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(363, "Maya Hand Right")]
    public class MayaHandRight : SoundMonsterObject
    {
        private GlowingEyesEffect _eyeGlow;

        protected override string AttackSoundPath => "Sound/w39/maya_hand_attack-01.wav";

        protected override string GetAttackSoundPath(int attackType) =>
            attackType == 2 ? "Sound/w39/maya_hand_attack-02.wav" : AttackSoundPath;

        public MayaHandRight()
        {
            Scale = 2.28f;
            // Primary eyes: bones 5,20 (original MoveEye also has 31,42,53 — secondary magical nodes)
            _eyeGlow = new GlowingEyesEffect { LeftEyeBone = 20, RightEyeBone = 5, GlowColor = new Color(200, 60, 50) };
            Children.Add(_eyeGlow);
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster120.bmd");
            await base.Load();
        }
    }
}
