using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Models;
using Client.Main.Objects.Effects;
using Client.Main.Objects.Player;
using Client.Main.Core.Utilities;
using Microsoft.Xna.Framework;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(38, "Balrog")]
    public class Balrog : MonsterObject
    {
        private WeaponObject _rightHandWeapon;
        public Balrog()
        {
            RenderShadow = true;
            Scale = 1.6f; // Set according to C++ Setting_Monster
            MoveSpeed = 250f; // SourceMain5.2: default monster MoveSpeed (10 * 25 FPS)

            // SourceMain5.2 RenderCharacter: MONSTER_BALROG gets the
            // RENDER_CHROME | RENDER_BRIGHT body pass tinted (1.0, 0.2, 0.0)
            // (PartObjectColor MODEL_BALROG = Color 1). The chrome UVs come from
            // vertex normals (environment mapping), so the pass itself is static.
            BrightOverlay = 1f;
            BrightOverlayTexturePath = "Effect/Chrome01.jpg";
            BrightOverlayTint = new Vector3(1f, 0.2f, 0f);

            _rightHandWeapon = new WeaponObject
            {
                LinkParentAnimation = false,
                ParentBoneLink = 17,
                ItemLevel = 9
            };
            Children.Add(_rightHandWeapon);
        }

        public override async Task Load()        {
            // Model Loading Type: 27 -> File Number: 27 + 1 = 28
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster28.bmd");
            var item = ItemDatabase.GetItemDefinition(3, 9); // Bill of Balrog
            if (item != null)
                _rightHandWeapon.Model = await BMDLoader.Instance.Prepare(item.TexturePath);
            await base.Load();
            SetActionSpeed(MonsterActionType.Stop1, 0.25f);
            SetActionSpeed(MonsterActionType.Stop2, 0.20f);
            SetActionSpeed(MonsterActionType.Walk, 0.34f);
            SetActionSpeed(MonsterActionType.Attack1, 0.33f);
            SetActionSpeed(MonsterActionType.Attack2, 0.33f);
            SetActionSpeed(MonsterActionType.Shock, 0.50f);
            SetActionSpeed(MonsterActionType.Die, 0.55f);
            // C++: Models[MODEL_MONSTER01+Type].BoneHead = 6;
            // C++: Models[MODEL_MONSTER01+Type].StreamMesh = 1; // May need special handling for streaming meshes if applicable
        }

        // Sound mapping based on C++ SetMonsterSound(MODEL_MONSTER01 + Type, 108, 109, 110, 111, 112);
        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            if (Status != GameControlStatus.Ready)
                return;

            // SourceMain5.2 MoveCharacterVisual MODEL_BALROG: the V scroll flows the
            // base texture of the stream mesh (mesh 1), NOT the chrome pass — the
            // chrome UVs are environment-mapped from vertex normals and ignore it.
            TextureCoordinateOffsetMeshIndex = 1;
            TextureCoordinateOffset = new Vector2(
                0f,
                -((long)gameTime.TotalGameTime.TotalMilliseconds % 1000L) * 0.001f);
        }

        protected override void OnIdle()
        {
            base.OnIdle();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            string sound = MuGame.Random.Next(2) == 0
                ? "Sound/mBalrog1.wav"
                : "Sound/mBalrog2.wav";
            SoundController.Instance.PlayBufferWithAttenuation(sound, Position, listenerPosition);
        }

        public override void OnPerformAttack(int attackType = 1)
        {
            base.OnPerformAttack(attackType);
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            string sound = MuGame.Random.Next(2) == 0
                ? "Sound/mWizardAttack2.wav"
                : "Sound/mGorgonAttack2.wav";
            SoundController.Instance.PlayBufferWithAttenuation(sound, Position, listenerPosition);

            // SourceMain5.2 AttackEffect MONSTER_BALROG boss branch
            // (Skill == AT_SKILL_BOSS, CheckAttackTime(1)): MODEL_CIRCLE +
            // MODEL_CIRCLE_LIGHT under the caster and SOUND_HELLFIRE, followed by
            // a MODEL_FIRE rain (random ground points +-512, SOUND_METEORITE01).
            if (attackType != 2)
                return;

            if (World == null || !TryConsumeAttackEffectWindow())
                return;

            SoundController.Instance.PlayBufferWithAttenuation("Sound/sHellFire.wav", Position, listenerPosition);
            SoundController.Instance.PlayBufferWithAttenuation("Sound/eMeteorite.wav", Position, listenerPosition);

            var circle = new MonsterMagicCircleEffect(Position);
            World.Objects.Add(circle);
            _ = circle.Load();

            var rain = new MonsterFireRainEffect(Position);
            World.Objects.Add(rain);
            _ = rain.Load();
        }

        public override void OnReceiveDamage()
        {
            base.OnReceiveDamage();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            string sound = MuGame.Random.Next(2) == 0
                ? "Sound/mWizardAttack2.wav"
                : "Sound/mGorgonAttack2.wav";
            SoundController.Instance.PlayBufferWithAttenuation(sound, Position, listenerPosition);
        }

        public override void OnDeathAnimationStart()
        {
            base.OnDeathAnimationStart();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mBalrogDie.wav", Position, listenerPosition); // Index 4 -> Sound 112
        }
    }
}
