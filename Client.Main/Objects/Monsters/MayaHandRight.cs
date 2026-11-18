using Client.Main.Content;
using Client.Main.Objects.Effects;
using Microsoft.Xna.Framework;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(363, "Maya Hand Right")]
    public class MayaHandRight : MayaHand
    {
        private GlowingEyesEffect _eyeGlow;

        protected override Vector3 AttackTint => new(1f, 0.56f, 0.32f);

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
