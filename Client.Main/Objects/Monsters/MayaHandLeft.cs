using Client.Main.Content;
using Client.Main.Objects.Effects;
using Microsoft.Xna.Framework;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(362, "Maya Hand Left")]
    public class MayaHandLeft : MayaHand
    {
        private GlowingEyesEffect _eyeGlow;

        protected override Vector3 AttackTint => new(0.25f, 0.55f, 1f);

        public MayaHandLeft()
        {
            Scale = 2.28f;
            // Primary eyes: bones 5,11 (original MoveEye also has 17,29,23 — secondary magical nodes)
            _eyeGlow = new GlowingEyesEffect { LeftEyeBone = 11, RightEyeBone = 5, GlowColor = new Color(30, 100, 255) };
            Children.Add(_eyeGlow);
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster119.bmd");
            await base.Load();
        }
    }
}
