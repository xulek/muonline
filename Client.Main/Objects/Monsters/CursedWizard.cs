using Client.Data.BMD;
using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Core.Utilities;
using Client.Main.Models;
using Client.Main.Objects.Player;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(34, "Cursed Wizard")]
    public class CursedWizard : MonsterObject
    {
        private static readonly Lazy<Task<BMD>> _modelTask = new(LoadModelAsync);

        private readonly PlayerHelmObject _helm;
        private readonly PlayerArmorObject _armor;
        private readonly PlayerPantObject _pants;
        private readonly PlayerGloveObject _gloves;
        private readonly PlayerBootObject _boots;
        private readonly WeaponObject _staff;
        private readonly WeaponObject _shield;

        public CursedWizard()
        {
            RenderShadow = true;
            Scale = 1.0f; // SourceMain5.2: SetCharacterScale / Devil Square scale
            MoveSpeed = 250f; // SourceMain5.2: default monster MoveSpeed (10 * 25 FPS)
            AnimationSpeed = 25f;

            _helm = new PlayerHelmObject { LinkParentAnimation = true, ItemLevel = 9 };
            _armor = new PlayerArmorObject { LinkParentAnimation = true, ItemLevel = 9 };
            _pants = new PlayerPantObject { LinkParentAnimation = true, ItemLevel = 9 };
            _gloves = new PlayerGloveObject { LinkParentAnimation = true, ItemLevel = 9 };
            _boots = new PlayerBootObject { LinkParentAnimation = true, ItemLevel = 9 };
            _staff = new WeaponObject { LinkParentAnimation = false, ParentBoneLink = 33 };
            _shield = new WeaponObject { LinkParentAnimation = false, ParentBoneLink = 42 };

            Children.Add(_helm);
            Children.Add(_armor);
            Children.Add(_pants);
            Children.Add(_gloves);
            Children.Add(_boots);
            Children.Add(_staff);
            Children.Add(_shield);
        }

        public override async Task Load()
        {
            Model = await _modelTask.Value;
            await LoadBodyPartAsync(_helm, 7, 3);
            await LoadBodyPartAsync(_armor, 8, 3);
            await LoadBodyPartAsync(_pants, 9, 3);
            await LoadBodyPartAsync(_gloves, 10, 3);
            await LoadBodyPartAsync(_boots, 11, 3);
            await LoadWeaponAsync(_staff, 5, 5);
            await LoadWeaponAsync(_shield, 6, 14);

            await base.Load();
        }

        public override void OnPerformAttack(int attackType = 1)
        {
            base.OnPerformAttack(attackType);
            SpawnMagicAttackEffect(new[] { 33, 42, 33, 42 }, attackType);
        }

        private static async Task<BMD> LoadModelAsync()
        {
            var playerModel = await BMDLoader.Instance.Prepare("Player/Player.bmd");
            if (playerModel == null)
                return null;

            // SourceMain selects staff animations from the item's TwoHands flag.
            // Cursed Wizard also carries a shield, so missing item data falls back
            // to the original-compatible one-handed staff animation set.
            bool isTwoHanded = ItemDatabase.GetItemDefinition(5, 5)?.TwoHanded == true;
            PlayerAction stopAction = isTwoHanded
                ? PlayerAction.PlayerStopScythe
                : PlayerAction.PlayerStopSword;
            PlayerAction walkAction = isTwoHanded
                ? PlayerAction.PlayerWalkScythe
                : PlayerAction.PlayerWalkSword;
            PlayerAction runAction = isTwoHanded
                ? PlayerAction.PlayerRunSpear
                : PlayerAction.PlayerRunSword;
            PlayerAction attack1Action = isTwoHanded
                ? PlayerAction.PlayerSkillWeapon1
                : PlayerAction.PlayerAttackSwordRight1;
            PlayerAction attack2Action = isTwoHanded
                ? PlayerAction.PlayerSkillWeapon2
                : PlayerAction.PlayerAttackSwordRight2;

            int count = Enum.GetValues(typeof(MonsterActionType)).Length;
            var map = new Dictionary<int, int>
            {
                [(int)MonsterActionType.Stop1] = (int)stopAction,
                [(int)MonsterActionType.Stop2] = (int)stopAction,
                [(int)MonsterActionType.Walk] = (int)walkAction,
                [(int)MonsterActionType.Attack1] = (int)attack1Action,
                [(int)MonsterActionType.Attack2] = (int)attack2Action,
                [(int)MonsterActionType.Shock] = (int)PlayerAction.PlayerShock,
                [(int)MonsterActionType.Die] = (int)PlayerAction.PlayerDie1,
                [(int)MonsterActionType.Appear] = (int)PlayerAction.PlayerComeUp,
                [(int)MonsterActionType.Attack3] = (int)attack1Action,
                [(int)MonsterActionType.Attack4] = (int)attack2Action,
                [(int)MonsterActionType.Run] = (int)runAction
            };
            var cursedWizardModel = new BMD
            {
                Version = playerModel.Version,
                Name = playerModel.Name,
                Meshes = playerModel.Meshes,
                Actions = BuildActionArray(playerModel, count, map),
                Bones = BuildBoneArray(playerModel, count, map)
            };
            BMDLoader.Instance.RegisterDerivedModel(playerModel, cursedWizardModel);
            return cursedWizardModel;
        }

        private static async Task LoadBodyPartAsync(ModelObject part, byte group, short id)
        {
            // The group/index select the per-item material colours (Legendary set: blue glow, not the
            // generic set glow).
            part.MaterialItemGroup = group;
            part.MaterialItemIndex = id;

            var item = ItemDatabase.GetItemDefinition(group, id);
            if (item == null || string.IsNullOrEmpty(item.TexturePath))
                return;

            string modelPath = item.TexturePath.Replace("Item/", "Player/");
            part.Model = await BMDLoader.Instance.Prepare(modelPath);
        }

        private static async Task LoadWeaponAsync(WeaponObject weapon, byte group, short id)
        {
            weapon.MaterialItemGroup = group;
            weapon.MaterialItemIndex = id;

            var item = ItemDatabase.GetItemDefinition(group, id);
            if (item == null || string.IsNullOrEmpty(item.TexturePath))
                return;

            weapon.Model = await BMDLoader.Instance.Prepare(item.TexturePath);
        }
    }
}
