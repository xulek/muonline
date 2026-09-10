using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Core.Utilities;
using Client.Main.Models;
using Client.Main.Objects.Effects;
using Client.Main.Objects.Player;
using Microsoft.Xna.Framework;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(67, "Metal Balrog")]
    public class MetalBalrog : MonsterObject
    {
        private readonly WeaponObject _rightHandWeapon;

        public MetalBalrog()
        {
            // SourceMain5.2 CreateMonster(MONSTER_METAL_BALROG): MODEL_BALROG,
            // Scale = 1.6f, Weapon[0] = Bill of Balrog +9 (LinkBone 17).
            // RenderCharacter: CHROME|BRIGHT|EXTRA pass tinted (0.8, 0.8, 1.0)
            // (PartObjectColor MODEL_BALROG + EXTRA = Color 8). The V scroll flows
            // the base stream-mesh texture, not the chrome pass.
            Scale = 1.6f;
            BrightOverlay = 1f;
            BrightOverlayTexturePath = "Effect/Chrome01.jpg";
            BrightOverlayTint = new Vector3(0.8f, 0.8f, 1f);
            _rightHandWeapon = new WeaponObject
            {
                LinkParentAnimation = false,
                ParentBoneLink = 17,
                ItemLevel = 9
            };
            Children.Add(_rightHandWeapon);
        }

        public override async Task Load()
        {
            var item = ItemDatabase.GetItemDefinition(3, 9); // Bill of Balrog
            if (item != null)
                _rightHandWeapon.Model = await BMDLoader.Instance.Prepare(item.TexturePath);

            Model = await BMDLoader.Instance.Prepare($"Monster/Monster28.bmd");
            await base.Load();
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            if (Status != GameControlStatus.Ready)
                return;

            // Same stream-mesh V scroll as MONSTER_BALROG (base texture of mesh 1).
            TextureCoordinateOffsetMeshIndex = 1;
            TextureCoordinateOffset = new Vector2(
                0f,
                -((long)gameTime.TotalGameTime.TotalMilliseconds % 1000L) * 0.001f);
        }

        public override void OnPerformAttack(int attackType = 1)        {
            base.OnPerformAttack(attackType);

            // Shared MONSTER_BALROG boss branch (Skill == AT_SKILL_BOSS,
            // CheckAttackTime(1)): MODEL_CIRCLE + MODEL_CIRCLE_LIGHT and
            // SOUND_HELLFIRE, followed by a MODEL_FIRE rain (+-512).
            if (attackType != 2)
                return;

            if (World == null || !TryConsumeAttackEffectWindow())
                return;

            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/sHellFire.wav", Position, listenerPosition);
            SoundController.Instance.PlayBufferWithAttenuation("Sound/eMeteorite.wav", Position, listenerPosition);

            var circle = new MonsterMagicCircleEffect(Position);
            World.Objects.Add(circle);
            _ = circle.Load();

            var rain = new MonsterFireRainEffect(Position);
            World.Objects.Add(rain);
            _ = rain.Load();
        }
    }
}
