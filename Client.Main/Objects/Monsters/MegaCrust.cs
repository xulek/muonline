using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Objects.Effects;
using Client.Main.Objects.Effects.Cloth;
using Client.Main.Objects.Player;
using Client.Main.Core.Utilities;
using Client.Main.Models;
using Microsoft.Xna.Framework;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(71, "Mega Crust")]
    public class MegaCrust : MonsterObject
    {
        private WeaponObject _rightHandWeapon;
        private WeaponObject _leftHandWeapon;
        private SourceMonsterEyeEffect _eyeGlow;
        private readonly MonsterMegaCrustGlowEffect _weaponGlow;

        public MegaCrust()
        {
            Scale = 1.1f;
            MoveSpeed = 250f;
            BlendMesh = 1;
            BlendMeshLight = 1.0f;
            _rightHandWeapon = new WeaponObject { LinkParentAnimation = false, ParentBoneLink = 36, ItemLevel = 5 };
            _leftHandWeapon = new WeaponObject { LinkParentAnimation = false, ParentBoneLink = 45, ItemLevel = 0 };
            Children.Add(_rightHandWeapon);
            Children.Add(_leftHandWeapon);

            // Eyes: bones 26 (L), 27 (R), size 2.0 — original RenderEye(o, 26, 27, 2.0f)
            _eyeGlow = new SourceMonsterEyeEffect { LeftEyeBone = 26, RightEyeBone = 27, SpriteScale = 2.0f };
            Children.Add(_eyeGlow);

            // Thunder Blade (Weapon[0]) and Legendary Shield (Weapon[1]) glows, RenderCharacter weapon loop.
            _weaponGlow = new MonsterMegaCrustGlowEffect();
            Children.Add(_weaponGlow);

            // Cloth: CPhysicsCloth::Create(o, 19, 0, 10, 0, 5, 15, 30, 300, BITMAP_ROBE+3, BITMAP_ROBE+3, PCT_RUBBER | PCT_MASK_ALPHA)
            // (ZzzCharacter.cpp:8630-8648), BITMAP_ROBE+3 = Monster/iui02.tga (ZzzOpenData.cpp:3497). No collision spheres.
            Children.Add(new PhysicsClothObject(new ClothDefinition
            {
                Bone = 19,
                Offset = new Vector3(0f, 10f, 0f),
                NumHor = 5,
                NumVer = 15,
                Width = 30f,
                Height = 300f,
                Type = PhysicsClothType.Rubber | PhysicsClothType.DrawAlpha,
                FrontTexture = "Monster/iui02.tga",
            }));
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
            // Legendary shield: BlendMesh 1 with random BlendMeshTexCoordU/V every frame (ZzzObject.cpp:5333-5337).
            _leftHandWeapon.BlendMesh = 1;
            _leftHandWeapon.SetBlendMeshTexCoordOffset(1, new Vector2(MuGame.Random.Next(10) * 0.1f, MuGame.Random.Next(10) * 0.1f));

            TextureCoordinateOffset = new Vector2(                -((long)gameTime.TotalGameTime.TotalMilliseconds % 10000L) * 0.0004f,
                0f);
        }

        // Sound mapping based on C++ SetMonsterSound(MODEL_MONSTER01 + Type, 180, 180, 181, 181, 182);
        protected override void OnIdle()
        {
            base.OnIdle();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mMegaCrust1.wav", Position, listenerPosition); // Sound 180
        }

        // Body pulse of MODEL_CRUST (ZzzObject.cpp:1298-1303, ExtraMon == 0): BodyLight = (L + 0.5, 0.3 - 0.5 L, 0.5 - 0.5 L),
        // L = sin(WorldTime * 0.002) * 0.3 + 0.5.
        protected override Vector3 GetMeshBodyLight(int mesh)
        {
            float l = MathF.Sin((float)FPSCounter.Instance.WorldTime * 0.002f) * 0.3f + 0.5f;
            return new Vector3(l + 0.5f, 0.3f - l * 0.5f, 0.5f - l * 0.5f) * StatusBodyLight;
        }

        public override void OnPerformAttack(int attackType = 1)
        {
            base.OnPerformAttack(attackType);
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mMegaCrustAttack1.wav", Position, listenerPosition); // Sound 181
        }

        public override void OnReceiveDamage()
        {
            base.OnReceiveDamage();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mMegaCrustAttack1.wav", Position, listenerPosition);
        }

        protected override void OnAttackProgress(in AttackProgress progress)
        {
            if (progress.Action is MonsterActionType.Attack1 or MonsterActionType.Attack2 && progress.CrossedTick(5))
                InfernoEffect.SpawnMonsterAttack(this);
        }

        public override void OnDeathAnimationStart()
        {
            base.OnDeathAnimationStart();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mMegaCrustDie.wav", Position, listenerPosition); // Sound 182
        }
    }
}
