using Client.Data;
using Client.Main.Content;
using Client.Main.Models;
using Client.Main.Objects.Player;
using System.Threading.Tasks;
using Client.Main.Objects.Effects;
using Microsoft.Xna.Framework;
using Microsoft.Extensions.Logging;
using System;

namespace Client.Main.Objects.NPCS
{
    [NpcInfo(257, "Elf Soldier")]
    public class ElfSoldier : NPCObject
    {
        private new readonly ILogger<ElfSoldier> _logger;

        private float _orbitAngle;

        private static void ConfigureSetPart(ModelObject part, int group)
        {
            part.ItemLevel = 13;
            part.MaterialItemGroup = group;
            part.MaterialItemIndex = 24;
        }

        public ElfSoldier()
        {
            _logger = AppLoggerFactory?.CreateLogger<ElfSoldier>();
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare("Player/Player.bmd");
            if (Model == null)
            {
                _logger.LogError("CRITICAL: Could not load base player model 'Player/Player.bmd'. NPC cannot be animated.");
                Status = GameControlStatus.Error;
                return;
            }

            await SetBodyPartsAsync("Player/", "HelmMale", "ArmorMale", "PantMale", "GloveMale", "BootMale", 25);

            // MakeElfHelper: Red Spirit set at +13 (item index 24) and Wings of Spirits.
            ConfigureSetPart(Helm, 7);
            ConfigureSetPart(Armor, 8);
            ConfigureSetPart(Pants, 9);
            ConfigureSetPart(Gloves, 10);
            ConfigureSetPart(Boots, 11);

            // The soldier uses MODEL_PLAYER, so CheckFullSet gives it the +13 full-set aura too.
            AddItemEffect(new FullSetAuraEffect());

            // CreateJoint(BITMAP_FLARE, ..., 42, o, 15): flare ribbons orbiting the soldier.
            AddItemEffect(new ItemLinkEffect
            {
                CustomEmitter = effect => ItemLinkEffectEmitter.EmitElfOrbit(effect, ref _orbitAngle),
            });

            await ConfigureNpcWingsAsync(
                "Item/Wing04.bmd",
                blendMesh: 0,
                blendState: Microsoft.Xna.Framework.Graphics.BlendState.Additive);

            await base.Load();
            
            AnimationSpeed = 25f;
            CurrentAction = (int)PlayerAction.PlayerStopFly;
            Scale = 1.0f;

            var currentBBox = BoundingBoxLocal;
            BoundingBoxLocal = new BoundingBox(currentBBox.Min,
                new Vector3(currentBBox.Max.X, currentBBox.Max.Y, currentBBox.Max.Z + 70f));
        }

        protected override void HandleClick()
        {
            _logger?.LogInformation("Elf Soldier clicked - sending buff request sequence (NetworkId: {NetworkId})", NetworkId);

            // Send complete buff sequence: TalkToNpc -> BuffRequest
            var characterService = MuGame.Network?.GetCharacterService();
            if (characterService != null)
            {
                _ = characterService.SendElfSoldierBuffSequenceAsync(NetworkId);
            }
            else
            {
                _logger?.LogWarning("CharacterService is null - cannot send Elf Soldier buff sequence");
            }
        }
    }
}
