using System;
using Client.Main.Controls.UI.Game.Inventory;
using static Client.Main.Core.Utilities.ItemTooltip.SourceConstants;

namespace Client.Main.Core.Utilities.ItemTooltip
{
    /// <summary>
    /// C# mirror of SourceMain5.2 ITEM (_struct.h tagITEM) restricted to the fields the tooltip uses.
    /// Integer widths follow the original (BYTE / WORD) where the calculations depend on truncation.
    /// </summary>
    public sealed class SourceItem
    {
        public const int MAX_ITEM_SPECIAL = 8;

        public int Type = -1;
        public int Level;
        public int Durability;
        public int OptionLevel;
        public int OptionType;
        public int ExcellentFlags;
        public int AncientDiscriminator;
        public int AncientBonusOption;
        public int Jewel_Of_Harmony_Option;
        public int Jewel_Of_Harmony_OptionLevel;
        public bool HasSkill;
        public bool HasLuck;
        public bool TwoHand;
        public int DamageMin;          // WORD
        public int DamageMax;          // WORD
        public int SuccessfulBlocking; // BYTE
        public int Defense;            // WORD
        public int MagicDefense;       // WORD
        public int MagicPower;         // BYTE
        public int WeaponSpeed;        // BYTE
        public int WalkSpeed;          // WORD
        public int RequireStrength;
        public int RequireDexterity;
        public int RequireEnergy;
        public int RequireVitality;
        public int RequireCharisma;
        public int RequireLevel;
        public int SpecialNum;
        public readonly int[] Special = new int[MAX_ITEM_SPECIAL];       // WORD
        public readonly int[] SpecialValue = new int[MAX_ITEM_SPECIAL];  // BYTE
        public bool option_380;
        public int SocketCount;
        public readonly int[] SocketSeedID = { 0xFE, 0xFE, 0xFE, 0xFE, 0xFE };   // SOCKET_EMPTY
        public readonly int[] SocketSphereLv = new int[5];
        public int SocketSeedSetOption;
        /// <summary>Dark Horse / Dark Raven pet info (giPetManager PET_INFO) when known.</summary>
        public SourcePetInfo PetInfo;
        public bool bPeriodItem;
        public bool bExpiredPeriod;

        /// <summary>The item.bmd row of <see cref="Type"/> (ItemAttribute[ip->Type]).</summary>
        public SourceItemAttribute Attribute;

        internal void AddSpecial(int special)
        {
            if (SpecialNum >= MAX_ITEM_SPECIAL) return;
            Special[SpecialNum] = special;
            SpecialNum++;
        }

        internal void AddSpecial(int special, int value)
        {
            if (SpecialNum >= MAX_ITEM_SPECIAL) return;
            SpecialValue[SpecialNum] = (byte)value;
            Special[SpecialNum] = special;
            SpecialNum++;
        }
    }

    /// <summary>C# mirror of ITEM_ATTRIBUTE (_struct.h), filled from the client's item.bmd definition.</summary>
    public sealed class SourceItemAttribute
    {
        public string Name = string.Empty;
        public bool TwoHand;
        public int Level;              // drop level
        public int m_wSkillIndex;
        public int Width, Height;
        public int DamageMin, DamageMax, SuccessfulBlocking, Defense, MagicDefense, WeaponSpeed, WalkSpeed;
        public int Durability, MagicDur, MagicPower;
        public int RequireStrength, RequireDexterity, RequireEnergy, RequireVitality, RequireCharisma, RequireLevel;
        public int Value;
        public int iZen;
        public int AttType;
        public byte[] RequireClass = new byte[MAX_CLASS];
        public byte[] Resistance = new byte[MAX_RESISTANCE + 1];

        public static SourceItemAttribute FromDefinition(ItemDefinition def)
        {
            var p = new SourceItemAttribute();
            if (def == null) return p;
            p.Name = def.Name ?? string.Empty;
            p.TwoHand = def.TwoHanded;
            p.Level = def.DropLevel;
            p.m_wSkillIndex = def.SkillIndex;
            p.Width = def.Width;
            p.Height = def.Height;
            p.DamageMin = def.DamageMin;
            p.DamageMax = def.DamageMax;
            p.SuccessfulBlocking = def.DefenseRate;
            p.Defense = def.Defense;
            p.MagicDefense = def.MagicDefense;
            p.WeaponSpeed = def.AttackSpeed;
            p.WalkSpeed = def.WalkSpeed;
            p.Durability = def.BaseDurability;
            p.MagicDur = def.MagicDurability;
            p.MagicPower = def.MagicPower;
            p.RequireStrength = def.RequiredStrength;
            p.RequireDexterity = def.RequiredDexterity;
            p.RequireEnergy = def.RequiredEnergy;
            p.RequireVitality = def.RequiredVitality;
            p.RequireCharisma = def.RequiredCommand;
            p.RequireLevel = def.RequiredLevel;
            p.Value = def.ItemValue;
            p.iZen = def.Money;
            p.AttType = def.SetAttribute;
            if (def.RequireClass != null)
                Array.Copy(def.RequireClass, p.RequireClass, Math.Min(def.RequireClass.Length, p.RequireClass.Length));
            if (def.Resistance != null)
                Array.Copy(def.Resistance, p.Resistance, Math.Min(def.Resistance.Length, MAX_RESISTANCE));
            return p;
        }
    }

    /// <summary>
    /// Faithful port of SourceMain5.2 SetItemAttributes and its helpers (ZzzInfomation.cpp:612-1590) and
    /// CalcMaxDurability (ZzzInventory.cpp:1553).
    /// </summary>
    public static class SourceItemAttributes
    {
        /// <summary>Hero level used by the excellent "+level/20" options (CharacterAttribute->Level).</summary>
        public static int HeroLevel(SourceItem ip) => ItemTooltipHero.Current.Level;

        // ZzzInfomation.cpp:598
        public static bool IsCepterItem(int iType)
        {
            return (iType >= ITEM_BATTLE_SCEPTER && iType <= ITEM_SHINING_SCEPTER) || iType == ITEM_ABSOLUTE_SCEPTER || iType == ITEM_STRYKER_SCEPTER;
        }

        // ZzzInfomation.cpp:612
        public static bool IsWing(SourceItem ip)
        {
            return (ip.Type >= ITEM_WINGS_OF_SPIRITS && ip.Type <= ITEM_WINGS_OF_DARKNESS)
                || (ip.Type >= ITEM_WING_OF_STORM && ip.Type <= ITEM_CAPE_OF_EMPEROR)
                || (ip.Type >= ITEM_WINGS_OF_DESPAIR && ip.Type <= ITEM_WING_OF_DIMENSION)
                || ip.Type == ITEM_DIVINE_SWORD_OF_ARCHANGEL
                || ip.Type == ITEM_DIVINE_CB_OF_ARCHANGEL
                || ip.Type == ITEM_DIVINE_STAFF_OF_ARCHANGEL
                || ip.Type == ITEM_DIVINE_SCEPTER_OF_ARCHANGEL
                || ip.Type == ITEM_CAPE_OF_LORD
                || (ITEM_WING + 130 <= ip.Type && ip.Type <= ITEM_WING + 134)
                || (ip.Type >= ITEM_CAPE_OF_FIGHTER && ip.Type <= ITEM_CAPE_OF_OVERRULE)
                || (ip.Type == ITEM_WING + 135);
        }

        /// <summary>IsWingItem (ZzzInventory.cpp) as used by CalcRequirements.</summary>
        public static bool IsWingItem(SourceItem ip)
        {
            switch (ip.Type)
            {
                case ITEM_WING:
                case ITEM_WINGS_OF_HEAVEN:
                case ITEM_WINGS_OF_SATAN:
                case ITEM_WINGS_OF_SPIRITS:
                case ITEM_WINGS_OF_SOUL:
                case ITEM_WINGS_OF_DRAGON:
                case ITEM_WINGS_OF_DARKNESS:
                case ITEM_CAPE_OF_LORD:
                case ITEM_WING_OF_CURSE:
                case ITEM_WINGS_OF_DESPAIR:
                case ITEM_CAPE_OF_FIGHTER:
                case ITEM_WING_OF_STORM:
                case ITEM_WING_OF_ETERNAL:
                case ITEM_WING_OF_ILLUSION:
                case ITEM_WING_OF_RUIN:
                case ITEM_CAPE_OF_EMPEROR:
                case ITEM_WING_OF_DIMENSION:
                case ITEM_CAPE_OF_OVERRULE:
                case ITEM_WING + 130:
                case ITEM_WING + 131:
                case ITEM_WING + 132:
                case ITEM_WING + 133:
                case ITEM_WING + 134:
                case ITEM_WING + 135:
                    return true;
            }
            return false;
        }

        // ZzzInventory.cpp:7727
        public static bool Check_LuckyItem(int _nIndex, int _nType = 0)
        {
            int nItemTabIndex = (_nIndex + _nType) % 512;
            if (_nIndex < ITEM_HELM || _nIndex > ITEM_WING) return false;
            if (nItemTabIndex >= 62 && nItemTabIndex <= 72) return true;
            return false;
        }

        // ZzzInfomation.cpp:627
        private static int GetDropLevel(SourceItemAttribute p) => p.Level + 30;

        // ZzzInfomation.cpp:632
        private static int GetExcellentAddValue(SourceItem ip)
        {
            if (ip.Type == ITEM_CHAOS_DRAGON_AXE) return 15;
            if (ip.Type == ITEM_CHAOS_NATURE_BOW) return 30;
            if (ip.Type == ITEM_CHAOS_LIGHTNING_STAFF) return 25;
            return 0;
        }

        private static int W(int v) => (ushort)v; // WORD truncation
        private static int B(int v) => (byte)v;   // BYTE truncation

        // ZzzInfomation.cpp:652
        private static void CalcDamageMin(SourceItem ip, SourceItemAttribute p, int excelAddValue)
        {
            if (p.DamageMin <= 0) return;

            if (ip.ExcellentFlags > 0)
            {
                if (p.Level != 0)
                {
                    if (excelAddValue != 0) ip.DamageMin = W(ip.DamageMin + excelAddValue);
                    else ip.DamageMin = W(ip.DamageMin + p.DamageMin * 25 / p.Level + 5);
                }
            }

            if (ip.AncientDiscriminator > 0)
                ip.DamageMin = W(ip.DamageMin + 5 + (GetDropLevel(p) / 40));

            ip.DamageMin = W(ip.DamageMin + Math.Min(9, ip.Level) * 3);
            if (ip.Level - 9 > 0)
                ip.DamageMin = W(ip.DamageMin + ip.Level - 6);
        }

        // ZzzInfomation.cpp:688
        private static void CalcDamageMax(SourceItem ip, SourceItemAttribute p, int excelAddValue)
        {
            if (p.DamageMax <= 0) return;

            if (ip.ExcellentFlags > 0)
            {
                if (p.Level != 0)
                {
                    if (excelAddValue != 0) ip.DamageMax = W(ip.DamageMax + excelAddValue);
                    else ip.DamageMax = W(ip.DamageMax + p.DamageMax * 25 / p.Level + 5);
                }
            }

            if (ip.AncientDiscriminator > 0)
                ip.DamageMax = W(ip.DamageMax + 5 + (GetDropLevel(p) / 40));

            ip.DamageMax = W(ip.DamageMax + Math.Min(9, ip.Level) * 3);
            if (ip.Level - 9 > 0)
                ip.DamageMax = W(ip.DamageMax + ip.Level - 6);
        }

        // ZzzInfomation.cpp:719
        private static void CalcMagicPower(SourceItem ip, SourceItemAttribute p, int excelAddValue)
        {
            if (p.MagicPower <= 0) return;

            if (ip.ExcellentFlags > 0)
            {
                if (p.Level != 0)
                {
                    if (excelAddValue != 0) ip.MagicPower = B(ip.MagicPower + excelAddValue);
                    else ip.MagicPower = B(ip.MagicPower + p.MagicPower * 25 / p.Level + 5);
                }
            }

            if (ip.AncientDiscriminator > 0)
                ip.MagicPower = B(ip.MagicPower + 2 + (GetDropLevel(p) / 60));

            ip.MagicPower = B(ip.MagicPower + Math.Min(9, ip.Level) * 3);
            if (ip.Level - 9 > 0)
                ip.MagicPower = B(ip.MagicPower + ip.Level - 6);

            ip.MagicPower /= 2;

            if (!IsCepterItem(ip.Type))
                ip.MagicPower = B(ip.MagicPower + ip.Level * 2);
        }

        // ZzzInfomation.cpp:757 (the original adds p->Level, the drop level, not the item level)
        private static void CalcSuccessfulBlocking(SourceItem ip, SourceItemAttribute p)
        {
            if (p.SuccessfulBlocking == 0) return;

            if (ip.ExcellentFlags > 0)
            {
                if (p.Level != 0)
                    ip.SuccessfulBlocking = B(ip.SuccessfulBlocking + p.SuccessfulBlocking * 25 / p.Level + 5);
            }
            ip.SuccessfulBlocking = B(ip.SuccessfulBlocking + Math.Min(9, p.Level) * 3);
            if (p.Level - 9 > 0)
                ip.SuccessfulBlocking = B(ip.SuccessfulBlocking + p.Level - 6);
        }

        // ZzzInfomation.cpp:776
        private static void CalcDefense(SourceItem ip, SourceItemAttribute p)
        {
            if (p.MagicDefense > 0)
            {
                ip.MagicDefense = W(ip.MagicDefense + Math.Min(9, p.Level) * 3);
                if (p.Level - 9 > 0)
                    ip.MagicDefense = W(ip.MagicDefense + p.Level - 6);
            }

            int pDefense = p.Defense;
            if (ip.Type == ITEM_CAPE_OF_LORD)
            {
                pDefense = 15;
                ip.Defense = 15;
            }

            if (pDefense == 0) return;

            bool isAncientItem = ip.AncientDiscriminator > 0;
            bool isExcellent = ip.ExcellentFlags > 0;
            int setItemDropLevel = GetDropLevel(p);

            if (ip.Type >= ITEM_SHIELD && ip.Type < ITEM_SHIELD + MAX_ITEM_INDEX)
            {
                ip.Defense = W(ip.Defense + ip.Level);
                if (isAncientItem)
                    ip.Defense = W(ip.Defense + (ip.Defense * 20 / setItemDropLevel + 2));
                return;
            }

            if (isExcellent && p.Level > 0)
                ip.Defense = W(ip.Defense + pDefense * 12 / p.Level + 4 + p.Level / 5);

            if (isAncientItem)
                ip.Defense = W(ip.Defense + pDefense + (ip.Defense * 3 / setItemDropLevel + 2 + setItemDropLevel / 30));

            if ((ip.Type >= ITEM_WINGS_OF_SPIRITS && ip.Type <= ITEM_WINGS_OF_DARKNESS) || ip.Type == ITEM_WINGS_OF_DESPAIR)
                ip.Defense = W(ip.Defense + Math.Min(9, ip.Level) * 2);
            else if (ip.Type == ITEM_CAPE_OF_LORD || ip.Type == ITEM_CAPE_OF_FIGHTER)
                ip.Defense = W(ip.Defense + Math.Min(9, ip.Level) * 2);
            else if ((ip.Type >= ITEM_WING_OF_STORM && ip.Type <= ITEM_CAPE_OF_EMPEROR) || ip.Type == ITEM_WING_OF_DIMENSION || ip.Type == ITEM_CAPE_OF_OVERRULE)
                ip.Defense = W(ip.Defense + Math.Min(9, ip.Level) * 4);
            else
                ip.Defense = W(ip.Defense + Math.Min(9, ip.Level) * 3);

            if ((ip.Type >= ITEM_WING_OF_STORM && ip.Type <= ITEM_CAPE_OF_EMPEROR) || ip.Type == ITEM_WING_OF_DIMENSION || ip.Type == ITEM_CAPE_OF_OVERRULE)
            {
                if (ip.Level - 9 > 0)
                    ip.Defense = W(ip.Defense + ip.Level - 5);
            }
            else
            {
                if (ip.Level - 9 > 0)
                    ip.Defense = W(ip.Defense + ip.Level - 6);
            }
        }

        // ZzzInfomation.cpp:858
        private static void CalcRequirements(SourceItem ip, SourceItemAttribute p)
        {
            bool isExcellent = ip.ExcellentFlags > 0;
            bool isAncientItem = ip.AncientDiscriminator > 0;
            int ItemLevel = p.Level;

            if (isExcellent) ItemLevel = p.Level + 25;
            else if (isAncientItem) ItemLevel = p.Level + 30;

            int addValue = 4;
            if ((ip.Type >= ITEM_WINGS_OF_SPIRITS && ip.Type <= ITEM_WINGS_OF_DARKNESS) || ip.Type == ITEM_WINGS_OF_DESPAIR)
                addValue = 5;

            if (p.RequireLevel != 0
                && ((ip.Type >= ITEM_SWORD && ip.Type < ITEM_WING)
                    || (ip.Type == ITEM_HORN_OF_FENRIR)
                    || (ip.Type >= ITEM_ORB_OF_TWISTING_SLASH && ip.Type <= ITEM_CAPE_OF_EMPEROR)
                    || (ip.Type >= ITEM_WING_OF_DIMENSION && ip.Type < ITEM_CAPE_OF_EMPEROR)
                    && (ip.Type != ITEM_CAPE_OF_FIGHTER)))
            {
                ip.RequireLevel = p.RequireLevel;
            }
            else if (p.RequireLevel != 0 && ((ip.Type >= ITEM_WING && ip.Type <= ITEM_ORB_OF_TWISTING_SLASH)
                || (ip.Type >= ITEM_WING_OF_CURSE && ip.Type <= ITEM_WINGS_OF_DESPAIR)
                || (ip.Type == ITEM_CAPE_OF_FIGHTER)
                || ip.Type >= ITEM_HELPER))
            {
                ip.RequireLevel = W(p.RequireLevel + ip.Level * addValue);
            }
            else
                ip.RequireLevel = 0;

            ip.RequireStrength = p.RequireStrength != 0 ? W(20 + p.RequireStrength * (ItemLevel + ip.Level * 3) * 3 / 100) : 0;
            ip.RequireDexterity = p.RequireDexterity != 0 ? W(20 + p.RequireDexterity * (ItemLevel + ip.Level * 3) * 3 / 100) : 0;
            ip.RequireVitality = p.RequireVitality != 0 ? W(20 + p.RequireVitality * (ItemLevel + ip.Level * 3) * 3 / 100) : 0;

            if (p.RequireEnergy != 0)
            {
                if (ip.Type >= ITEM_BOOK_OF_SAHAMUTT && ip.Type <= ITEM_STAFF + 29)
                    ip.RequireEnergy = W(20 + p.RequireEnergy * (ItemLevel + ip.Level * 1) * 3 / 100);
                else if ((p.RequireLevel > 0) && (ip.Type >= ITEM_ETC && ip.Type < ITEM_ETC + MAX_ITEM_INDEX))
                    ip.RequireEnergy = W(20 + p.RequireEnergy * p.RequireLevel * 4 / 100);
                else
                    ip.RequireEnergy = W(20 + p.RequireEnergy * (ItemLevel + ip.Level * 3) * 4 / 100);
            }
            else
            {
                ip.RequireEnergy = 0;
            }

            ip.RequireCharisma = p.RequireCharisma != 0 ? W(20 + p.RequireCharisma * (ItemLevel + ip.Level * 3) * 3 / 100) : 0;

            if (ip.Type == ITEM_ORB_OF_SUMMONING)
            {
                int Energy = 0;
                switch (ip.Level)
                {
                    case 0: Energy = 30; break;
                    case 1: Energy = 60; break;
                    case 2: Energy = 90; break;
                    case 3: Energy = 130; break;
                    case 4: Energy = 170; break;
                    case 5: Energy = 210; break;
                    case 6: Energy = 300; break;
                    case 7: Energy = 500; break;
                }
                ip.RequireEnergy = Energy;
            }

            if (p.RequireCharisma != 0)
            {
                if (ip.Type == ITEM_DARK_RAVEN_ITEM)
                    ip.RequireCharisma = W(185 + (p.RequireCharisma * 15));
                else
                    ip.RequireCharisma = p.RequireCharisma;
            }

            if (ip.Type == ITEM_TRANSFORMATION_RING)
                ip.RequireLevel = ip.Level <= 2 ? 20 : 50;

            if ((ip.Type >= ITEM_DRAGON_KNIGHT_HELM && ip.Type <= ITEM_SUNLIGHT_MASK) ||
                (ip.Type >= ITEM_DRAGON_KNIGHT_ARMOR && ip.Type <= ITEM_SUNLIGHT_ARMOR) ||
                (ip.Type >= ITEM_DRAGON_KNIGHT_PANTS && ip.Type <= ITEM_SUNLIGHT_PANTS) ||
                (ip.Type >= ITEM_DRAGON_KNIGHT_GLOVES && ip.Type <= ITEM_SUNLIGHT_GLOVES) ||
                (ip.Type >= ITEM_DRAGON_KNIGHT_BOOTS && ip.Type <= ITEM_SUNLIGHT_BOOTS) ||
                ip.Type == ITEM_BONE_BLADE ||
                ip.Type == ITEM_EXPLOSION_BLADE ||
                ip.Type == ITEM_GRAND_VIPER_STAFF ||
                ip.Type == ITEM_SYLPH_WIND_BOW ||
                ip.Type == ITEM_SOLEIL_SCEPTER
                || ITEM_STORM_BLITZ_STICK == ip.Type
                || ITEM_AURA_HELM == ip.Type
                || ITEM_AURA_ARMOR == ip.Type
                || ITEM_AURA_PANTS == ip.Type
                || ITEM_AURA_GLOVES == ip.Type
                || ITEM_AURA_BOOTS == ip.Type
                || Check_LuckyItem(ip.Type))
            {
                isExcellent = false;
            }

            if (isExcellent && ip.RequireLevel > 0 && !IsWingItem(ip) && ip.Type != ITEM_HORN_OF_FENRIR)
                ip.RequireLevel = W(ip.RequireLevel + 20);
        }

        // ZzzInfomation.cpp:1000
        private static void CalcWingOptions(SourceItem ip)
        {
            int f = ip.ExcellentFlags;
            if ((ip.Type >= ITEM_WINGS_OF_SPIRITS && ip.Type <= ITEM_WINGS_OF_DARKNESS) || ip.Type == ITEM_WINGS_OF_DESPAIR)
            {
                if ((f & 0x01) != 0) ip.AddSpecial(AT_IMPROVE_HP_MAX, 50 + ip.Level * 5);
                if (((f >> 1) & 0x01) != 0) ip.AddSpecial(AT_IMPROVE_MP_MAX, 50 + ip.Level * 5);
                if (((f >> 2) & 0x01) != 0) ip.AddSpecial(AT_ONE_PERCENT_DAMAGE, 3);
                if (((f >> 3) & 0x01) != 0) ip.AddSpecial(AT_IMPROVE_AG_MAX, 50);
                if (((f >> 4) & 0x01) != 0) ip.AddSpecial(AT_IMPROVE_ATTACK_SPEED, 5);
            }
            else if (ip.Type == ITEM_CAPE_OF_LORD || ip.Type == ITEM_CAPE_OF_FIGHTER)
            {
                if ((f & 0x01) != 0) ip.AddSpecial(AT_IMPROVE_HP_MAX, 50 + ip.Level * 5);
                if (((f >> 1) & 0x01) != 0) ip.AddSpecial(AT_IMPROVE_MP_MAX, 50 + ip.Level * 5);
                if (((f >> 2) & 0x01) != 0) ip.AddSpecial(AT_ONE_PERCENT_DAMAGE, 3);
                if (((f >> 3) & 0x01) != 0 && ip.Type != ITEM_CAPE_OF_FIGHTER) ip.AddSpecial(AT_SET_OPTION_IMPROVE_CHARISMA, 10 + ip.Level * 5);
            }
            else if ((ip.Type >= ITEM_WING_OF_STORM && ip.Type <= ITEM_CAPE_OF_EMPEROR) || ip.Type == ITEM_WING_OF_DIMENSION || ip.Type == ITEM_CAPE_OF_OVERRULE)
            {
                if ((f & 0x01) != 0) ip.AddSpecial(AT_ONE_PERCENT_DAMAGE, 5);
                if (((f >> 1) & 0x01) != 0) ip.AddSpecial(AT_DAMAGE_REFLECTION, 5);
                if (((f >> 2) & 0x01) != 0) ip.AddSpecial(AT_RECOVER_FULL_LIFE, 5);
                if (((f >> 3) & 0x01) != 0) ip.AddSpecial(AT_RECOVER_FULL_MANA, 5);
            }
        }

        // ZzzInfomation.cpp:1085
        private static void CalcExcellentOptions(SourceItem ip)
        {
            int f = ip.ExcellentFlags;
            if ((ip.Type >= ITEM_SHIELD && ip.Type < ITEM_BOOTS + MAX_ITEM_INDEX) || (ip.Type >= ITEM_RING_OF_ICE && ip.Type <= ITEM_RING_OF_POISON) || (ip.Type >= ITEM_RING_OF_FIRE && ip.Type <= ITEM_RING_OF_MAGIC))
            {
                if (((f >> 5) & 1) != 0) ip.AddSpecial(AT_IMPROVE_LIFE);
                if (((f >> 4) & 1) != 0) ip.AddSpecial(AT_IMPROVE_MANA);
                if (((f >> 3) & 1) != 0) ip.AddSpecial(AT_DECREASE_DAMAGE);
                if (((f >> 2) & 1) != 0) ip.AddSpecial(AT_REFLECTION_DAMAGE);
                if (((f >> 1) & 1) != 0) ip.AddSpecial(AT_IMPROVE_BLOCKING_PERCENT);
                if ((f & 1) != 0) ip.AddSpecial(AT_IMPROVE_GAIN_GOLD);
            }
            if ((ip.Type >= ITEM_SWORD && ip.Type < ITEM_STAFF + MAX_ITEM_INDEX) || (ip.Type >= ITEM_PENDANT_OF_LIGHTING && ip.Type <= ITEM_PENDANT_OF_FIRE) || (ip.Type >= ITEM_PENDANT_OF_ICE && ip.Type <= ITEM_PENDANT_OF_ABILITY))
            {
                if (((f >> 5) & 1) != 0) ip.AddSpecial(AT_EXCELLENT_DAMAGE);
                if ((ip.Type >= ITEM_STAFF && ip.Type < ITEM_STAFF + MAX_ITEM_INDEX) ||
                    (ip.Type == ITEM_PENDANT_OF_LIGHTING) ||
                    (ip.Type == ITEM_PENDANT_OF_ICE || ip.Type == ITEM_PENDANT_OF_WATER))
                {
                    if (((f >> 4) & 1) != 0) ip.AddSpecial(AT_IMPROVE_MAGIC_LEVEL, HeroLevel(ip) / 20);
                    if (((f >> 3) & 1) != 0) ip.AddSpecial(AT_IMPROVE_MAGIC_PERCENT, 2);
                }
                else
                {
                    if (((f >> 4) & 1) != 0) ip.AddSpecial(AT_IMPROVE_DAMAGE_LEVEL, HeroLevel(ip) / 20);
                    if (((f >> 3) & 1) != 0) ip.AddSpecial(AT_IMPROVE_DAMAGE_PERCENT, 2);
                }
                if (((f >> 2) & 1) != 0) ip.AddSpecial(AT_IMPROVE_ATTACK_SPEED, 7);
                if (((f >> 1) & 1) != 0) ip.AddSpecial(AT_IMPROVE_GAIN_LIFE);
                if ((f & 1) != 0) ip.AddSpecial(AT_IMPROVE_GAIN_MANA);
            }
            if (ip.Type == ITEM_WIZARDS_RING)
            {
                switch (ip.Level)
                {
                    case 0:
                    case 3:
                        ip.AddSpecial(AT_IMPROVE_MAGIC_PERCENT, 10);
                        ip.AddSpecial(AT_IMPROVE_DAMAGE_PERCENT, 10);
                        ip.AddSpecial(AT_IMPROVE_ATTACK_SPEED, 10);
                        break;
                }
            }
            if (ip.Type == ITEM_HELPER + 107)
            {
                ip.AddSpecial(AT_IMPROVE_MAGIC_PERCENT, 15);
                ip.AddSpecial(AT_IMPROVE_DAMAGE_PERCENT, 15);
                ip.AddSpecial(AT_IMPROVE_ATTACK_SPEED, 10);
            }
        }

        /// <summary>SetItemAttributes (ZzzInfomation.cpp:1241). <paramref name="p"/> is ItemAttribute[ip->Type].</summary>
        public static void SetItemAttributes(SourceItem ip, SourceItemAttribute p)
        {
            ip.Attribute = p;
            int excelAddValue = GetExcellentAddValue(ip);

            ip.TwoHand = p.TwoHand;
            ip.WeaponSpeed = B(p.WeaponSpeed);
            ip.DamageMin = W(p.DamageMin);
            ip.DamageMax = W(p.DamageMax);
            ip.SuccessfulBlocking = B(p.SuccessfulBlocking);
            ip.Defense = W(p.Defense);
            ip.MagicDefense = W(p.MagicDefense);
            ip.WalkSpeed = W(p.WalkSpeed);
            ip.MagicPower = B(p.MagicPower);

            CalcDamageMin(ip, p, excelAddValue);
            CalcDamageMax(ip, p, excelAddValue);
            CalcMagicPower(ip, p, excelAddValue);
            CalcSuccessfulBlocking(ip, p);
            CalcDefense(ip, p);
            CalcRequirements(ip, p);

            ip.SpecialNum = 0;

            CalcWingOptions(ip);

            if (ip.HasSkill)
            {
                if (p.m_wSkillIndex != 0)
                    ip.AddSpecial(p.m_wSkillIndex);
            }

            if (ip.HasLuck)
            {
                if (ip.Type >= ITEM_SWORD && ip.Type < ITEM_BOOTS + MAX_ITEM_INDEX)
                {
                    if (ip.Type != ITEM_BOLT && ip.Type != ITEM_ARROWS)
                        ip.AddSpecial(AT_LUCK);
                }
                if ((ip.Type >= ITEM_WING && ip.Type <= ITEM_WINGS_OF_DARKNESS) || (ip.Type >= ITEM_WING_OF_CURSE && ip.Type <= ITEM_WINGS_OF_DESPAIR))
                    ip.AddSpecial(AT_LUCK);
                if (ip.Type == ITEM_CAPE_OF_LORD || ip.Type == ITEM_CAPE_OF_FIGHTER)
                    ip.AddSpecial(AT_LUCK);
                if ((ip.Type >= ITEM_WING_OF_STORM && ip.Type <= ITEM_CAPE_OF_EMPEROR) || ip.Type == ITEM_WING_OF_DIMENSION || ip.Type == ITEM_CAPE_OF_OVERRULE)
                    ip.AddSpecial(AT_LUCK);
            }

            int Option3 = ip.OptionLevel;
            int f = ip.ExcellentFlags;
            if (Option3 != 0)
            {
                if (ip.Type == ITEM_HORN_OF_DINORANT)
                {
                    if ((Option3 & 0x01) != 0) ip.AddSpecial(AT_DAMAGE_ABSORB, 5);
                    if (((Option3 >> 1) & 0x01) != 0) ip.AddSpecial(AT_IMPROVE_AG_MAX, 50);
                    if (((Option3 >> 2) & 0x01) != 0) ip.AddSpecial(AT_IMPROVE_ATTACK_SPEED, 5);
                }
                else
                {
                    if (ip.Type >= ITEM_SWORD && ip.Type < ITEM_BOW + MAX_ITEM_INDEX)
                    {
                        if (ip.Type != ITEM_BOLT && ip.Type != ITEM_ARROWS)
                        {
                            ip.AddSpecial(AT_IMPROVE_DAMAGE, Option3 * 4);
                            ip.RequireStrength = W(ip.RequireStrength + Option3 * 5);
                        }
                    }
                    if (ip.Type >= ITEM_STAFF && ip.Type < ITEM_STAFF + MAX_ITEM_INDEX)
                    {
                        if (ip.Type >= ITEM_BOOK_OF_SAHAMUTT && ip.Type <= ITEM_STAFF + 29)
                            ip.AddSpecial(AT_IMPROVE_CURSE, Option3 * 4);
                        else
                            ip.AddSpecial(AT_IMPROVE_MAGIC, Option3 * 4);
                        ip.RequireStrength = W(ip.RequireStrength + Option3 * 5);
                    }
                    if (ip.Type >= ITEM_SHIELD && ip.Type < ITEM_SHIELD + MAX_ITEM_INDEX)
                    {
                        ip.AddSpecial(AT_IMPROVE_BLOCKING, Option3 * 5);
                        ip.RequireStrength = W(ip.RequireStrength + Option3 * 5);
                    }
                    if (ip.Type >= ITEM_HELM && ip.Type < ITEM_BOOTS + MAX_ITEM_INDEX)
                    {
                        ip.AddSpecial(AT_IMPROVE_DEFENSE, Option3 * 4);
                        ip.RequireStrength = W(ip.RequireStrength + Option3 * 5);
                    }
                    if (ip.Type >= ITEM_RING_OF_ICE && ip.Type < ITEM_HELPER + MAX_ITEM_INDEX && ip.Type != ITEM_CAPE_OF_LORD)
                    {
                        if (ip.Type == ITEM_RING_OF_MAGIC)
                            ip.AddSpecial(AT_IMPROVE_MAX_MANA, Option3);
                        else if (ip.Type == ITEM_PENDANT_OF_ABILITY)
                            ip.AddSpecial(AT_IMPROVE_MAX_AG, Option3);
                        else
                            ip.AddSpecial(AT_LIFE_REGENERATION, Option3);
                    }

                    bool exc5 = ((f >> 5) & 0x01) != 0;
                    bool exc4 = ((f >> 4) & 0x01) != 0;
                    if (ip.Type == ITEM_WING)
                        ip.AddSpecial(AT_LIFE_REGENERATION, Option3);
                    else if (ip.Type == ITEM_WINGS_OF_HEAVEN || ip.Type == ITEM_WING_OF_CURSE)
                        ip.AddSpecial(AT_IMPROVE_MAGIC, Option3 * 4);
                    else if (ip.Type == ITEM_WINGS_OF_SATAN)
                        ip.AddSpecial(AT_IMPROVE_DAMAGE, Option3 * 4);
                    else if (ip.Type == ITEM_WINGS_OF_SPIRITS)
                    {
                        if (exc5) ip.AddSpecial(AT_LIFE_REGENERATION, Option3);
                        else ip.AddSpecial(AT_IMPROVE_DAMAGE, Option3 * 4);
                    }
                    else if (ip.Type == ITEM_WINGS_OF_SOUL)
                    {
                        if (exc5) ip.AddSpecial(AT_IMPROVE_MAGIC, Option3 * 4);
                        else ip.AddSpecial(AT_LIFE_REGENERATION, Option3);
                    }
                    else if (ip.Type == ITEM_WINGS_OF_DRAGON)
                    {
                        if (exc5) ip.AddSpecial(AT_IMPROVE_DAMAGE, Option3 * 4);
                        else ip.AddSpecial(AT_LIFE_REGENERATION, Option3);
                    }
                    else if (ip.Type == ITEM_WINGS_OF_DARKNESS)
                    {
                        if (exc5) ip.AddSpecial(AT_IMPROVE_DAMAGE, Option3 * 4);
                        else ip.AddSpecial(AT_IMPROVE_MAGIC, Option3 * 4);
                    }
                    else if (ip.Type == ITEM_CAPE_OF_LORD)
                        ip.AddSpecial(AT_IMPROVE_DAMAGE, Option3 * 4);
                    else if (ip.Type == ITEM_CAPE_OF_FIGHTER)
                    {
                        if (exc5) ip.AddSpecial(AT_IMPROVE_DAMAGE, Option3 * 4);
                        else ip.AddSpecial(AT_LIFE_REGENERATION, Option3);
                    }
                    else if (ip.Type == ITEM_WINGS_OF_DESPAIR)
                    {
                        ip.AddSpecial(exc5 ? AT_IMPROVE_MAGIC : AT_IMPROVE_CURSE, Option3 * 4);
                    }
                    else if (ip.Type == ITEM_WING_OF_STORM || ip.Type == ITEM_WING_OF_ILLUSION || ip.Type == ITEM_CAPE_OF_EMPEROR || ip.Type == ITEM_CAPE_OF_OVERRULE)
                    {
                        if (exc4) ip.AddSpecial(AT_IMPROVE_DAMAGE, Option3 * 4);
                        else if (exc5) ip.AddSpecial(AT_IMPROVE_DEFENSE, Option3 * 4);
                        else ip.AddSpecial(AT_LIFE_REGENERATION, Option3);
                    }
                    else if (ip.Type == ITEM_WING_OF_ETERNAL)
                    {
                        if (exc4) ip.AddSpecial(AT_IMPROVE_MAGIC, Option3 * 4);
                        else if (exc5) ip.AddSpecial(AT_IMPROVE_DEFENSE, Option3 * 4);
                        else ip.AddSpecial(AT_LIFE_REGENERATION, Option3);
                    }
                    else if (ip.Type == ITEM_WING_OF_RUIN)
                    {
                        if (exc4) ip.AddSpecial(AT_IMPROVE_DAMAGE, Option3 * 4);
                        else if (exc5) ip.AddSpecial(AT_IMPROVE_MAGIC, Option3 * 4);
                        else ip.AddSpecial(AT_LIFE_REGENERATION, Option3);
                    }
                    else if (ip.Type == ITEM_WING_OF_DIMENSION)
                    {
                        if (exc4) ip.AddSpecial(AT_IMPROVE_MAGIC, Option3 * 4);
                        else if (exc5) ip.AddSpecial(AT_IMPROVE_CURSE, Option3 * 4);
                        else ip.AddSpecial(AT_LIFE_REGENERATION, Option3);
                    }
                }
            }

            // ITEM_DARK_HORSE_ITEM: giPetManager::SetPetItemConvert is not ported (no pet info table).

            CalcExcellentOptions(ip);
        }

        /// <summary>CalcMaxDurability (ZzzInventory.cpp:1553).</summary>
        public static int CalcMaxDurability(SourceItem ip, SourceItemAttribute p, int Level)
        {
            int maxDurability = p.Durability;

            if (ip.Type >= ITEM_STAFF && ip.Type < ITEM_STAFF + MAX_ITEM_INDEX)
                maxDurability = p.MagicDur;

            for (int i = 0; i < Level; i++)
            {
                if (ip.Type >= ITEM_SCROLL_OF_BLOOD)
                    break;
                else if (i >= 14)
                    maxDurability = maxDurability + 8 >= 255 ? 255 : maxDurability + 8;
                else if (i >= 13)
                    maxDurability = maxDurability + 7 >= 255 ? 255 : maxDurability + 7;
                else if (i >= 12)
                    maxDurability += 6;
                else if (i >= 11)
                    maxDurability += 5;
                else if (i >= 10)
                    maxDurability += 4;
                else if (i >= 9)
                    maxDurability += 3;
                else if (i >= 4)
                    maxDurability += 2;
                else
                    maxDurability++;
            }

            if (ip.Type == ITEM_DARK_HORSE_ITEM || ip.Type == ITEM_DARK_RAVEN_ITEM)
                maxDurability = 255;

            if (ip.AncientDiscriminator > 0)
            {
                maxDurability += 20;
            }
            else if (ip.ExcellentFlags > 0 &&
                (ip.Type < ITEM_WINGS_OF_SPIRITS || ip.Type > ITEM_WINGS_OF_DARKNESS) &&
                (ip.Type != ITEM_DIVINE_SWORD_OF_ARCHANGEL && ip.Type != ITEM_DIVINE_CB_OF_ARCHANGEL && ip.Type != ITEM_DIVINE_STAFF_OF_ARCHANGEL)
                && ip.Type != ITEM_CAPE_OF_LORD
                && (ip.Type < ITEM_WING_OF_STORM || ip.Type > ITEM_CAPE_OF_EMPEROR)
                && (ip.Type < ITEM_WINGS_OF_DESPAIR || ip.Type > ITEM_WING_OF_DIMENSION)
                && ip.Type != ITEM_DIVINE_SCEPTER_OF_ARCHANGEL
                && !(ip.Type >= ITEM_CAPE_OF_FIGHTER && ip.Type <= ITEM_CAPE_OF_OVERRULE))
            {
                maxDurability += 15;
            }

            if (Check_LuckyItem(ip.Type))
                maxDurability = 255;

            return (ushort)maxDurability;
        }
    }
}
