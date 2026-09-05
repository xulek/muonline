using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Models;
using Client.Main.Objects.Effects;
using Microsoft.Xna.Framework;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(53, "Golden Titan")]
    public class GoldenTitan : MonsterObject
    {
        private GlowingEyesEffect _eyeGlow;

        public GoldenTitan()
        {
            RenderShadow = true;
            Scale = 1.8f;
            MoveSpeed = 250f; // SourceMain5.2: default monster MoveSpeed (10 * 25 FPS)
            BlendMesh = 2;
            BlendMeshLight = 1f;

            // SourceMain5.2 RenderCharacter: GOLDEN_TITAN/GOLDEN_SOLDIER get the
            // RENDER_METAL | RENDER_BRIGHT pass with BITMAP_SHINY+1 (= Effect/Shiny02.jpg),
            // tinted (1.0, 0.5, 0.0) (PartObjectColor Color 0).
            BrightOverlay = 1f;
            BrightOverlayTexturePath = "Effect/Shiny02.jpg";
            BrightOverlayTint = new Vector3(1f, 0.5f, 0f);

            _eyeGlow = new GlowingEyesEffect
            {
                LeftEyeBone = 28,
                RightEyeBone = 27,
                GlowColor = Color.White,
                EnableTrail = false
            };
            Children.Add(_eyeGlow);
        }

        public override async Task Load()
        {
            // Model Loading Type: 39 -> File Number: 39 + 1 = 40
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster40.bmd"); // Titan's model
            await base.Load();
            SetActionSpeed(MonsterActionType.Stop1, 0.25f);
            SetActionSpeed(MonsterActionType.Stop2, 0.20f);
            SetActionSpeed(MonsterActionType.Walk, 0.22f);
            SetActionSpeed(MonsterActionType.Attack1, 0.33f);
            SetActionSpeed(MonsterActionType.Attack2, 0.33f);
            SetActionSpeed(MonsterActionType.Shock, 0.50f);
            SetActionSpeed(MonsterActionType.Die, 0.55f);

            // C++: Models[MODEL_MONSTER01+Type].BoneHead = 28; (Titan's bone head)
        }

        // Sounds are like Dark Knight according to C++ comment (which might be an error?)
        // Using Dark Knight sounds as per C++ comment
        protected override void OnIdle()
        {
            base.OnIdle();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            string sound = MuGame.Random.Next(2) == 0
                ? "Sound/mDarkKnight1.wav"
                : "Sound/mDarkKnight2.wav";
            SoundController.Instance.PlayBufferWithAttenuation(sound, Position, listenerPosition);
        }

        public override void OnPerformAttack(int attackType = 1)
        {
            base.OnPerformAttack(attackType);
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            string sound = MuGame.Random.Next(2) == 0
                ? "Sound/mDarkKnightAttack1.wav"
                : "Sound/mDarkKnightAttack2.wav";
            SoundController.Instance.PlayBufferWithAttenuation(sound, Position, listenerPosition);
        }

        public override void OnReceiveDamage()
        {
            base.OnReceiveDamage();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            string sound = MuGame.Random.Next(2) == 0
                ? "Sound/mDarkKnightAttack1.wav"
                : "Sound/mDarkKnightAttack2.wav";
            SoundController.Instance.PlayBufferWithAttenuation(sound, Position, listenerPosition);
        }

        public override void OnDeathAnimationStart()
        {
            base.OnDeathAnimationStart();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mDarkKnightDie.wav", Position, listenerPosition);
        }
    }
}
