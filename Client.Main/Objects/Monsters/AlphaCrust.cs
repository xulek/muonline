using Client.Main.Content;
using Client.Main.Objects.Effects;
using Client.Main.Objects.Player;
using Client.Main.Core.Utilities;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Models;
using Microsoft.Xna.Framework;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(74, "Alpha Crust")]
    public class AlphaCrust : MonsterObject
    {
        private WeaponObject _rightHandWeapon;
        private WeaponObject _leftHandWeapon;
        private SourceMonsterEyeEffect _eyeGlow;

        public AlphaCrust()
        {
            Scale = 1.3f;
            MoveSpeed = 250f;
            BlendMesh = 1;
            BlendMeshLight = 1.0f;

            EnableCustomShader = true;
            SimpleColorMode = true;
            GlowColor = new Vector3(1.0f, 1.4f, 1.6f);
            GlowIntensity = 3.0f;

            _rightHandWeapon = new WeaponObject { LinkParentAnimation = false, ParentBoneLink = 36, ItemLevel = 9 };
            _leftHandWeapon = new WeaponObject { LinkParentAnimation = false, ParentBoneLink = 45, ItemLevel = 9 };
            Children.Add(_rightHandWeapon);
            Children.Add(_leftHandWeapon);

            // Eyes: bones 26 (L), 27 (R), size 2.0 — original RenderEye(o, 26, 27, 2.0f)
            _eyeGlow = new SourceMonsterEyeEffect { LeftEyeBone = 26, RightEyeBone = 27, SpriteScale = 2.0f };
            Children.Add(_eyeGlow);
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster53.bmd");
            var item = ItemDatabase.GetItemDefinition(0, 18); // Thunder Blade
            if (item != null)
                _rightHandWeapon.Model = await BMDLoader.Instance.Prepare(item.TexturePath);
            var shield = ItemDatabase.GetItemDefinition(6, 14); // Legendary Shield
            if (shield != null)
                _leftHandWeapon.Model = await BMDLoader.Instance.Prepare(shield.TexturePath);

            await base.Load();
            SetActionSpeed(MonsterActionType.Stop1, 0.25f);
            SetActionSpeed(MonsterActionType.Stop2, 0.20f);
            SetActionSpeed(MonsterActionType.Walk, 0.34f);
            SetActionSpeed(MonsterActionType.Attack1, 0.33f);
            SetActionSpeed(MonsterActionType.Attack2, 0.33f);
            SetActionSpeed(MonsterActionType.Shock, 0.50f);
            SetActionSpeed(MonsterActionType.Die, 0.22f);
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
            TextureCoordinateOffsetMeshIndex = BlendMesh;
            TextureCoordinateOffset = new Vector2(
                -((long)gameTime.TotalGameTime.TotalMilliseconds % 10000L) * 0.0004f,
                0f);
        }

        protected override void OnIdle()
        {
            base.OnIdle();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mMegaCrust1.wav", Position, listenerPosition);
        }

        public override void OnPerformAttack(int attackType = 1)
        {
            base.OnPerformAttack(attackType);
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mMegaCrustAttack1.wav", Position, listenerPosition);
        }

        protected override void OnAttackProgress(in AttackProgress progress)
        {
            if (progress.Action is MonsterActionType.Attack1 or MonsterActionType.Attack2 && progress.CrossedTick(5))
                InfernoEffect.SpawnMonsterAttack(this);
        }

        public override void OnReceiveDamage()
        {
            base.OnReceiveDamage();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mMegaCrustAttack1.wav", Position, listenerPosition);
        }

        public override void OnDeathAnimationStart()
        {
            base.OnDeathAnimationStart();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mMegaCrustDie.wav", Position, listenerPosition);
        }
    }
}
