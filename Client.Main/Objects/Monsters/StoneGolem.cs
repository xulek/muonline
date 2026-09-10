using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Models;
using Client.Main.Objects.Effects;
using Microsoft.Xna.Framework;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(32, "Stone Golem")]
    public class StoneGolem : MonsterObject
    {
        public StoneGolem()
        {
            RenderShadow = true;
            MoveSpeed = 250f; // SourceMain5.2: default monster MoveSpeed (10 * 25 FPS)
        }

        public override async Task Load()
        {
            // Model Loading Type: 25 -> File Number: 25 + 1 = 26
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster26.bmd");
            await base.Load();
            // C++: PlaySpeed *= 0.7f for actions Stop1 to Die (except Die itself) if Type == 25
            // Apply if needed based on action indices
            SetActionSpeed(MonsterActionType.Stop1, 0.25f * 0.7f);
            SetActionSpeed(MonsterActionType.Stop2, 0.20f * 0.7f);
            SetActionSpeed(MonsterActionType.Walk, 0.34f * 0.7f);
            SetActionSpeed(MonsterActionType.Attack1, 0.33f * 0.7f);
            SetActionSpeed(MonsterActionType.Attack2, 0.33f * 0.7f);
            SetActionSpeed(MonsterActionType.Shock, 0.5f * 0.7f);
            SetActionSpeed(MonsterActionType.Die, 0.55f);
            // C++: Models[MODEL_MONSTER01+Type].BoneHead = 5;
        }

        // Sound mapping based on C++ SetMonsterSound(MODEL_MONSTER01 + Type, 100, 101, 102, 103, 104);
        protected override void OnIdle()
        {
            base.OnIdle();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            string sound = MuGame.Random.Next(2) == 0
                ? "Sound/mGolem1.wav"
                : "Sound/mGolem2.wav";
            SoundController.Instance.PlayBufferWithAttenuation(sound, Position, listenerPosition);
        }

        public override void OnPerformAttack(int attackType = 1)
        {
            base.OnPerformAttack(attackType);
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            string sound = MuGame.Random.Next(2) == 0
                ? "Sound/mGolemAttack1.wav"
                : "Sound/mGolemAttack2.wav";
            SoundController.Instance.PlayBufferWithAttenuation(sound, Position, listenerPosition);
        }

        public override void OnReceiveDamage()
        {
            base.OnReceiveDamage();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            string sound = MuGame.Random.Next(2) == 0
                ? "Sound/mGolemAttack1.wav"
                : "Sound/mGolemAttack2.wav";
            SoundController.Instance.PlayBufferWithAttenuation(sound, Position, listenerPosition);
        }

        public override void OnDeathAnimationStart()
        {
            base.OnDeathAnimationStart();
            Blood = false;
            if (World != null)
            {
                var effect = new StoneGolemDeathRockEffect(Position, Angle);
                World.Objects.Add(effect);
                _ = effect.Load();
            }
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mGolemDie.wav", Position, listenerPosition);
        }
    }
}
