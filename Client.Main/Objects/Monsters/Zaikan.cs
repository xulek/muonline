using Client.Main.Content;
using Client.Main.Objects.Effects;
using Client.Main.Objects.Player;
using Client.Main.Core.Utilities;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(59, "Zaikan")]
    public class Zaikan : MonsterObject
    {
        private WeaponObject _rightHandWeapon;
        private GlowingEyesEffect _eyeGlow;

        public Zaikan()
        {
            RenderShadow = false; // SourceMain5.2 RenderCharacter: MONSTER_ZAIKAN excluded from blob shadow pass
            Scale = 2.1f;
            BlendMesh = 2;
            BlendMeshLight = 1.0f;

            // SourceMain5.2 RenderCharacter: ZAIKAN gets the chrome/bright pass
            // tinted (0.5, 0.25, 0.0) (Bright = 0.5, PartObjectColor Color 0).
            BrightOverlay = 1f;
            BrightOverlayTexturePath = "Effect/Chrome01.jpg";
            BrightOverlayTint = new Vector3(0.5f, 0.25f, 0f);

            _rightHandWeapon = new WeaponObject
            {
                LinkParentAnimation = false,
                ParentBoneLink = 43
            };
            Children.Add(_rightHandWeapon);
            Children.Add(new MonsterBoneFireEffect
            {
                SourceBones = new[] { 6, 13 },
                EmitAllSourceBones = true,
                EmissionRate = 6.25f
            });

            // Same model as Tantalos — eyes: 24 (R), 25 (L)
            _eyeGlow = new GlowingEyesEffect { LeftEyeBone = 25, RightEyeBone = 24, GlowColor = new Color(80, 170, 255) };
            Children.Add(_eyeGlow);
            Children.Add(new SourceMonsterSandSmokeEffect());
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster43.bmd");
            var weapon = ItemDatabase.GetItemDefinition(5, 8); // Staff of Destruction
            if (weapon != null)
                _rightHandWeapon.Model = await BMDLoader.Instance.Prepare(weapon.TexturePath);
            await base.Load();
            SetActionSpeed(MonsterActionType.Stop1, 0.25f);
            SetActionSpeed(MonsterActionType.Stop2, 0.20f);
            SetActionSpeed(MonsterActionType.Walk, 0.34f);
            SetActionSpeed(MonsterActionType.Attack1, 0.35f);
            SetActionSpeed(MonsterActionType.Attack2, 0.35f);
            SetActionSpeed(MonsterActionType.Shock, 0.50f);
            SetActionSpeed(MonsterActionType.Die, 0.55f);
        }
    }
}
