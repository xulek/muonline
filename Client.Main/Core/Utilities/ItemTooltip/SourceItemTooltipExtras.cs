using System;
using System.Collections.Generic;
using Client.Main.Controls.UI.Game.Inventory;
using static Client.Main.Core.Utilities.ItemTooltip.SourceConstants;

namespace Client.Main.Core.Utilities.ItemTooltip
{
public sealed partial class SourceItemTooltip
{
    // ------------------------------------------------------------------ sockets (SocketSystem.cpp)

    // CSocketItemMgr::AttachToolTipForSocketItem (SocketSystem.cpp:223)
    private int AttachToolTipForSocketItem(SourceItem pItem, int iTextNum)
    {
        if (pItem.SocketCount == 0) return iTextNum;

        swprintf(iTextNum, "\n"); ++iTextNum; ++SkipNum;
        swprintf(iTextNum, "%s %s", GlobalText[2650], GlobalText[159]);
        TextListColor[iTextNum] = TEXT_COLOR_PURPLE;
        TextBold[iTextNum] = false;
        ++iTextNum;
        swprintf(iTextNum, "\n"); ++iTextNum; ++SkipNum;

        for (int i = 0; i < pItem.SocketCount && i < pItem.SocketSeedID.Length; ++i)
        {
            string szOptionText = string.Empty;
            if (pItem.SocketSeedID[i] == SOCKET_EMPTY)
            {
                szOptionText = Fmt(GlobalText[2652]);
                TextListColor[iTextNum] = TEXT_COLOR_GRAY;
            }
            else if (pItem.SocketSeedID[i] < SourceSocketItems.MAX_SOCKET_OPTION)
            {
                szOptionText = SourceSocketItems.CreateSocketOptionText(pItem.SocketSeedID[i], pItem.SocketSphereLv[i]);
                TextListColor[iTextNum] = TEXT_COLOR_BLUE;
            }

            swprintf(iTextNum, GlobalText[2655], i + 1, szOptionText);
            TextBold[iTextNum] = false;
            ++iTextNum;
        }

        if (pItem.SocketSeedSetOption < SourceSocketItems.MAX_SOCKET_OPTION)
        {
            var pInfo = SourceSocketItems.Info(SourceSocketItems.SOT_MIX_SET_BONUS_OPTIONS, pItem.SocketSeedSetOption);
            if (pInfo.m_iOptionValue[0] > 0)
            {
                swprintf(iTextNum, "\n"); ++iTextNum; ++SkipNum;
                swprintf(iTextNum, "%s", GlobalText[2656]);
                TextListColor[iTextNum] = TEXT_COLOR_PURPLE;
                TextBold[iTextNum] = false;
                ++iTextNum;
                swprintf(iTextNum, "\n"); ++iTextNum; ++SkipNum;

                string value = SourceSocketItems.CalcSocketOptionValueText(pInfo.m_bOptionType, pInfo.m_iOptionValue[0]);
                swprintf(iTextNum, "%s %s", pInfo.m_szOptionName, value);
                TextListColor[iTextNum] = TEXT_COLOR_BLUE;
                TextBold[iTextNum] = false;
                ++iTextNum;
            }
        }
        return iTextNum;
    }

    private static int SeedIdFromCategory(int iCategoryIndex, int iLevel) => iCategoryIndex switch
    {
        1 => 0 + iLevel,
        2 => 10 + iLevel,
        3 => 16 + iLevel,
        4 => 21 + iLevel,
        5 => 29 + iLevel,
        6 => 34 + iLevel,
        _ => 0,
    };

    // CSocketItemMgr::AttachToolTipForSeedSphereItem (SocketSystem.cpp:282)
    private int AttachToolTipForSeedSphereItem(SourceItem pItem, int iTextNum)
    {
        if (pItem.Type >= ITEM_SEED_FIRE && pItem.Type <= ITEM_SEED_EARTH)
        {
            int iCategoryIndex = pItem.Type - ITEM_SEED_FIRE + 1;
            swprintf(iTextNum, GlobalText[2653], GlobalText[2640 + iCategoryIndex - 1]);
            TextListColor[iTextNum] = TEXT_COLOR_WHITE;
            TextBold[iTextNum] = false;
            ++iTextNum;
            swprintf(iTextNum, "\n"); ++iTextNum; ++SkipNum;

            var pInfo = SourceSocketItems.Info(SourceSocketItems.SOT_SOCKET_ITEM_OPTIONS, SeedIdFromCategory(iCategoryIndex, pItem.Level));
            swprintf(iTextNum, pInfo.m_szOptionName);
            TextListColor[iTextNum] = TEXT_COLOR_BLUE;
            TextBold[iTextNum] = false;
            ++iTextNum;
        }
        else if (pItem.Type >= ITEM_SPHERE_MONO && pItem.Type <= ITEM_SPHERE_5)
        {
            int iSphereLevel = pItem.Type - ITEM_SPHERE_MONO + 1;
            swprintf(iTextNum, GlobalText[2654], iSphereLevel);
            TextListColor[iTextNum] = TEXT_COLOR_WHITE;
            TextBold[iTextNum] = false;
            ++iTextNum;
        }
        else if (pItem.Type >= ITEM_SEED_SPHERE_FIRE_1 && pItem.Type <= ITEM_SEED_SPHERE_EARTH_5)
        {
            int iCategoryIndex = (pItem.Type - ITEM_SEED_SPHERE_FIRE_1) % 6 + 1;
            swprintf(iTextNum, GlobalText[2653], GlobalText[2640 + iCategoryIndex - 1]);
            TextListColor[iTextNum] = TEXT_COLOR_WHITE;
            TextBold[iTextNum] = false;
            ++iTextNum;
            swprintf(iTextNum, "\n"); ++iTextNum; ++SkipNum;

            var pInfo = SourceSocketItems.Info(SourceSocketItems.SOT_SOCKET_ITEM_OPTIONS, SeedIdFromCategory(iCategoryIndex, pItem.Level));
            int valueIndex = (pItem.Type - ITEM_SEED_SPHERE_FIRE_1) / 6;
            float fOptionValue = valueIndex >= 0 && valueIndex < 5 ? pInfo.m_iOptionValue[valueIndex] : 0;
            string value = SourceSocketItems.CalcSocketOptionValueText(pInfo.m_bOptionType, fOptionValue);
            swprintf(iTextNum, "%s %s", pInfo.m_szOptionName, value);
            TextListColor[iTextNum] = TEXT_COLOR_BLUE;
            TextBold[iTextNum] = false;
            ++iTextNum;
        }
        return iTextNum;
    }

    // ------------------------------------------------------------------ pets (GIPetManager.cpp)

    // giPetManager::CalcPetInfo (GIPetManager.cpp:379)
    private void CalcPetInfo(SourcePetInfo pPetInfo)
    {
        int Charisma = Hero.Charisma + Hero.AddCharisma;
        int Strength = Hero.Strength + Hero.AddStrength;
        int Level = pPetInfo.m_wLevel + 1;
        switch (pPetInfo.m_dwPetType)
        {
            case SourcePetInfo.PET_TYPE_DARK_SPIRIT:
                pPetInfo.m_dwExp2 = (long)(10 + Level) * Level * Level * Level * 100;
                pPetInfo.m_wDamageMin = 180 + (pPetInfo.m_wLevel * 15) + (Charisma / 8);
                pPetInfo.m_wDamageMax = 200 + (pPetInfo.m_wLevel * 15) + (Charisma / 4);
                pPetInfo.m_wAttackSpeed = 20 + (pPetInfo.m_wLevel * 4 / 5) + (Charisma / 50);
                pPetInfo.m_wAttackSuccess = (1000 + pPetInfo.m_wLevel) + (pPetInfo.m_wLevel * 15);
                break;
            case SourcePetInfo.PET_TYPE_DARK_HORSE:
                pPetInfo.m_dwExp2 = (long)(10 + Level) * Level * Level * Level * 100;
                pPetInfo.m_wDamageMin = (Strength / 10) + (Charisma / 10) + (pPetInfo.m_wLevel * 5);
                pPetInfo.m_wDamageMax = pPetInfo.m_wDamageMin + (pPetInfo.m_wDamageMin / 2);
                pPetInfo.m_wAttackSpeed = 20 + (pPetInfo.m_wLevel * 4 / 5) + (Charisma / 50);
                pPetInfo.m_wAttackSuccess = (1000 + pPetInfo.m_wLevel) + (pPetInfo.m_wLevel * 15);
                break;
        }
    }

    // giPetManager::SetPetItemConvert (GIPetManager.cpp:406)
    private void SetPetItemConvert(SourceItem ip, SourcePetInfo pPetInfo)
    {
        if (ip.Type != ITEM_DARK_HORSE_ITEM) return;
        int Index = 0;
        for (int i = 0; i < ip.SpecialNum; ++i)
        {
            if (ip.Special[i] == AT_SET_OPTION_IMPROVE_DEFENCE) { Index = i; break; }
        }
        int value = (5 + (Hero.Dexterity / 20) + pPetInfo.m_wLevel * 2) & 0xFF;
        if (Index == 0)
            ip.AddSpecial(AT_SET_OPTION_IMPROVE_DEFENCE, value);
        else
        {
            ip.SpecialValue[Index] = value;
            ip.Special[Index] = AT_SET_OPTION_IMPROVE_DEFENCE;
        }
    }

    // giPetManager::RenderPetItemInfo (GIPetManager.cpp:454)
    private bool RenderPetItemInfo(SourceItem pItem, int iInvenType)
    {
        var pPetInfo = pItem.PetInfo;
        if (pPetInfo == null || pPetInfo.m_dwPetType < 0) return false;
        CalcPetInfo(pPetInfo);
        if (pItem.Type == ITEM_DARK_HORSE_ITEM) SetPetItemConvert(pItem, pPetInfo); // SetPetInfo -> SetPetItemConvert for the equipped horse

        int RequireLevel = 0;
        int RequireCharisma = 0;

        if (IsNpcShopVisible)
        {
            long Gold = (pPetInfo.m_dwPetType == SourcePetInfo.PET_TYPE_DARK_HORSE ? pPetInfo.m_wLevel * 2000000L : pPetInfo.m_wLevel * 1000000L) / 3;
            Gold = Gold / 100 * 100;
            swprintf(TextNum, GlobalText[63], ConvertGold(Gold));
            TextListColor[TextNum] = TEXT_COLOR_WHITE;
            TextBold[TextNum] = true;
            TextNum++;
            swprintf(TextNum, "\n"); TextNum++; SkipNum++;
        }
        else if (iInvenType == TOOLTIP_TYPE_MY_SHOP || iInvenType == TOOLTIP_TYPE_PURCHASE_SHOP)
        {
            int price = 0;
            if (GetPersonalItemPrice(0, ref price, g_IsPurchaseShop))
            {
                swprintf(TextNum, GlobalText[63], ConvertGold(price));
                if (price >= 10000000) TextListColor[TextNum] = TEXT_COLOR_RED;
                else if (price >= 1000000) TextListColor[TextNum] = TEXT_COLOR_YELLOW;
                else if (price >= 100000) TextListColor[TextNum] = TEXT_COLOR_GREEN;
                else TextListColor[TextNum] = TEXT_COLOR_WHITE;
                TextBold[TextNum] = true;
                TextNum++;
                swprintf(TextNum, "\n"); TextNum++; SkipNum++;

                if (Hero.Gold < price && g_IsPurchaseShop == PSHOPWNDTYPE_PURCHASE)
                {
                    TextListColor[TextNum] = TEXT_COLOR_RED;
                    TextBold[TextNum] = true;
                    swprintf(TextNum, GlobalText[423]);
                    TextNum++;
                    swprintf(TextNum, "\n"); TextNum++; SkipNum++;
                }
            }
            else if (g_IsPurchaseShop == PSHOPWNDTYPE_SALE)
            {
                TextListColor[TextNum] = TEXT_COLOR_RED;
                TextBold[TextNum] = true;
                swprintf(TextNum, GlobalText[1101]);
                TextNum++;
                swprintf(TextNum, "\n"); TextNum++; SkipNum++;
            }
        }

        TextListColor[TextNum] = TEXT_COLOR_BLUE; TextBold[TextNum] = true;
        if (pItem.Type == ITEM_DARK_HORSE_ITEM)
        {
            RequireLevel = 218 + (pPetInfo.m_wLevel * 2);
            RequireCharisma = 0;
            swprintf(TextNum, GlobalText[1187]); TextNum++; SkipNum++;
        }
        else if (pItem.Type == ITEM_DARK_RAVEN_ITEM)
        {
            RequireCharisma = 185 + (pPetInfo.m_wLevel * 15);
            swprintf(TextNum, GlobalText[1214]); TextNum++; SkipNum++;
        }

        TextListColor[TextNum] = TEXT_COLOR_WHITE;
        swprintf(TextNum, "\n"); TextNum++; SkipNum++;
        swprintf(TextNum, "\n"); TextNum++; SkipNum++;
        swprintf(TextNum, GlobalText[201], pPetInfo.m_dwExp1, pPetInfo.m_dwExp2); TextNum++; SkipNum++;
        swprintf(TextNum, "%s : %d", GlobalText[368], pPetInfo.m_wLevel); TextNum++; SkipNum++;

        if (pItem.Type == ITEM_DARK_RAVEN_ITEM)
        {
            swprintf(TextNum, GlobalText[203], pPetInfo.m_wDamageMin, pPetInfo.m_wDamageMax, pPetInfo.m_wAttackSuccess); TextNum++; SkipNum++;
            swprintf(TextNum, GlobalText[64], pPetInfo.m_wAttackSpeed); TextNum++; SkipNum++;
        }
        swprintf(TextNum, GlobalText[70], pPetInfo.m_wLife); TextNum++; SkipNum++;

        if (pItem.Type == ITEM_DARK_HORSE_ITEM)
        {
            swprintf(TextNum, GlobalText[76], RequireLevel);
            if (Hero.Level < RequireLevel)
            {
                TextListColor[TextNum] = TEXT_COLOR_RED; TextBold[TextNum] = false; TextNum++;
                swprintf(TextNum, GlobalText[74], RequireLevel - Hero.Level);
                TextListColor[TextNum] = TEXT_COLOR_RED; TextBold[TextNum] = false; TextNum++;
            }
            else
            {
                TextListColor[TextNum] = TEXT_COLOR_WHITE; TextBold[TextNum] = false; TextNum++;
            }
        }
        else if (pItem.Type == ITEM_DARK_RAVEN_ITEM)
        {
            swprintf(TextNum, GlobalText[698], RequireCharisma);
            int Charisma = Hero.Charisma + Hero.AddCharisma;
            if (Charisma < RequireCharisma)
            {
                TextListColor[TextNum] = TEXT_COLOR_RED; TextBold[TextNum] = false; TextNum++;
                swprintf(TextNum, GlobalText[74], RequireCharisma - Charisma);
                TextListColor[TextNum] = TEXT_COLOR_RED; TextBold[TextNum] = false; TextNum++;
            }
            else
            {
                TextListColor[TextNum] = TEXT_COLOR_WHITE; TextBold[TextNum] = false; TextNum++;
            }
        }

        swprintf(TextNum, "\n"); TextNum++; SkipNum++;

        TextListColor[TextNum] = gCharacterManager.GetBaseClass(Hero.Class) == CLASS_DARK_LORD ? TEXT_COLOR_WHITE : TEXT_COLOR_DARKRED;
        swprintf(TextNum, GlobalText[61], GlobalText[24]); TextNum++; SkipNum++;

        for (int i = 0; i < pItem.SpecialNum; ++i)
        {
            SetPetItemConvert(pItem, pPetInfo);
            TextList[TextNum] = GetSpecialOptionText(pItem.Type, pItem.Special[i], pItem.SpecialValue[i], 0);
            TextListColor[TextNum] = TEXT_COLOR_BLUE;
            TextBold[TextNum] = false; TextNum++; SkipNum++;
        }

        if (pItem.Type == ITEM_DARK_HORSE_ITEM)
        {
            swprintf(TextNum, GlobalText[744], (30 + pPetInfo.m_wLevel) / 2);
            TextListColor[TextNum] = TEXT_COLOR_BLUE; TextNum++; SkipNum++;
            swprintf(TextNum, GlobalText[1188], 2);
            TextListColor[TextNum] = TEXT_COLOR_BLUE; TextBold[TextNum] = false; TextNum++;
        }

        return true;
    }

    // ------------------------------------------------------------------ repair (ZzzInventory.cpp:5642 RenderRepairInfo)

    // CChangeRingManager::CheckRepair (ChangeRingManager.cpp:160)
    private static bool CheckRepairChangeRing(int iType)
        => iType == ITEM_TRANSFORMATION_RING || iType == ITEM_ELITE_TRANSFER_SKELETON_RING || iType == ITEM_JACK_OLANTERN_TRANSFORMATION_RING
        || iType == ITEM_CHRISTMAS_TRANSFORMATION_RING || iType == ITEM_GAME_MASTER_TRANSFORMATION_RING || iType == ITEM_SNOWMAN_TRANSFORMATION_RING
        || iType == ITEM_PANDA_TRANSFORMATION_RING || iType == ITEM_SKELETON_TRANSFORMATION_RING;

    // IsRepairBan (ZzzInventory.cpp:7879)
    public static bool IsRepairBan(SourceItem pItem)
    {
        if (CheckRepairChangeRing(pItem.Type)) return true;
        if (IsPartChargeItem(pItem) || (pItem.Type >= ITEM_TYPE_CHARM_MIXWING + EWS_BEGIN && pItem.Type <= ITEM_TYPE_CHARM_MIXWING + EWS_END)) return true;

        if ((pItem.Type >= ITEM_POTION + 55 && pItem.Type <= ITEM_POTION + 57)
            || pItem.Type == ITEM_HELPER + 43 || pItem.Type == ITEM_HELPER + 44 || pItem.Type == ITEM_HELPER + 45
            || (pItem.Type >= ITEM_HELPER && pItem.Type <= ITEM_HORN_OF_DINORANT)
            || pItem.Type == ITEM_BOLT || pItem.Type == ITEM_ARROWS
            || pItem.Type >= ITEM_POTION
            || (pItem.Type >= ITEM_ORB_OF_TWISTING_SLASH && pItem.Type <= ITEM_ORB_OF_DEATH_STAB)
            || (pItem.Type >= ITEM_LOCHS_FEATHER && pItem.Type <= ITEM_WEAPON_OF_ARCHANGEL)
            || pItem.Type == ITEM_POTION + 21
            || pItem.Type == ITEM_DARK_HORSE_ITEM || pItem.Type == ITEM_DARK_RAVEN_ITEM
            || pItem.Type == ITEM_MOONSTONE_PENDANT || pItem.Type == ITEM_PET_RUDOLF || pItem.Type == ITEM_PET_PANDA
            || pItem.Type == ITEM_PANDA_TRANSFORMATION_RING || pItem.Type == ITEM_SKELETON_TRANSFORMATION_RING
            || pItem.Type == ITEM_PET_SKELETON || pItem.Type == ITEM_PET_UNICORN
            || pItem.Type == ITEM_CHERRY_BLOSSOM_PLAYBOX || pItem.Type == ITEM_CHERRY_BLOSSOM_WINE
            || pItem.Type == ITEM_CHERRY_BLOSSOM_RICE_CAKE || pItem.Type == ITEM_CHERRY_BLOSSOM_FLOWER_PETAL
            || pItem.Type == ITEM_POTION + 88 || pItem.Type == ITEM_POTION + 89 || pItem.Type == ITEM_GOLDEN_CHERRY_BLOSSOM_BRANCH
            || pItem.Type == ITEM_HELPER + 7 || pItem.Type == ITEM_TRANSFORMATION_RING || pItem.Type == ITEM_LIFE_STONE_ITEM
            || pItem.Type == ITEM_WIZARDS_RING || pItem.Type == ITEM_ARMOR_OF_GUARDSMAN
            || pItem.Type == ITEM_SPLINTER_OF_ARMOR || pItem.Type == ITEM_BLESS_OF_GUARDIAN || pItem.Type == ITEM_CLAW_OF_BEAST
            || pItem.Type == ITEM_FRAGMENT_OF_HORN || pItem.Type == ITEM_BROKEN_HORN || pItem.Type == ITEM_HORN_OF_FENRIR
            || pItem.Type == ITEM_OLD_SCROLL || pItem.Type == ITEM_ILLUSION_SORCERER_COVENANT || pItem.Type == ITEM_SCROLL_OF_BLOOD
            || pItem.Type == ITEM_HELPER + 66 || pItem.Type == ITEM_HELPER + 71 || pItem.Type == ITEM_HELPER + 72
            || pItem.Type == ITEM_HELPER + 73 || pItem.Type == ITEM_HELPER + 74 || pItem.Type == ITEM_HELPER + 75)
            return true;

        return Check_ItemAction(pItem, eITEM_REPAIR);
    }

    /// <summary>ConvertRepairGold (ZzzInventory.cpp:1261): NPC repair shop price or inventory self-repair price.</summary>
    public static long RepairGold(SourceItem ip, bool npcRepairShop)
    {
        int maxDurability = SourceItemAttributes.CalcMaxDurability(ip, ip.Attribute, ip.Level);
        long gold = SourceItemValue.ItemValue(ip, 2, ip.PetInfo?.m_wLevel ?? -1);
        if (gold == -1) return -1;
        return SourceItemValue.CalcRepairCost(gold, ip.Durability, maxDurability, ip.Type, !npcRepairShop);
    }

    /// <summary>RenderRepairInfo (ZzzInventory.cpp:5642). Returns an empty list for repair-banned items.</summary>
    public List<SourceTooltipLine> BuildRepair(InventoryItem item, ItemTooltipContext context)
    {
        var result = new List<SourceTooltipLine>();
        Context = context ?? ItemTooltipContext.Default;
        _item = item;
        var state = Context.Character ?? MuGame.Network?.GetCharacterState();
        Hero = ItemTooltipHero.From(state);
        ItemTooltipHero.SetCurrent(Hero);
        var ip = CreateSourceItem(item);
        if (ip == null) return result;
        ip.PetInfo = Context.PetInfo;

        if (IsRepairBan(ip)) return result;
        if (ip.Type >= ITEM_TYPE_CHARM_MIXWING + EWS_BEGIN && ip.Type <= ITEM_TYPE_CHARM_MIXWING + EWS_END) return result;
        switch (ip.Type)
        {
            case ITEM_HELPER + 107: case ITEM_HELPER + 104: case ITEM_HELPER + 105: case ITEM_HELPER + 103: case ITEM_POTION + 133:
            case ITEM_HELPER + 109: case ITEM_HELPER + 110: case ITEM_HELPER + 111: case ITEM_HELPER + 112: case ITEM_HELPER + 113:
            case ITEM_HELPER + 114: case ITEM_HELPER + 115: case ITEM_POTION + 112: case ITEM_POTION + 113: case ITEM_POTION + 120:
            case ITEM_POTION + 121: case ITEM_POTION + 122: case ITEM_POTION + 123: case ITEM_POTION + 124: case ITEM_HELPER + 121:
                return result;
        }
        if ((ITEM_POTION + 134 <= ip.Type && ip.Type <= ITEM_POTION + 139) || (ITEM_WING + 130 <= ip.Type && ip.Type <= ITEM_WING + 135)
            || (ITEM_POTION + 114 <= ip.Type && ip.Type <= ITEM_POTION + 119) || (ITEM_POTION + 126 <= ip.Type && ip.Type <= ITEM_POTION + 129)
            || (ITEM_POTION + 130 <= ip.Type && ip.Type <= ITEM_POTION + 132))
            return result;

        SourceItemAttribute p = ip.Attribute;
        Array.Clear(TextListColor);
        Array.Clear(TextBold);
        for (int i = 0; i < MAX_TEXT; i++) TextList[i] = null;
        TextNum = 0;
        SkipNum = 0;

        int Level = ip.Level;
        int Color;
        if (ip.Type == ITEM_JEWEL_OF_BLESS || ip.Type == ITEM_JEWEL_OF_SOUL || ip.Type == ITEM_JEWEL_OF_CHAOS) Color = TEXT_COLOR_YELLOW;
        else if (COMGEM.isCompiledGem(ip)) Color = TEXT_COLOR_YELLOW;
        else if (ip.Type == ITEM_DIVINE_STAFF_OF_ARCHANGEL || ip.Type == ITEM_DIVINE_SWORD_OF_ARCHANGEL || ip.Type == ITEM_DIVINE_CB_OF_ARCHANGEL || ip.Type == ITEM_DIVINE_SCEPTER_OF_ARCHANGEL) Color = TEXT_COLOR_PURPLE;
        else if (ip.Type == ITEM_DEVILS_EYE || ip.Type == ITEM_DEVILS_KEY || ip.Type == ITEM_DEVILS_INVITATION) Color = TEXT_COLOR_YELLOW;
        else if (ip.SpecialNum > 0 && ip.ExcellentFlags > 0) Color = TEXT_COLOR_GREEN;
        else if (Level >= 7) Color = TEXT_COLOR_YELLOW;
        else Color = ip.SpecialNum > 0 ? TEXT_COLOR_BLUE : TEXT_COLOR_WHITE;

        // the original compares ip->Type >= ITEM_CAPE_OF_LORD here (ZzzInventory.cpp:5803)
        bool wingLike = (ip.Type >= ITEM_WINGS_OF_SPIRITS && ip.Type <= ITEM_WINGS_OF_DARKNESS) || ip.Type >= ITEM_CAPE_OF_LORD
            || (ip.Type >= ITEM_WING_OF_STORM && ip.Type <= ITEM_CAPE_OF_EMPEROR) || (ip.Type >= ITEM_WINGS_OF_DESPAIR && ip.Type <= ITEM_WING_OF_DIMENSION)
            || (ip.Type >= ITEM_CAPE_OF_FIGHTER && ip.Type <= ITEM_CAPE_OF_OVERRULE);
        if (wingLike)
            Color = Level >= 7 ? TEXT_COLOR_YELLOW : ip.SpecialNum > 0 ? TEXT_COLOR_BLUE : TEXT_COLOR_WHITE;

        if (ip.Type < ITEM_POTION)
        {
            swprintf(TextNum, "\n"); TextNum++; SkipNum++;
            int maxDurability = CalcMaxDurability(ip, p, Level);
            if (ip.Durability < maxDurability)
            {
                long iGold = RepairGold(ip, Context.NpcRepairShop);
                if (iGold == -1) return result;
                swprintf(TextNum, GlobalText[238], ConvertGold(iGold));
                TextListColor[TextNum] = Color;
            }
            else
            {
                swprintf(TextNum, GlobalText[238], "0");
                TextListColor[TextNum] = Color;
            }
            TextBold[TextNum] = true;
            TextNum++;
        }
        swprintf(TextNum, "\n"); TextNum++; SkipNum++;

        if (ip.Type == ITEM_ORB_OF_SUMMONING)
            swprintf(TextNum, "%s %s", SkillName(30 + Level), GlobalText[102]);
        else if (ip.Type == ITEM_TRANSFORMATION_RING)
        {
            string monsterName = TransformationRingMonsterName(Level);
            if (monsterName != null) swprintf(TextNum, "%s %s", monsterName, GlobalText[103]);
        }
        else if (ip.Type == ITEM_DARK_HORSE_ITEM || ip.Type == ITEM_DARK_RAVEN_ITEM)
            swprintf(TextNum, "%s", p.Name);
        else if (wingLike)
        {
            if (Level == 0) swprintf(TextNum, "%s", p.Name);
            else swprintf(TextNum, "%s +%d", p.Name, Level);
        }
        else if (ip.ExcellentFlags > 0)
        {
            if (Level == 0) swprintf(TextNum, "%s %s", GlobalText[620], p.Name);
            else swprintf(TextNum, "%s %s +%d", GlobalText[620], p.Name, Level);
        }
        else
        {
            if (Level == 0) swprintf(TextNum, "%s", p.Name);
            else swprintf(TextNum, "%s +%d", p.Name, Level);
        }
        TextListColor[TextNum] = Color; TextBold[TextNum] = true; TextNum++;
        swprintf(TextNum, "\n"); TextNum++; SkipNum++;

        if (ip.Type < ITEM_POTION && !ip.bPeriodItem)
        {
            int maxDurability = CalcMaxDurability(ip, p, Level);
            swprintf(TextNum, GlobalText[71], ip.Durability, maxDurability);
            TextListColor[TextNum] = TEXT_COLOR_WHITE;
            TextBold[TextNum] = false;
            TextNum++;
        }
        swprintf(TextNum, "\n"); TextNum++; SkipNum++;

        for (int i = 0; i < Math.Min(TextNum, MAX_TEXT); i++)
        {
            string text = TextList[i];
            if (string.IsNullOrEmpty(text)) break;
            if (text[0] == '\n') text = string.Empty;
            result.Add(new SourceTooltipLine(text, TextListColor[i], TextBold[i]));
        }
        return result;
    }
}
}
