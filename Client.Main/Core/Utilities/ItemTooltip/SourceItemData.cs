using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using Client.Main.Data;
using static Client.Main.Core.Utilities.ItemTooltip.SourceConstants;

namespace Client.Main.Core.Utilities.ItemTooltip
{
    internal static class SourceBmd
    {
        private static readonly byte[] BuxCode = { 0xFC, 0xCF, 0xAB };

        /// <summary>BuxConvert (ZzzInfomation.cpp:43): XOR with {0xFC,0xCF,0xAB}[i % 3] starting at <paramref name="offset"/>.</summary>
        public static void BuxConvert(byte[] buffer, int offset, int size)
        {
            for (int i = 0; i < size && offset + i < buffer.Length; i++)
                buffer[offset + i] ^= BuxCode[i % 3];
        }

        public static byte[] ReadAll(string fileName)
        {
            try
            {
                using var stream = EmbeddedS6Data.Open(fileName);
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                return ms.ToArray();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ItemTooltip: failed to load {fileName}: {ex.Message}");
                return Array.Empty<byte>();
            }
        }

        public static string Utf8Z(byte[] buffer, int offset, int size)
        {
            int length = 0;
            while (length < size && offset + length < buffer.Length && buffer[offset + length] != 0) length++;
            return Encoding.UTF8.GetString(buffer, offset, length);
        }
    }

    /// <summary>
    /// Ancient set tables of CSItemOption (CSItemOption.cpp:61 OpenItemSetType, :107 OpenItemSetOption):
    /// itemsettype.bmd (MAX_ITEM x {byOption[2], byMixItemLevel[2]}) and itemsetoption_eng.bmd (64 x ITEM_SET_OPTION_FILE).
    /// </summary>
    public sealed class ItemSetOptionData
    {
        public const int MAX_SET_OPTION = 64;
        public const int MASTERY_OPTION = 24;
        public const int MAX_ITEM_SET_STANDARD_OPTION_COUNT = 6;
        public const int MAX_ITEM_SET_STANDARD_OPTION_PER_ITEM_COUNT = 2;
        public const int MAX_ITEM_SET_EXT_OPTION_COUNT = 2;
        public const int MAX_ITEM_SETS_PER_ITEM = 2;
        public const int MAX_ITEM_SET_FULL_OPTION_COUNT = 5;
        public const byte EMPTY_OPTION = 0xFF;

        public sealed class SetType
        {
            public readonly byte[] byOption = new byte[MAX_ITEM_SETS_PER_ITEM];
            public readonly byte[] byMixItemLevel = new byte[MAX_ITEM_SETS_PER_ITEM];
        }

        public sealed class SetOption
        {
            public string strSetName = string.Empty;
            public int bySetItemCount;
            public int byOptionCount;
            public readonly byte[,] byStandardOption = new byte[MAX_ITEM_SET_STANDARD_OPTION_COUNT, MAX_ITEM_SET_STANDARD_OPTION_PER_ITEM_COUNT];
            public readonly byte[,] byStandardOptionValue = new byte[MAX_ITEM_SET_STANDARD_OPTION_COUNT, MAX_ITEM_SET_STANDARD_OPTION_PER_ITEM_COUNT];
            public readonly byte[] byExtOption = new byte[MAX_ITEM_SET_EXT_OPTION_COUNT];
            public readonly byte[] byExtOptionValue = new byte[MAX_ITEM_SET_EXT_OPTION_COUNT];
            public readonly byte[] byFullOption = new byte[MAX_ITEM_SET_FULL_OPTION_COUNT];
            public readonly byte[] byFullOptionValue = new byte[MAX_ITEM_SET_FULL_OPTION_COUNT];
            public readonly byte[] byRequireClass = new byte[MAX_CLASS];
        }

        private static readonly Lazy<ItemSetOptionData> s_instance = new(() => new ItemSetOptionData(), LazyThreadSafetyMode.ExecutionAndPublication);
        public static ItemSetOptionData Instance => s_instance.Value;

        public readonly SetType[] m_ItemSetType = new SetType[MAX_ITEM];
        public readonly SetOption[] m_ItemSetOption = new SetOption[MAX_SET_OPTION];

        private ItemSetOptionData()
        {
            for (int i = 0; i < MAX_ITEM; i++) m_ItemSetType[i] = new SetType();
            for (int i = 0; i < MAX_SET_OPTION; i++) m_ItemSetOption[i] = new SetOption();

            const int typeSize = 4;
            byte[] types = SourceBmd.ReadAll("itemsettype.bmd");
            if (types.Length >= typeSize * MAX_ITEM)
            {
                for (int i = 0; i < MAX_ITEM; i++)
                {
                    int o = i * typeSize;
                    SourceBmd.BuxConvert(types, o, typeSize);
                    m_ItemSetType[i].byOption[0] = types[o];
                    m_ItemSetType[i].byOption[1] = types[o + 1];
                    m_ItemSetType[i].byMixItemLevel[0] = types[o + 2];
                    m_ItemSetType[i].byMixItemLevel[1] = types[o + 3];
                }
            }

            const int optionSize = 64 + 12 + 12 + 2 + 2 + 1 + 5 + 5 + MAX_CLASS; // ITEM_SET_OPTION_FILE (110 bytes)
            byte[] options = SourceBmd.ReadAll("itemsetoption_eng.bmd");
            if (options.Length >= optionSize * MAX_SET_OPTION)
            {
                for (int i = 0; i < MAX_SET_OPTION; i++)
                {
                    int o = i * optionSize;
                    SourceBmd.BuxConvert(options, o, optionSize);
                    var target = m_ItemSetOption[i];
                    target.strSetName = SourceBmd.Utf8Z(options, o, 64);
                    int p = o + 64;
                    for (int a = 0; a < MAX_ITEM_SET_STANDARD_OPTION_COUNT; a++)
                        for (int n = 0; n < MAX_ITEM_SET_STANDARD_OPTION_PER_ITEM_COUNT; n++)
                            target.byStandardOption[a, n] = options[p++];
                    for (int a = 0; a < MAX_ITEM_SET_STANDARD_OPTION_COUNT; a++)
                        for (int n = 0; n < MAX_ITEM_SET_STANDARD_OPTION_PER_ITEM_COUNT; n++)
                            target.byStandardOptionValue[a, n] = options[p++];
                    for (int a = 0; a < MAX_ITEM_SET_EXT_OPTION_COUNT; a++) target.byExtOption[a] = options[p++];
                    for (int a = 0; a < MAX_ITEM_SET_EXT_OPTION_COUNT; a++) target.byExtOptionValue[a] = options[p++];
                    target.byOptionCount = options[p++];
                    for (int a = 0; a < MAX_ITEM_SET_FULL_OPTION_COUNT; a++) target.byFullOption[a] = options[p++];
                    for (int a = 0; a < MAX_ITEM_SET_FULL_OPTION_COUNT; a++) target.byFullOptionValue[a] = options[p++];
                    for (int a = 0; a < MAX_CLASS; a++) target.byRequireClass[a] = options[p++];
                    target.bySetItemCount = 0;
                }

                for (int j = 0; j < MAX_ITEM; j++)
                {
                    for (int k = 0; k < MAX_ITEM_SETS_PER_ITEM; k++)
                    {
                        int optionNumber = m_ItemSetType[j].byOption[k];
                        if (optionNumber < MAX_SET_OPTION)
                            m_ItemSetOption[optionNumber].bySetItemCount++;
                    }
                }
            }
        }

        /// <summary>CSItemOption::GetSetItemName (CSItemOption.cpp:309).</summary>
        public bool GetSetItemName(ref string strName, int iType, int setType)
        {
            int setItemType = setType % 0x04;
            if (setItemType > 0 && iType >= 0 && iType < MAX_ITEM)
            {
                var itemSType = m_ItemSetType[iType];
                if (itemSType.byOption[setItemType - 1] != 255 && itemSType.byOption[setItemType - 1] != 0)
                {
                    var itemOption = m_ItemSetOption[itemSType.byOption[setItemType - 1] % MAX_SET_OPTION];
                    strName = itemOption.strSetName + " ";
                    return true;
                }
            }
            return false;
        }
    }

    /// <summary>One equipped ancient set (SET_SEARCH_RESULT_OPT, CSItemOption.h).</summary>
    public sealed class SetSearchResult
    {
        public int SetNumber;
        public int CompleteSetItemCount;
        public int ItemCount;
        public int SetTypeIndex;
        public string SetName = string.Empty;
        public readonly List<SetOptionEntry> SetOption = new();
    }

    public struct SetOptionEntry
    {
        public bool IsActive;
        public bool IsFullOption;
        public bool IsExtOption;
        public bool FulfillsClassRequirement;
        public int OptionNumber;
        public int Value;
    }

    /// <summary>PET_INFO subset (GIPetManager.cpp) filled from the client's pet info packet.</summary>
    public sealed class SourcePetInfo
    {
        public const int PET_TYPE_DARK_SPIRIT = 0, PET_TYPE_DARK_HORSE = 1;
        public int m_dwPetType = -1;
        public int m_wLevel;
        public long m_dwExp1;
        public long m_dwExp2;
        public int m_wLife;
        public int m_wDamageMin, m_wDamageMax, m_wAttackSpeed, m_wAttackSuccess;
    }

    public enum StrengthenItem
    {
        SI_Weapon = 0,
        SI_Staff,
        SI_Defense,
        SI_None,
    }

    public sealed class HarmonyJewelOption
    {
        public int OptionType = -1;
        public string Name = string.Empty;
        public int Minlevel = -1;
        public readonly int[] HarmonyJewelLevel = new int[14];
        public readonly int[] Zen = new int[14];
    }

    public sealed class StrengthenCapability
    {
        public NaturalAbility SI_NB = new();
        public bool SI_isNB;
        public StrikingPower SI_SP = new();
        public bool SI_isSP;
        public StrengthenDefense SI_SD = new();
        public bool SI_isSD;

        public sealed class NaturalAbility { public int SI_force; public int SI_activity; }
        public sealed class StrikingPower { public int SI_minattackpower; public int SI_maxattackpower; public int SI_magicalpower; public int SI_attackpowerRate; public int SI_skillattackpower; }
        public sealed class StrengthenDefense { public int SI_defense; public int SI_AG; public int SI_HP; public int SI_defenseRate; }
    }

    /// <summary>JewelHarmonyInfo (UIJewelHarmony.cpp) backed by JewelOfHarmonyOption_eng.bmd.</summary>
    public sealed class JewelHarmonyInfo
    {
        public const int MAXHARMONYJEWELOPTIONTYPE = 3;
        public const int MAXHARMONYJEWELOPTIONINDEX = 10;

        private static readonly Lazy<JewelHarmonyInfo> s_instance = new(() => new JewelHarmonyInfo(), LazyThreadSafetyMode.ExecutionAndPublication);
        public static JewelHarmonyInfo Instance => s_instance.Value;

        private readonly HarmonyJewelOption[,] m_OptionData = new HarmonyJewelOption[MAXHARMONYJEWELOPTIONTYPE, MAXHARMONYJEWELOPTIONINDEX];

        private JewelHarmonyInfo()
        {
            const int NAME_SIZE = 60, LEVEL_COUNT = 14;
            const int ENTRY_SIZE = 4 + NAME_SIZE + 4 + LEVEL_COUNT * 4 + LEVEL_COUNT * 4;
            for (int t = 0; t < MAXHARMONYJEWELOPTIONTYPE; t++)
                for (int o = 0; o < MAXHARMONYJEWELOPTIONINDEX; o++)
                    m_OptionData[t, o] = new HarmonyJewelOption();

            byte[] data = SourceBmd.ReadAll("JewelOfHarmonyOption_eng.bmd");
            int nEntries = MAXHARMONYJEWELOPTIONTYPE * MAXHARMONYJEWELOPTIONINDEX;
            if (data.Length < ENTRY_SIZE * nEntries) return;
            SourceBmd.BuxConvert(data, 0, ENTRY_SIZE * nEntries);
            for (int i = 0; i < nEntries; i++)
            {
                var entry = m_OptionData[i / MAXHARMONYJEWELOPTIONINDEX, i % MAXHARMONYJEWELOPTIONINDEX];
                int b = i * ENTRY_SIZE;
                entry.OptionType = BitConverter.ToInt32(data, b);
                entry.Name = SourceBmd.Utf8Z(data, b + 4, NAME_SIZE);
                entry.Minlevel = BitConverter.ToInt32(data, b + 4 + NAME_SIZE);
                for (int l = 0; l < LEVEL_COUNT; l++)
                {
                    entry.HarmonyJewelLevel[l] = BitConverter.ToInt32(data, b + 4 + NAME_SIZE + 4 + l * 4);
                    entry.Zen[l] = BitConverter.ToInt32(data, b + 4 + NAME_SIZE + 4 + LEVEL_COUNT * 4 + l * 4);
                }
            }
        }

        public StrengthenItem GetItemType(int type)
        {
            if (ITEM_SWORD <= type && ITEM_STAFF > type) return StrengthenItem.SI_Weapon;
            if (ITEM_STAFF <= type && ITEM_SHIELD > type) return StrengthenItem.SI_Staff;
            if (ITEM_SHIELD <= type && ITEM_WING > type) return StrengthenItem.SI_Defense;
            return StrengthenItem.SI_None;
        }

        public bool IsHarmonyJewelOption(StrengthenItem type, int option)
        {
            switch (type)
            {
                case StrengthenItem.SI_Weapon: return 0 <= option && (MAXHARMONYJEWELOPTIONINDEX + 1) > option;
                case StrengthenItem.SI_Staff: return 0 <= option && (MAXHARMONYJEWELOPTIONINDEX + 1) - 2 > option;
                case StrengthenItem.SI_Defense: return 0 <= option && (MAXHARMONYJEWELOPTIONINDEX + 1) - 2 > option;
            }
            return false;
        }

        public HarmonyJewelOption GetHarmonyJewelOptionInfo(StrengthenItem type, int option)
        {
            int index = option - 1;
            if ((int)type < 0 || (int)type >= MAXHARMONYJEWELOPTIONTYPE || index < 0 || index >= MAXHARMONYJEWELOPTIONINDEX)
                return new HarmonyJewelOption();
            return m_OptionData[(int)type, index];
        }

        private static int LevelValue(HarmonyJewelOption h, int level) => level >= 0 && level < h.HarmonyJewelLevel.Length ? h.HarmonyJewelLevel[level] : 0;

        /// <summary>JewelHarmonyInfo::GetStrengthenCapability (UIJewelHarmony.cpp:203).</summary>
        public void GetStrengthenCapability(StrengthenCapability pitemSC, SourceItem pitem, int index)
        {
            if (pitem.Jewel_Of_Harmony_Option == 0) return;
            var type = GetItemType(pitem.Type);
            if (type >= StrengthenItem.SI_None || !IsHarmonyJewelOption(type, pitem.Jewel_Of_Harmony_Option)) return;

            var harmonyjewel = GetHarmonyJewelOptionInfo(type, pitem.Jewel_Of_Harmony_Option);
            int value = LevelValue(harmonyjewel, pitem.Jewel_Of_Harmony_OptionLevel);
            int opt = pitem.Jewel_Of_Harmony_Option;

            if (index == 0)
            {
                if (type == StrengthenItem.SI_Weapon)
                {
                    pitemSC.SI_isNB = true;
                    if (opt == 3) pitemSC.SI_NB.SI_force = value;
                    else if (opt == 4) pitemSC.SI_NB.SI_activity = value;
                }
                else if (type == StrengthenItem.SI_Staff)
                {
                    pitemSC.SI_isNB = true;
                    if (opt == 2) pitemSC.SI_NB.SI_force = value;
                    else if (opt == 3) pitemSC.SI_NB.SI_activity = value;
                }
            }
            else if (index == 1)
            {
                if (type == StrengthenItem.SI_Weapon)
                {
                    pitemSC.SI_isSP = true;
                    if (opt == 1) pitemSC.SI_SP.SI_minattackpower = value;
                    else if (opt == 2) pitemSC.SI_SP.SI_maxattackpower = value;
                    else if (opt == 5) { pitemSC.SI_SP.SI_minattackpower = value; pitemSC.SI_SP.SI_maxattackpower = value; }
                    else if (opt == 7) pitemSC.SI_SP.SI_skillattackpower = value;
                    else if (opt == 8) pitemSC.SI_SP.SI_attackpowerRate = value;
                }
                else if (type == StrengthenItem.SI_Staff)
                {
                    pitemSC.SI_isSP = true;
                    if (opt == 1) pitemSC.SI_SP.SI_magicalpower = value;
                    else if (opt == 4) pitemSC.SI_SP.SI_skillattackpower = value;
                    else if (opt == 7) pitemSC.SI_SP.SI_attackpowerRate = value;
                }
            }
            else if (index == 2)
            {
                if (type == StrengthenItem.SI_Defense)
                {
                    pitemSC.SI_isSD = true;
                    if (opt == 1) pitemSC.SI_SD.SI_defense = value;
                    else if (opt == 2) pitemSC.SI_SD.SI_AG = value;
                    else if (opt == 3) pitemSC.SI_SD.SI_HP = value;
                    else if (opt == 6) pitemSC.SI_SD.SI_defenseRate = value;
                }
            }
        }
    }

    public struct ItemAddOption
    {
        public int m_byOption1;
        public int m_byValue1;
        public int m_byOption2;
        public int m_byValue2;
        public int m_Type;
        public uint m_Time;
    }

    /// <summary>ItemAddOptioninfo (ItemAddOptioninfo.cpp) backed by ItemAddOption.bmd (MAX_ITEM x 16 bytes).</summary>
    public sealed class ItemAddOptionInfo
    {
        private static readonly Lazy<ItemAddOptionInfo> s_instance = new(() => new ItemAddOptionInfo(), LazyThreadSafetyMode.ExecutionAndPublication);
        public static ItemAddOptionInfo Instance => s_instance.Value;

        private readonly ItemAddOption[] m_ItemAddOption = new ItemAddOption[MAX_ITEM];

        private ItemAddOptionInfo()
        {
            const int size = 16;
            byte[] data = SourceBmd.ReadAll("ItemAddOption.bmd");
            if (data.Length < size * MAX_ITEM) return;
            SourceBmd.BuxConvert(data, 0, size * MAX_ITEM);
            for (int i = 0; i < MAX_ITEM; i++)
            {
                int o = i * size;
                m_ItemAddOption[i] = new ItemAddOption
                {
                    m_byOption1 = data[o],
                    m_byValue1 = BitConverter.ToUInt16(data, o + 2),
                    m_byOption2 = data[o + 4],
                    m_byValue2 = BitConverter.ToUInt16(data, o + 6),
                    m_Type = data[o + 8],
                    m_Time = BitConverter.ToUInt32(data, o + 12),
                };
            }
        }

        public ItemAddOption GetItemAddOtioninfo(int type)
            => type >= 0 && type < MAX_ITEM ? m_ItemAddOption[type] : default;

        /// <summary>ItemAddOptioninfo::GetItemAddOtioninfoText (ItemAddOptioninfo.cpp).</summary>
        public void GetItemAddOtioninfoText(List<string> outtextlist, int type)
        {
            var option = GetItemAddOtioninfo(type);
            for (int i = 0; i < 2; ++i)
            {
                int optiontype = i == 0 ? option.m_byOption1 : option.m_byOption2;
                int optionvalue = i == 0 ? option.m_byValue1 : option.m_byValue2;
                string TempText = string.Empty;
                switch (optiontype)
                {
                    case 1: TempText = GlobalText.Format(GlobalText.Get(2184), optionvalue); break;
                    case 2: TempText = GlobalText.Format(GlobalText.Get(2185), optionvalue); break;
                    case 3: TempText = GlobalText.Format(GlobalText.Get(2186), optionvalue); break;
                    case 4: TempText = GlobalText.Format(GlobalText.Get(2187), optionvalue); break;
                    case 5: TempText = GlobalText.Format(GlobalText.Get(2188), optionvalue); break;
                    case 6: TempText = GlobalText.Format(GlobalText.Get(2189), optionvalue); break;
                    case 7: TempText = GlobalText.Format(GlobalText.Get(2190)); break;
                    case 8: TempText = GlobalText.Format(GlobalText.Get(2191), optionvalue); break;
                }
                outtextlist.Add(TempText);
            }
        }
    }

    /// <summary>COMGEM (CComGem.cpp / _enum.h:3561) subset used by RenderItemInfo.</summary>
    public static class COMGEM
    {
        public const int NOGEM = -1;
        public const int FIRST = 10;
        public const int eGEM_NAME = 0, eGEM_INDEX = 1;
        public const int eBLESS = 0, eBLESS_C = 1, eSOUL = 2, eSOUL_C = 3, eLIFE = 4, eLIFE_C = 5, eCREATE = 6, eCREATE_C = 7,
            ePROTECT = 8, ePROTECT_C = 9, eGEMSTONE = 10, eGEMSTONE_C = 11, eHARMONY = 12, eHARMONY_C = 13, eCHAOS = 14, eCHAOS_C = 15,
            eLOW = 16, eLOW_C = 17, eUPPER = 18, eUPPER_C = 19;

        public static int Check_Jewel(int _nJewel, int _nType = 0)
        {
            bool bCom = (_nType & 1) == 0;
            bool bNon = (_nType & 2) == 0;

            if (bNon)
            {
                if (_nJewel == ITEM_JEWEL_OF_BLESS) return eBLESS;
                if (_nJewel == ITEM_JEWEL_OF_SOUL) return eSOUL;
                if (_nJewel == ITEM_JEWEL_OF_LIFE) return eLIFE;
                if (_nJewel == ITEM_JEWEL_OF_CREATION) return eCREATE;
                if (_nJewel == ITEM_JEWEL_OF_GUARDIAN) return ePROTECT;
                if (_nJewel == ITEM_GEMSTONE) return eGEMSTONE;
                if (_nJewel == ITEM_JEWEL_OF_HARMONY) return eHARMONY;
                if (_nJewel == ITEM_JEWEL_OF_CHAOS) return eCHAOS;
                if (_nJewel == ITEM_LOWER_REFINE_STONE) return eLOW;
                if (_nJewel == ITEM_HIGHER_REFINE_STONE) return eUPPER;
            }

            if (bCom)
            {
                if (_nJewel == ITEM_PACKED_JEWEL_OF_BLESS) return eBLESS_C;
                if (_nJewel == ITEM_PACKED_JEWEL_OF_SOUL) return eSOUL_C;
                if (_nJewel == ITEM_PACKED_JEWEL_OF_LIFE) return eLIFE_C;
                if (_nJewel == ITEM_PACKED_JEWEL_OF_CREATION) return eCREATE_C;
                if (_nJewel == ITEM_PACKED_JEWEL_OF_GUARDIAN) return ePROTECT_C;
                if (_nJewel == ITEM_PACKED_GEMSTONE) return eGEMSTONE_C;
                if (_nJewel == ITEM_PACKED_JEWEL_OF_HARMONY) return eHARMONY_C;
                if (_nJewel == ITEM_PACKED_JEWEL_OF_CHAOS) return eCHAOS_C;
                if (_nJewel == ITEM_PACKED_LOWER_REFINE_STONE) return eLOW_C;
                if (_nJewel == ITEM_PACKED_HIGHER_REFINE_STONE) return eUPPER_C;
            }

            return NOGEM;
        }

        public static int Check_Jewel_Com(int _nJewel) => Check_Jewel(_nJewel, 2);
        public static bool isCompiledGem(SourceItem p) => Check_Jewel_Com(p.Type) != NOGEM;
        public static int CheckOneItem(SourceItem p) => Check_Jewel(p.Type);

        public static int CalcCompiledCount(SourceItem p)
            => (CheckOneItem(p) % 2) != 0 ? (p.Level + 1) * FIRST : 0;

        public static int GetJewelIndex(int _nJewel, int _nType)
        {
            if (_nType != eGEM_NAME) return -1;
            switch (_nJewel)
            {
                case eBLESS: case eBLESS_C: return 1806;
                case eSOUL: case eSOUL_C: return 1807;
                case eLIFE: case eLIFE_C: return 3312;
                case eCREATE: case eCREATE_C: return 3313;
                case ePROTECT: case ePROTECT_C: return 3314;
                case eGEMSTONE: case eGEMSTONE_C: return 2081;
                case eHARMONY: case eHARMONY_C: return 3315;
                case eCHAOS: case eCHAOS_C: return 3316;
                case eLOW: case eLOW_C: return 3317;
                case eUPPER: case eUPPER_C: return 3318;
            }
            return -1;
        }
    }

    /// <summary>
    /// Hero values the tooltip reads (CharacterAttribute / Hero->Class in the original). Taken from the
    /// client's CharacterState; class mapping follows CCharacterManager::GetBaseClass/GetStepClass.
    /// </summary>
    public sealed class ItemTooltipHero
    {
        public int Level;
        public int Strength, AddStrength;
        public int Dexterity, AddDexterity;
        public int Vitality, AddVitality;
        public int Energy, AddEnergy;
        public int Charisma, AddCharisma;
        public int Class;      // OpenMU CharacterClassNumber value
        public long Gold;
        public int LifeMax, ManaMax, MasterLevel;

        [ThreadStatic] private static ItemTooltipHero t_current;
        public static ItemTooltipHero Current => t_current ??= new ItemTooltipHero();
        internal static void SetCurrent(ItemTooltipHero hero) => t_current = hero;

        public static ItemTooltipHero From(global::Client.Main.Core.Client.CharacterState state)
        {
            var hero = new ItemTooltipHero();
            if (state == null) return hero;
            hero.Level = state.Level;
            hero.Strength = state.Strength; hero.AddStrength = state.AddedStrength;
            hero.Dexterity = state.Agility; hero.AddDexterity = state.AddedAgility;
            hero.Vitality = state.Vitality; hero.AddVitality = state.AddedVitality;
            hero.Energy = state.Energy; hero.AddEnergy = state.AddedEnergy;
            hero.Charisma = state.Leadership; hero.AddCharisma = state.AddedLeadership;
            hero.Class = (int)state.Class;
            hero.Gold = state.InventoryZen;
            hero.LifeMax = (int)state.MaximumHealth;
            hero.ManaMax = (int)state.MaximumMana;
            hero.MasterLevel = state.MasterLevel;
            return hero;
        }

        /// <summary>CCharacterManager::GetBaseClass: CLASS_WIZARD..CLASS_RAGEFIGHTER (0..6).</summary>
        public static int GetBaseClass(int cls) => (cls >> 2) & 0x07;

        /// <summary>CCharacterManager::IsSecondClass.</summary>
        public static bool IsSecondClass(int cls) => (cls & 0x03) == 2;

        /// <summary>CCharacterManager::IsThirdClass (also IsMasterLevel).</summary>
        public static bool IsThirdClass(int cls)
        {
            int sub = cls & 0x03;
            int baseClass = GetBaseClass(cls);
            if (baseClass == CLASS_DARK || baseClass == CLASS_DARK_LORD || baseClass == CLASS_RAGEFIGHTER)
                return sub == 1;
            return sub == 3;
        }

        /// <summary>CCharacterManager::GetStepClass: 1, 2 or 3.</summary>
        public static int GetStepClass(int cls)
        {
            if (IsThirdClass(cls)) return 3;
            if (IsSecondClass(cls)) return 2;
            return 1;
        }
    }
}
