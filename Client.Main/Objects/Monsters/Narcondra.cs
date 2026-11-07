using Client.Main.Content;
using Client.Main.Objects.Effects;
using Microsoft.Xna.Framework;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(576, "Narcondra")]
    public class NarCondra : SoundMonsterObject
    {
        private GlowingEyesEffect _eyeGlow;
        private bool _walkSoundToggle;

        protected override string AttackSoundPath => "Sound/Karutan/NarCondra_attack.wav";
        protected override string DeathSoundPath => "Sound/Karutan/Condra_death.wav";

        protected override string GetMoveSoundPath()
        {
            _walkSoundToggle = !_walkSoundToggle;
            return _walkSoundToggle ? "Sound/Karutan/Condra_move1.wav" : "Sound/Karutan/Condra_move2.wav";
        }

        public NarCondra()
        {
            // Both eyes use bone 9 in original (single central eye / cyclops type)
            _eyeGlow = new GlowingEyesEffect { LeftEyeBone = 9, RightEyeBone = 9, GlowColor = new Color(60, 150, 255) };
            Children.Add(_eyeGlow);
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster217.bmd");
            await base.Load();
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
            BlendMesh = 4;
        }
    }
}
