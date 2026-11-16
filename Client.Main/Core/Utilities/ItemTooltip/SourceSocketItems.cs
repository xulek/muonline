using System;
using System.Threading;
using static Client.Main.Core.Utilities.ItemTooltip.SourceConstants;

namespace Client.Main.Core.Utilities.ItemTooltip
{
    /// <summary>
    /// CSocketItemMgr (SocketSystem.cpp) data and value helpers, backed by socketitem_eng.bmd
    /// (MAX_SOCKET_OPTION_TYPES x MAX_SOCKET_OPTION x SOCKET_OPTION_INFO_FILE, 104 bytes each).
    /// </summary>
    public static class SourceSocketItems
    {
        public const int SOCKET_EMPTY = 0xFE;          // _define.h:354
        public const int MAX_SOCKET_OPTION = 50;
        public const int MAX_SOCKET_TYPES = 6;
        public const int SOT_SOCKET_ITEM_OPTIONS = 0, SOT_MIX_SET_BONUS_OPTIONS = 1, SOT_EQUIP_SET_BONUS_OPTIONS = 2, MAX_SOCKET_OPTION_TYPES = 3;

        public sealed class SocketOptionInfo
        {
            public int m_iOptionID;
            public int m_iOptionCategory;
            public string m_szOptionName = string.Empty;
            public int m_bOptionType;
            public readonly int[] m_iOptionValue = new int[5];
            public readonly byte[] m_bySocketCheckInfo = new byte[6];
        }

        private static readonly Lazy<SocketOptionInfo[,]> s_info = new(Load, LazyThreadSafetyMode.ExecutionAndPublication);
        public static SocketOptionInfo Info(int type, int index)
            => index >= 0 && index < MAX_SOCKET_OPTION ? s_info.Value[type, index] : new SocketOptionInfo();

        // CSocketItemMgr::OpenSocketItemScript (SocketSystem.cpp:577)
        private static SocketOptionInfo[,] Load()
        {
            var info = new SocketOptionInfo[MAX_SOCKET_OPTION_TYPES, MAX_SOCKET_OPTION];
            for (int j = 0; j < MAX_SOCKET_OPTION_TYPES; j++)
                for (int i = 0; i < MAX_SOCKET_OPTION; i++)
                    info[j, i] = new SocketOptionInfo();

            const int size = 104;
            byte[] data = SourceBmd.ReadAll("socketitem_eng.bmd");
            if (data.Length < size * MAX_SOCKET_OPTION_TYPES * MAX_SOCKET_OPTION) return info;
            for (int j = 0; j < MAX_SOCKET_OPTION_TYPES; j++)
            {
                for (int i = 0; i < MAX_SOCKET_OPTION; i++)
                {
                    int o = (j * MAX_SOCKET_OPTION + i) * size;
                    SourceBmd.BuxConvert(data, o, size);
                    var t = info[j, i];
                    t.m_iOptionID = BitConverter.ToInt32(data, o);
                    t.m_iOptionCategory = BitConverter.ToInt32(data, o + 4);
                    t.m_szOptionName = SourceBmd.Utf8Z(data, o + 8, 64);
                    t.m_bOptionType = (sbyte)data[o + 72];
                    for (int v = 0; v < 5; v++) t.m_iOptionValue[v] = BitConverter.ToInt32(data, o + 76 + v * 4);
                    for (int v = 0; v < 6; v++) t.m_bySocketCheckInfo[v] = data[o + 96 + v];
                }
            }
            return info;
        }

        // CSocketItemMgr::IsSocketItem (SocketSystem.cpp:44)
        public static bool IsSocketItem(int type)
        {
            switch (type)
            {
                case ITEM_FLAMBERGE: case ITEM_SWORD_BREAKER: case ITEM_IMPERIAL_SWORD: case ITEM_FROST_MACE: case ITEM_ABSOLUTE_SCEPTER:
                case ITEM_STINGER_BOW: case ITEM_DEADLY_STAFF: case ITEM_IMPERIAL_STAFF: case ITEM_STAFF + 32: case ITEM_CRIMSONGLORY:
                case ITEM_SALAMANDER_SHIELD: case ITEM_FROST_BARRIER: case ITEM_GUARDIAN_SHILED:
                case ITEM_TITAN_HELM: case ITEM_TITAN_ARMOR: case ITEM_TITAN_PANTS: case ITEM_TITAN_GLOVES: case ITEM_TITAN_BOOTS:
                case ITEM_BRAVE_HELM: case ITEM_BRAVE_ARMOR: case ITEM_BRAVE_PANTS: case ITEM_BRAVE_GLOVES: case ITEM_BRAVE_BOOTS:
                case ITEM_HELM + 47: case ITEM_DESTORY_ARMOR: case ITEM_DESTORY_PANTS: case ITEM_DESTORY_GLOVES: case ITEM_DESTORY_BOOTS:
                case ITEM_HELM + 48: case ITEM_PHANTOM_ARMOR: case ITEM_PHANTOM_PANTS: case ITEM_PHANTOM_GLOVES: case ITEM_PHANTOM_BOOTS:
                case ITEM_SERAPHIM_HELM: case ITEM_SERAPHIM_ARMOR: case ITEM_SERAPHIM_PANTS: case ITEM_SERAPHIM_GLOVES: case ITEM_SERAPHIM_BOOTS:
                case ITEM_FAITH_HELM: case ITEM_FAITH_ARMOR: case ITEM_FAITH_PANTS: case ITEM_FAITH_GLOVES: case ITEM_FAITH_BOOTS:
                case ITEM_PAEWANG_MASK: case ITEM_PAEWANG_ARMOR: case ITEM_PAEWANG_PANTS: case ITEM_PAEWANG_GLOVES: case ITEM_PHAEWANG_BOOTS:
                case ITEM_HADES_HELM: case ITEM_HADES_ARMOR: case ITEM_HADES_PANTS: case ITEM_HADES_GLOVES: case ITEM_HADES_BOOTS:
                case ITEM_HELM + 53: case ITEM_ARMOR + 53: case ITEM_PANTS + 53: case ITEM_GLOVES + 53: case ITEM_BOOTS + 53:
                    return true;
            }
            return false;
        }

        // CSocketItemMgr::CalcSocketOptionValue (SocketSystem.cpp:148)
        public static int CalcSocketOptionValue(int iOptionType, float fOptionValue)
        {
            var hero = ItemTooltipHero.Current;
            switch (iOptionType)
            {
                case 1:
                case 2:
                    return (int)fOptionValue;
                case 3:
                {
                    int wLevel = ItemTooltipHero.IsThirdClass(hero.Class) ? hero.Level + hero.MasterLevel : hero.Level;
                    return fOptionValue == 0 ? 0 : (int)((float)(ushort)wLevel / fOptionValue);
                }
                case 4:
                    return fOptionValue == 0 ? 0 : (int)((float)hero.LifeMax / fOptionValue);
                case 5:
                    return fOptionValue == 0 ? 0 : (int)((float)hero.ManaMax / fOptionValue);
                default:
                    return 0;
            }
        }

        // CSocketItemMgr::CalcSocketOptionValueText (SocketSystem.cpp:193)
        public static string CalcSocketOptionValueText(int iOptionType, float fOptionValue)
            => iOptionType == 2
                ? GlobalText.Format("+%d%%", CalcSocketOptionValue(iOptionType, fOptionValue))
                : GlobalText.Format("+%d", CalcSocketOptionValue(iOptionType, fOptionValue));

        // CSocketItemMgr::CreateSocketOptionText (SocketSystem.cpp:206)
        public static string CreateSocketOptionText(int iSeedID, int iSphereLv)
        {
            var pInfo = Info(SOT_SOCKET_ITEM_OPTIONS, iSeedID);
            float fOptionValue = iSphereLv >= 1 && iSphereLv <= 5 ? pInfo.m_iOptionValue[iSphereLv - 1] : 0;
            string value = CalcSocketOptionValueText(pInfo.m_bOptionType, fOptionValue);
            return GlobalText.Format("%s(%s %s)", GlobalText.Get(2640 + pInfo.m_iOptionCategory - 1), pInfo.m_szOptionName, value);
        }

        // CSocketItemMgr::GetSocketOptionValue (SocketSystem.cpp:466)
        public static int GetSocketOptionValue(SourceItem pItem, int iSocketIndex)
        {
            if (pItem.SocketCount > 0 && pItem.SocketSeedID[iSocketIndex] != SOCKET_EMPTY)
            {
                var pInfo = Info(SOT_SOCKET_ITEM_OPTIONS, pItem.SocketSeedID[iSocketIndex]);
                int lv = pItem.SocketSphereLv[iSocketIndex];
                float fOptionValue = lv >= 1 && lv <= 5 ? pInfo.m_iOptionValue[lv - 1] : 0;
                return CalcSocketOptionValue(pInfo.m_bOptionType, fOptionValue);
            }
            return 0;
        }

        // CSocketItemMgr::CalcSocketBonusItemValue (SocketSystem.cpp:119)
        public static long CalcSocketBonusItemValue(SourceItem pItem, long iOrgGold)
        {
            long iGoldResult = 0;
            if (IsSocketItem(pItem.Type))
            {
                iGoldResult += (long)(iOrgGold * (pItem.SocketCount * 0.8f));
                for (int i = 0; i < pItem.SocketCount; ++i)
                {
                    int seed = pItem.SocketSeedID[i];
                    if (seed == SOCKET_EMPTY) continue;
                    int iSeedSphereType = 0;
                    if (seed >= 0 && seed <= 9) iSeedSphereType = 0;
                    else if (seed >= 10 && seed <= 15) iSeedSphereType = 1;
                    else if (seed >= 16 && seed <= 20) iSeedSphereType = 2;
                    else if (seed >= 21 && seed <= 28) iSeedSphereType = 3;
                    else if (seed >= 29 && seed <= 33) iSeedSphereType = 4;
                    else if (seed >= 34 && seed <= 40) iSeedSphereType = 5;

                    // ITEM TempSeedSphere: only Type is set (memory otherwise zero / default)
                    var temp = new SourceItem { Type = ITEM_SEED_SPHERE_FIRE_1 + (pItem.SocketSphereLv[i] - 1) * MAX_SOCKET_TYPES + iSeedSphereType };
                    var def = ItemDatabase.GetItemDefinition((byte)(temp.Type / MAX_ITEM_INDEX), (short)(temp.Type % MAX_ITEM_INDEX));
                    temp.Attribute = SourceItemAttribute.FromDefinition(def);
                    iGoldResult += SourceItemValue.ItemValue(temp, 0);
                }
            }
            return iGoldResult;
        }
    }
}
