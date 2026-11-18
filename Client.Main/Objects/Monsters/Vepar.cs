using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Models;
using Client.Main.Objects.Effects;
using Microsoft.Xna.Framework;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(46, "Vepar")]
    public class Vepar : MonsterObject
    {
        private static readonly int[] HandBones = { 30, 39 };

        public Vepar()
        {
            RenderShadow = true;
            Scale = 1.0f; // Set according to C++ Setting_Monster
            MoveSpeed = 250f; // SourceMain5.2: default monster MoveSpeed (10 * 25 FPS)
            Children.Add(new MonsterBoneSpriteEffect
            {
                BoneIndices = HandBones,
                PrimaryTexturePath = "Effect/lightning2.jpg",
                PrimaryScale = 0.5f,
                SecondaryTexturePath = "Effect/Spark02.jpg",
                SecondaryScale = 4f,
                TertiaryTexturePath = "Effect/Shiny03.jpg",
                TertiaryScale = 2f
            });
        }

        public override async Task Load()
        {
            // Model Loading Type: 34 -> File Number: 34 + 1 = 35
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster35.bmd");
            await base.Load();

            SetActionSpeed(MonsterActionType.Stop1, 0.25f);
            SetActionSpeed(MonsterActionType.Stop2, 0.20f);
            SetActionSpeed(MonsterActionType.Walk, 0.34f);
            SetActionSpeed(MonsterActionType.Attack1, 0.50f);
            SetActionSpeed(MonsterActionType.Attack2, 0.50f);
            SetActionSpeed(MonsterActionType.Shock, 0.50f);
            SetActionSpeed(MonsterActionType.Die, 0.55f);
            // C++: b->BoneHead = 20;//인어
        }

        // Sound mapping based on C++ SetMonsterSound(MODEL_MONSTER01 + Type, 132, 133, 104, 104, 133)
        protected override void OnIdle()
        {
            base.OnIdle();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            string sound = MuGame.Random.Next(2) == 0
                ? "Sound/mBepar1.wav"
                : "Sound/mBepar2.wav";
            SoundController.Instance.PlayBufferWithAttenuation(sound, Position, listenerPosition);
        }

        public override void OnPerformAttack(int attackType = 1)
        {
            base.OnPerformAttack(attackType);
            PlayAttackSound("Sound/mGolemDie.wav");
        }

        protected override void OnAttackProgress(in AttackProgress progress)
        {
            if (!progress.SkillStarted || progress.SkillId != EnergyBallAttackSkill ||
                World is not { } world || !TryGetAttackTarget(out var target))
                return;

            PlayAttackSound("Sound/sEvil.wav");
            var effect = new VeparAttackEffect(this, target, HandBones, progress.Action);
            world.Objects.Add(effect);
            _ = effect.Load();
        }

        public override void OnReceiveDamage()
        {
            base.OnReceiveDamage();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mGolemDie.wav", Position, listenerPosition);
        }

        public override void OnDeathAnimationStart()
        {
            base.OnDeathAnimationStart();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mBepar2.wav", Position, listenerPosition); // Index 4 -> Sound 133
        }
    }
}
