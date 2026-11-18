using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Models;
using Client.Main.Objects.Effects;
using Microsoft.Xna.Framework;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters.BloodCastle
{
    [NpcInfo(87, "Giant Ogre")]
    public class GiantOgre1 : MonsterObject
    {
        public GiantOgre1()
        {
            Scale = 0.8f;
            MoveSpeed = 250f;
            Children.Add(new MonsterBoneFireEffect
            {
                SourceBone = 6,
                EmissionRate = 6.25f,
                EmitOnlyDuringAttack = true,
                ParticleScaleMin = 0.28f,
                ParticleScaleMax = 0.32f
            });
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster59.bmd");
            await base.Load();
            SetActionSpeed(MonsterActionType.Stop1, 0.25f);
            SetActionSpeed(MonsterActionType.Stop2, 0.20f);
            SetActionSpeed(MonsterActionType.Walk, 0.34f);
            SetActionSpeed(MonsterActionType.Attack1, 0.33f);
            SetActionSpeed(MonsterActionType.Attack2, 0.33f);
            SetActionSpeed(MonsterActionType.Shock, 0.50f);
            SetActionSpeed(MonsterActionType.Die, 0.55f);
        }

        protected override void OnIdle()
        {
            base.OnIdle();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mHunter2.wav", Position, listenerPosition);
        }

        public override void OnPerformAttack(int attackType = 1)
        {
            base.OnPerformAttack(attackType);
            PlayAttackSound("Sound/mBlackSkullAttack.wav");
        }

        protected override void OnAttackProgress(in AttackProgress progress)
        {
            if (!progress.CrossedTick(13) || World == null)
                return;

            Matrix[] bones = GetBoneTransforms();
            if (bones == null || bones.Length <= 6)
                return;

            Vector3 origin = Vector3.Transform(new Vector3(60f, 30f, 0f), bones[6] * WorldPosition);
            MonsterAttackModelEffect.Spawn(World, "Skill/Fire01.bmd", origin, Angle,
                new Vector3(1f, 0.55f, 0.23f), 0.65f, 0.9f, speed: 225f, animated: true);
        }

        public override void OnReceiveDamage()
        {
            base.OnReceiveDamage();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mBlackSkullAttack.wav", Position, listenerPosition);
        }

        public override void OnDeathAnimationStart()
        {
            base.OnDeathAnimationStart();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mGhaintOrgerDie.wav", Position, listenerPosition);
        }
    }
}
