using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Models;
using Client.Main.Objects.Effects;
using Client.Main.Objects.Player;
using Microsoft.Xna.Framework;
using System.Threading.Tasks;

using Joints = Client.Main.Objects.Effects.Joints;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(42, "Red Dragon")]
    public class RedDragon : MonsterObject
    {
        private readonly WeaponObject _bossHead;
        private readonly WeaponObject _princess;

        public RedDragon()
        {
            RenderShadow = false; // SourceMain5.2 RenderCharacter: MONSTER_RED_DRAGON excluded from blob shadow pass
            Scale = 1.3f; // Set according to C++ Setting_Monster
            MoveSpeed = 250f; // SourceMain5.2: default monster MoveSpeed (10 * 25 FPS)

            // SourceMain5.2 RenderCharacter(MONSTER_RED_DRAGON): two link objects —
            // MODEL_BOSS_HEAD on bone 9 (action 1, PlaySpeed 0.2) and MODEL_PRINCESS
            // on bone 61 rendered at scale 0.9.
            _bossHead = new WeaponObject
            {
                LinkParentAnimation = false,
                ParentBoneLink = 9,
                RenderShadow = false
            };
            _princess = new WeaponObject
            {
                LinkParentAnimation = false,
                ParentBoneLink = 61,
                Scale = 0.7f, // 0.9 absolute / 1.3 parent scale, per original RenderLinkObject override
                RenderShadow = false
            };
            Children.Add(_bossHead);
            Children.Add(_princess);
        }

        public override async Task Load()
        {
            // Model Loading Type: 31 -> File Number: 31 + 1 = 32
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster32.bmd");
            _bossHead.Model = await BMDLoader.Instance.Prepare($"Object6/BossHead1.bmd");   // MODEL_BOSS_HEAD (MapManager.cpp:116)
            _princess.Model = await BMDLoader.Instance.Prepare($"Object6/Princess1.bmd");   // MODEL_PRINCESS  (MapManager.cpp:117)
            await base.Load();

            // Original link-object action speed: PlaySpeed = 0.2 (x2 port factor)
            if (_bossHead.Model?.Actions != null && _bossHead.Model.Actions.Length > 1 && _bossHead.Model.Actions[1] != null)
                _bossHead.Model.Actions[1].PlaySpeed = 0.4f;

            SetActionSpeed(MonsterActionType.Stop1, 0.25f * 0.4f);
            SetActionSpeed(MonsterActionType.Stop2, 0.8f);
            SetActionSpeed(MonsterActionType.Walk, 0.34f * 0.4f);
            SetActionSpeed(MonsterActionType.Attack1, 0.5f);
            SetActionSpeed(MonsterActionType.Attack2, 0.7f);
            SetActionSpeed(MonsterActionType.Shock, 0.50f * 0.4f);
            SetActionSpeed(MonsterActionType.Die, 0.55f);
        }

        // Sound mapping based on C++ SetMonsterSound(MODEL_MONSTER01 + Type, 123, 123, 124, 124, 125); (Uses Yeti/Bull sounds)
        protected override void OnIdle()
        {
            base.OnIdle();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mYeti1.wav", Position, listenerPosition); // Index 0 -> Sound 123
        }

        public override void OnPerformAttack(int attackType = 1)
        {
            base.OnPerformAttack(attackType);
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mBullAttack1.wav", Position, listenerPosition); // Index 2 -> Sound 124

            // SourceMain5.2 AttackEffect MONSTER_RED_DRAGON is fully gated on
            // Skill == AT_SKILL_BOSS (the MODEL_FIRE rain below it too, which has
            // no port equivalent yet).
            if (attackType != 2)
                return;

            if (World == null || !TryConsumeAttackEffectWindow())
                return;

            SoundController.Instance.PlayBufferWithAttenuation("Sound/eMeteorite.wav", Position, listenerPosition);

            // Boss branch (CheckAttackTime(1)): three MODEL_FIRE sub2 from bone 11
            // with pitch/yaw offsets (-20,-30), (-30,0), (-20,+30) degrees.
            Vector3 mouth = GetBoneWorldPosition(11);
            Span<(float pitch, float yaw)> shots = stackalloc (float, float)[]
            {
                (-20f, -30f),
                (-30f, 0f),
                (-20f, 30f)
            };
            for (int i = 0; i < shots.Length; i++)
            {
                var fire = Joints.SourceProjectileEffect.Create(
                    Joints.SourceProjectileKind.FireCone,
                    mouth,
                    new Vector3(shots[i].pitch, 0f, MathHelper.ToDegrees(Angle.Z) + shots[i].yaw));
                World.Objects.Add(fire);
                _ = fire.Load();
            }
        }

        private Vector3 GetBoneWorldPosition(int boneIndex)
        {
            Matrix[] bones = GetBoneTransforms();
            if (bones == null || boneIndex < 0 || boneIndex >= bones.Length)
                return WorldPosition.Translation;

            return (bones[boneIndex] * WorldPosition).Translation;
        }

        public override void OnReceiveDamage()
        {
            base.OnReceiveDamage();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mBullAttack1.wav", Position, listenerPosition);
        }

        public override void OnDeathAnimationStart()
        {
            base.OnDeathAnimationStart();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mYetiDie.wav", Position, listenerPosition); // Index 4 -> Sound 125
        }
    }
}
