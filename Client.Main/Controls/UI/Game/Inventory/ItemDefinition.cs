using System.Collections.Generic;

namespace Client.Main.Controls.UI.Game.Inventory
{
    public class ItemDefinition
    {
        private static readonly HashSet<int> s_upgradeJewelIds = new() { 13, 14, 16 };
        private static readonly HashSet<int> s_quickSlotConsumablePotionIds = new()
        {
            // SourceMain5.2 reference: CanRegisterItemHotKey (ITEM_POTION + id)
            0,   // Apple
            1,   // Small Healing Potion
            2,   // Medium Healing Potion
            3,   // Large Healing Potion
            4,   // Small Mana Potion
            5,   // Medium Mana Potion
            6,   // Large Mana Potion
            7,   // Siege Potion
            8,   // Antidote
            9,   // Ale
            10,  // Town Portal Scroll
            20,  // Remedy of Love
            35,  // Small Shield Potion
            36,  // Medium Shield Potion
            37,  // Large Shield Potion
            38,  // Small Complex Potion
            39,  // Medium Complex Potion
            40,  // Large Complex Potion
            46,  // Jack O'Lantern Blessings
            47,  // Jack O'Lantern Wrath
            48,  // Jack O'Lantern Cry
            49,  // Jack O'Lantern Food
            50,  // Jack O'Lantern Drink
            70,  // Elite HP potion (custom/event)
            71,  // Elite MP potion (custom/event)
            78,  // Secret Potion 1
            79,  // Secret Potion 2
            80,  // Secret Potion 3
            81,  // Secret Potion 4
            82,  // Secret Potion 5
            85,  // Cherry Blossom Wine
            86,  // Cherry Blossom Rice Cake
            87,  // Cherry Blossom Flower Petal
            94,  // Elite extra HP potion (custom/event)
            133, // Elite SD potion (custom/event)
        };
        private static readonly HashSet<int> s_skillLearningWingItemIds = new()
        {
            // SourceMain5.2 reference: NewUIMyInventory::TryConsumeItem
            // ITEM_WING + [7..14], [16..24], 35, 44, 45, 46, 47, 48
            7, 8, 9, 10, 11, 12, 13, 14,
            16, 17, 18, 19, 20, 21, 22, 23, 24,
            35,
            44, 45, 46, 47, 48,
        };
        public int Id { get; set; } // Unique ID of the item type (e.g., from Item.bmd)
        public string Name { get; set; }
        public int Width { get; set; }  // Width in slots
        public int Height { get; set; } // Height in slots
        public string TexturePath { get; set; } // Path to the item's texture/model

        // Additional stats loaded from items.json for richer tooltips
        public int DamageMin { get; set; }
        public int DamageMax { get; set; }
        public int MagicPower { get; set; }  // Wizard damage for staffs
        public int AttackSpeed { get; set; }
        public int Defense { get; set; }
        public int DefenseRate { get; set; }
        public int BaseDurability { get; set; }
        public int MagicDurability { get; set; }  // Max durability for staffs (uses MagicDur instead of Durability)
        public int WalkSpeed { get; set; }
        public int RequiredStrength { get; set; }
        public int RequiredDexterity { get; set; }
        public int RequiredEnergy { get; set; }
        public int RequiredLevel { get; set; }
        public bool TwoHanded { get; set; }
        public int Group { get; set; }
        public bool IsExpensive { get; set; }
        public bool CanSellToNpc { get; set; }
        public int Money { get; set; } // Base buy price (iZen) from Item.bmd, can be 0
        public int ItemValue { get; set; } // Legacy value fallback if Money is missing
        public int DropLevel { get; set; } // Drop level from BMD, used as a proxy for price curve

        // Classes which can equip this item
        public List<string> AllowedClasses { get; set; } = new();

        public ItemDefinition(int id, string name, int width, int height, string texturePath = null)
        {
            Id = id;
            Name = name;
            Width = width;
            Height = height;
            TexturePath = texturePath;
        }

        /// <summary>
        /// Checks if this item is consumable (potions, scrolls, etc.).
        /// </summary>
        public bool IsConsumable()
        {
            return IsQuickSlotConsumable() || IsSkillLearningConsumable();
        }

        /// <summary>
        /// Checks if this item can be assigned to Q/W/E quick slots.
        /// Reference: SourceMain5.2 CanRegisterItemHotKey.
        /// </summary>
        public bool IsQuickSlotConsumable()
        {
            return Group == 14 && s_quickSlotConsumablePotionIds.Contains(Id);
        }

        /// <summary>
        /// Checks if this item is consumed on right-click for skill learning/use,
        /// but should not be assignable to Q/W/E.
        /// Reference: SourceMain5.2 NewUIMyInventory::TryConsumeItem.
        /// </summary>
        public bool IsSkillLearningConsumable()
        {
            // ITEM_ETC group (15): skill scrolls / parchments.
            if (Group == 15)
            {
                return true;
            }

            // ITEM_WING group (12): skill orbs / books / crystals used via right-click.
            return Group == 12 && s_skillLearningWingItemIds.Contains(Id);
        }

        /// <summary>
        /// Determines if the item is a jewel (non-consumable items in group 14/12).
        /// Jewels should not show "Right-click to use" even though they're in consumable groups.
        /// </summary>
        public bool IsJewel()
        {
            // Group 14 jewels: Bless (13), Soul (14), Life (16), Creation (22), Guardian (31), etc.
            // Group 12 jewels: Chaos (15), etc.
            if (Group == 14 && (Id == 13 || Id == 14 || Id == 16 || Id == 22 || Id == 31))
                return true;
            if (Group == 12 && Id == 15)
                return true;
            return false;
        }

        /// <summary>
        /// Determines if the item is an upgrade jewel (Bless, Soul, Life).
        /// </summary>
        public bool IsUpgradeJewel()
        {
            return Group == 14 && s_upgradeJewelIds.Contains(Id);
        }
    }
}
