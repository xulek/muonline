using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Models;
using Client.Main.Objects.Effects;
using Microsoft.Xna.Framework;
using System.Threading.Tasks;

using Joints = Client.Main.Objects.Effects.Joints;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(49, "Hydra")]
    public class Hydra : MonsterObject
    {
        public Hydra()
        {
            RenderShadow = true;
            Scale = 1.0f; // Set according to C++ Setting_Monster
            MoveSpeed = 250f; // SourceMain5.2: default monster MoveSpeed (10 * 25 FPS)
            BlendMesh = 5;
            BlendMeshLight = 0.0f;
            Children.Add(new MonsterBoneSpriteEffect
            {
                BoneIndices = new[] { 63 },
                PrimaryTexturePath = "Effect/lightning2.jpg",
                PrimaryScale = 1f,
                SecondaryTexturePath = "Effect/Shiny03.jpg",
                SecondaryScale = 4f
            });
        }

        public override async Task Load()
        {
            // Model Loading Type: 37 -> File Number: 37 + 1 = 38
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster38.bmd");
            await base.Load();

            SetActionSpeed(MonsterActionType.Stop1, 0.25f * 0.4f);
            SetActionSpeed(MonsterActionType.Stop2, 0.20f * 0.4f);
            SetActionSpeed(MonsterActionType.Walk, 0.34f * 0.4f);
            SetActionSpeed(MonsterActionType.Attack1, 0.15f);
            SetActionSpeed(MonsterActionType.Attack2, 0.15f);
            SetActionSpeed(MonsterActionType.Shock, 0.50f * 0.4f);
            SetActionSpeed(MonsterActionType.Die, 0.20f);
        }

        // Sound mapping based on C++ SetMonsterSound(MODEL_MONSTER01 + Type, 141, 141, 142, 142, 141);
        protected override void OnIdle()
        {
            base.OnIdle();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mHydra1.wav", Position, listenerPosition); // Index 0 -> Sound 141
        }

        public override void OnPerformAttack(int attackType = 1)
        {
            base.OnPerformAttack(attackType);
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mHydraAttack1.wav", Position, listenerPosition); // Index 2 -> Sound 142

            if (World == null || !TryConsumeAttackEffectWindow())
                return;

            if (attackType == 1)
            {
                // SourceMain5.2 AttackEffect MONSTER_HYDRA normal branch
                // (attackTime % 5 == 1): BITMAP_BOSS_LASER+1 (orange) fired from
                // bone 63 along the facing direction.
                Vector3 mouth = GetBoneWorldPosition(63);
                var laser = Joints.SourceProjectileEffect.Create(
                    Joints.SourceProjectileKind.BossLaserOrange,
                    mouth,
                    new Vector3(0f, 0f, MathHelper.ToDegrees(Angle.Z)));
                World.Objects.Add(laser);
                _ = laser.Load();
            }
            else
            {
                // Boss branch (CheckAttackTime(1)): 9x BITMAP_BOSS_LASER (blue),
                // yaw starting +20 deg stepping +40 deg, origin +50 Z.
                for (int i = 0; i < 9; i++)
                {
                    float yawDeg = MathHelper.ToDegrees(Angle.Z) + 20f + i * 40f;
                    var laser = Joints.SourceProjectileEffect.Create(
                        Joints.SourceProjectileKind.BossLaser,
                        Position + Vector3.UnitZ * 50f,
                        new Vector3(0f, 0f, yawDeg));
                    World.Objects.Add(laser);
                    _ = laser.Load();
                }
            }
        }

        private Vector3 GetBoneWorldPosition(int boneIndex)
        {
            Matrix[] bones = GetBoneTransforms();
            if (bones == null || boneIndex < 0 || boneIndex >= bones.Length)
                return WorldPosition.Translation;

            return (bones[boneIndex] * WorldPosition).Translation;
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
            if (Status != GameControlStatus.Ready)
                return;

            float amount = (float)gameTime.ElapsedGameTime.TotalSeconds * 25f * 0.1f;
            bool attacking = CurrentAction >= (int)MonsterActionType.Attack1 &&
                CurrentAction <= (int)MonsterActionType.Attack2;
            BlendMeshLight = MathHelper.Clamp(
                BlendMeshLight + (attacking ? amount : -amount),
                0f,
                1f);
        }

        public override void OnDeathAnimationStart()
        {
            base.OnDeathAnimationStart();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mHydra1.wav", Position, listenerPosition); // Index 4 -> Sound 141
        }
    }
}
