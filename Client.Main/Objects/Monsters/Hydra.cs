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
        private const int LaserMesh = 5;
        private const int TextureReferenceMesh = 6; // Degenerate triangle that supplies the alternate body texture.

        public Hydra()
        {
            RenderShadow = true;
            Scale = 1.0f; // Set according to C++ Setting_Monster
            MoveSpeed = 250f; // SourceMain5.2: default monster MoveSpeed (10 * 25 FPS)
            BlendMesh = LaserMesh;
            BlendMeshLight = 0.0f;
            HiddenMesh = LaserMesh;
            Children.Add(new MonsterBoneSpriteEffect
            {
                BoneIndices = new[] { 63 },
                PrimaryTexturePath = "Effect/lightning2.jpg",
                PrimaryScale = 1f,
                SecondaryTexturePath = "Effect/Shiny03.jpg",
                SecondaryScale = 4f
            });
        }

        protected override bool ShouldRenderMesh(int mesh) => mesh != TextureReferenceMesh;

        protected override bool ShouldIncludeMeshInBounds(int meshIndex) =>
            meshIndex is not (LaserMesh or TextureReferenceMesh);

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
            PlayAttackSound("Sound/mHydraAttack1.wav"); // Index 2 -> Sound 142
        }

        protected override void OnAttackProgress(in AttackProgress progress)
        {
            if (World is not { } world)
                return;

            float yaw = MathHelper.ToDegrees(Angle.Z);
            // The reference checks 1, 6 and 11 before its 15-tick attack limit.
            for (int tick = 1; tick < 15; tick += 5)
            {
                if (progress.CrossedTick(tick))
                    Joints.SourceProjectileEffect.Spawn(world, Joints.SourceProjectileKind.BossLaserOrange,
                        GetBoneWorldPosition(63), new Vector3(0f, 0f, yaw));
            }

            // A confirmed boss skill gets its own ring marker, independent of the orange burst.
            if (progress.SkillId == BossAttackSkill && progress.SkillStarted)
            {
                Vector3 origin = Position + Vector3.UnitZ * 50f;
                for (int i = 0; i < 9; i++)
                    Joints.SourceProjectileEffect.Spawn(world, Joints.SourceProjectileKind.BossLaser,
                        origin, new Vector3(0f, 0f, yaw + 20f + i * 40f));
            }
        }

        public override void OnReceiveDamage()
        {
            base.OnReceiveDamage();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mHydraAttack1.wav", Position, listenerPosition);
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

            bool attacking = !IsDead && (MonsterActionType)CurrentAction is MonsterActionType.Attack1 or MonsterActionType.Attack2;
            HiddenMesh = attacking ? -1 : LaserMesh;
            float amount = (float)gameTime.ElapsedGameTime.TotalSeconds * 25f * 0.1f;
            BlendMeshLight = attacking ? MathHelper.Clamp(BlendMeshLight + amount, 0f, 1f) : 0f;
        }

        public override void OnDeathAnimationStart()
        {
            base.OnDeathAnimationStart();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mHydra1.wav", Position, listenerPosition); // Index 4 -> Sound 141
        }
    }
}
