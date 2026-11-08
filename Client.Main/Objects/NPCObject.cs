using Client.Main.Objects.Player;
using Client.Main.Objects.Wings;
using Client.Main.Controls;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Threading.Tasks;
using Client.Main.Content;
using Client.Main.Core.Utilities;
using Microsoft.Xna.Framework;
using Client.Main.Models;
using System;
using System.Reflection;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects
{
    public abstract class NPCObject : WalkerObject, IEquippedCharacter
    {
        protected new ILogger _logger;
        private DateTime _lastClickTime = DateTime.MinValue;
        private const double CLICK_COOLDOWN_SECONDS = 0.5; // Global debounce for all NPCs

        /// <summary>
        /// Indicates whether this NPC can repair items. Override in derived classes for NPCs that offer repair services.
        /// </summary>
        public virtual bool CanRepair => false;

        /// <summary>
        /// Gets the NPC's display name defined by <see cref="NpcInfoAttribute"/>.
        /// </summary>
        public override string DisplayName
        {
            get
            {
                var attrs = GetType().GetCustomAttributes(typeof(NpcInfoAttribute), inherit: false);
                foreach (var a in attrs)
                {
                    if (a is NpcInfoAttribute attr)
                    {
                        return attr.DisplayName;
                    }
                }
                return base.DisplayName;
            }
        }

        public PlayerMaskHelmObject HelmMask { get; private set; }
        public PlayerHelmObject Helm { get; private set; }
        public PlayerArmorObject Armor { get; private set; }
        public PlayerPantObject Pants { get; private set; }
        public PlayerGloveObject Gloves { get; private set; }
        public PlayerBootObject Boots { get; private set; }
        public WeaponObject Weapon1 { get; private set; }
        public WeaponObject Weapon2 { get; private set; }
        public WingObject Wings { get; private set; }

        public NPCObject()
        {
            _logger = AppLoggerFactory?.CreateLogger(GetType());
            Interactive = true;
            AnimationSpeed = 6f;

            // Initialize body part objects and link their animations to this parent object
            var equipmentVisuals = new EquipmentVisualSet();
            HelmMask = equipmentVisuals.HelmMask;
            Helm = equipmentVisuals.Helm;
            Armor = equipmentVisuals.Armor;
            Pants = equipmentVisuals.Pants;
            Gloves = equipmentVisuals.Gloves;
            Boots = equipmentVisuals.Boots;
            Weapon1 = equipmentVisuals.Weapon1;
            Weapon2 = equipmentVisuals.Weapon2;
            Wings = equipmentVisuals.Wings;
            equipmentVisuals.AddTo(Children);
        }

        public override void OnClick()
        {
            base.OnClick();
            // Prevent world click-to-move from triggering on NPC clicks
            MuGame.Instance?.ActiveScene?.SetMouseInputConsumed();

            // Debounce clicks - prevent spam
            var now = DateTime.UtcNow;
            var timeSinceLastClick = (now - _lastClickTime).TotalSeconds;

            if (timeSinceLastClick < CLICK_COOLDOWN_SECONDS)
            {
                _logger?.LogDebug("NPC click ignored - cooldown active ({TimeRemaining:F2}s remaining)",
                    CLICK_COOLDOWN_SECONDS - timeSinceLastClick);
                return;
            }

            _lastClickTime = now;

            // Track the NPC type for repair mode detection
            var characterState = MuGame.Network?.GetCharacterState();
            if (characterState != null)
            {
                characterState.LastNpcNetworkId = NetworkId;
                // Get TypeNumber from NpcInfoAttribute
                var attr = GetType().GetCustomAttribute<NpcInfoAttribute>();
                if (attr != null)
                {
                    characterState.LastNpcTypeNumber = attr.TypeId;
                }
            }

            if (TryQueueInteraction())
                return;

            HandleClick();
        }

        internal void ExecuteInteraction()
        {
            HandleClick();
        }

        private bool TryQueueInteraction()
        {
            if (MuGame.Instance?.ActiveScene?.World is not WalkableWorldControl world)
                return false;

            if (world.Walker is not PlayerObject player)
                return false;

            return player.TryQueueNpcInteraction(this);
        }
        protected abstract void HandleClick();


        /// <summary>
        /// Loads the models for all body parts based on a specified path prefix, part prefixes, and a file suffix.
        /// Example: ("Npc/", "FemaleHead", "FemaleUpper", ..., 2) -> "Data/Npc/FemaleHead02.bmd"
        /// </summary>
        protected async Task SetBodyPartsAsync(
            string pathPrefix, string helmPrefix, string armorPrefix, string pantPrefix,
            string glovePrefix, string bootPrefix, int skinIndex)
        {
            // Format skin index to two digits (e.g., 1 -> "01", 10 -> "10")
            string fileSuffix = skinIndex.ToString("D2");

            var tasks = new List<Task>
            {
                LoadPartAsync(Helm, $"{pathPrefix}{helmPrefix}{fileSuffix}.bmd"),
                LoadPartAsync(Armor, $"{pathPrefix}{armorPrefix}{fileSuffix}.bmd"),
                LoadPartAsync(Pants, $"{pathPrefix}{pantPrefix}{fileSuffix}.bmd"),
                LoadPartAsync(Gloves, $"{pathPrefix}{glovePrefix}{fileSuffix}.bmd"),
                LoadPartAsync(Boots, $"{pathPrefix}{bootPrefix}{fileSuffix}.bmd")
            };

            await Task.WhenAll(tasks);
        }

        /// <summary>
        /// Loads the five armour parts of an item set from the item database, the lookup players use. Sets whose
        /// model files do not follow the item index resolve correctly this way (e.g. index 30 = Venom Mist is
        /// <c>HDK_*02</c>, not <c>ArmorMale31</c>).
        /// </summary>
        protected async Task SetBodyPartsFromItemsAsync(short itemIndex)
        {
            await Task.WhenAll(
                LoadPartAsync(Helm, ItemPlayerModelPath(7, itemIndex)),
                LoadPartAsync(Armor, ItemPlayerModelPath(8, itemIndex)),
                LoadPartAsync(Pants, ItemPlayerModelPath(9, itemIndex)),
                LoadPartAsync(Gloves, ItemPlayerModelPath(10, itemIndex)),
                LoadPartAsync(Boots, ItemPlayerModelPath(11, itemIndex)));
        }

        private static string ItemPlayerModelPath(byte group, short itemIndex) =>
            ItemDatabase.GetItemDefinition(group, itemIndex)?.TexturePath?.Replace("Item/", "Player/");

        private async Task LoadPartAsync(ModelObject part, string modelPath)
        {
            if (part != null && !string.IsNullOrEmpty(modelPath))
            {
                part.Model = await BMDLoader.Instance.Prepare(modelPath);
                if (part == Helm)
                    Helm.ModelPath = modelPath;

                if (part.Model == null)
                {
                    _logger?.LogDebug("Model part not found (this is often normal for NPCs): {Path}", modelPath);
                }
            }
        }

        /// <summary>
        /// Configures shared NPC wing attachment using a single authoritative child object.
        /// Keeps animation linkage deterministic for player-model NPCs.
        /// </summary>
        protected async Task ConfigureNpcWingsAsync(string modelPath, int blendMesh = -1, BlendState blendState = null)
        {
            if (Wings == null)
                return;

            Wings.Hidden = false;
            // Wings should attach to parent backbone (ParentBoneLink) instead of copying full parent animation,
            // otherwise wing skeleton can become distorted for player-model NPCs.
            Wings.LinkParentAnimation = false;
            Wings.Model = await BMDLoader.Instance.Prepare(modelPath);

            if (Wings.Model == null)
            {
                Wings.Hidden = true;
                _logger?.LogWarning("Could not load NPC wings model: {Path}", modelPath);
                return;
            }

            Wings.BlendMesh = blendMesh;
            if (blendState != null)
            {
                Wings.BlendMeshState = blendState;
            }
        }

        public override void Update(GameTime gameTime)
        {
            bool wasMoving = IsMoving;
            base.Update(gameTime);

            if (wasMoving && !IsMoving && !IsOneShotPlaying)
            {
                if (CurrentAction == (int)PlayerAction.PlayerWalkMale || CurrentAction == (int)PlayerAction.PlayerWalkFemale)
                {
                    PlayAction((ushort)PlayerAction.PlayerStopMale);
                }
            }
        }
    }
}
