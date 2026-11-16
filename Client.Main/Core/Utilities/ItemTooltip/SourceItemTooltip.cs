// Port of SourceMain5.2 RenderItemInfo (ZzzInventory.cpp:2096-5640) and its helpers. The body below was translated
// line by line from the C++ source; the original line numbers are kept in the comments where useful.
#pragma warning disable CS0162, CS0219, CS0642, CS0168, IDE0059
using System;
using System.Collections.Generic;
using Client.Main.Controls.UI.Game.Inventory;
using static Client.Main.Core.Utilities.ItemTooltip.SourceConstants;

namespace Client.Main.Core.Utilities.ItemTooltip
{
public enum ItemTooltipType
{
    Inventory = 1,      // TOOLTIP_TYPE_INVENTORY
    NpcShop = 3,        // TOOLTIP_TYPE_NPC_SHOP (RenderItemInfo Sell = true)
    MyShop = 4,         // TOOLTIP_TYPE_MY_SHOP
    PurchaseShop = 5,   // TOOLTIP_TYPE_PURCHASE_SHOP
}

/// <summary>Tooltip context that the original reads from globals (open NPC shop, personal shop price, equipped item ...).</summary>
public sealed class ItemTooltipContext
{
    public static readonly ItemTooltipContext Default = new();

    public ItemTooltipType Type = ItemTooltipType.Inventory;
    /// <summary>g_pNewUISystem->IsVisible(INTERFACE_NPCSHOP).</summary>
    public bool NpcShopOpen;
    /// <summary>NPC tax rate in percent (g_pNPCShop->GetTaxRate()).</summary>
    public int NpcTaxRate;
    /// <summary>GetPersonalItemPrice result for MyShop / PurchaseShop tooltips.</summary>
    public long? PersonalShopPrice;
    /// <summary>true when the hovered item is worn (FindItemByKey == NULL in the original).</summary>
    public bool IsEquipped;
    /// <summary>Dark Horse / Dark Raven pet info received from the server (0xA9 PetInfo), or null.</summary>
    public SourcePetInfo PetInfo;
    /// <summary>NPC repair shop open (g_pNPCShop->IsRepairShop()); otherwise inventory self-repair.</summary>
    public bool NpcRepairShop;
    /// <summary>Overrides MuGame.Network's character (tests).</summary>
    public global::Client.Main.Core.Client.CharacterState Character;
}

public readonly record struct SourceTooltipLine(string Text, int TextColor, bool Bold);

public sealed partial class SourceItemTooltip
{
    private const int MAX_TEXT = 120;
    private static readonly GlobalText.TextTable GlobalText = global::Client.Main.Core.Utilities.GlobalText.Table;
    private readonly string[] TextList = new string[MAX_TEXT];
    private readonly int[] TextListColor = new int[MAX_TEXT];
    private readonly bool[] TextBold = new bool[MAX_TEXT];
    private int TextNum;
    private int SkipNum;

    private ItemTooltipContext Context = ItemTooltipContext.Default;
    private InventoryItem _item;
    private ItemTooltipHero Hero = new();
    private ItemTooltipHero CharacterAttribute => Hero;
    private readonly CharacterManagerShim gCharacterManager = new();
    private readonly ItemSetOptionRenderer g_csItemOption;
    private readonly JewelHarmonyInfo g_pUIJewelHarmonyinfo = JewelHarmonyInfo.Instance;
    private readonly ItemAddOptionInfo g_pItemAddOptioninfo = ItemAddOptionInfo.Instance;
    private int g_IsPurchaseShop = PSHOPWNDTYPE_NONE;
    private List<SourceItem> _equipment = new();

    private const int PSHOPWNDTYPE_NONE = -1, PSHOPWNDTYPE_SALE = 1, PSHOPWNDTYPE_PURCHASE = 2;
    private const int TOOLTIP_TYPE_INVENTORY = 1, TOOLTIP_TYPE_REPAIR = 2, TOOLTIP_TYPE_NPC_SHOP = 3, TOOLTIP_TYPE_MY_SHOP = 4, TOOLTIP_TYPE_PURCHASE_SHOP = 5;
    private const int NUM_HELLAS = 7;
    private const int SOCKET_EMPTY = SourceSocketItems.SOCKET_EMPTY;

    private sealed class CharacterManagerShim
    {
        public int GetBaseClass(int cls) => ItemTooltipHero.GetBaseClass(cls);
        public int GetStepClass(int cls) => ItemTooltipHero.GetStepClass(cls);
        public bool IsSecondClass(int cls) => ItemTooltipHero.IsSecondClass(cls);
        public bool IsMasterLevel(int cls) => ItemTooltipHero.IsThirdClass(cls);
    }

    public SourceItemTooltip()
    {
        g_csItemOption = new ItemSetOptionRenderer(this);
    }

    // ------------------------------------------------------------------ public entry

    /// <summary>Builds the SourceItem (ITEM) of an inventory item and runs SetItemAttributes.</summary>
    public static SourceItem CreateSourceItem(InventoryItem item)
    {
        var def = item?.Definition;
        if (def == null) return null;
        var ip = new SourceItem { Type = def.Group * MAX_ITEM_INDEX + def.Id };
        var d = item.Details;
        ip.Level = d.Level;
        ip.Durability = item.Durability;
        ip.HasLuck = d.HasLuck;
        ip.HasSkill = d.HasSkill;
        ip.OptionLevel = d.OptionLevel;
        var raw = item.RawData ?? Array.Empty<byte>();
        if (ItemDataParser.TryParseExtendedItemData(raw, out var ext) && ext.Length <= raw.Length)
        {
            ip.OptionType = ext.OptionType;
            ip.ExcellentFlags = ext.ExcellentFlags;
            ip.AncientDiscriminator = ext.HasAncient ? ext.AncientDiscriminator : 0;
            ip.AncientBonusOption = ext.HasAncient ? ext.AncientBonusOption : 0;
            if (ext.HasHarmony)
            {
                ip.Jewel_Of_Harmony_Option = (ext.HarmonyOption >> 4) & 0x0F;
                ip.Jewel_Of_Harmony_OptionLevel = ext.HarmonyOption & 0x0F;
            }
            ip.option_380 = ext.HasGuardian;
            ip.SocketCount = ext.SocketCount;
            ip.SocketSeedSetOption = ext.SocketBonusOption;
            // Socket bytes follow the socket info byte (NewUIItemMng.cpp:58, CreateItemByParameters:171)
            int socketStart = ext.Length - ext.SocketCount;
            for (int i = 0; i < ext.SocketCount && i < ip.SocketSeedID.Length; i++)
            {
                int option = raw[socketStart + i];
                if (option == SourceSocketItems.SOCKET_EMPTY)
                {
                    ip.SocketSeedID[i] = SourceSocketItems.SOCKET_EMPTY;
                }
                else
                {
                    ip.SocketSeedID[i] = option % SourceSocketItems.MAX_SOCKET_OPTION;
                    ip.SocketSphereLv[i] = option / SourceSocketItems.MAX_SOCKET_OPTION + 1;
                }
            }
        }
        else
        {
            ip.ExcellentFlags = d.ExcellentFlags & 0x3F;
            byte anc = raw.Length > 4 ? raw[4] : (byte)0;
            ip.AncientDiscriminator = anc & 0x03;
            ip.AncientBonusOption = (anc >> 2) & 0x03;
        }
        SourceItemAttributes.SetItemAttributes(ip, SourceItemAttribute.FromDefinition(def));
        return ip;
    }

    /// <summary>RenderItemInfo: returns the text rows (blank rows are "\n" in TextList, returned as empty text).</summary>
    public List<SourceTooltipLine> Build(InventoryItem item, ItemTooltipContext context)
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
        _equipment = BuildEquipment(state);

        bool sell = Context.Type == ItemTooltipType.NpcShop;
        int inventype = (int)Context.Type;
        g_IsPurchaseShop = Context.Type == ItemTooltipType.MyShop ? PSHOPWNDTYPE_SALE
            : Context.Type == ItemTooltipType.PurchaseShop ? PSHOPWNDTYPE_PURCHASE : PSHOPWNDTYPE_NONE;

        Array.Clear(TextListColor);
        Array.Clear(TextBold);
        for (int i = 0; i < MAX_TEXT; i++) TextList[i] = null;
        TextNum = 0;
        SkipNum = 0;

        RenderItemInfo(ip, sell, inventype, false);

        int count = Math.Min(TextNum, MAX_TEXT);
        for (int i = 0; i < count; i++)
        {
            string text = TextList[i];
            if (string.IsNullOrEmpty(text)) break; // RenderTipTextList stops at the first empty row
            if (text[0] == '\n') text = string.Empty;
            result.Add(new SourceTooltipLine(text, TextListColor[i], TextBold[i]));
        }
        return result;
    }

    private static List<SourceItem> BuildEquipment(global::Client.Main.Core.Client.CharacterState state)
    {
        var list = new List<SourceItem>();
        if (state == null) return list;
        IReadOnlyDictionary<byte, byte[]> items;
        try { items = state.GetInventoryItems(); } catch { return list; }
        for (byte slot = 0; slot < 12; slot++)
        {
            SourceItem ip = null;
            if (items != null && items.TryGetValue(slot, out var data) && data != null)
            {
                var def = ItemDatabase.GetItemDefinition(data);
                if (def != null)
                    ip = CreateSourceItem(new InventoryItem(def, Microsoft.Xna.Framework.Point.Zero, data, ItemDatabase.GetItemDurability(data)));
            }
            list.Add(ip ?? new SourceItem { Type = -1 });
        }
        return list;
    }

    // ------------------------------------------------------------------ helpers for the translated code

    private void swprintf(int index, string format, params object[] args)
    {
        if (index < 0 || index >= MAX_TEXT) return;
        TextList[index] = global::Client.Main.Core.Utilities.GlobalText.Format(format, args);
    }

    private static string Fmt(string format, params object[] args) => global::Client.Main.Core.Utilities.GlobalText.Format(format, args);

    private bool IsNpcShopVisible => Context.NpcShopOpen || Context.Type == ItemTooltipType.NpcShop;

    /// <summary>ItemValue(ip, goldType): 0 = purchase price, 1 = selling price (ItemPriceCalculator, ZzzInfomation.cpp:1592).</summary>
    private static long ItemValue(SourceItem ip, int goldType)
        => SourceItemValue.ItemValue(ip, goldType, ip.PetInfo?.m_wLevel ?? -1);

    /// <summary>ConvertGold (ZzzInventory.cpp:1116).</summary>
    private static string ConvertGold(double dGold)
    {
        int iCipherCnt = 0;
        uint dwValueTemp = (uint)dGold;
        while (dwValueTemp / 1000 > 0)
        {
            iCipherCnt += 3;
            dwValueTemp /= 1000;
        }
        string szText = dwValueTemp.ToString();
        while (iCipherCnt > 0)
        {
            dwValueTemp = (uint)dGold;
            dwValueTemp = (uint)((dwValueTemp % (long)Math.Pow(10, iCipherCnt)) / (long)Math.Pow(10, iCipherCnt - 3));
            szText += "," + dwValueTemp.ToString("000");
            iCipherCnt -= 3;
        }
        return szText;
    }

    /// <summary>ConvertTaxGold (ZzzInventory.cpp:1169).</summary>
    private string ConvertTaxGold(long goldValue)
    {
        uint Gold = (uint)goldValue;
        Gold += (uint)((long)Gold * Context.NpcTaxRate / 100);
        uint Gold1 = Gold % 1000, Gold2 = Gold % 1000000 / 1000, Gold3 = Gold % 1000000000 / 1000000, Gold4 = Gold / 1000000000;
        if (Gold >= 1000000000) return Fmt("%d,%03d,%03d,%03d", Gold4, Gold3, Gold2, Gold1);
        if (Gold >= 1000000) return Fmt("%d,%03d,%03d", Gold3, Gold2, Gold1);
        if (Gold >= 1000) return Fmt("%d,%03d", Gold2, Gold1);
        return Fmt("%d", Gold1);
    }

    private bool GetPersonalItemPrice(int index, ref int price, int shopType)
    {
        if (Context.PersonalShopPrice is long value && value > 0)
        {
            price = (int)Math.Min(value, int.MaxValue);
            return true;
        }
        return false;
    }

    private static bool IsSocketItem(SourceItem ip) => SourceSocketItems.IsSocketItem(ip.Type);
    private int GetSocketOptionValue(SourceItem ip, int index) => SourceSocketItems.GetSocketOptionValue(ip, index);

    // CPortalMgr is not ported (no saved revive/portal position in this client).
    private bool IsRevivePositionSaved() => false;
    private string GetRevivePositionText() => " ";
    private string GetPortalPositionText() => " ";

    // ZzzInventory.cpp:1540 (the Korean Chaos event prizes)
    private static readonly string[] s_chaosEventName =
    {
        "히돼?고향 여행권", "펜티엄4 컴퓨터", "디지탈카메라", "로지텍 무선 마우스+키보드 세트", "256M 램",
        "6개?잡지 구독권", "문화상품권(만원)", "뮤 머그컵", "뮤 T셔츠", "뮤 10시간 무료이용권",
    };
    private static string ChaosEventNameAt(int index) => index >= 0 && index < s_chaosEventName.Length ? s_chaosEventName[index] : string.Empty;

    // SommonTable { 2,7,14,8,9,41 } (ZzzInventory.cpp:1538) resolved to MonsterScript names (Data/Local/Eng/NpcName_Eng.txt).
    private static readonly string[] s_summonMonsterNames = { "Budge Dragon", "Giant", "Skeleton", "Poison Bull Fighter", "Thunder Lich", "Death Cow" };
    private static string TransformationRingMonsterName(int level) => level >= 0 && level < s_summonMonsterNames.Length ? s_summonMonsterNames[level] : null;

    private static string SkillName(int skill) => SkillDatabase.GetSkillName(skill) ?? string.Empty;
    private static int SkillManaCost(int skill) => SkillDatabase.GetSkillManaCost(skill);
    private static int SkillDamage(int skill) => SkillDatabase.GetSkillDefinition(skill)?.Damage ?? 0;

    // ZzzInfomation.cpp:585
    private static int getSkillIndexByBook(int Type)
    {
        int SkillIndex = Type - ITEM_SCROLL_OF_METEORITE;
        switch (SkillIndex)
        {
            case 17: SkillIndex = AT_SKILL_BLAST_POISON; break;
            case 18: SkillIndex = AT_SKILL_BLAST_FREEZE; break;
            case 19: SkillIndex = AT_SKILL_BLAST_HELL; break;
        }
        return (byte)SkillIndex;
    }

    private static bool IsCepterItem(int iType) => SourceItemAttributes.IsCepterItem(iType);
    private static bool Check_LuckyItem(int index) => SourceItemAttributes.Check_LuckyItem(index);
    private static int CalcMaxDurability(SourceItem ip, SourceItemAttribute p, int Level) => SourceItemAttributes.CalcMaxDurability(ip, p, Level);

    private const int eITEM_PERSONALSHOP = 0, eITEM_STORE = 1, eITEM_TRADE = 2, eITEM_DROP = 3, eITEM_SELL = 4, eITEM_REPAIR = 5;

    // Check_ItemAction (ZzzInventory.cpp:7680-7725) with _bType = false.
    private static bool Check_ItemAction(SourceItem item, int action)
    {
        int[][] nItemOption = { new[] { 0, 1, 1, 0, 0, 0 }, new[] { 0, 0, 0, 0, 1, 0 } };
        int option = -1;
        if (item.Type >= ITEM_HELPER + 135 && item.Type < ITEM_HELPER + 135 + 12) option = 0;
        else if (item.Type >= ITEM_POTION + 160 && item.Type < ITEM_POTION + 162) option = 0;
        else
        {
            int index = item.Type % MAX_ITEM_INDEX;
            int group = item.Type / MAX_ITEM_INDEX;
            if (index >= 62 && index < 74 && (group == ITEM_GROUP_ARMOR || group == ITEM_GROUP_HELM || group == ITEM_GROUP_BOOTS || group == ITEM_GROUP_GLOVES || group == ITEM_GROUP_PANTS))
                option = 1;
        }
        if (option < 0) return false;
        return (nItemOption[option][action] ^ 1) != 0;
    }

    // BuffTimeControl::GetStringTime (w_BuffTimeControl.cpp:170), isSecond = true
    private static string g_StringTime(uint time, bool isSecond)
    {
        uint day = time / (1440 * 60);
        uint oClock = (time - (day * (1440 * 60))) / 3600;
        uint minutes = (time - ((oClock * 3600) + (day * (1440 * 60)))) / 60;
        uint second = time % 60;
        if (day != 0) return Fmt("%d %s %d %s %d %s %d %s", day, GlobalText[2298], oClock, GlobalText[2299], minutes, GlobalText[2300], second, GlobalText[2301]);
        if (oClock != 0) return Fmt("%d %s %d %s %d %s", oClock, GlobalText[2299], minutes, GlobalText[2300], second, GlobalText[2301]);
        if (minutes != 0) return Fmt("%d %s %d %s", minutes, GlobalText[2300], second, GlobalText[2301]);
        return Fmt("%s", GlobalText[2308]);
    }

    private static readonly int[][] g_iChaosCastleLevel =
    {
        new[] { 15, 49 }, new[] { 50, 119 }, new[] { 120, 179 }, new[] { 180, 239 }, new[] { 240, 299 }, new[] { 300, 999 },
        new[] { 15, 29 }, new[] { 30, 99 }, new[] { 100, 159 }, new[] { 160, 219 }, new[] { 220, 279 }, new[] { 280, 999 },
    };
    private static readonly int[] g_iChaosCastleZen = { 25, 80, 150, 250, 400, 650 };
    private static readonly int[][] g_iKalimaLevel =
    {
        new[] { 40, 999 }, new[] { 131, 999 }, new[] { 181, 999 }, new[] { 231, 999 }, new[] { 281, 999 }, new[] { 331, 999 }, new[] { 350, 999 },
        new[] { 20, 999 }, new[] { 111, 999 }, new[] { 161, 999 }, new[] { 211, 999 }, new[] { 261, 999 }, new[] { 311, 999 }, new[] { 350, 999 },
    };

    // ------------------------------------------------------------------ RenderItemInfo

    private void RenderItemInfo(SourceItem ip, bool Sell, int Inventype, bool bItemTextListBoxUse)
    {
        if (ip.Type == -1)
            return;

        SourceItemAttribute p = ip.Attribute;
        int iMana = 0;

        if (!Sell && (ip.Type == ITEM_DARK_HORSE_ITEM || ip.Type == ITEM_DARK_RAVEN_ITEM))
        {
            // ZzzInventory.cpp:2121 - the pet info is requested from the server and giPetManager::RenderPetItemInfo renders it.
            if (!RenderPetItemInfo(ip, Inventype))
            {
                // PET_TYPE_NONE: the original renders nothing until the 0xA9 answer arrives; keep the name visible meanwhile.
                swprintf(TextNum, "%s", p.Name); TextListColor[TextNum] = TEXT_COLOR_WHITE; TextBold[TextNum] = true; TextNum++;
            }
            return;
        }
        // Dark Horse / Dark Raven: giPetManager::RenderPetItemInfo is not ported; they use the generic path below.
    swprintf(TextNum, "\n"); TextNum++; SkipNum++;

    int Level = ip.Level;
    int Color;

    if (ip.Type == ITEM_JEWEL_OF_BLESS || ip.Type == ITEM_JEWEL_OF_SOUL || ip.Type == ITEM_JEWEL_OF_CHAOS || ip.Type == ITEM_JEWEL_OF_GUARDIAN ||
        (COMGEM.isCompiledGem(ip)) ||
        ip.Type == ITEM_FLAME_OF_DEATH_BEAM_KNIGHT || ip.Type == ITEM_HORN_OF_HELL_MAINE || ip.Type == ITEM_FEATHER_OF_DARK_PHOENIX ||
        ip.Type == ITEM_EYE_OF_ABYSSAL ||
        ip.Type == ITEM_FLAME_OF_CONDOR || ip.Type == ITEM_FEATHER_OF_CONDOR ||
        ip.Type == ITEM_POTION + 100 ||
        (ip.Type >= ITEM_POTION + 141 && ip.Type <= ITEM_POTION + 144) ||
        (ip.Type >= ITEM_HELPER + 135 && ip.Type <= ITEM_HELPER + 145) ||
        (ip.Type == ITEM_POTION + 160 || ip.Type == ITEM_POTION + 161) ||
        ip.Type == ITEM_JEWEL_OF_LIFE || ip.Type == ITEM_JEWEL_OF_CREATION)
    {
        Color = TEXT_COLOR_YELLOW;
    }
    else if (ip.Type == ITEM_DIVINE_STAFF_OF_ARCHANGEL || ip.Type == ITEM_DIVINE_SWORD_OF_ARCHANGEL || ip.Type == ITEM_DIVINE_CB_OF_ARCHANGEL || ip.Type == ITEM_DIVINE_SCEPTER_OF_ARCHANGEL)
    {
        Color = TEXT_COLOR_PURPLE;
    }
    else if (ip.Type == ITEM_DEVILS_EYE || ip.Type == ITEM_DEVILS_KEY || ip.Type == ITEM_DEVILS_INVITATION)
    {
        Color = TEXT_COLOR_YELLOW;
    }
    else if (ip.Type == ITEM_SCROLL_OF_ARCHANGEL || ip.Type == ITEM_BLOOD_BONE)
    {
        Color = TEXT_COLOR_YELLOW;
    }
    else if (ip.AncientDiscriminator > 0)
    {
        Color = TEXT_COLOR_GREEN_BLUE;
    }
    else if (IsSocketItem(ip))
    {
        Color = TEXT_COLOR_VIOLET;
    }
    else if (ip.SpecialNum > 0 && ip.ExcellentFlags > 0)
    {
        Color = TEXT_COLOR_GREEN;
    }
    else if (Level >= 7)
    {
        Color = TEXT_COLOR_YELLOW;
    }
    else
    {
        if (ip.SpecialNum > 0)
        {
            Color = TEXT_COLOR_BLUE;
        }
        else
        {
            Color = TEXT_COLOR_WHITE;
        }
    }

    if ((ip.Type >= ITEM_WINGS_OF_SPIRITS && ip.Type <= ITEM_WINGS_OF_DARKNESS) || ip.Type == ITEM_CAPE_OF_LORD
        || (ip.Type >= ITEM_WING_OF_STORM && ip.Type <= ITEM_CAPE_OF_EMPEROR)
        || (ip.Type >= ITEM_WINGS_OF_DESPAIR && ip.Type <= ITEM_WING_OF_DIMENSION)
        || (ip.Type >= ITEM_CAPE_OF_FIGHTER && ip.Type <= ITEM_CAPE_OF_OVERRULE))
    {
        if (Level >= 7)
        {
            Color = TEXT_COLOR_YELLOW;
        }
        else
        {
            if (ip.SpecialNum > 0)
            {
                Color = TEXT_COLOR_BLUE;
            }
            else
            {
                Color = TEXT_COLOR_WHITE;
            }
        }
    }

    int nGemType = COMGEM.Check_Jewel(ip.Type);
    if (nGemType != COMGEM.NOGEM)
    {
        Color = TEXT_COLOR_YELLOW;
    }

    if (IsNpcShopVisible && !IsSellingBan(ip))
    {
        string Text = "";
        {
            if (Sell)
            {
                long dwValue = ItemValue(ip, 0);
                Text = ConvertGold(dwValue);
                string Text2 = "";

                Text2 = ConvertTaxGold(ItemValue(ip, 0));
                swprintf(TextNum, GlobalText[1620], Text2, Text);
            }
            else
            {
                Text = ConvertGold(ItemValue(ip, 1));
                swprintf(TextNum, GlobalText[63], Text);
            }

            TextListColor[TextNum] = Color;
            //			TextBold[TextNum] = true;
            TextNum++;
            swprintf(TextNum, "\n"); TextNum++; SkipNum++;
        }
    }
    if ((Inventype == TOOLTIP_TYPE_MY_SHOP || Inventype == TOOLTIP_TYPE_PURCHASE_SHOP)
        && !IsPersonalShopBan(ip))
    {
        {
            int price = 0;
            int indexInv = 0;
            string Text = "";

            if (GetPersonalItemPrice(indexInv, ref price, g_IsPurchaseShop))
            {
                Text = ConvertGold(price);
                swprintf(TextNum, GlobalText[63], Text);

                if (price >= 10000000)
                    TextListColor[TextNum] = TEXT_COLOR_RED;
                else if (price >= 1000000)
                    TextListColor[TextNum] = TEXT_COLOR_YELLOW;
                else if (price >= 100000)
                    TextListColor[TextNum] = TEXT_COLOR_GREEN;
                else
                    TextListColor[TextNum] = TEXT_COLOR_WHITE;
                TextBold[TextNum] = true;
                TextNum++;
                swprintf(TextNum, "\n"); TextNum++; SkipNum++;

                long gold = Hero.Gold;

                if (gold < price && g_IsPurchaseShop == PSHOPWNDTYPE_PURCHASE)
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
    }

    if (ip.Type >= ITEM_SCROLL_OF_EMPEROR_RING_OF_HONOR && ip.Type <= ITEM_SOUL_SHARD_OF_WIZARD)
    {
        if (ip.Type == ITEM_SCROLL_OF_EMPEROR_RING_OF_HONOR)
        {
            Color = TEXT_COLOR_YELLOW;
            switch (Level)
            {
            case 0: swprintf(TextNum, "%s", p.Name); break;
            case 1: swprintf(TextNum, GlobalText[906]); break;
            }
        }
        else if (ip.Type == ITEM_BROKEN_SWORD_DARK_STONE)
        {
            Color = TEXT_COLOR_YELLOW;
            switch (Level)
            {
            case 0: swprintf(TextNum, "%s", p.Name); break;
            case 1: swprintf(TextNum, GlobalText[907]); break;
            }
        }
        else {
            swprintf(TextNum, "%s", p.Name);
            Color = TEXT_COLOR_YELLOW;
        }
    }
    else if (ip.Type == ITEM_POTION + 12)
    {
        switch (Level)
        {
        case 0:swprintf(TextNum, GlobalText[100]); break;
        case 1:swprintf(TextNum, GlobalText[101]); break;
        case 2:swprintf(TextNum, "%s", ChaosEventNameAt(ip.Durability)); break;
        }
    }
    else if (ip.Type == ITEM_BOX_OF_LUCK)
    {
        switch (Level)
        {
        case 0:swprintf(TextNum, "%s", p.Name); break;
        case 1:swprintf(TextNum, GlobalText[105]); break;
        case 2:swprintf(TextNum, GlobalText[106]); break;
        case 3:swprintf(TextNum, GlobalText[107]); break;
        case 5:swprintf(TextNum, GlobalText[109]); break;
        case 6:swprintf(TextNum, GlobalText[110]); break;
        case 7:swprintf(TextNum, GlobalText[111]); break;
        case 8:
        case 9:
        case 10:
        case 11:
        case 12:
            swprintf(TextNum, "%s +%d", GlobalText[115], Level - 7);
            break;
        case 13:
            swprintf(TextNum, GlobalText[117]);
            break;
        case 14:
            swprintf(TextNum, GlobalText[1650]);
            break;
        case 15:
            swprintf(TextNum, GlobalText[1651]);
            break;
        }
    }
    else if (ip.Type == ITEM_POTION + 12)
    {
        switch (Level)
        {
        case 0:swprintf(TextNum, GlobalText[100]); break;
        case 1:swprintf(TextNum, GlobalText[101]); break;
        case 2:swprintf(TextNum, "%s", ChaosEventNameAt(ip.Durability)); break;
        }
    }
    else if (ip.Type == ITEM_FRUITS)
    {
        Color = TEXT_COLOR_YELLOW;
        switch (Level)
        {
        case 0:swprintf(TextNum, "%s %s", GlobalText[168], p.Name); break;
        case 1:swprintf(TextNum, "%s %s", GlobalText[169], p.Name); break;
        case 2:swprintf(TextNum, "%s %s", GlobalText[167], p.Name); break;
        case 3:swprintf(TextNum, "%s %s", GlobalText[166], p.Name); break;
        case 4:swprintf(TextNum, "%s %s", GlobalText[1900], p.Name); break;
        }
    }
    else if (ip.Type == ITEM_LOCHS_FEATHER)
    {
        Color = TEXT_COLOR_YELLOW;
        switch (Level)
        {
        case 0: swprintf(TextNum, "%s", p.Name); break;
        case 1: swprintf(TextNum, "%s", GlobalText[1235]); break;
        }
    }
    else if (ip.Type == ITEM_POTION + 21)
    {
        Color = TEXT_COLOR_YELLOW;
        switch (Level)
        {
        case 0: swprintf(TextNum, "%s", p.Name); break;
        case 1: swprintf(TextNum, GlobalText[810]); break;
        case 2: swprintf(TextNum, GlobalText[1098]); break;
        case 3: swprintf(TextNum, GlobalText[1290]); break;
        }
    }
    else if (ip.Type == ITEM_WEAPON_OF_ARCHANGEL)
    {
        Color = TEXT_COLOR_YELLOW;
        switch (Level)
        {
        case 0: swprintf(TextNum, GlobalText[811]); break;
        case 1: swprintf(TextNum, GlobalText[812]); break;
        case 2: swprintf(TextNum, GlobalText[817]); break;
        }
    }
    else if (ip.Type == ITEM_WIZARDS_RING)
    {
        Color = TEXT_COLOR_YELLOW;
        switch (Level)
        {
        case 0: swprintf(TextNum, p.Name); break;
        case 1: swprintf(TextNum, GlobalText[922]); break;
        case 2: swprintf(TextNum, GlobalText[928]); break;
        case 3: swprintf(TextNum, GlobalText[929]); break;
        }
    }
    else if (ip.Type == ITEM_HELPER + 107)
    {
        Color = TEXT_COLOR_YELLOW;
        swprintf(TextNum, p.Name);
    }
    else if (ip.Type == ITEM_SIEGE_POTION)
    {
        switch (Level)
        {
        case 0: swprintf(TextNum, GlobalText[1413]); break;
        case 1: swprintf(TextNum, GlobalText[1414]); break;
        }
    }
    else if (ip.Type == ITEM_HELPER + 7)
    {
        switch (Level)
        {
        case 0: swprintf(TextNum, GlobalText[1460]); break;
        case 1: swprintf(TextNum, GlobalText[1461]); break;
        }
    }
    else if (ip.Type == ITEM_LIFE_STONE_ITEM)
    {
        switch (Level)
        {
        case 0: swprintf(TextNum, GlobalText[1416]); break;
        case 1: swprintf(TextNum, GlobalText[1462]); break;
        }
    }
    else if (ip.Type == ITEM_ALE)
    {
        switch (Level)
        {
        case 0:swprintf(TextNum, "%s", p.Name); break;
        case 1:swprintf(TextNum, GlobalText[108]); break;
        }
    }
    else if (ip.Type == ITEM_ORB_OF_SUMMONING)
    {
        swprintf(TextNum, "%s %s", SkillName(30 + Level), GlobalText[102]);
    }
    else if (ip.Type == ITEM_TRANSFORMATION_RING)
    {
        { string monsterName = TransformationRingMonsterName(Level); if (monsterName != null) swprintf(TextNum, "%s %s", monsterName, GlobalText[103]); }







    }
    else if (ip.Type >= ITEM_WINGS_OF_SPIRITS && ip.Type <= ITEM_WINGS_OF_DARKNESS)
    {
        if (Level == 0)
            swprintf(TextNum, "%s", p.Name);
        else
            swprintf(TextNum, "%s +%d", p.Name, Level);
    }
    else if ((ip.Type >= ITEM_WING_OF_STORM && ip.Type <= ITEM_CAPE_OF_EMPEROR) || (ip.Type >= ITEM_WINGS_OF_DESPAIR && ip.Type <= ITEM_WING_OF_DIMENSION)
        || (ip.Type >= ITEM_CAPE_OF_FIGHTER && ip.Type <= ITEM_CAPE_OF_OVERRULE))
    {
        if (Level == 0)
            swprintf(TextNum, "%s", p.Name);
        else
            swprintf(TextNum, "%s +%d", p.Name, Level);
    }
    else if (ip.Type == ITEM_SPIRIT)
    {
        switch (Level)
        {
        case 0: swprintf(TextNum, "%s of %s", p.Name, GlobalText[1187]); break;
        case 1: swprintf(TextNum, "%s of %s", p.Name, GlobalText[1214]); break;
        }
    }
    else if (ip.Type == ITEM_CAPE_OF_LORD)
    {
        if (Level == 0)
            swprintf(TextNum, "%s", p.Name);
        else
            swprintf(TextNum, "%s +%d", p.Name, Level);
    }
    else if (ip.Type == ITEM_SYMBOL_OF_KUNDUN)
    {
        swprintf(TextNum, GlobalText[1180], Level);
    }
    else if (ip.Type == ITEM_LOST_MAP)
    {
        Color = TEXT_COLOR_YELLOW;
        swprintf(TextNum, "%s +%d", p.Name, Level);
    }
    else if (ip.Type == ITEM_RED_RIBBON_BOX)
    {
        swprintf(TextNum, "%s", p.Name);
    }
    else if (ip.Type == ITEM_GREEN_RIBBON_BOX)
    {
        swprintf(TextNum, "%s", p.Name);
    }
    else if (ip.Type == ITEM_BLUE_RIBBON_BOX)
    {
        swprintf(TextNum, "%s", p.Name);
    }
    else if (ip.Type == ITEM_SCROLL_OF_FIRE_SCREAM)
    {
        swprintf(TextNum, "%s", p.Name);
    }
    else if (ip.Type >= ITEM_PUMPKIN_OF_LUCK && ip.Type <= ITEM_JACK_OLANTERN_DRINK)
    {
        swprintf(TextNum, "%s", p.Name);
    }
    else if (ip.Type == ITEM_PINK_CHOCOLATE_BOX)
    {
        switch (Level)
        {
        case 0:
            swprintf(TextNum, "%s", p.Name); break;
        case 1:
            swprintf(TextNum, "%s", GlobalText[2012]); break;
        }
    }
    else if (ip.Type == ITEM_RED_CHOCOLATE_BOX)
    {
        switch (Level)
        {
        case 0:
            swprintf(TextNum, "%s", p.Name); break;
        case 1:
            swprintf(TextNum, "%s", GlobalText[2013]); break;
        }
    }
    else if (ip.Type == ITEM_BLUE_CHOCOLATE_BOX)
    {
        switch (Level)
        {
        case 0:
            swprintf(TextNum, "%s", p.Name); break;
        case 1:
            swprintf(TextNum, "%s", GlobalText[2014]); break;
        }
    }
    else if (ip.Type >= ITEM_SPLINTER_OF_ARMOR && ip.Type <= ITEM_HORN_OF_FENRIR)
    {
        if (ip.Type == ITEM_HORN_OF_FENRIR)
        {
            Color = TEXT_COLOR_BLUE;
            if ((ip.ExcellentFlags & 63) == 0x01)
                swprintf(TextNum, "%s %s", p.Name, GlobalText[1863]);
            else if ((ip.ExcellentFlags & 63) == 0x02)
                swprintf(TextNum, "%s %s", p.Name, GlobalText[1864]);
            else if ((ip.ExcellentFlags & 63) == 0x04)
                swprintf(TextNum, "%s %s", p.Name, GlobalText[1866]);
            else
                swprintf(TextNum, "%s", p.Name);
        }
        else
        {
            Color = TEXT_COLOR_WHITE;
            swprintf(TextNum, "%s", p.Name);
        }
    }
    else if (ip.Type == ITEM_DIVINE_SWORD_OF_ARCHANGEL || ip.Type == ITEM_DIVINE_CB_OF_ARCHANGEL || ip.Type == ITEM_DIVINE_STAFF_OF_ARCHANGEL || ip.Type == ITEM_DIVINE_SCEPTER_OF_ARCHANGEL)
    {
        if (Level == 0)
            swprintf(TextNum, "%s", p.Name);
        else
            swprintf(TextNum, "%s +%d", p.Name, Level);
    }
    else if (nGemType != COMGEM.NOGEM && nGemType % 2 == 1)
    {
        int nGlobalIndex = COMGEM.GetJewelIndex(nGemType, COMGEM.eGEM_NAME);
        swprintf(TextNum, "%s +%d", GlobalText[nGlobalIndex], Level + 1);
    }
    else if (ip.Type == ITEM_GEMSTONE || ip.Type == ITEM_JEWEL_OF_HARMONY ||
        ip.Type == ITEM_LOWER_REFINE_STONE || ip.Type == ITEM_HIGHER_REFINE_STONE ||
        ip.Type == ITEM_MOONSTONE_PENDANT
        )
    {
        swprintf(TextNum, "%s", p.Name);
        Color = TEXT_COLOR_YELLOW;
    }
    else if ((ip.Type >= ITEM_SEED_FIRE && ip.Type <= ITEM_SEED_EARTH)
        || (ip.Type >= ITEM_SPHERE_MONO && ip.Type <= ITEM_SPHERE_5)
        || (ip.Type >= ITEM_SEED_SPHERE_FIRE_1 && ip.Type <= ITEM_SEED_SPHERE_EARTH_5))
    {
        swprintf(TextNum, "%s", p.Name);
        Color = TEXT_COLOR_VIOLET;
    }
    else if (ip.Type == ITEM_POTION + 111)
    {
        Color = TEXT_COLOR_YELLOW;
        swprintf(TextNum, "%s", p.Name);
    }
    else if (ITEM_SUSPICIOUS_SCRAP_OF_PAPER <= ip.Type && ip.Type <= ITEM_COMPLETE_SECROMICON)
    {
        Color = TEXT_COLOR_YELLOW;
        swprintf(TextNum, "%s", p.Name);
    }
    else
    {
        string TextName = "";
        if (g_csItemOption.GetSetItemName(ref TextName, ip.Type, ip.AncientDiscriminator))
        {
            TextName += p.Name;
        }
        else
        {
            TextName = p.Name;
        }

        if (ip.ExcellentFlags > 0)
        {
            if (Level == 0)
                swprintf(TextNum, "%s %s", GlobalText[620], TextName);
            else
                swprintf(TextNum, "%s %s +%d", GlobalText[620], TextName, Level);
        }
        else
        {
            if (Level == 0)
                swprintf(TextNum, "%s", TextName);
            else
                swprintf(TextNum, "%s +%d", TextName, Level);
        }
    }

    TextListColor[TextNum] = Color; TextBold[TextNum] = true; TextNum++;
    swprintf(TextNum, "\n"); TextNum++; SkipNum++;

    if (ip.Type == ITEM_WEAPON_OF_ARCHANGEL)
    {

        int iWeaponSpeed = 0;
        int iNeedStrength = 0;
        int iNeedDex = 0;

        TextListColor[TextNum] = TEXT_COLOR_WHITE;
        swprintf(TextNum, GlobalText[730]); TextBold[TextNum] = false; TextNum++;

        TextListColor[TextNum] = TEXT_COLOR_DARKRED;
        swprintf(TextNum, GlobalText[815]); TextBold[TextNum] = false; TextNum++;
        swprintf(TextNum, "\n"); TextBold[TextNum] = false; TextNum++; SkipNum++;

        switch (Level)
        {
        case 0:
            swprintf(TextNum, "%s: %d ~ %d", GlobalText[42], 107, 110);  TextListColor[TextNum] = TEXT_COLOR_WHITE; TextBold[TextNum] = false; TextNum++;
            iWeaponSpeed = 20;
            iNeedStrength = 132;
            iNeedDex = 32;
            break;
        case 1:
            swprintf(TextNum, "%s: %d ~ %d", GlobalText[40], 110, 120);  TextListColor[TextNum] = TEXT_COLOR_WHITE; TextBold[TextNum] = false; TextNum++;
            iWeaponSpeed = 35;
            iNeedStrength = 381;
            iNeedDex = 149;
            break;
        case 2:
            swprintf(TextNum, "%s: %d ~ %d", GlobalText[41], 120, 140);  TextListColor[TextNum] = TEXT_COLOR_WHITE; TextBold[TextNum] = false; TextNum++;
            iWeaponSpeed = 35;
            iNeedStrength = 140;
            iNeedDex = 350;
            break;
        }

        swprintf(TextNum, GlobalText[64], iWeaponSpeed); TextListColor[TextNum] = TEXT_COLOR_WHITE; TextBold[TextNum] = false; TextNum++;
        swprintf(TextNum, GlobalText[73], iNeedStrength); TextListColor[TextNum] = TEXT_COLOR_WHITE; TextBold[TextNum] = false; TextNum++;
        swprintf(TextNum, GlobalText[75], iNeedDex);     TextListColor[TextNum] = TEXT_COLOR_WHITE; TextBold[TextNum] = false; TextNum++;
        swprintf(TextNum, "\n"); TextBold[TextNum] = false; TextNum++; SkipNum++;
        swprintf(TextNum, GlobalText[87]);     TextListColor[TextNum] = TEXT_COLOR_BLUE; TextBold[TextNum] = false; TextNum++;
        swprintf(TextNum, GlobalText[94], 20); TextListColor[TextNum] = TEXT_COLOR_BLUE; TextBold[TextNum] = false; TextNum++;

        switch (Level)
        {
        case 0:
        {
            swprintf(TextNum, GlobalText[79], 53); TextListColor[TextNum] = TEXT_COLOR_BLUE; TextBold[TextNum] = true; TextNum++;
            swprintf(TextNum, GlobalText[631]); TextListColor[TextNum] = TEXT_COLOR_BLUE; TextBold[TextNum] = false; TextNum++;
            swprintf(TextNum, GlobalText[632], 2); TextListColor[TextNum] = TEXT_COLOR_BLUE; TextBold[TextNum] = false; TextNum++;
        }
        break;
        case 1:
        {
            iMana = SkillManaCost(AT_SKILL_SWORD4);
            swprintf(TextNum, GlobalText[84], iMana); TextListColor[TextNum] = TEXT_COLOR_BLUE; TextBold[TextNum] = false; TextNum++;
            swprintf(TextNum, GlobalText[629]); TextListColor[TextNum] = TEXT_COLOR_BLUE; TextBold[TextNum] = false; TextNum++;
            swprintf(TextNum, GlobalText[630], 2); TextListColor[TextNum] = TEXT_COLOR_BLUE; TextBold[TextNum] = false; TextNum++;
        }
        break;
        case 2:
        {
            iMana = SkillManaCost(AT_SKILL_CROSSBOW);
            swprintf(TextNum, GlobalText[86], iMana); TextListColor[TextNum] = TEXT_COLOR_BLUE; TextBold[TextNum] = false; TextNum++;
            swprintf(TextNum, GlobalText[629]); TextListColor[TextNum] = TEXT_COLOR_BLUE; TextBold[TextNum] = false; TextNum++;
            swprintf(TextNum, GlobalText[630], 2); TextListColor[TextNum] = TEXT_COLOR_BLUE; TextBold[TextNum] = false; TextNum++;
        }
        break;
        }
        swprintf(TextNum, GlobalText[628]); TextListColor[TextNum] = TEXT_COLOR_BLUE; TextBold[TextNum] = false; TextNum++;
        swprintf(TextNum, GlobalText[633], 7); TextListColor[TextNum] = TEXT_COLOR_BLUE; TextBold[TextNum] = false; TextNum++;
        swprintf(TextNum, GlobalText[634]); TextListColor[TextNum] = TEXT_COLOR_BLUE; TextBold[TextNum] = false; TextNum++;
        swprintf(TextNum, GlobalText[635]); TextListColor[TextNum] = TEXT_COLOR_BLUE; TextBold[TextNum] = false; TextNum++;
    }

    if (ip.Type >= ITEM_SCROLL_OF_EMPEROR_RING_OF_HONOR && ip.Type <= ITEM_SOUL_SHARD_OF_WIZARD)
    {
        swprintf(TextNum, GlobalText[730]);
        TextListColor[TextNum] = TEXT_COLOR_WHITE;
        TextBold[TextNum] = false;
        TextNum++;
        swprintf(TextNum, GlobalText[731]);
        TextListColor[TextNum] = TEXT_COLOR_RED;
        TextBold[TextNum] = false;
        TextNum++;
        swprintf(TextNum, GlobalText[732]);
        TextListColor[TextNum] = TEXT_COLOR_RED;
        TextBold[TextNum] = false;
        TextNum++;
    }
    else if (ip.Type == ITEM_POTION + 12)
    {
        if (Level <= 1)
        {
            swprintf(TextNum, GlobalText[119]);
            TextListColor[TextNum] = TEXT_COLOR_WHITE;
            TextBold[TextNum] = false;
            TextNum++;
        }
    }
    else if (ip.Type >= ITEM_HELPER + 46 && ip.Type <= ITEM_HELPER + 48)
    {
        int iMap = 0;
        if (ip.Type == ITEM_HELPER + 46)
            iMap = 39;
        else if (ip.Type == ITEM_HELPER + 47)
            iMap = 56;
        else if (ip.Type == ITEM_HELPER + 48)
            iMap = 58;

        swprintf(TextNum, GlobalText[2259], GlobalText[iMap]);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;
        swprintf(TextNum, GlobalText[2270]);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;
        swprintf(TextNum, "\n");
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;
    }

    else if (ip.Type >= ITEM_HELPER + 125 && ip.Type <= ITEM_HELPER + 127)
    {
        int iMap = 0;
        if (ip.Type == ITEM_HELPER + 125)
            iMap = 3057;
        else if (ip.Type == ITEM_HELPER + 126)
            iMap = 2806;
        else if (ip.Type == ITEM_HELPER + 127)
            iMap = 3107;

        swprintf(TextNum, GlobalText[2259], GlobalText[iMap]);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;
        swprintf(TextNum, "\n");
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;
    }
    else if (ip.Type == ITEM_POTION + 54)
    {
        swprintf(TextNum, GlobalText[2261]);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;
    }
    else if (ip.Type >= ITEM_POTION + 58 && ip.Type <= ITEM_POTION + 62)
    {
        swprintf(TextNum, GlobalText[2269]);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;
    }
    else if (ip.Type == ITEM_POTION + 83)
    {
        swprintf(TextNum, GlobalText[2269]);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;
    }
    else if (ip.Type >= ITEM_POTION + 145 && ip.Type <= ITEM_POTION + 150)
    {
        swprintf(TextNum, GlobalText[2269]);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;
    }
    else if (ip.Type == ITEM_POTION + 53)
    {
        swprintf(TextNum, GlobalText[2250]);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;
    }
    else if (ip.Type == ITEM_HELPER + 43)
    {
        swprintf(TextNum, GlobalText[2256]);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;
        swprintf(TextNum, GlobalText[2297]);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;
        swprintf(TextNum, GlobalText[2567]);
        TextListColor[TextNum] = TEXT_COLOR_YELLOW;
        TextBold[TextNum] = false;
        TextNum++;
        swprintf(TextNum, GlobalText[2568]);
        TextListColor[TextNum] = TEXT_COLOR_YELLOW;
        TextBold[TextNum] = false;
        TextNum++;
    }
    else if (ip.Type == ITEM_HELPER + 44)
    {
        swprintf(TextNum, GlobalText[2257]);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;
        swprintf(TextNum, GlobalText[2297]);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;
        swprintf(TextNum, GlobalText[2567]);
        TextListColor[TextNum] = TEXT_COLOR_YELLOW;
        TextBold[TextNum] = false;
        TextNum++;
        swprintf(TextNum, GlobalText[2568]);
        TextListColor[TextNum] = TEXT_COLOR_YELLOW;
        TextBold[TextNum] = false;
        TextNum++;
    }
    else if (ip.Type == ITEM_HELPER + 45)
    {
        swprintf(TextNum, GlobalText[2258]);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;
        swprintf(TextNum, GlobalText[2297]);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;
        swprintf(TextNum, GlobalText[2566]);
        TextListColor[TextNum] = TEXT_COLOR_YELLOW;
        TextBold[TextNum] = false;
        TextNum++;
    }
    else if (ip.Type >= ITEM_POTION + 70 && ip.Type <= ITEM_POTION + 71)
    {
        swprintf(TextNum, GlobalText[69], ip.Durability);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;

        int index = ip.Type - (ITEM_POTION + 70);

        swprintf(TextNum, GlobalText[2500 + index]);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;
    }
    else if (ip.Type >= ITEM_POTION + 72 && ip.Type <= ITEM_POTION + 77)
    {
        var Item_data = g_pItemAddOptioninfo.GetItemAddOtioninfo(ip.Type);

        swprintf(TextNum, GlobalText[2503 + (ip.Type - (ITEM_POTION + 72))], Item_data.m_byValue1);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;

        swprintf(TextNum, GlobalText[2502]);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;
    }
    else if (ip.Type == ITEM_HELPER + 59)
    {
        swprintf(TextNum, GlobalText[2509]);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;
    }
    else if (ip.Type >= ITEM_HELPER + 54 && ip.Type <= ITEM_HELPER + 58)
    {
        int statpoint = 0;
        statpoint = ip.Durability * 10;

        swprintf(TextNum, GlobalText[2511], statpoint);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;

        swprintf(TextNum, GlobalText[2510]);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;

        swprintf(TextNum, "\n"); TextNum++; SkipNum++;

        TextListColor[TextNum] = TEXT_COLOR_DARKRED;
        swprintf(TextNum, GlobalText[1908]);
        TextNum++;

        if (ip.Type == ITEM_HELPER + 58)
        {
            if (gCharacterManager.GetBaseClass(Hero.Class) == CLASS_DARK_LORD)
            {
                TextListColor[TextNum] = TEXT_COLOR_WHITE;
            }
            else
            {
                TextListColor[TextNum] = TEXT_COLOR_DARKRED;
            }

            swprintf(TextNum, GlobalText[61], GlobalText[24]);
            TextNum++;
        }
    }
    else if (ip.Type >= ITEM_POTION + 78 && ip.Type <= ITEM_POTION + 82)
    {
        int index = ip.Type - (ITEM_POTION + 78);
        int value = 0;

        swprintf(TextNum, GlobalText[69], ip.Durability);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;

        var Item_data = g_pItemAddOptioninfo.GetItemAddOtioninfo(ip.Type);
        value = Item_data.m_byValue1;

        swprintf(TextNum, GlobalText[2512 + index], value);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;
        swprintf(TextNum, GlobalText[2517]);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;
        swprintf(TextNum, GlobalText[2518]);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;

        if (ip.Type == ITEM_POTION + 82)
        {
            swprintf(TextNum, GlobalText[3115]);
            TextListColor[TextNum] = TEXT_COLOR_YELLOW;
            TextBold[TextNum] = false;
            TextNum++;
        }

        string timetext = "";
        timetext = g_StringTime(Item_data.m_Time, true);
        swprintf(TextNum, timetext);
        TextListColor[TextNum] = TEXT_COLOR_PURPLE;
        TextBold[TextNum] = false;
        TextNum++;

        swprintf(TextNum, GlobalText[2302]);
        TextListColor[TextNum] = TEXT_COLOR_WHITE;
        TextBold[TextNum] = false;
        TextNum++;
    }
    else if (ip.Type == ITEM_HELPER + 60)
    {
        swprintf(TextNum, GlobalText[2519]);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;
    }
    else if (ip.Type == ITEM_HELPER + 62)
    {
        var Item_data = g_pItemAddOptioninfo.GetItemAddOtioninfo(ip.Type);

        swprintf(TextNum, GlobalText[2253], Item_data.m_byValue1);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;
        swprintf(TextNum, GlobalText[2569], Item_data.m_byValue2);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;
        swprintf(TextNum, GlobalText[2570]);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;
        swprintf(TextNum, GlobalText[2566]);
        TextListColor[TextNum] = TEXT_COLOR_YELLOW;
        TextBold[TextNum] = false;
        TextNum++;
    }
    else if (ip.Type == ITEM_HELPER + 63)
    {
        var Item_data = g_pItemAddOptioninfo.GetItemAddOtioninfo(ip.Type);

        swprintf(TextNum, GlobalText[2254], Item_data.m_byValue1);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;
        swprintf(TextNum, GlobalText[2571], Item_data.m_byValue2);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;
        swprintf(TextNum, GlobalText[2572]);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;
    }
    else if (ip.Type == ITEM_POTION + 97)
    {
        var Item_data = g_pItemAddOptioninfo.GetItemAddOtioninfo(ip.Type);

        swprintf(TextNum, GlobalText[2580]);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;

        swprintf(TextNum, GlobalText[2502]);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;
    }
    else if (ip.Type == ITEM_POTION + 98)
    {
        var Item_data = g_pItemAddOptioninfo.GetItemAddOtioninfo(ip.Type);

        swprintf(TextNum, GlobalText[2581]);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;
        swprintf(TextNum, GlobalText[2502]);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;
    }
    else if (ip.Type == ITEM_POTION + 140)
    {
        swprintf(TextNum, GlobalText[92], 3);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;
        swprintf(TextNum, GlobalText[2188], 100);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;
    }
    else if (ip.Type == ITEM_POTION + 96)
    {
        swprintf(TextNum, GlobalText[2573]);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;
        swprintf(TextNum, GlobalText[2574]);
        TextListColor[TextNum] = TEXT_COLOR_PURPLE;
        TextBold[TextNum] = false;
        TextNum++;
        swprintf(TextNum, GlobalText[2708]);
        TextListColor[TextNum] = TEXT_COLOR_PURPLE;
        TextBold[TextNum] = false;
        TextNum++;
    }
    else if (ip.Type == ITEM_DEMON)
    {
        swprintf(TextNum, GlobalText[2575]);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;
        swprintf(TextNum, GlobalText[2576]);
        TextListColor[TextNum] = TEXT_COLOR_PURPLE;
        TextBold[TextNum] = false;
        TextNum++;
    }
    else if (ip.Type == ITEM_SPIRIT_OF_GUARDIAN)
    {
        swprintf(TextNum, GlobalText[2577]);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;
        swprintf(TextNum, GlobalText[2578]);
        TextListColor[TextNum] = TEXT_COLOR_PURPLE;
        TextBold[TextNum] = false;
        TextNum++;
    }
    else if (ip.Type == ITEM_PET_RUDOLF)
    {
        swprintf(TextNum, GlobalText[2600]);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;
    }
    else if (ip.Type == ITEM_PET_SKELETON)
    {
        swprintf(TextNum, GlobalText[2600]);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;

        swprintf(TextNum, GlobalText[3068]);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;

        swprintf(TextNum, GlobalText[3069]);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;

        swprintf(TextNum, GlobalText[3070]);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;
    }
    else if (ip.Type == ITEM_PET_PANDA)
    {
        swprintf(TextNum, GlobalText[2746]);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;
        swprintf(TextNum, GlobalText[2747]);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;
        swprintf(TextNum, GlobalText[2748]);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;
    }
    else if (ip.Type == ITEM_PET_UNICORN)
    {
        swprintf(TextNum, GlobalText[2746]);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;
        swprintf(TextNum, GlobalText[2744]);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;
        swprintf(TextNum, GlobalText[2748]);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;
    }
    else if (ip.Type == ITEM_HELPER + 107)
    {
        swprintf(TextNum, GlobalText[926]);
        TextListColor[TextNum] = TEXT_COLOR_RED;
        TextBold[TextNum] = false;
        TextNum++;
    }
    else if (ip.Type == ITEM_HELPER + 104)
    {
        swprintf(TextNum, GlobalText[2968]);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;
    }
    else if (ip.Type == ITEM_HELPER + 105)
    {
        swprintf(TextNum, GlobalText[2969]);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;
    }
    else if (ip.Type == ITEM_HELPER + 103)
    {
        swprintf(TextNum, GlobalText[2970], 170);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;
    }
    else if (ip.Type == ITEM_HELPER + 69)
    {
        swprintf(TextNum, GlobalText[2602]);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;
        if (IsRevivePositionSaved())
        {
            swprintf(TextNum, GlobalText[2603]);
            TextListColor[TextNum] = TEXT_COLOR_BLUE;
            TextBold[TextNum] = false;
            TextNum++;
            TextList[TextNum] = GetRevivePositionText();
            TextListColor[TextNum] = TEXT_COLOR_BLUE;
            TextBold[TextNum] = false;
            TextNum++;
        }
    }
    else if (ip.Type == ITEM_HELPER + 70)
    {
        swprintf(TextNum, GlobalText[2604]);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;
    }
    else if (ip.Type == ITEM_HELPER + 81)
    {
        swprintf(TextNum, GlobalText[2714]);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum++] = false;
        swprintf(TextNum, GlobalText[2729]);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum++] = false;
        swprintf(TextNum, GlobalText[3084]);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum++] = false;
    }
    else if (ip.Type == ITEM_HELPER + 82)
    {
        swprintf(TextNum, GlobalText[2715]);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum++] = false;
        swprintf(TextNum, GlobalText[2730]);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum++] = false;
        swprintf(TextNum, GlobalText[2716]);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum++] = false;
    }
    else if (ip.Type == ITEM_HELPER + 93)
    {
        swprintf(TextNum, GlobalText[2256]);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum++] = false;
        swprintf(TextNum, GlobalText[2297]);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum++] = false;
    }
    else if (ip.Type == ITEM_HELPER + 94)
    {
        swprintf(TextNum, GlobalText[2257]);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum++] = false;
        swprintf(TextNum, GlobalText[2297]);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum++] = false;
    }
    else if (ip.Type == ITEM_HELPER + 61)
    {
        swprintf(TextNum, GlobalText[2259], GlobalText[2369]);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;
        swprintf(TextNum, GlobalText[2270]);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;
        swprintf(TextNum, "\n");
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;
    }
    else if (ip.Type == ITEM_POTION + 91)
    {
        swprintf(TextNum, GlobalText[2551]);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;
    }
    else if (ip.Type == ITEM_POTION + 92)
    {
        swprintf(TextNum, GlobalText[2261]);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;
        swprintf(TextNum, GlobalText[2553]);
        TextListColor[TextNum] = TEXT_COLOR_YELLOW;
        TextBold[TextNum] = false;
        TextNum++;
    }
    else if (ip.Type == ITEM_POTION + 93)
    {
        swprintf(TextNum, GlobalText[2261]);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;
        swprintf(TextNum, GlobalText[2556]);
        TextListColor[TextNum] = TEXT_COLOR_YELLOW;
        TextBold[TextNum] = false;
        TextNum++;
    }
    else if (ip.Type == ITEM_POTION + 95)
    {
        swprintf(TextNum, GlobalText[2261]);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;
        swprintf(TextNum, GlobalText[2552]);
        TextListColor[TextNum] = TEXT_COLOR_YELLOW;
        TextBold[TextNum] = false;
        TextNum++;
    }
    else if (ip.Type == ITEM_POTION + 94)
    {
        swprintf(TextNum, GlobalText[69], ip.Durability);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;

        swprintf(TextNum, GlobalText[2559]);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;
    }
    else if (ip.Type == ITEM_CHERRY_BLOSSOM_PLAYBOX)
    {
        swprintf(TextNum, GlobalText[2011]);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;
    }
    else if (ip.Type == ITEM_CHERRY_BLOSSOM_WINE)
    {
        swprintf(TextNum, GlobalText[2549]);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;

        string timetext = "";
        var Item_data = g_pItemAddOptioninfo.GetItemAddOtioninfo(ip.Type);
        timetext = g_StringTime(Item_data.m_Time, true);
        swprintf(TextNum, timetext);
        TextListColor[TextNum] = TEXT_COLOR_PURPLE;
        TextBold[TextNum] = false;
        TextNum++;
    }
    else if (ip.Type == ITEM_CHERRY_BLOSSOM_RICE_CAKE)
    {
        swprintf(TextNum, GlobalText[2550]);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;
        string timetext = "";
        var Item_data = g_pItemAddOptioninfo.GetItemAddOtioninfo(ip.Type);
        timetext = g_StringTime(Item_data.m_Time, true);
        swprintf(TextNum, timetext);
        TextListColor[TextNum] = TEXT_COLOR_PURPLE;
        TextBold[TextNum] = false;
        TextNum++;
    }
    else if (ip.Type == ITEM_CHERRY_BLOSSOM_FLOWER_PETAL)
    {
        swprintf(TextNum, GlobalText[2532]);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;

        string timetext = "";
        var Item_data = g_pItemAddOptioninfo.GetItemAddOtioninfo(ip.Type);
        timetext = g_StringTime(Item_data.m_Time, true);
        swprintf(TextNum, timetext);
        TextListColor[TextNum] = TEXT_COLOR_PURPLE;
        TextBold[TextNum] = false;
        TextNum++;
    }
    else if (ip.Type == ITEM_POTION + 88)
    {
        swprintf(TextNum, GlobalText[69], ip.Durability);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;
        swprintf(TextNum, GlobalText[2534]);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;
        swprintf(TextNum, GlobalText[2535]);
        TextListColor[TextNum] = TEXT_COLOR_DARKYELLOW;
        TextBold[TextNum] = false;
        TextNum++;
    }
    else if (ip.Type == ITEM_POTION + 89)
    {
        swprintf(TextNum, GlobalText[69], ip.Durability);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;
        swprintf(TextNum, GlobalText[2534]);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;
        swprintf(TextNum, GlobalText[2536]);
        TextListColor[TextNum] = TEXT_COLOR_DARKYELLOW;
        TextBold[TextNum] = false;
        TextNum++;
    }
    else if (ip.Type == ITEM_GOLDEN_CHERRY_BLOSSOM_BRANCH)
    {
        swprintf(TextNum, GlobalText[69], ip.Durability);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;
        swprintf(TextNum, GlobalText[2534]);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;
        swprintf(TextNum, GlobalText[2537]);
        TextListColor[TextNum] = TEXT_COLOR_DARKYELLOW;
        TextBold[TextNum] = false;
        TextNum++;
    }
    else if (ip.Type == ITEM_OLD_SCROLL)
    {
        swprintf(TextNum, GlobalText[2397]);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;
    }
    else if (ip.Type == ITEM_ILLUSION_SORCERER_COVENANT)
    {
        swprintf(TextNum, GlobalText[2398]);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;
    }
    else if (ip.Type == ITEM_SCROLL_OF_BLOOD)
    {
        swprintf(TextNum, GlobalText[2399]);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;
    }
    else if (ip.Type == ITEM_POTION + 64)
    {
        swprintf(TextNum, GlobalText[2420]);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;
    }
    else if (ip.Type >= ITEM_FLAME_OF_DEATH_BEAM_KNIGHT && ip.Type <= ITEM_EYE_OF_ABYSSAL)
    {
        swprintf(TextNum, GlobalText[730]);
        TextListColor[TextNum] = TEXT_COLOR_WHITE;
        TextBold[TextNum] = false;
        TextNum++;
        swprintf(TextNum, GlobalText[731]);
        TextListColor[TextNum] = TEXT_COLOR_RED;
        TextBold[TextNum] = false;
        TextNum++;
        swprintf(TextNum, GlobalText[732]);
        TextListColor[TextNum] = TEXT_COLOR_RED;
        TextBold[TextNum] = false;
        TextNum++;
    }
    else if (ip.Type == ITEM_FLAME_OF_CONDOR)
    {
        swprintf(TextNum, GlobalText[1665]);
        TextListColor[TextNum] = TEXT_COLOR_WHITE;
        TextBold[TextNum] = false;
        TextNum++;
    }
    else if (ip.Type == ITEM_FEATHER_OF_CONDOR)
    {
        swprintf(TextNum, GlobalText[1665]);
        TextListColor[TextNum] = TEXT_COLOR_WHITE;
        TextBold[TextNum] = false;
        TextNum++;
    }
    else if ((ip.Type >= ITEM_SEED_FIRE && ip.Type <= ITEM_SEED_EARTH) || (ip.Type >= ITEM_SPHERE_MONO && ip.Type <= ITEM_SPHERE_5) || (ip.Type >= ITEM_SEED_SPHERE_FIRE_1 && ip.Type <= ITEM_SEED_SPHERE_EARTH_5))
    {
        TextNum = AttachToolTipForSeedSphereItem(ip, TextNum);
    }
    else if (ip.Type == ITEM_HELPER + 71 || ip.Type == ITEM_HELPER + 72 || ip.Type == ITEM_HELPER + 73 || ip.Type == ITEM_HELPER + 74 || ip.Type == ITEM_HELPER + 75)
    {
        swprintf(TextNum, GlobalText[2709]);
        TextListColor[TextNum] = TEXT_COLOR_WHITE;
        TextBold[TextNum] = false;
        TextNum++;
    }
    else if (ip.Type == ITEM_MOONSTONE_PENDANT)
    {
        swprintf(TextNum, GlobalText[926]);
        TextListColor[TextNum] = TEXT_COLOR_RED;
        TextBold[TextNum] = false;
        TextNum++;
    }
    else if (ip.Type == ITEM_WIZARDS_RING)
    {
        switch (Level)
        {
        case 0:
        {
            swprintf(TextNum, GlobalText[926]);
            TextListColor[TextNum] = TEXT_COLOR_RED;
            TextBold[TextNum] = false;
            TextNum++;
        }
        break;

        case 1:
        {
            swprintf(TextNum, GlobalText[924], 40);
            TextListColor[TextNum] = TEXT_COLOR_WHITE;
            TextBold[TextNum] = false;
            TextNum++;
            swprintf(TextNum, GlobalText[731]);
            TextListColor[TextNum] = TEXT_COLOR_RED;
            TextBold[TextNum] = false;
            TextNum++;
            swprintf(TextNum, GlobalText[732]);
            TextListColor[TextNum] = TEXT_COLOR_RED;
            TextBold[TextNum] = false;
            TextNum++;
            swprintf(TextNum, GlobalText[733]);
            TextListColor[TextNum] = TEXT_COLOR_RED;
            TextBold[TextNum] = false;
            TextNum++;
        }
        break;
        case 2:
        {
            swprintf(TextNum, GlobalText[924], 80);
            TextListColor[TextNum] = TEXT_COLOR_WHITE;
            TextBold[TextNum] = false;
            TextNum++;
            swprintf(TextNum, GlobalText[731]);
            TextListColor[TextNum] = TEXT_COLOR_RED;
            TextBold[TextNum] = false;
            TextNum++;
            swprintf(TextNum, GlobalText[732]);
            TextListColor[TextNum] = TEXT_COLOR_RED;
            TextBold[TextNum] = false;
            TextNum++;
            swprintf(TextNum, GlobalText[733]);
            TextListColor[TextNum] = TEXT_COLOR_RED;
            TextBold[TextNum] = false;
            TextNum++;
        }
        break;
        case 3:
        {
            swprintf(TextNum, GlobalText[926]);
            TextListColor[TextNum] = TEXT_COLOR_RED;
            TextBold[TextNum] = false;
            TextNum++;
        }
        break;
        }
    }
    else if (ip.Type >= ITEM_TYPE_CHARM_MIXWING + EWS_BEGIN && ip.Type <= ITEM_TYPE_CHARM_MIXWING + EWS_END)
    {
        var Item_data = g_pItemAddOptioninfo.GetItemAddOtioninfo(ip.Type);
        swprintf(TextNum, GlobalText[2717]);
        TextBold[TextNum] = false;
        TextNum++;

        switch (ip.Type)
        {
        case ITEM_TYPE_CHARM_MIXWING + EWS_KNIGHT_1_CHARM:
        {
            swprintf(TextNum, GlobalText[2718], Item_data.m_byValue1);
        }break;
        case ITEM_TYPE_CHARM_MIXWING + EWS_MAGICIAN_1_CHARM:
        {
            swprintf(TextNum, GlobalText[2720], Item_data.m_byValue1);
        }break;
        case ITEM_TYPE_CHARM_MIXWING + EWS_ELF_1_CHARM:
        {
            swprintf(TextNum, GlobalText[2722], Item_data.m_byValue1);
        }break;
        case ITEM_TYPE_CHARM_MIXWING + EWS_SUMMONER_1_CHARM:
        {
            swprintf(TextNum, GlobalText[2724], Item_data.m_byValue1);
        }break;
        case ITEM_TYPE_CHARM_MIXWING + EWS_DARKLORD_1_CHARM:
        {
            swprintf(TextNum, GlobalText[2727], Item_data.m_byValue1);
        }break;
        case ITEM_TYPE_CHARM_MIXWING + EWS_KNIGHT_2_CHARM:
        {
            swprintf(TextNum, GlobalText[2719], Item_data.m_byValue1);
        }break;
        case ITEM_TYPE_CHARM_MIXWING + EWS_MAGICIAN_2_CHARM:
        {
            swprintf(TextNum, GlobalText[2721], Item_data.m_byValue1);
        }break;
        case ITEM_TYPE_CHARM_MIXWING + EWS_ELF_2_CHARM:
        {
            swprintf(TextNum, GlobalText[2723], Item_data.m_byValue1);
        }break;
        case ITEM_TYPE_CHARM_MIXWING + EWS_SUMMONER_2_CHARM:
        {
            swprintf(TextNum, GlobalText[2725], Item_data.m_byValue1);
        }break;
        case ITEM_TYPE_CHARM_MIXWING + EWS_DARKKNIGHT_2_CHARM:
        {
            swprintf(TextNum, GlobalText[2726], Item_data.m_byValue1);
        }break;
        }

        swprintf(TextNum, GlobalText[2732 + (ip.Type - (ITEM_TYPE_CHARM_MIXWING + EWS_BEGIN))],
            Item_data.m_byValue1);

        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;
    }
    else if (ip.Type == ITEM_POTION + 110)
    {
        swprintf(TextNum, GlobalText[2773]);
        TextListColor[TextNum] = TEXT_COLOR_WHITE;
        TextBold[TextNum] = false;
        TextNum++;
        swprintf(TextNum, GlobalText[2774]);
        TextListColor[TextNum] = TEXT_COLOR_WHITE;
        TextBold[TextNum] = false;
        TextNum++;
        swprintf(TextNum, GlobalText[2775]);
        TextListColor[TextNum] = TEXT_COLOR_WHITE;
        TextBold[TextNum] = false;
        TextNum++;
        swprintf(TextNum, "\n"); TextNum++; SkipNum++;
        swprintf(TextNum, GlobalText[1181], ip.Durability, 5);
        TextListColor[TextNum] = TEXT_COLOR_WHITE;
        TextBold[TextNum] = false;
        TextNum++;
        swprintf(TextNum, GlobalText[2776], 5 - ip.Durability);
        TextListColor[TextNum] = TEXT_COLOR_YELLOW;
        TextBold[TextNum] = false;
        TextNum++;
    }
    else if (ip.Type == ITEM_POTION + 111)
    {
        swprintf(TextNum, GlobalText[2777]);
        TextListColor[TextNum] = TEXT_COLOR_DARKBLUE;
        TextBold[TextNum] = false;
        TextNum++;
        swprintf(TextNum, GlobalText[2778]);
        TextListColor[TextNum] = TEXT_COLOR_DARKBLUE;
        TextBold[TextNum] = false;
        TextNum++;
    }
    else if (ITEM_SUSPICIOUS_SCRAP_OF_PAPER <= ip.Type && ip.Type <= ITEM_COMPLETE_SECROMICON)
    {
        switch (ip.Type)
        {
        case ITEM_SUSPICIOUS_SCRAP_OF_PAPER:
        {
            swprintf(TextNum, GlobalText[1181], ip.Durability, 5);
            TextListColor[TextNum] = TEXT_COLOR_WHITE;
            TextBold[TextNum] = false;
            TextNum++;

            swprintf(TextNum, GlobalText[2788]);
            TextListColor[TextNum] = TEXT_COLOR_WHITE;
            TextBold[TextNum] = false;
            TextNum++;
        }break;
        case ITEM_GAIONS_ORDER:
        {
            swprintf(TextNum, GlobalText[2784]);
            TextListColor[TextNum] = TEXT_COLOR_WHITE;
            TextBold[TextNum] = false;
            TextNum++;
            swprintf(TextNum, GlobalText[2785]);
            TextListColor[TextNum] = TEXT_COLOR_WHITE;
            TextBold[TextNum] = false;
            TextNum++;
            swprintf(TextNum, GlobalText[2786]);
            TextListColor[TextNum] = TEXT_COLOR_WHITE;
            TextBold[TextNum] = false;
            TextNum++;
        }
        break;
        case ITEM_FIRST_SECROMICON_FRAGMENT:
        case ITEM_SECOND_SECROMICON_FRAGMENT:
        case ITEM_THIRD_SECROMICON_FRAGMENT:
        case ITEM_FOURTH_SECROMICON_FRAGMENT:
        case ITEM_FIFTH_SECROMICON_FRAGMENT:
        case ITEM_SIXTH_SECROMICON_FRAGMENT:
        {
            swprintf(TextNum, GlobalText[2790]);
            TextListColor[TextNum] = TEXT_COLOR_WHITE;
            TextBold[TextNum] = false;
            TextNum++;
        }break;
        case ITEM_COMPLETE_SECROMICON:
        {
            swprintf(TextNum, GlobalText[2792]);
            TextListColor[TextNum] = TEXT_COLOR_WHITE;
            TextBold[TextNum] = false;
            TextNum++;
            swprintf(TextNum, GlobalText[2793]);
            TextListColor[TextNum] = TEXT_COLOR_WHITE;
            TextBold[TextNum] = false;
            TextNum++;
        }break;
        }
    }
    else if (ITEM_HELPER + 109 == ip.Type)
    {
        swprintf(TextNum, GlobalText[92], 3);
        TextListColor[TextNum] = TEXT_COLOR_WHITE;
        TextBold[TextNum] = false;
        TextNum++;
        swprintf(TextNum, GlobalText[3058], 4);
        TextListColor[TextNum] = TEXT_COLOR_WHITE;
        TextBold[TextNum] = false;
        TextNum++;
    }
    else if (ITEM_HELPER + 110 == ip.Type)
    {
        swprintf(TextNum, GlobalText[92], 3);
        TextListColor[TextNum] = TEXT_COLOR_WHITE;
        TextBold[TextNum] = false;
        TextNum++;
        swprintf(TextNum, GlobalText[622], 4);
        TextListColor[TextNum] = TEXT_COLOR_WHITE;
        TextBold[TextNum] = false;
        TextNum++;
    }
    else if (ITEM_HELPER + 111 == ip.Type)
    {
        swprintf(TextNum, GlobalText[92], 3);
        TextListColor[TextNum] = TEXT_COLOR_WHITE;
        TextBold[TextNum] = false;
        TextNum++;
        swprintf(TextNum, GlobalText[627], 50);
        TextListColor[TextNum] = TEXT_COLOR_WHITE;
        TextBold[TextNum] = false;
        TextNum++;
    }
    else if (ITEM_HELPER + 112 == ip.Type)
    {
        swprintf(TextNum, GlobalText[92], 3);
        TextListColor[TextNum] = TEXT_COLOR_WHITE;
        TextBold[TextNum] = false;
        TextNum++;
        swprintf(TextNum, GlobalText[624], 4);
        TextListColor[TextNum] = TEXT_COLOR_WHITE;
        TextBold[TextNum] = false;
        TextNum++;
    }
    else if (ITEM_HELPER + 113 == ip.Type)
    {
        swprintf(TextNum, GlobalText[92], 3);
        TextListColor[TextNum] = TEXT_COLOR_WHITE;
        TextBold[TextNum] = false;
        TextNum++;
        swprintf(TextNum, GlobalText[628], 10);
        TextListColor[TextNum] = TEXT_COLOR_WHITE;
        TextBold[TextNum] = false;
        TextNum++;
    }
    else if (ITEM_HELPER + 114 == ip.Type)
    {
        swprintf(TextNum, GlobalText[92], 3);
        TextListColor[TextNum] = TEXT_COLOR_WHITE;
        TextBold[TextNum] = false;
        TextNum++;
        swprintf(TextNum, GlobalText[2229], 7);
        TextListColor[TextNum] = TEXT_COLOR_WHITE;
        TextBold[TextNum] = false;
        TextNum++;
    }
    else if (ITEM_HELPER + 115 == ip.Type)
    {
        swprintf(TextNum, GlobalText[92], 3);
        TextListColor[TextNum] = TEXT_COLOR_WHITE;
        TextBold[TextNum] = false;
        TextNum++;
        swprintf(TextNum, GlobalText[635]);
        TextListColor[TextNum] = TEXT_COLOR_WHITE;
        TextBold[TextNum] = false;
        TextNum++;
    }
    else if (ITEM_POTION + 112 == ip.Type)
    {
        swprintf(TextNum, GlobalText[2876]);
        TextListColor[TextNum] = TEXT_COLOR_WHITE;
        TextBold[TextNum] = false;
        TextNum++;
    }
    else if (ITEM_POTION + 113 == ip.Type)
    {
        swprintf(TextNum, GlobalText[2875]);
        TextListColor[TextNum] = TEXT_COLOR_WHITE;
        TextBold[TextNum] = false;
        TextNum++;
    }
    else if (ip.Type == ITEM_POTION + 120)
    {
        swprintf(TextNum, GlobalText[2971]);
        TextListColor[TextNum] = TEXT_COLOR_WHITE;
        TextBold[TextNum] = false;
        TextNum++;
    }
    else if (ITEM_POTION + 121 == ip.Type)
    {
        swprintf(TextNum, GlobalText[2877]);
        TextListColor[TextNum] = TEXT_COLOR_WHITE;
        TextBold[TextNum] = false;
        TextNum++;
    }
    else if (ITEM_POTION + 122 == ip.Type)
    {
        swprintf(TextNum, GlobalText[2878]);
        TextListColor[TextNum] = TEXT_COLOR_WHITE;
        TextBold[TextNum] = false;
        TextNum++;
    }
    else if (ITEM_POTION + 123 == ip.Type)
    {
        swprintf(TextNum, GlobalText[2879]);
        TextListColor[TextNum] = TEXT_COLOR_WHITE;
        TextBold[TextNum] = false;
        TextNum++;
    }
    else if (ITEM_POTION + 124 == ip.Type)
    {
        swprintf(TextNum, GlobalText[2880]);
        TextListColor[TextNum] = TEXT_COLOR_WHITE;
        TextBold[TextNum] = false;
        TextNum++;
    }
    else if (ITEM_POTION + 134 <= ip.Type && ITEM_POTION + 139 >= ip.Type)
    {
        swprintf(TextNum, GlobalText[2972]);
        TextListColor[TextNum] = TEXT_COLOR_WHITE;
        TextBold[TextNum] = false;
        TextNum++;
    }
    else if (ITEM_HELPER + 116 == ip.Type)
    {
        swprintf(TextNum, GlobalText[3018]);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;

        TextBold[TextNum] = false;
        TextNum++;
    }
    else if (ITEM_HELPER + 121 == ip.Type)
    {
        int iMap = 57;
        swprintf(TextNum, GlobalText[2259], GlobalText[iMap]);
        TextListColor[TextNum] = TEXT_COLOR_WHITE;
        TextBold[TextNum] = false;
        TextNum++;
        swprintf(TextNum, GlobalText[2270]);
        TextListColor[TextNum] = TEXT_COLOR_WHITE;
        TextBold[TextNum] = false;
        TextNum++;
        swprintf(TextNum, GlobalText[2260], ip.Durability);
        TextListColor[TextNum] = TEXT_COLOR_WHITE;
        TextBold[TextNum] = false;
        TextNum++;
    }
    else if (ip.Type == ITEM_HELPER + 124)
    {
        swprintf(TextNum, GlobalText[3116]);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;
        swprintf(TextNum, GlobalText[3127]);
        TextListColor[TextNum] = TEXT_COLOR_YELLOW;
        TextBold[TextNum] = true;
        TextNum++;
    }
    else if (ip.Type >= ITEM_POTION + 141 && ip.Type <= ITEM_POTION + 144)
    {
        swprintf(TextNum, GlobalText[571]);
        TextListColor[TextNum] = TEXT_COLOR_WHITE;
        TextBold[TextNum] = false;
        TextNum++;
    }
    else if (ip.Type == ITEM_POTION + 133)
    {
        swprintf(TextNum, GlobalText[69], ip.Durability);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;

        swprintf(TextNum, GlobalText[3267]);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;
    }

    Color = TEXT_COLOR_YELLOW;
    swprintf(TextNum, "%s", p.Name);

    if (ip.Type == ITEM_DEVILS_INVITATION)
    {
        swprintf(TextNum, GlobalText[638]);
        TextListColor[TextNum] = TEXT_COLOR_WHITE;
        TextBold[TextNum] = false;
        TextNum++;
        swprintf(TextNum, GlobalText[639]);
        TextListColor[TextNum] = TEXT_COLOR_WHITE;
        TextBold[TextNum] = false;
        TextNum++;
    }

    if (ip.DamageMin != 0)
    {
        int minindex = 0, maxindex = 0, magicalindex = 0;

        if (Level >= ip.Jewel_Of_Harmony_OptionLevel)
        {
            var SC = new StrengthenCapability();
            g_pUIJewelHarmonyinfo.GetStrengthenCapability(SC, ip, 1);

            if (SC.SI_isSP)
            {
                minindex = SC.SI_SP.SI_minattackpower;
                maxindex = SC.SI_SP.SI_maxattackpower;
                magicalindex = SC.SI_SP.SI_magicalpower;
            }
        }
        int DamageMin = ip.DamageMin;
        int DamageMax = ip.DamageMax;
        if (ip.Type >> 4 == 15)
        {
            swprintf(TextNum, "%s: %d ~ %d", GlobalText[40 + 2], DamageMin, DamageMax);
        }
        else if (ip.Type != ITEM_SCROLL_OF_TELEPORT && ip.Type != ITEM_SCROLL_OF_TELEPORT_ALLY && ip.Type != ITEM_SCROLL_OF_SOUL_BARRIER)
        {
            if (ip.Type >= ITEM_ETC && ip.Type < ITEM_ETC + MAX_ITEM_INDEX)
            {
                int SkillIndex = getSkillIndexByBook(ip.Type);

                int skillDamage = SkillDamage(SkillIndex);

                DamageMin = skillDamage;
                DamageMax = skillDamage + skillDamage / 2;

                swprintf(TextNum, "%s: %d ~ %d", GlobalText[42], DamageMin, DamageMax);
            }
            else
            {
                if (DamageMin + minindex >= DamageMax + maxindex)
                    swprintf(TextNum, "%s: %d ~ %d", GlobalText[40 + (p.TwoHand ? 1 : 0)], DamageMax + maxindex, DamageMax + maxindex);
                else
                    swprintf(TextNum, "%s: %d ~ %d", GlobalText[40 + (p.TwoHand ? 1 : 0)], DamageMin + minindex, DamageMax + maxindex);
            }
        }
        else
        {
            TextNum--;
        }

        if (DamageMin > 0)
        {
            if (minindex != 0 || maxindex != 0)
            {
                TextListColor[TextNum] = TEXT_COLOR_YELLOW;
                TextBold[TextNum] = false;
                TextNum++;
            }
            else
            {
                if (ip.ExcellentFlags > 0)
                    TextListColor[TextNum] = TEXT_COLOR_BLUE;
                else
                    TextListColor[TextNum] = TEXT_COLOR_WHITE;
                TextBold[TextNum] = false;
                TextNum++;
            }
        }
        else
        {
            TextNum--;
        }
    }
    if (ip.Defense != 0)
    {
        int maxdefense = 0;

        if (Level >= ip.Jewel_Of_Harmony_OptionLevel)
        {
            var SC = new StrengthenCapability();
            g_pUIJewelHarmonyinfo.GetStrengthenCapability(SC, ip, 2);

            if (SC.SI_isSD)
            {
                maxdefense = SC.SI_SD.SI_defense;
            }
        }
        swprintf(TextNum, GlobalText[65], ip.Defense + maxdefense);

        if (maxdefense != 0)
            TextListColor[TextNum] = TEXT_COLOR_YELLOW;
        else
        {
            if (ip.Type >= ITEM_HELM && ip.Type < ITEM_BOOTS + MAX_ITEM_INDEX && ip.ExcellentFlags > 0)
                TextListColor[TextNum] = TEXT_COLOR_BLUE;
            else
                TextListColor[TextNum] = TEXT_COLOR_WHITE;
        }

        TextBold[TextNum] = false;
        TextNum++;
    }
    if (ip.MagicDefense != 0)
    {
        swprintf(TextNum, GlobalText[66], ip.MagicDefense);
        TextListColor[TextNum] = TEXT_COLOR_WHITE;
        TextBold[TextNum] = false;
        TextNum++;
    }
    if (p.SuccessfulBlocking != 0)
    {
        swprintf(TextNum, GlobalText[67], ip.SuccessfulBlocking);
        if (ip.ExcellentFlags > 0)
            TextListColor[TextNum] = TEXT_COLOR_BLUE;
        else
            TextListColor[TextNum] = TEXT_COLOR_WHITE;
        TextBold[TextNum] = false;
        TextNum++;
    }
    if (p.WeaponSpeed != 0)
    {
        swprintf(TextNum, GlobalText[64], p.WeaponSpeed);
        TextListColor[TextNum] = TEXT_COLOR_WHITE;
        TextBold[TextNum] = false;
        TextNum++;
    }
    if (p.WalkSpeed != 0)
    {
        swprintf(TextNum, GlobalText[68], p.WalkSpeed);
        TextListColor[TextNum] = TEXT_COLOR_WHITE;
        TextBold[TextNum] = false;
        TextNum++;
    }
    if (ip.Type >= ITEM_RED_RIBBON_BOX && ip.Type <= ITEM_BLUE_RIBBON_BOX)
    {
        swprintf(TextNum, GlobalText[571]);
        switch (ip.Type)
        {
        case ITEM_RED_RIBBON_BOX:
            TextListColor[TextNum] = TEXT_COLOR_RED;
            break;
        case ITEM_GREEN_RIBBON_BOX:
            TextListColor[TextNum] = TEXT_COLOR_GREEN;
            break;
        case ITEM_BLUE_RIBBON_BOX:
            TextListColor[TextNum] = TEXT_COLOR_BLUE;
            break;
        }
        TextBold[TextNum] = false; TextNum++;
    }
    if (ip.Type >= ITEM_PUMPKIN_OF_LUCK && ip.Type <= ITEM_JACK_OLANTERN_DRINK) //Halloween event
    {
        string Text_data = "";
        var Item_data = g_pItemAddOptioninfo.GetItemAddOtioninfo(ip.Type);

        switch (ip.Type)
        {
        case ITEM_PUMPKIN_OF_LUCK:
            swprintf(TextNum, GlobalText[2011]);
            TextListColor[TextNum] = TEXT_COLOR_DARKYELLOW;
            break;
        case ITEM_JACK_OLANTERN_BLESSINGS:
            Text_data = Fmt(GlobalText[2229], Item_data.m_byValue1);
            swprintf(TextNum, Text_data);
            TextListColor[TextNum] = TEXT_COLOR_DARKYELLOW;
            break;
        case ITEM_JACK_OLANTERN_WRATH:
            Text_data = Fmt(GlobalText[2230], Item_data.m_byValue1);
            swprintf(TextNum, Text_data);
            TextListColor[TextNum] = TEXT_COLOR_DARKYELLOW;
            break;
        case ITEM_JACK_OLANTERN_CRY:
            Text_data = Fmt(GlobalText[2231], Item_data.m_byValue1);
            swprintf(TextNum, Text_data);
            TextListColor[TextNum] = TEXT_COLOR_DARKYELLOW;
            break;
        case ITEM_JACK_OLANTERN_FOOD:
            Text_data = Fmt(GlobalText[960], Item_data.m_byValue1);
            swprintf(TextNum, Text_data);
            TextListColor[TextNum] = TEXT_COLOR_DARKYELLOW;
            break;
        case ITEM_JACK_OLANTERN_DRINK:
            Text_data = Fmt(GlobalText[961], Item_data.m_byValue1);
            swprintf(TextNum, Text_data);
            TextListColor[TextNum] = TEXT_COLOR_DARKYELLOW;
            break;
        }
        TextBold[TextNum] = false; TextNum++;
    }
    if (ip.Type >= ITEM_PINK_CHOCOLATE_BOX && ip.Type <= ITEM_BLUE_CHOCOLATE_BOX)
    {
        swprintf(TextNum, GlobalText[2011]);
        switch (ip.Type)
        {
        case ITEM_PINK_CHOCOLATE_BOX:
            if (Level == 0)
                TextListColor[TextNum] = TEXT_COLOR_PURPLE;
            else
                if (Level == 1)
                    TextListColor[TextNum] = TEXT_COLOR_PURPLE;
            break;
        case ITEM_RED_CHOCOLATE_BOX:
            if (Level == 0)
                TextListColor[TextNum] = TEXT_COLOR_RED;
            else
                if (Level == 1)
                    TextListColor[TextNum] = TEXT_COLOR_RED;
            break;
        case ITEM_BLUE_CHOCOLATE_BOX:
            if (Level == 0)
                TextListColor[TextNum] = TEXT_COLOR_BLUE;
            else
                if (Level == 1)
                    TextListColor[TextNum] = TEXT_COLOR_BLUE;
            break;
        }
        TextBold[TextNum] = false; TextNum++;
    }
    if (ip.Type == ITEM_BOX_OF_LUCK)
    {
        if (Level == 7)
        {
            swprintf(TextNum, GlobalText[112]);
            TextListColor[TextNum] = TEXT_COLOR_WHITE; TextBold[TextNum] = false; TextNum++;
            swprintf(TextNum, GlobalText[113]);
            TextListColor[TextNum] = TEXT_COLOR_WHITE; TextBold[TextNum] = false; TextNum++;
            swprintf(TextNum, GlobalText[114]);
            TextListColor[TextNum] = TEXT_COLOR_WHITE; TextBold[TextNum] = false; TextNum++;
        }
        else if (Level == 14)
        {
            swprintf(TextNum, GlobalText[1652]);
            TextListColor[TextNum] = TEXT_COLOR_WHITE; TextBold[TextNum] = false; TextNum++;
            swprintf(TextNum, GlobalText[1653]);
            TextListColor[TextNum] = TEXT_COLOR_WHITE; TextBold[TextNum] = false; TextNum++;
        }
        else
        {
            swprintf(TextNum, GlobalText[571]);
            TextListColor[TextNum] = TEXT_COLOR_WHITE; TextBold[TextNum] = false; TextNum++;
        }
        swprintf(TextNum, GlobalText[733]);
        TextListColor[TextNum] = TEXT_COLOR_RED;
        TextBold[TextNum] = false;
        TextNum++;
        if (Level == 13)
        {
            swprintf(TextNum, GlobalText[731]);
            TextListColor[TextNum] = TEXT_COLOR_RED;
            TextBold[TextNum] = false;
            TextNum++;
        }
    }
    int tCount = COMGEM.CalcCompiledCount(ip);
    if (tCount > 0)
    {
        int	nJewelIndex = COMGEM.Check_Jewel_Com(ip.Type);
        if (nJewelIndex != COMGEM.NOGEM)
        {
            swprintf(TextNum, GlobalText[1819], tCount, GlobalText[COMGEM.GetJewelIndex(nJewelIndex, COMGEM.eGEM_NAME)]);
            TextListColor[TextNum] = TEXT_COLOR_WHITE; TextBold[TextNum] = false; TextNum++;
            swprintf(TextNum, GlobalText[1820]);
            TextListColor[TextNum] = TEXT_COLOR_WHITE; TextBold[TextNum] = false; TextNum++;
        }
    }
    if (ip.Type == ITEM_JEWEL_OF_BLESS)
    {
        swprintf(TextNum, GlobalText[572]);
        TextListColor[TextNum] = TEXT_COLOR_WHITE; TextBold[TextNum] = false; TextNum++;
    }
    if (ip.Type == ITEM_JEWEL_OF_SOUL)
    {
        swprintf(TextNum, GlobalText[573]);
        TextListColor[TextNum] = TEXT_COLOR_WHITE; TextBold[TextNum] = false; TextNum++;
    }
    if (ip.Type == ITEM_JEWEL_OF_LIFE)
    {
        swprintf(TextNum, GlobalText[621]);
        TextListColor[TextNum] = TEXT_COLOR_WHITE; TextBold[TextNum] = false; TextNum++;
    }
    if (ip.Type == ITEM_DEVILS_EYE || ip.Type == ITEM_DEVILS_KEY)
    {
        swprintf(TextNum, GlobalText[637]);
        TextListColor[TextNum] = TEXT_COLOR_WHITE; TextBold[TextNum] = false; TextNum++;
    }
    if (ip.Type == ITEM_TOWN_PORTAL_SCROLL && Level >= 1 && Level <= 8)
    {
        swprintf(TextNum, GlobalText[157], 3);
        TextListColor[TextNum] = TEXT_COLOR_WHITE; TextBold[TextNum] = false; TextNum++;
    }
    if (ip.Type == ITEM_JEWEL_OF_CHAOS)
    {
        swprintf(TextNum, GlobalText[574]);
        TextListColor[TextNum] = TEXT_COLOR_WHITE; TextBold[TextNum] = false; TextNum++;
    }
    if (ip.Type == ITEM_JEWEL_OF_CREATION)
    {
        swprintf(TextNum, GlobalText[619]);
        TextListColor[TextNum] = TEXT_COLOR_WHITE; TextBold[TextNum] = false; TextNum++;
    }
    if (ip.Type == ITEM_JEWEL_OF_GUARDIAN)
    {
        swprintf(TextNum, GlobalText[1289]);
        TextListColor[TextNum] = TEXT_COLOR_WHITE; TextBold[TextNum] = false; TextNum++;
    }
    if (ip.Type == ITEM_GUARDIAN_ANGEL) //
    {
        swprintf(TextNum, GlobalText[578], 20);
        TextListColor[TextNum] = TEXT_COLOR_WHITE; TextBold[TextNum] = false; TextNum++;
        swprintf(TextNum, GlobalText[739], 50);
        TextListColor[TextNum] = TEXT_COLOR_WHITE; TextBold[TextNum] = false; TextNum++;
    }
    if (ip.Type == ITEM_IMP)
    {
        swprintf(TextNum, GlobalText[576]);
        TextListColor[TextNum] = TEXT_COLOR_WHITE; TextBold[TextNum] = false; TextNum++;
    }
    if ((ip.Type >= ITEM_WING && ip.Type <= ITEM_WINGS_OF_SATAN) || ip.Type == ITEM_WING_OF_CURSE)
    {
        swprintf(TextNum, GlobalText[577], 12 + Level * 2);
        TextListColor[TextNum] = TEXT_COLOR_WHITE; TextBold[TextNum] = false; TextNum++;
        swprintf(TextNum, GlobalText[578], 12 + Level * 2);
        TextListColor[TextNum] = TEXT_COLOR_WHITE; TextBold[TextNum] = false; TextNum++;
        swprintf(TextNum, GlobalText[579]);
        TextListColor[TextNum] = TEXT_COLOR_WHITE; TextBold[TextNum] = false; TextNum++;
    }
    else if (ip.Type == ITEM_MOONSTONE_PENDANT)
    {
        swprintf(TextNum, GlobalText[2207]);
        TextListColor[TextNum] = TEXT_COLOR_WHITE; TextBold[TextNum] = false; TextNum++;
    }
    else if (ip.Type == ITEM_GEMSTONE)
    {
        swprintf(TextNum, GlobalText[2208]);
        TextListColor[TextNum] = TEXT_COLOR_WHITE; TextBold[TextNum] = false; TextNum++;
    }
    else if (ip.Type == ITEM_JEWEL_OF_HARMONY)
    {
        swprintf(TextNum, GlobalText[2209]);
        TextListColor[TextNum] = TEXT_COLOR_WHITE; TextBold[TextNum] = false; TextNum++;
    }
    else if (ip.Type == ITEM_LOWER_REFINE_STONE)
    {
        swprintf(TextNum, GlobalText[2210]);
        TextListColor[TextNum] = TEXT_COLOR_WHITE; TextBold[TextNum] = false; TextNum++;
    }
    else if (ip.Type == ITEM_HIGHER_REFINE_STONE)
    {
        swprintf(TextNum, GlobalText[2210]);
        TextListColor[TextNum] = TEXT_COLOR_WHITE; TextBold[TextNum] = false; TextNum++;
    }
    else if (ip.Type == ITEM_POTION + 160)
    {
        // 연장의 보석
        swprintf(TextNum, GlobalText[3305]);
        TextListColor[TextNum] = TEXT_COLOR_WHITE; TextBold[TextNum] = false; TextNum++;
    }
    else if (ip.Type == ITEM_POTION + 161)
    {
        // 상승의 보석
        swprintf(TextNum, GlobalText[2209]);
        TextListColor[TextNum] = TEXT_COLOR_WHITE; TextBold[TextNum] = false; TextNum++;
    }
    else if ((ip.Type >= ITEM_WINGS_OF_SPIRITS && ip.Type <= ITEM_WINGS_OF_DARKNESS) || ip.Type == ITEM_WINGS_OF_DESPAIR) //날개
    {
        swprintf(TextNum, GlobalText[577], 32 + Level);  //  데미지 몇%증가.
        TextListColor[TextNum] = TEXT_COLOR_WHITE; TextBold[TextNum] = false; TextNum++;
        swprintf(TextNum, GlobalText[578], 25 + Level * 2);  //  데미지 몇%흡수.
        TextListColor[TextNum] = TEXT_COLOR_WHITE; TextBold[TextNum] = false; TextNum++;
        swprintf(TextNum, GlobalText[579]);             //  이동 속도 향상.
        TextListColor[TextNum] = TEXT_COLOR_WHITE; TextBold[TextNum] = false; TextNum++;
    }
    else if ((ip.Type >= ITEM_WING_OF_STORM && ip.Type <= ITEM_CAPE_OF_EMPEROR) || ip.Type == ITEM_WING_OF_DIMENSION
        || ip.Type == ITEM_CAPE_OF_OVERRULE)
    {
        swprintf(TextNum, GlobalText[577], 39 + Level * 2);
        TextListColor[TextNum] = TEXT_COLOR_WHITE; TextBold[TextNum] = false; TextNum++;
        if (ip.Type == ITEM_CAPE_OF_EMPEROR || ip.Type == ITEM_CAPE_OF_OVERRULE)
        {
            swprintf(TextNum, GlobalText[578], 24 + Level * 2);
        }
        else
        {
            swprintf(TextNum, GlobalText[578], 39 + Level * 2);
        }
        TextListColor[TextNum] = TEXT_COLOR_WHITE; TextBold[TextNum] = false; TextNum++;
        swprintf(TextNum, GlobalText[579]);
        TextListColor[TextNum] = TEXT_COLOR_WHITE; TextBold[TextNum] = false; TextNum++;
    }
    else if (ITEM_WING + 130 <= ip.Type && ip.Type <= ITEM_WING + 135)
    {
        switch (ip.Type)
        {
        case ITEM_WING + 130:
        case ITEM_WING + 135:
        {
            swprintf(TextNum, GlobalText[577], 20 + Level * 2);
            TextListColor[TextNum] = TEXT_COLOR_WHITE; TextBold[TextNum] = false; TextNum++;
            swprintf(TextNum, GlobalText[578], 20 + Level * 2);
            TextListColor[TextNum] = TEXT_COLOR_WHITE; TextBold[TextNum] = false; TextNum++;
        }break;
        case ITEM_WING + 131:
        case ITEM_WING + 132:
        case ITEM_WING + 133:
        case ITEM_WING + 134:
        {
            swprintf(TextNum, GlobalText[577], 12 + Level * 2);
            TextListColor[TextNum] = TEXT_COLOR_WHITE; TextBold[TextNum] = false; TextNum++;
            swprintf(TextNum, GlobalText[578], 12 + Level * 2);
            TextListColor[TextNum] = TEXT_COLOR_WHITE; TextBold[TextNum] = false; TextNum++;
        }break;
        }
        swprintf(TextNum, GlobalText[579]);
        TextListColor[TextNum] = TEXT_COLOR_WHITE; TextBold[TextNum] = false; TextNum++;
    }
    else if (ip.Type == ITEM_HORN_OF_DINORANT)
    {
        swprintf(TextNum, GlobalText[577], 15);
        TextListColor[TextNum] = TEXT_COLOR_WHITE; TextBold[TextNum] = false; TextNum++;
        swprintf(TextNum, GlobalText[578], 10);
        TextListColor[TextNum] = TEXT_COLOR_WHITE; TextBold[TextNum] = false; TextNum++;
    }
    else if (ip.Type == ITEM_SPIRIT)
    {
        TextListColor[TextNum] = TEXT_COLOR_WHITE;
        switch (Level)
        {
        case 0: swprintf(TextNum, GlobalText[1215]); TextNum++; break;
        case 1: swprintf(TextNum, GlobalText[1216]); TextNum++; break;
        }
    }
    else if (ip.Type == ITEM_LOCHS_FEATHER)
    {
        TextListColor[TextNum] = TEXT_COLOR_WHITE;
        switch (Level)
        {
        case 0: swprintf(TextNum, "%s", GlobalText[748]); TextNum++; break;
        case 1: swprintf(TextNum, "%s", GlobalText[1236]); TextNum++; break;
        }
    }

    else if (ip.Type == ITEM_FRUITS)
    {
        TextListColor[TextNum] = TEXT_COLOR_WHITE;
        switch (Level)
        {
        case 0:swprintf(TextNum, "%s %s", GlobalText[168], GlobalText[636]); break;
        case 1:swprintf(TextNum, "%s %s", GlobalText[169], GlobalText[636]); break;
        case 2:swprintf(TextNum, "%s %s", GlobalText[167], GlobalText[636]); break;
        case 3:swprintf(TextNum, "%s %s", GlobalText[166], GlobalText[636]); break;
        case 4:swprintf(TextNum, "%s %s", GlobalText[1900], GlobalText[636]); break;
        }
        TextNum++;
        TextListColor[TextNum] = TEXT_COLOR_WHITE;
        switch (Level)
        {
        case 0:swprintf(TextNum, "%s %s", GlobalText[168], GlobalText[1910]); break;
        case 1:swprintf(TextNum, "%s %s", GlobalText[169], GlobalText[1910]); break;
        case 2:swprintf(TextNum, "%s %s", GlobalText[167], GlobalText[1910]); break;
        case 3:swprintf(TextNum, "%s %s", GlobalText[166], GlobalText[1910]); break;
        case 4:swprintf(TextNum, "%s %s", GlobalText[1900], GlobalText[1910]); break;
        }
        TextNum++;
        TextListColor[TextNum] = TEXT_COLOR_DARKRED;
        swprintf(TextNum, GlobalText[1908]);

        if (Level == 4)
        {
            TextNum++;
            TextListColor[TextNum] = TEXT_COLOR_WHITE;
            swprintf(TextNum, GlobalText[61], GlobalText[24]);
        }
        TextNum++;
    }
    else if (ip.Type == ITEM_SCROLL_OF_ARCHANGEL)
    {
        TextListColor[TextNum] = TEXT_COLOR_WHITE;
        swprintf(TextNum, GlobalText[816]);
        TextNum++;
    }
    else if (ip.Type == ITEM_BLOOD_BONE)
    {
        TextListColor[TextNum] = TEXT_COLOR_WHITE;
        swprintf(TextNum, GlobalText[816]);
        TextNum++;
    }
    else if (ip.Type == ITEM_INVISIBILITY_CLOAK)
    {
        TextListColor[TextNum] = TEXT_COLOR_WHITE;
        swprintf(TextNum, GlobalText[814]);
        TextNum++;
        swprintf(TextNum, "\n");
        TextNum++; SkipNum++;
        swprintf(TextNum, GlobalText[638]);
        TextListColor[TextNum] = TEXT_COLOR_WHITE;
        TextBold[TextNum] = false;
        TextNum++;
        swprintf(TextNum, GlobalText[639]);
        TextListColor[TextNum] = TEXT_COLOR_WHITE;
        TextBold[TextNum] = false;
        TextNum++;
    }
    else if (ip.Type == ITEM_SIEGE_POTION)
    {
        switch (Level)
        {
        case 0:
        {
            swprintf(TextNum, GlobalText[1417]); TextListColor[TextNum] = TEXT_COLOR_DARKRED; TextBold[TextNum] = false; TextNum++;
            swprintf(TextNum, GlobalText[1418]); TextListColor[TextNum] = TEXT_COLOR_DARKRED; TextBold[TextNum] = false; TextNum++;
            swprintf(TextNum, GlobalText[1419]); TextListColor[TextNum] = TEXT_COLOR_DARKRED; TextBold[TextNum] = false; TextNum++;
        }
        break;
        case 1:
        {
            swprintf(TextNum, GlobalText[1638]); TextListColor[TextNum] = TEXT_COLOR_DARKRED; TextBold[TextNum] = false; TextNum++;
            swprintf(TextNum, GlobalText[1639]); TextListColor[TextNum] = TEXT_COLOR_DARKRED; TextBold[TextNum] = false; TextNum++;
            swprintf(TextNum, GlobalText[1472]); TextListColor[TextNum] = TEXT_COLOR_DARKRED; TextBold[TextNum] = false; TextNum++;
        }
        break;
        }
    }
    else if (ip.Type == ITEM_HELPER + 7)
    {
        switch (Level)
        {
        case 0: swprintf(TextNum, GlobalText[1460]); break;
        case 1: swprintf(TextNum, GlobalText[1461]); break;
        }
        TextListColor[TextNum] = TEXT_COLOR_YELLOW;
        TextBold[TextNum] = false;
        TextNum++;
    }
    else if (ip.Type == ITEM_LIFE_STONE_ITEM)
    {
        switch (Level)
        {
        case 0: swprintf(TextNum, GlobalText[1416]); break;
        case 1: swprintf(TextNum, GlobalText[1462]); break;
        }
        TextListColor[TextNum] = TEXT_COLOR_YELLOW;
        TextBold[TextNum] = false;
        TextNum++;
    }
    else if (ip.Type == ITEM_ARMOR_OF_GUARDSMAN)
    {
        int startIndex = 0;
        if (gCharacterManager.GetBaseClass(Hero.Class) == CLASS_DARK || gCharacterManager.GetBaseClass(Hero.Class) == CLASS_DARK_LORD
            || gCharacterManager.GetBaseClass(Hero.Class) == CLASS_RAGEFIGHTER
            )
        {
            startIndex = 6;
        }

        int HeroLevel = CharacterAttribute.Level;

        TextListColor[TextNum] = TEXT_COLOR_WHITE;
        swprintf(TextNum, "%s %s    %s      %s    ", GlobalText[1147], GlobalText[368], GlobalText[935], GlobalText[936]); TextListColor[TextNum] = TEXT_COLOR_WHITE; TextBold[TextNum] = false; TextNum++;

        for (int i = 0; i < 6; i++)
        {
            int Zen = g_iChaosCastleZen[i];

            swprintf(TextNum, "        %d             %3d~%3d     %3d,000", i + 1, g_iChaosCastleLevel[startIndex + i][0], Math.Min(400, g_iChaosCastleLevel[startIndex + i][1]), Zen);
            if ((HeroLevel >= g_iChaosCastleLevel[startIndex + i][0] && HeroLevel <= g_iChaosCastleLevel[startIndex + i][1]) && gCharacterManager.IsMasterLevel(Hero.Class) == false)
            {
                TextListColor[TextNum] = TEXT_COLOR_DARKYELLOW;
            }
            else
            {
                TextListColor[TextNum] = TEXT_COLOR_WHITE;
            }
            TextBold[TextNum] = false; TextNum++;
        }
        swprintf(TextNum, "         %d          %s   %3d,000", 7, GlobalText[737], 1000);
        if (gCharacterManager.IsMasterLevel(Hero.Class) == true)
        {
            TextListColor[TextNum] = TEXT_COLOR_DARKYELLOW;
        }
        else
        {
            TextListColor[TextNum] = TEXT_COLOR_WHITE;
        }
        TextBold[TextNum] = false;
        TextNum++;

        swprintf(TextNum, "\n");
        TextNum++;
        SkipNum++;
        swprintf(TextNum, GlobalText[1157]);
        TextListColor[TextNum] = TEXT_COLOR_DARKBLUE;
        TextNum++;
    }
    else if (ip.Type == ITEM_POTION + 21)
    {
        TextListColor[TextNum] = TEXT_COLOR_WHITE;
        switch (Level)
        {
        case 1: swprintf(TextNum, GlobalText[813]); break;
        case 2: swprintf(TextNum, GlobalText[1099]); break;
        case 3: swprintf(TextNum, GlobalText[1291]); break;
        default: break;
        }
        TextNum++;
    }
    else if (ip.Type == ITEM_LOST_MAP || ip.Type == ITEM_SYMBOL_OF_KUNDUN)
    {
        TextNum = RenderHellasItemInfo(ip, TextNum);
    }
    else if (ip.Type == ITEM_CAPE_OF_FIGHTER || ip.Type == ITEM_CAPE_OF_LORD)
    {
        // 망토 관련 옵션변경
        swprintf(TextNum, GlobalText[577], 20 + Level * 2);  //  데미지 몇%증가
        TextListColor[TextNum] = TEXT_COLOR_WHITE; TextBold[TextNum] = false; TextNum++;
        int _iDamage = (ip.Type == ITEM_CAPE_OF_FIGHTER) ? 10 + Level * 2 : 10 + Level;
        swprintf(TextNum, GlobalText[578], _iDamage);  //  데미지 몇%흡수
        TextListColor[TextNum] = TEXT_COLOR_WHITE; TextBold[TextNum] = false; TextNum++;
    }

    bool bDurExist = false;
    if ((p.Durability != 0 || p.MagicDur != 0) &&
        ((ip.Type < ITEM_WING || ip.Type >= ITEM_HELPER) && ip.Type < ITEM_POTION) ||
        (ip.Type >= ITEM_WING && ip.Type <= ITEM_WINGS_OF_DARKNESS)
        || (ip.Type >= ITEM_WING_OF_STORM && ip.Type <= ITEM_WING_OF_DIMENSION)
        || (ip.Type >= ITEM_CAPE_OF_FIGHTER && ip.Type <= ITEM_CAPE_OF_OVERRULE))
    {
        bDurExist = true;
    }
    if ((bDurExist || ip.Durability != 0) &&
        (ip.Type<ITEM_LOCHS_FEATHER || ip.Type>ITEM_WEAPON_OF_ARCHANGEL)
        && !(ip.Type == ITEM_WIZARDS_RING && Level == 1)
        && !(ip.Type == ITEM_WIZARDS_RING && Level == 2)
        && !(ip.Type == ITEM_ARMOR_OF_GUARDSMAN)
        && ip.Type != ITEM_SIEGE_POTION && ip.Type != ITEM_HELPER + 7 && ip.Type != ITEM_LIFE_STONE_ITEM
        && ip.Type != ITEM_FRAGMENT_OF_HORN
        && !(ip.Type >= ITEM_POTION + 70 && ip.Type <= ITEM_POTION + 71)
        && !(ip.Type >= ITEM_HELPER + 54 && ip.Type <= ITEM_HELPER + 58)
        && !(ip.Type >= ITEM_POTION + 78 && ip.Type <= ITEM_POTION + 82)
        && !(ip.Type == ITEM_HELPER + 66)
        && !(ip.Type == ITEM_HELPER + 71 || ip.Type == ITEM_HELPER + 72 || ip.Type == ITEM_HELPER + 73 || ip.Type == ITEM_HELPER + 74 || ip.Type == ITEM_HELPER + 75)
        && !(ip.Type == ITEM_HELPER + 97)
        && !(ip.Type == ITEM_HELPER + 98)
        && !(ip.Type == ITEM_POTION + 91)
        && !(ip.Type == ITEM_HELPER + 99)
        && !(ip.Type == ITEM_POTION + 133)
        )
    {
        bool Success = false;
        bool arrow = false;
        if (ip.Type >= ITEM_POTION && ip.Type <= ITEM_ANTIDOTE)
        {
            swprintf(TextNum, GlobalText[69], ip.Durability);
            Success = true;
        }
        else if (ip.Type == ITEM_POTION + 21 && Level == 3)
        {
            swprintf(TextNum, GlobalText[69], ip.Durability);
            Success = true;
        }
        else if (ip.Type == ITEM_BOLT || ip.Type == ITEM_ARROWS)
        {
            swprintf(TextNum, GlobalText[69], ip.Durability);
            Success = true;
            arrow = true;
        }
        else if (ip.Type >= ITEM_SMALL_SHIELD_POTION && ip.Type <= ITEM_LARGE_COMPLEX_POTION)
        {
            swprintf(TextNum, GlobalText[69], ip.Durability);
            Success = true;
        }
        else if (ip.Type == ITEM_POTION + 133)
        {
            swprintf(TextNum, GlobalText[69], ip.Durability);
            Success = true;
        }
        else if (ip.Type >= ITEM_JACK_OLANTERN_BLESSINGS && ip.Type <= ITEM_JACK_OLANTERN_DRINK)
        {
            swprintf(TextNum, GlobalText[69], ip.Durability);
            Success = true;
        }
        else if (ip.Type >= ITEM_POTION + 153 && ip.Type <= ITEM_POTION + 156)
        {
            swprintf(TextNum, GlobalText[69], ip.Durability);
            Success = true;
        }
        else if (ip.Type >= ITEM_SPLINTER_OF_ARMOR && ip.Type <= ITEM_BLESS_OF_GUARDIAN)
        {
            swprintf(TextNum, GlobalText[1181], ip.Durability, 20);
            Success = true;
        }
        else if (ip.Type == ITEM_CLAW_OF_BEAST)
        {
            swprintf(TextNum, GlobalText[1181], ip.Durability, 10);
            Success = true;
        }
        else if (ip.Type == ITEM_HORN_OF_FENRIR)
        {
            if (ip.bPeriodItem == false)
            {
                swprintf(TextNum, GlobalText[70], ip.Durability);
                Success = true;
            }
        }
        else if (ip.Type >= ITEM_HELPER && ip.Type <= ITEM_HELPER + 7)
        {
            if (ip.bPeriodItem == false)
            {
                swprintf(TextNum, GlobalText[70], ip.Durability);
                Success = true;
            }
        }
        else if (ip.Type == ITEM_TRANSFORMATION_RING)
        {
            swprintf(TextNum, GlobalText[95], ip.Durability);
            Success = true;
        }
        else if (ip.Type == ITEM_DEMON || ip.Type == ITEM_SPIRIT_OF_GUARDIAN)
        {
            if (ip.bPeriodItem == false)
            {
                swprintf(TextNum, GlobalText[70], ip.Durability);
                Success = true;
            }
        }
        else if (ip.Type == ITEM_PET_RUDOLF || ip.Type == ITEM_PET_PANDA
            || ip.Type == ITEM_PET_UNICORN || ip.Type == ITEM_PET_SKELETON)
        {
            if (ip.bPeriodItem == false)
            {
                swprintf(TextNum, GlobalText[70], ip.Durability);
                Success = true;
            }
        }
        else if (ip.Type >= ITEM_HELPER + 46 && ip.Type <= ITEM_HELPER + 48)
        {
            swprintf(TextNum, GlobalText[2260], ip.Durability);
            Success = true;
        }
        else if (ip.Type >= ITEM_HELPER + 125 && ip.Type <= ITEM_HELPER + 127)
        {
            swprintf(TextNum, GlobalText[2260], ip.Durability);

            if (ip.Type == ITEM_HELPER + 126)
            {
                TextNum++;
                swprintf(TextNum, GlobalText[3105]);
            }
            else if (ip.Type == ITEM_HELPER + 127)
            {
                TextNum++;
                swprintf(TextNum, GlobalText[3106]);
            }
            Success = true;
        }
        else if (ip.Type == ITEM_POTION + 53)
        {
            swprintf(TextNum, GlobalText[2296], ip.Durability);
            Success = true;
        }
        else if (ip.Type == ITEM_HELPER + 61)
        {
            swprintf(TextNum, GlobalText[2260], ip.Durability);
            Success = true;
        }
        else if (ip.Type == ITEM_POTION + 100)
        {
            swprintf(TextNum, GlobalText[69], ip.Durability);
            Success = true;
        }
        else if (ip.Type == ITEM_HELPER + 70)
        {
            if (ip.Durability == 2)
            {
                swprintf(TextNum, GlobalText[2605]);
                TextListColor[TextNum] = TEXT_COLOR_BLUE;
                TextBold[TextNum] = false;
                TextNum++;
            }
            else if (ip.Durability == 1)
            {
                swprintf(TextNum, GlobalText[2606]);
                TextListColor[TextNum] = TEXT_COLOR_BLUE;
                TextBold[TextNum] = false;
                TextNum++;
                TextList[TextNum] = GetPortalPositionText();
                TextListColor[TextNum] = TEXT_COLOR_BLUE;
                TextBold[TextNum] = false;
                TextNum++;
            }
        }
        else if (ip.Type >= ITEM_HELPER + 135 && ip.Type <= ITEM_HELPER + 145)
        {
            swprintf(TextNum, GlobalText[2261]);
            TextListColor[TextNum] = TEXT_COLOR_BLUE;
            TextBold[TextNum] = false;
            TextNum++;
            swprintf(TextNum, "\n"); TextNum++; SkipNum++;
            Success = true;
        }
        else if ((bDurExist) && (ip.bPeriodItem == false))
        {
            int maxDurability = CalcMaxDurability(ip, p, Level);

            swprintf(TextNum, GlobalText[71], ip.Durability, maxDurability);
            Success = true;
        }
        else if (ip.Type >= ITEM_TYPE_CHARM_MIXWING + EWS_BEGIN && ip.Type <= ITEM_TYPE_CHARM_MIXWING + EWS_END)
        {
            swprintf(TextNum, GlobalText[2732 + (ip.Type - (ITEM_TYPE_CHARM_MIXWING + EWS_BEGIN))]);

            Success = true;
        }
        else if (ip.Type == ITEM_HELPER + 121)
        {
            swprintf(TextNum, GlobalText[2260], ip.Durability);
            Success = true;
        }

        if (Success)
        {
            TextListColor[TextNum] = TEXT_COLOR_WHITE;
            TextBold[TextNum] = false;
            TextNum++;
        }
    }
    else
    {
        if (ip.Type == ITEM_TRANSFORMATION_RING)
        {
            swprintf(TextNum, GlobalText[95], ip.Durability);

            TextListColor[TextNum] = TEXT_COLOR_WHITE;
            TextBold[TextNum] = false;
            TextNum++;
        }
    }

    if (ip.Type == ITEM_BOLT || ip.Type == ITEM_ARROWS)
    {
        if (Level >= 1)
        {
            int value = Level * 2 + 1;

            swprintf(TextNum, GlobalText[577], value);
            TextListColor[TextNum] = TEXT_COLOR_BLUE;
            TextBold[TextNum] = false;
            TextNum++;
            swprintf(TextNum, GlobalText[88], 1);
            TextListColor[TextNum] = TEXT_COLOR_BLUE;
            TextBold[TextNum] = false;
            TextNum++;
        }
    }

    for (int i = 0; i < MAX_RESISTANCE; i++)
    {
        if (p.Resistance[i] != 0)
        {
            swprintf(TextNum, GlobalText[72], GlobalText[48 + i], Level + 1);
            TextListColor[TextNum] = TEXT_COLOR_WHITE;
            TextBold[TextNum] = false;
            TextNum++;
        }
    }

    if (ip.RequireLevel != 0 && ip.Type != ITEM_LOCHS_FEATHER)
    {
        swprintf(TextNum, GlobalText[76], ip.RequireLevel);
        if (CharacterAttribute.Level < ip.RequireLevel)
        {
            TextListColor[TextNum] = TEXT_COLOR_RED;
            TextBold[TextNum] = false;
            TextNum++;
            swprintf(TextNum, GlobalText[74], ip.RequireLevel - CharacterAttribute.Level);
            TextListColor[TextNum] = TEXT_COLOR_RED;
            TextBold[TextNum] = false;
            TextNum++;
        }
        else
        {
            TextListColor[TextNum] = TEXT_COLOR_WHITE;
            TextBold[TextNum] = false;
            TextNum++;
        }
    }

    int si_iNeedStrength = 0, si_iNeedDex = 0;
    bool bRequireStat = true;

    if (Check_LuckyItem(ip.Type)) bRequireStat = false;

    if (Level >= ip.Jewel_Of_Harmony_OptionLevel)
    {
        var SC = new StrengthenCapability();
        g_pUIJewelHarmonyinfo.GetStrengthenCapability(SC, ip, 0);

        if (SC.SI_isNB)
        {
            si_iNeedStrength = SC.SI_NB.SI_force;
            si_iNeedDex = SC.SI_NB.SI_activity;
        }
    }

    if (ip.SocketCount > 0)
    {
        for (int i = 0; i < ip.SocketCount; ++i)
        {
            if (ip.SocketSeedID[i] == 38)
            {
                int iReqStrengthDown = GetSocketOptionValue(ip, i);
                si_iNeedStrength += iReqStrengthDown;
            }
            else if (ip.SocketSeedID[i] == 39)
            {
                int iReqDexterityDown = GetSocketOptionValue(ip, i);
                si_iNeedDex += iReqDexterityDown;
            }
        }
    }

    if (ip.RequireStrength != 0 && bRequireStat)
    {
        swprintf(TextNum, GlobalText[73], ip.RequireStrength - si_iNeedStrength);

        int Strength;
        Strength = CharacterAttribute.Strength + CharacterAttribute.AddStrength;
        if (Strength < ip.RequireStrength - si_iNeedStrength)
        {
            TextListColor[TextNum] = TEXT_COLOR_RED;
            TextBold[TextNum] = false;
            TextNum++;
            swprintf(TextNum, GlobalText[74], (ip.RequireStrength - Strength) - si_iNeedStrength);
            TextListColor[TextNum] = TEXT_COLOR_RED;
            TextBold[TextNum] = false;
            TextNum++;
        }
        else
        {
            if (si_iNeedStrength != 0)
            {
                TextListColor[TextNum] = TEXT_COLOR_YELLOW;
            }
            else
            {
                TextListColor[TextNum] = TEXT_COLOR_WHITE;
            }

            TextBold[TextNum] = false;
            TextNum++;
        }
    }
    if (ip.RequireDexterity != 0 && bRequireStat)
    {
        swprintf(TextNum, GlobalText[75], ip.RequireDexterity - si_iNeedDex);
        int Dexterity;
        Dexterity = CharacterAttribute.Dexterity + CharacterAttribute.AddDexterity;
        if (Dexterity < (ip.RequireDexterity - si_iNeedDex))
        {
            TextListColor[TextNum] = TEXT_COLOR_RED;
            TextBold[TextNum] = false;
            TextNum++;

            swprintf(TextNum, GlobalText[74], (ip.RequireDexterity - Dexterity) - si_iNeedDex);
            TextListColor[TextNum] = TEXT_COLOR_RED;
            TextBold[TextNum] = false;
            TextNum++;
        }
        else
        {
            if (si_iNeedDex != 0)
            {
                TextListColor[TextNum] = TEXT_COLOR_YELLOW;
            }
            else
            {
                TextListColor[TextNum] = TEXT_COLOR_WHITE;
            }
            TextBold[TextNum] = false;
            TextNum++;
        }
    }

    if (ip.RequireVitality != 0 && bRequireStat) //  요구체력.
    {
        swprintf(TextNum, GlobalText[1930], ip.RequireVitality);

        int Vitality;
        Vitality = CharacterAttribute.Vitality + CharacterAttribute.AddVitality;
        if (Vitality < ip.RequireVitality)
        {
            TextListColor[TextNum] = TEXT_COLOR_RED;
            TextBold[TextNum] = false;
            TextNum++;
            swprintf(TextNum, GlobalText[74], ip.RequireVitality - Vitality);
            TextListColor[TextNum] = TEXT_COLOR_RED;
            TextBold[TextNum] = false;
            TextNum++;
        }
        else
        {
            TextListColor[TextNum] = TEXT_COLOR_WHITE;
            TextBold[TextNum] = false;
            TextNum++;
        }
    }

    if (ip.RequireEnergy != 0 && bRequireStat)
    {
        swprintf(TextNum, GlobalText[77], ip.RequireEnergy);

        int Energy;
        Energy = CharacterAttribute.Energy + CharacterAttribute.AddEnergy;

        if (Energy < ip.RequireEnergy)
        {
            TextListColor[TextNum] = TEXT_COLOR_RED;
            TextBold[TextNum] = false;
            TextNum++;
            swprintf(TextNum, GlobalText[74], ip.RequireEnergy - Energy);
            TextListColor[TextNum] = TEXT_COLOR_RED;
            TextBold[TextNum] = false;
            TextNum++;
        }
        else
        {
            TextListColor[TextNum] = TEXT_COLOR_WHITE;
            TextBold[TextNum] = false;
            TextNum++;
        }
    }

    if (ip.RequireCharisma != 0 && bRequireStat)
    {
        swprintf(TextNum, GlobalText[698], ip.RequireCharisma);

        int Charisma;
        Charisma = CharacterAttribute.Charisma + CharacterAttribute.AddCharisma;
        if (Charisma < ip.RequireCharisma)
        {
            TextListColor[TextNum] = TEXT_COLOR_RED;
            TextBold[TextNum] = false;
            TextNum++;
            swprintf(TextNum, GlobalText[74], ip.RequireCharisma - Charisma);
            TextListColor[TextNum] = TEXT_COLOR_RED;
            TextBold[TextNum] = false;
            TextNum++;
        }
        else
        {
            TextListColor[TextNum] = TEXT_COLOR_WHITE;
            TextBold[TextNum] = false;
            TextNum++;
        }
    }

    if (IsRequireClassRenderItem(ip.Type))
    {
        RequireClass(p);
    }

    if (ip.Type >= ITEM_BOOTS && ip.Type < ITEM_BOOTS + MAX_ITEM_INDEX)
    {
        if (Level >= 5)
        {
            swprintf(TextNum, "\n"); TextNum++; SkipNum++;
            swprintf(TextNum, GlobalText[78]);
            TextListColor[TextNum] = TEXT_COLOR_BLUE; TextBold[TextNum] = true; TextNum++;
        }
    }

    if (ip.Type >= ITEM_GLOVES && ip.Type < ITEM_GLOVES + MAX_ITEM_INDEX)
    {
        if (Level >= 5)
        {
            swprintf(TextNum, "\n"); TextNum++; SkipNum++;

            swprintf(TextNum, GlobalText[93]);
            TextListColor[TextNum] = TEXT_COLOR_BLUE; TextBold[TextNum] = true; TextNum++;
        }
    }
    if ((ip.Type >= ITEM_STAFF && ip.Type < ITEM_STAFF + MAX_ITEM_INDEX)
        || (ip.Type == (ITEM_RUNE_BLADE))
        || (ip.Type == (ITEM_EXPLOSION_BLADE))
        || (ip.Type == (ITEM_SWORD_DANCER))
        || (ip.Type == (ITEM_DARK_REIGN_BLADE))
        || (ip.Type == (ITEM_IMPERIAL_SWORD))
        )
    {
        swprintf(TextNum, "\n"); TextNum++; SkipNum++;

        int nText = ITEM_BOOK_OF_SAHAMUTT <= ip.Type && ip.Type <= ITEM_STAFF + 29 ? 1691 : 79;
        swprintf(TextNum, GlobalText[nText], ip.MagicPower);

        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = true;
        TextNum++;
    }

    if (IsCepterItem(ip.Type) == true)
    {
        swprintf(TextNum, "\n"); TextNum++; SkipNum++;
        swprintf(TextNum, GlobalText[1234], ip.MagicPower);
        TextListColor[TextNum] = TEXT_COLOR_BLUE; TextBold[TextNum] = true; TextNum++;
    }

    if (ip.SpecialNum > 0)
    {
        int iModelType = ip.Type;
        int iStartModelType = ITEM_HELPER + 109;
        int iEndModelType = ITEM_HELPER + 115;

        if (!(ITEM_HELPER + 109 <= ip.Type && ITEM_HELPER + 115 >= ip.Type))
        {
            swprintf(TextNum, "\n"); TextNum++; SkipNum++;
        }
    }

    if (ip.option_380)
    {
        var Text380 = new List<string>();

        if (g_pItemAddOptioninfo != null)
        {
            g_pItemAddOptioninfo.GetItemAddOtioninfoText(Text380, ip.Type);

            swprintf(TextNum, "\n"); TextNum++; SkipNum++;

            for (int i = 0; i < Text380.Count; ++i)
            {
                TextList[TextNum] = Text380[i];
                TextListColor[TextNum] = TEXT_COLOR_REDPURPLE; TextBold[TextNum] = true; TextNum++;
            }

            swprintf(TextNum, "\n"); TextNum++; SkipNum++;
        }
    }
    //#ifndef PBG_MOD_NEWCHAR_MONK_WING_2
    //#ifdef PBG_MOD_NEWCHAR_MONK_WING
    //	if(ip.Type==ITEM_WING+49)
    //	{
    //		swprintf(TextNum, GlobalText[578],15+Level);
    //		TextListColor[TextNum] = TEXT_COLOR_BLUE;
    //		TextNum++;
    //	}
    //#endif //PBG_MOD_NEWCHAR_MONK_WING
    //#endif //PBG_MOD_NEWCHAR_MONK_WING_2
    if (IsSocketItem(ip)) { }
    else
        if (ip.Jewel_Of_Harmony_Option != 0)
        {
            StrengthenItem type = g_pUIJewelHarmonyinfo.GetItemType(ip.Type);

            if (type < StrengthenItem.SI_None)
            {
                if (g_pUIJewelHarmonyinfo.IsHarmonyJewelOption(type, ip.Jewel_Of_Harmony_Option))
                {
                    swprintf(TextNum, "\n"); TextNum++; SkipNum++;

                    var harmonyjewel = g_pUIJewelHarmonyinfo.GetHarmonyJewelOptionInfo(type, ip.Jewel_Of_Harmony_Option);

                    if (type == StrengthenItem.SI_Defense && ip.Jewel_Of_Harmony_Option == 7)
                    {
                        swprintf(TextNum, "%s +%d%%", harmonyjewel.Name, harmonyjewel.HarmonyJewelLevel[ip.Jewel_Of_Harmony_OptionLevel]);
                    }
                    else
                    {
                        swprintf(TextNum, "%s +%d", harmonyjewel.Name, harmonyjewel.HarmonyJewelLevel[ip.Jewel_Of_Harmony_OptionLevel]);
                    }

                    if (Level >= ip.Jewel_Of_Harmony_OptionLevel) TextListColor[TextNum] = TEXT_COLOR_YELLOW;
                    else TextListColor[TextNum] = TEXT_COLOR_GRAY;

                    TextBold[TextNum] = true; TextNum++;

                    swprintf(TextNum, "\n"); TextNum++; SkipNum++;
                }
                else
                {
                    swprintf(TextNum, "\n"); TextNum++; SkipNum++;

                    swprintf(TextNum, "%s : %d %d %d"
                        , GlobalText[2204]
                        , (int)type
                        , (int)ip.Jewel_Of_Harmony_Option
                        , (int)ip.Jewel_Of_Harmony_OptionLevel
                    );

                    TextListColor[TextNum] = TEXT_COLOR_DARKRED;
                    TextBold[TextNum] = true; TextNum++;

                    swprintf(TextNum, GlobalText[2205]);

                    TextListColor[TextNum] = TEXT_COLOR_DARKRED;
                    TextBold[TextNum] = true; TextNum++;

                    swprintf(TextNum, "\n"); TextNum++; SkipNum++;
                }
            }
        }

    TextNum = g_csItemOption.RenderDefaultOptionText(ip, TextNum);

    for (int i = 0; i < ip.SpecialNum; i++)
    {
        if (ITEM_HELPER + 109 <= ip.Type && ITEM_HELPER + 115 >= ip.Type)
        {
            break;
        }

        iMana = SkillManaCost(ip.Special[i]);
        TextList[TextNum] = GetSpecialOptionText(ip.Type, ip.Special[i], ip.SpecialValue[i], iMana);

        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false; TextNum++;

        if (ip.Special[i] == AT_LUCK)
        {
            swprintf(TextNum, GlobalText[94], ip.SpecialValue[i]);
            TextListColor[TextNum] = TEXT_COLOR_BLUE; TextBold[TextNum] = false; TextNum++;
        }
        else if (ip.Special[i] == AT_SKILL_RIDER)
        {
            swprintf(TextNum, GlobalText[179]);
            TextListColor[TextNum] = TEXT_COLOR_DARKRED; TextBold[TextNum] = false; TextNum++;
        }
        else if (ip.Special[i] == AT_SKILL_DARK_HORSE || (AT_SKILL_ASHAKE_UP <= ip.Special[i] && ip.Special[i] <= AT_SKILL_ASHAKE_UP + 4))
        {
            swprintf(TextNum, GlobalText[1201]);
            TextListColor[TextNum] = TEXT_COLOR_DARKRED; TextBold[TextNum] = false; TextNum++;
        }
        else if ((ip.Special[i] == AT_IMPROVE_DAMAGE) &&
            ((ip.Type == ITEM_RUNE_BLADE)
                || (ip.Type == ITEM_DARK_REIGN_BLADE)
                || (ip.Type == ITEM_EXPLOSION_BLADE)
                || (ip.Type == ITEM_SWORD_DANCER)
                || (ip.Type == ITEM_IMPERIAL_SWORD)
                )
            )
        {
            swprintf(TextNum, GlobalText[89], ip.SpecialValue[i]);
            TextListColor[TextNum] = TEXT_COLOR_BLUE;
            TextBold[TextNum] = false; TextNum++;
        }
    }

    swprintf(TextNum, "\n"); TextNum++; SkipNum++;

    if (ip.Type == ITEM_SPLINTER_OF_ARMOR || ip.Type == ITEM_BLESS_OF_GUARDIAN)
    {
        swprintf(TextNum, GlobalText[1917]);
        TextListColor[TextNum] = TEXT_COLOR_YELLOW;
        TextNum++;
    }
    else if (ip.Type == ITEM_CLAW_OF_BEAST || ip.Type == ITEM_FRAGMENT_OF_HORN)
    {
        swprintf(TextNum, GlobalText[1918]);
        TextListColor[TextNum] = TEXT_COLOR_YELLOW;
        TextNum++;
    }
    else if (ip.Type == ITEM_BROKEN_HORN)
    {
        swprintf(TextNum, GlobalText[1919]);
        TextListColor[TextNum] = TEXT_COLOR_YELLOW;
        TextNum++;
    }
    else if (ip.Type == ITEM_HORN_OF_FENRIR)
    {
        TextList[TextNum] = GetSpecialOptionText(0, AT_SKILL_PLASMA_STORM_FENRIR, 0, 0);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false; TextNum++; SkipNum++;
        if (ip.ExcellentFlags == 0x01)
        {
            swprintf(TextNum, GlobalText[1860], 10);
            TextListColor[TextNum] = TEXT_COLOR_BLUE;
            TextNum++;

            swprintf(TextNum, GlobalText[579]);
            TextListColor[TextNum] = TEXT_COLOR_BLUE;
            TextNum++;
        }
        else if (ip.ExcellentFlags == 0x02)
        {
            swprintf(TextNum, GlobalText[1861], 10);
            TextListColor[TextNum] = TEXT_COLOR_BLUE;
            TextNum++;

            swprintf(TextNum, GlobalText[579]);
            TextListColor[TextNum] = TEXT_COLOR_BLUE;
            TextNum++;
        }
        else if (ip.ExcellentFlags == 0x04)
        {
            int wLevel = CharacterAttribute.Level;

            swprintf(TextNum, GlobalText[1867], (wLevel / 2));
            TextListColor[TextNum] = TEXT_COLOR_BLUE;
            TextNum++;
            swprintf(TextNum, GlobalText[1868], (wLevel / 2));
            TextListColor[TextNum] = TEXT_COLOR_BLUE;
            TextNum++;
            swprintf(TextNum, GlobalText[1869], (wLevel / 12));
            TextListColor[TextNum] = TEXT_COLOR_BLUE;
            TextNum++;
            swprintf(TextNum, GlobalText[1870], (wLevel / 25));
            TextListColor[TextNum] = TEXT_COLOR_BLUE;
            TextNum++;
            swprintf(TextNum, "\n");
            TextNum++;
            swprintf(TextNum, GlobalText[1871], (Hero.Level / 2));
            TextListColor[TextNum] = TEXT_COLOR_GREEN;
            TextNum++;
            swprintf(TextNum, GlobalText[1872], (Hero.Level / 2));
            TextListColor[TextNum] = TEXT_COLOR_GREEN;
            TextNum++;
        }

        swprintf(TextNum, "\n"); TextNum++; SkipNum++;

        swprintf(TextNum, GlobalText[1920]);
        TextListColor[TextNum] = TEXT_COLOR_YELLOW;
        TextNum++;

        if (ip.ExcellentFlags == 0x00)
        {
            swprintf(TextNum, GlobalText[1929]);
            TextListColor[TextNum] = TEXT_COLOR_YELLOW;
            TextNum++;
        }
    }
    else if (ip.Type == ITEM_TRANSFORMATION_RING)
    {
        swprintf(TextNum, GlobalText[3088]);
        TextListColor[TextNum] = TEXT_COLOR_RED;
        TextBold[TextNum] = false;
        TextNum++;
    }
    else if (ip.Type == ITEM_ELITE_TRANSFER_SKELETON_RING)
    {
        string strText = "";
        strText = Fmt(GlobalText[959], 10);
        swprintf(TextNum, "%s%%", strText);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextNum++;

        int wlevel = CharacterAttribute.Level;
        swprintf(TextNum, GlobalText[2225], wlevel);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextNum++;

        swprintf(TextNum, "\n"); TextNum++; SkipNum++;

        swprintf(TextNum, GlobalText[3088]);
        TextListColor[TextNum] = TEXT_COLOR_RED;
        TextBold[TextNum] = false;
        TextNum++;
    }
    else if (ip.Type == ITEM_FIRECRACKER)
    {
        TextListColor[TextNum] = TEXT_COLOR_WHITE;
        swprintf(TextNum, GlobalText[2244]); TextNum++;
    }
    else if (ip.Type == ITEM_GM_GIFT)
    {
        TextListColor[TextNum] = TEXT_COLOR_WHITE;
        swprintf(TextNum, GlobalText[2323]); TextNum++;
        TextListColor[TextNum] = TEXT_COLOR_WHITE;
        swprintf(TextNum, GlobalText[2011]); TextNum++;
    }
    else if (ip.Type == ITEM_JACK_OLANTERN_TRANSFORMATION_RING)
    {
        swprintf(TextNum, "%s", GlobalText[2232]);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;

        swprintf(TextNum, GlobalText[3088]);
        TextListColor[TextNum] = TEXT_COLOR_RED;
        TextBold[TextNum] = false;
        TextNum++;
        TextNum++;

        swprintf(TextNum, "\n"); TextNum++; SkipNum++;

        swprintf(TextNum, GlobalText[3088]);
        TextListColor[TextNum] = TEXT_COLOR_RED;
        TextBold[TextNum] = false;
        TextNum++;
    }
    else if (ip.Type == ITEM_CHRISTMAS_TRANSFORMATION_RING)
    {
        swprintf(TextNum, GlobalText[88], 20);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextNum++;
        swprintf(TextNum, GlobalText[89], 20);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextNum++;
        swprintf(TextNum, "\n"); TextNum++; SkipNum++;
        swprintf(TextNum, "%s", GlobalText[2248]);
        TextListColor[TextNum] = TEXT_COLOR_RED;
        TextNum++;
        swprintf(TextNum, "\n"); TextNum++; SkipNum++;
        swprintf(TextNum, GlobalText[3088]);
        TextListColor[TextNum] = TEXT_COLOR_RED;
        TextBold[TextNum] = false;
        TextNum++;
    }
    else if (ip.Type == ITEM_PANDA_TRANSFORMATION_RING)
    {
        swprintf(TextNum, GlobalText[2743]);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;
        swprintf(TextNum, GlobalText[2744]);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;
        swprintf(TextNum, GlobalText[2745]);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;
        swprintf(TextNum, "\n"); TextNum++; SkipNum++;
        swprintf(TextNum, GlobalText[3088]);
        TextListColor[TextNum] = TEXT_COLOR_RED;
        TextBold[TextNum] = false;
        TextNum++;
    }
    else if (ip.Type == ITEM_SKELETON_TRANSFORMATION_RING)
    {
        swprintf(TextNum, GlobalText[3065]);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;
        swprintf(TextNum, GlobalText[3066]);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;
        swprintf(TextNum, "\n"); TextNum++; SkipNum++;
        swprintf(TextNum, GlobalText[3067]);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;
        swprintf(TextNum, GlobalText[3072]);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;
        swprintf(TextNum, "\n"); TextNum++; SkipNum++;
        swprintf(TextNum, GlobalText[3088]);
        TextListColor[TextNum] = TEXT_COLOR_RED;
        TextBold[TextNum] = false;
        TextNum++;
    }
    else if (ip.Type == ITEM_CHRISTMAS_STAR)
    {
        swprintf(TextNum, "%s", GlobalText[2244]);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextNum++;
    }
    else if (ip.Type == ITEM_GAME_MASTER_TRANSFORMATION_RING)
    {
        for (int i = 0; i < 7; ++i)
        {
            swprintf(TextNum, GlobalText[976 + i], 255);
            TextListColor[TextNum] = TEXT_COLOR_BLUE;
            TextNum++;
        }

        swprintf(TextNum, "\n"); TextNum++; SkipNum++;
        swprintf(TextNum, GlobalText[3088]);
        TextListColor[TextNum] = TEXT_COLOR_RED;
        TextBold[TextNum] = false;
        TextNum++;
    }
    else if (ip.Type == ITEM_HELPER + 66)
    {
        TextNum--;
        swprintf(TextNum, GlobalText[2260], ip.Durability);
        TextListColor[TextNum] = TEXT_COLOR_RED;
        TextNum++;
        swprintf(TextNum, "%s", GlobalText[2589]);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextNum++;
    }
    else if (ip.Type == ITEM_POTION + 100)
    {
        swprintf(TextNum, GlobalText[1887], ip.Durability);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextNum++;
    }
    else if (ip.Type == ITEM_PET_SKELETON)
    {
        TextNum--; SkipNum--;

        swprintf(TextNum, GlobalText[3071]);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;

        swprintf(TextNum, GlobalText[3072]);
        TextListColor[TextNum] = TEXT_COLOR_BLUE;
        TextBold[TextNum] = false;
        TextNum++;
    }


























































































































    else if (ip.Type == ITEM_SNOWMAN_TRANSFORMATION_RING)
    {
        swprintf(TextNum, GlobalText[3088]);
        TextListColor[TextNum] = TEXT_COLOR_RED;
        TextBold[TextNum] = false;
        TextNum++;
    }

    if (ip.bPeriodItem == true)
    {
        if (ip.bExpiredPeriod == true)
        {
            swprintf(TextNum, GlobalText[3266]);
            TextListColor[TextNum] = TEXT_COLOR_RED;
        }
        else
        {
            swprintf(TextNum, GlobalText[3265]);
            TextListColor[TextNum] = TEXT_COLOR_ORANGE;
            TextNum++;
            SkipNum++;

                swprintf(TextNum, "%s", "");

            TextListColor[TextNum] = TEXT_COLOR_BLUE;
        }

        TextNum++;
    }

    if (!bItemTextListBoxUse)
    {
        bool bThisisEquippedItem = false;

        bThisisEquippedItem = Context.IsEquipped;



        TextNum = g_csItemOption.RenderSetOptionListInItem(ip, TextNum, bThisisEquippedItem);

        TextNum = AttachToolTipForSocketItem(ip, TextNum);
    }
    }

    // ------------------------------------------------------------------ helpers translated from the C++ source

    private static bool IsRequireClassRenderItem(int sType)
{
    if (
        sType == ITEM_WEAPON_OF_ARCHANGEL
        || sType == ITEM_ARMOR_OF_GUARDSMAN
        || sType == ITEM_WING + 26
        || (sType >= ITEM_PACKED_JEWEL_OF_BLESS && sType <= ITEM_PACKED_JEWEL_OF_SOUL)
        || (sType >= ITEM_HELPER + 43 && sType <= ITEM_HELPER + 45)
        || sType == ITEM_TRANSFORMATION_RING
        || (sType >= ITEM_ELITE_TRANSFER_SKELETON_RING && sType <= ITEM_GAME_MASTER_TRANSFORMATION_RING)
        || sType == ITEM_HORN_OF_FENRIR
        || sType == ITEM_JEWEL_OF_CHAOS
        || sType == ITEM_RED_RIBBON_BOX
        || sType == ITEM_GREEN_RIBBON_BOX
        || sType == ITEM_BLUE_RIBBON_BOX
        )
    {
        return false;
    }

    if ((sType >= ITEM_HELPER + 43 && sType <= ITEM_HELPER + 45)
        || (sType >= ITEM_HELPER + 46 && sType <= ITEM_HELPER + 48)
        || (sType >= ITEM_HELPER + 125 && sType <= ITEM_HELPER + 127)
        || (sType == ITEM_POTION + 54)
        || (sType >= ITEM_POTION + 58 && sType <= ITEM_POTION + 62)
        || (sType == ITEM_POTION + 53)
        || (sType >= ITEM_POTION + 70 && sType <= ITEM_POTION + 71)
        || (sType >= ITEM_POTION + 72 && sType <= ITEM_POTION + 77)
        || (sType >= ITEM_TYPE_CHARM_MIXWING + EWS_BEGIN
            && sType <= ITEM_TYPE_CHARM_MIXWING + EWS_END)
        || (sType == ITEM_HELPER + 59)
        || (sType >= ITEM_HELPER + 54 && sType <= ITEM_HELPER + 58)
        || (sType >= ITEM_POTION + 78 && sType <= ITEM_POTION + 82)
        || (sType == ITEM_HELPER + 60)
        || (sType == ITEM_HELPER + 61)
        || (sType == ITEM_POTION + 91)
        || (sType == ITEM_POTION + 94)
        || (sType >= ITEM_POTION + 92 && sType <= ITEM_POTION + 93)
        || (sType == ITEM_POTION + 95)
        || (sType >= ITEM_HELPER + 62 && sType <= ITEM_HELPER + 63)
        || (sType >= ITEM_POTION + 97 && sType <= ITEM_POTION + 98)
        || (sType == ITEM_POTION + 96)
        || (sType == ITEM_DEMON || sType == ITEM_SPIRIT_OF_GUARDIAN)
        || (sType == ITEM_PET_RUDOLF)
        || (sType == ITEM_SNOWMAN_TRANSFORMATION_RING)
        || (sType == ITEM_PANDA_TRANSFORMATION_RING)
        || (sType == ITEM_SKELETON_TRANSFORMATION_RING)
        || (sType == ITEM_HELPER + 69)
        || (sType == ITEM_HELPER + 70)
        || (sType == ITEM_HELPER + 71 || sType == ITEM_HELPER + 72 || sType == ITEM_HELPER + 73 || sType == ITEM_HELPER + 74 || sType == ITEM_HELPER + 75)
        || (sType == ITEM_PET_PANDA)
        || (sType == ITEM_PET_UNICORN)
        || sType == ITEM_HELPER + 81
        || sType == ITEM_HELPER + 82
        || sType == ITEM_HELPER + 93
        || sType == ITEM_HELPER + 94
        || sType == ITEM_HELPER + 121
        || (sType >= ITEM_POTION + 145 && sType <= ITEM_POTION + 150)
        || (sType == ITEM_POTION + 133)
        )
    {
        return false;
    }

    return true;
}

    private void RequireClass(SourceItemAttribute pItem)
{
    if (pItem == null)
        return;

    int byFirstClass = gCharacterManager.GetBaseClass(Hero.Class);
    int byStepClass = gCharacterManager.GetStepClass(Hero.Class);

    int iTextColor = 0;

    TextListColor[TextNum + 2] = TextListColor[TextNum + 3] = TEXT_COLOR_WHITE;
    swprintf(TextNum, "\n"); TextNum++; SkipNum++;

    int iCount = 0;
    for (int i = 0; i < MAX_CLASS; ++i)
    {
        if (pItem.RequireClass[i] == 1)
        {
            iCount++;
        }
    }
    if (iCount == MAX_CLASS)
        return;

    for (int i = 0; i < MAX_CLASS; ++i)
    {
        int byRequireClass = pItem.RequireClass[i];

        if (byRequireClass == 0)
            continue;

        if (i == byFirstClass && byRequireClass <= byStepClass)
        {
            iTextColor = TEXT_COLOR_WHITE;
        }
        else
        {
            iTextColor = TEXT_COLOR_DARKRED;
        }

        switch (i)
        {
        case CLASS_WIZARD:
        {
            if (byRequireClass == 1)
            {
                swprintf(TextNum, GlobalText[61], GlobalText[20]);
                TextListColor[TextNum] = iTextColor;
            }
            else if (byRequireClass == 2)
            {
                swprintf(TextNum, GlobalText[61], GlobalText[25]);
                TextListColor[TextNum] = iTextColor;
            }
            else if (byRequireClass == 3)
            {
                swprintf(TextNum, GlobalText[61], GlobalText[1669]);
                TextListColor[TextNum] = iTextColor;
            }

            TextBold[TextNum] = false;	TextNum++;
        }
        break;
        case CLASS_KNIGHT:
        {
            if (byRequireClass == 1)
            {
                swprintf(TextNum, GlobalText[61], GlobalText[21]);
                TextListColor[TextNum] = iTextColor;
            }
            else if (byRequireClass == 2)
            {
                swprintf(TextNum, GlobalText[61], GlobalText[26]);
                TextListColor[TextNum] = iTextColor;
            }
            else if (byRequireClass == 3)
            {
                swprintf(TextNum, GlobalText[61], GlobalText[1668]);
                TextListColor[TextNum] = iTextColor;
            }

            TextBold[TextNum] = false;	TextNum++;
        }
        break;
        case CLASS_ELF:
        {
            if (byRequireClass == 1)
            {
                swprintf(TextNum, GlobalText[61], GlobalText[22]);
                TextListColor[TextNum] = iTextColor;
            }
            else if (byRequireClass == 2)
            {
                swprintf(TextNum, GlobalText[61], GlobalText[27]);
                TextListColor[TextNum] = iTextColor;
            }
            else if (byRequireClass == 3)
            {
                swprintf(TextNum, GlobalText[61], GlobalText[1670]);
                TextListColor[TextNum] = iTextColor;
            }

            TextBold[TextNum] = false;	TextNum++;
        }
        break;
        case CLASS_DARK:
        {
            if (byRequireClass == 1)
            {
                swprintf(TextNum, GlobalText[61], GlobalText[23]);
                TextListColor[TextNum] = iTextColor;
            }
            else if (byRequireClass == 3)
            {
                swprintf(TextNum, GlobalText[61], GlobalText[1671]);
                TextListColor[TextNum] = iTextColor;
            }

            TextBold[TextNum] = false;	TextNum++;
        }
        break;
        case CLASS_DARK_LORD:
        {
            if (byRequireClass == 1)
            {
                swprintf(TextNum, GlobalText[61], GlobalText[24]);
                TextListColor[TextNum] = iTextColor;
            }
            else if (byRequireClass == 3)
            {
                swprintf(TextNum, GlobalText[61], GlobalText[1672]);
                TextListColor[TextNum] = iTextColor;
            }

            TextBold[TextNum] = false;	TextNum++;
        }
        break;
        case CLASS_SUMMONER:
        {
            if (byRequireClass == 1)
            {
                swprintf(TextNum, GlobalText[61], GlobalText[1687]);
                TextListColor[TextNum] = iTextColor;
            }
            else if (byRequireClass == 2)
            {
                swprintf(TextNum, GlobalText[61], GlobalText[1688]);
                TextListColor[TextNum] = iTextColor;
            }
            else if (byRequireClass == 3)
            {
                swprintf(TextNum, GlobalText[61], GlobalText[1689]);
                TextListColor[TextNum] = iTextColor;
            }

            TextBold[TextNum] = false;	TextNum++;
        }
        break;
        case CLASS_RAGEFIGHTER:
        {
            if (byRequireClass == 1)
            {
                swprintf(TextNum, GlobalText[61], GlobalText[3150]);
                TextListColor[TextNum] = iTextColor;
            }
            else if (byRequireClass == 3)
            {
                swprintf(TextNum, GlobalText[61], GlobalText[3151]);
                TextListColor[TextNum] = iTextColor;
            }
            TextBold[TextNum] = false;	TextNum++;
        }
        break;
        }
    }
}

    private string GetSpecialOptionText(int Type, int Option, int Value, int iMana)
{
        string Text = "";
    switch (Option)
    {
    case AT_SKILL_BLOCKING:
        iMana = SkillManaCost(Option);
        Text = Fmt(GlobalText[80], iMana);
        break;
    case AT_SKILL_SWORD1:
        iMana = SkillManaCost(Option);
        Text = Fmt(GlobalText[81], iMana);
        break;
    case AT_SKILL_SWORD2:
        iMana = SkillManaCost(Option);
        Text = Fmt(GlobalText[82], iMana);
        break;
    case AT_SKILL_SWORD3:
        iMana = SkillManaCost(Option);
        Text = Fmt(GlobalText[83], iMana);
        break;
    case AT_SKILL_SWORD4:
        iMana = SkillManaCost(Option);
        Text = Fmt(GlobalText[84], iMana);
        break;
    case AT_SKILL_SWORD5:
        iMana = SkillManaCost(Option);
        Text = Fmt(GlobalText[85], iMana);
        break;
    case AT_SKILL_MANY_ARROW_UP:
    case AT_SKILL_MANY_ARROW_UP + 1:
    case AT_SKILL_MANY_ARROW_UP + 2:
    case AT_SKILL_MANY_ARROW_UP + 3:
    case AT_SKILL_MANY_ARROW_UP + 4:

    case AT_SKILL_CROSSBOW:
        iMana = SkillManaCost(Option);
        Text = Fmt(GlobalText[86], iMana);
        break;
    case AT_SKILL_BLAST_CROSSBOW4:
        iMana = SkillManaCost(Option);
        Text = Fmt(GlobalText[920], iMana);
        break;
    case AT_SKILL_MULTI_SHOT:
        iMana = SkillManaCost(Option);
        Text = Fmt(GlobalText[920], iMana);
        break;
    case AT_SKILL_RECOVER:
        iMana = SkillManaCost(Option);
        Text = Fmt(GlobalText[920], iMana);
        break;
    case AT_SKILL_POWER_SLASH_UP:
    case AT_SKILL_POWER_SLASH_UP + 1:
    case AT_SKILL_POWER_SLASH_UP + 2:
    case AT_SKILL_POWER_SLASH_UP + 3:
    case AT_SKILL_POWER_SLASH_UP + 4:
    case AT_SKILL_ICE_BLADE:
        iMana = SkillManaCost(Option);
        Text = Fmt(GlobalText[98], iMana);
        break;
    case AT_LUCK:
        Text = Fmt(GlobalText[87]);
        break;
    case AT_IMPROVE_DAMAGE:
        Text = Fmt(GlobalText[88], Value);
        break;
    case AT_IMPROVE_MAGIC:
        Text = Fmt(GlobalText[89], Value);
        break;
    case AT_IMPROVE_CURSE:
        Text = Fmt(GlobalText[1697], Value);
        break;
    case AT_IMPROVE_BLOCKING:
        Text = Fmt(GlobalText[90], Value);
        break;
    case AT_IMPROVE_DEFENSE:
        Text = Fmt(GlobalText[91], Value);
        break;
    case AT_LIFE_REGENERATION:
        if (!(ITEM_LOCHS_FEATHER <= Type && Type <= ITEM_INVISIBILITY_CLOAK))
        {
            Text = Fmt(GlobalText[92], Value);
        }
        break;
    case AT_IMPROVE_LIFE:
        Text = Fmt(GlobalText[622]);
        break;
    case AT_IMPROVE_MANA:
        Text = Fmt(GlobalText[623]);
        break;
    case AT_DECREASE_DAMAGE:
        Text = Fmt(GlobalText[624]);
        break;

    case AT_REFLECTION_DAMAGE:
        Text = Fmt(GlobalText[625]);
        break;
    case AT_IMPROVE_BLOCKING_PERCENT:
        Text = Fmt(GlobalText[626]);
        break;
    case AT_IMPROVE_GAIN_GOLD:
        Text = Fmt(GlobalText[627]);
        break;
    case AT_EXCELLENT_DAMAGE:
        Text = Fmt(GlobalText[628]);
        break;
    case AT_IMPROVE_DAMAGE_LEVEL:
        Text = Fmt(GlobalText[629]);
        break;
    case AT_IMPROVE_DAMAGE_PERCENT:
        Text = Fmt(GlobalText[630], Value);
        break;
    case AT_IMPROVE_MAGIC_LEVEL:
        Text = Fmt(GlobalText[631]);
        break;
    case AT_IMPROVE_MAGIC_PERCENT:
        Text = Fmt(GlobalText[632], Value);
        break;
    case AT_IMPROVE_ATTACK_SPEED:
        Text = Fmt(GlobalText[633], Value);
        break;
    case AT_IMPROVE_GAIN_LIFE:
        Text = Fmt(GlobalText[634]);
        break;
    case AT_IMPROVE_GAIN_MANA:
        Text = Fmt(GlobalText[635]);
        break;
    case AT_IMPROVE_EVADE:
        Text = Fmt(GlobalText[746]);
        break;
    case AT_SKILL_RIDER:
        iMana = SkillManaCost(Option);
        Text = Fmt(GlobalText[745], iMana);
        break;
    case AT_SKILL_FORCE:  //
        iMana = SkillManaCost(Option);
        Text = Fmt(GlobalText[1210], iMana);
        break;
    case AT_SKILL_FORCE_WAVE:
        iMana = SkillManaCost(Option);
        Text = Fmt(GlobalText[1186], iMana);
        break;
    case AT_SKILL_ASHAKE_UP:
    case AT_SKILL_ASHAKE_UP + 1:
    case AT_SKILL_ASHAKE_UP + 2:
    case AT_SKILL_ASHAKE_UP + 3:
    case AT_SKILL_ASHAKE_UP + 4:
    case AT_SKILL_DARK_HORSE:
        iMana = SkillManaCost(Option);
        Text = Fmt(GlobalText[1189], iMana);
        break;
    case AT_SKILL_PLASMA_STORM_FENRIR:
        iMana = SkillManaCost(Option);
        Text = Fmt(GlobalText[1928], iMana);
        break;
    case AT_SET_OPTION_IMPROVE_DEFENCE:
        Text = Fmt(GlobalText[959], Value);
        break;
    case AT_SET_OPTION_IMPROVE_CHARISMA:
        Text = Fmt(GlobalText[954], Value);
        break;
    case AT_SET_OPTION_IMPROVE_DAMAGE:
        Text = Fmt(GlobalText[577], Value);
        break;
    case AT_IMPROVE_HP_MAX:
        Text = Fmt(GlobalText[740], Value);
        break;
    case AT_IMPROVE_MP_MAX:
        Text = Fmt(GlobalText[741], Value);
        break;
    case AT_ONE_PERCENT_DAMAGE:
        Text = Fmt(GlobalText[742], Value);
        break;
    case AT_IMPROVE_AG_MAX:
        Text = Fmt(GlobalText[743], Value);
        break;
    case AT_DAMAGE_ABSORB:
        Text = Fmt(GlobalText[744], Value);
        break;
    case AT_SET_OPTION_IMPROVE_STRENGTH:
        Text = Fmt(GlobalText[985], Value);
        break;
    case AT_SET_OPTION_IMPROVE_DEXTERITY:
        Text = Fmt(GlobalText[986], Value);
        break;
    case AT_SET_OPTION_IMPROVE_VITALITY:
        Text = Fmt(GlobalText[987], Value);
        break;
    case AT_SET_OPTION_IMPROVE_ENERGY:
        Text = Fmt(GlobalText[988], Value);
        break;
    case AT_IMPROVE_MAX_MANA:
        Text = Fmt(GlobalText[1087], Value);
        break;
    case AT_IMPROVE_MAX_AG:
        Text = Fmt(GlobalText[1088], Value);
        break;
    case AT_DAMAGE_REFLECTION:
        Text = Fmt(GlobalText[1673], Value);
        break;
    case AT_RECOVER_FULL_LIFE:
        Text = Fmt(GlobalText[1674], Value);
        break;
    case AT_RECOVER_FULL_MANA:
        Text = Fmt(GlobalText[1675], Value);
        break;
    case AT_SKILL_SUMMON_EXPLOSION:
        Text = Fmt(GlobalText[1695], iMana);
        break;
    case AT_SKILL_SUMMON_REQUIEM:
        Text = Fmt(GlobalText[1696], iMana);
        break;
    case AT_SKILL_SUMMON_POLLUTION:
        Text = Fmt(GlobalText[1789], iMana);
        break;
    case AT_SKILL_THRUST:
        iMana = SkillManaCost(Option);
        Text = Fmt(GlobalText[3153], iMana);
        break;
    case AT_SKILL_STAMP:
        iMana = SkillManaCost(Option);
        Text = Fmt(GlobalText[3154], iMana);
        break;
    }
        return Text;
    }

    private int RenderHellasItemInfo(SourceItem ip, int textNum)
{
    int TextNum = textNum;
    switch (ip.Type)
    {
    case ITEM_LOST_MAP:
    {
        int startIndex = 0;
        int baseClass = gCharacterManager.GetBaseClass(Hero.Class);
        if (baseClass == CLASS_DARK || baseClass == CLASS_DARK_LORD || baseClass == CLASS_RAGEFIGHTER)
        {
            startIndex = NUM_HELLAS;
        }

        int HeroLevel = CharacterAttribute.Level;
        int ItemLevel = ip.Level;

        TextListColor[TextNum] = TEXT_COLOR_WHITE;
        swprintf(TextNum, "%s %s       %s    ", GlobalText[58], GlobalText[368], GlobalText[935]); TextListColor[TextNum] = TEXT_COLOR_WHITE; TextBold[TextNum] = false; TextNum++;
        for (int i = 0; i < NUM_HELLAS; i++)
        {
            swprintf(TextNum, "        %d             %3d~%3d     ", i + 1, g_iKalimaLevel[startIndex + i][0], Math.Min(400, g_iKalimaLevel[startIndex + i][1]));

            if (ItemLevel == i + 1)
            {
                TextListColor[TextNum] = TEXT_COLOR_DARKYELLOW;
            }
            else
            {
                TextListColor[TextNum] = TEXT_COLOR_WHITE;
            }
            TextBold[TextNum] = false; TextNum++;
        }

        swprintf(TextNum, "\n"); TextNum++;
        swprintf(TextNum, GlobalText[1184]);  TextListColor[TextNum] = TEXT_COLOR_DARKBLUE; TextNum++;

        if (HeroLevel < g_iKalimaLevel[startIndex][0])
        {
            swprintf(TextNum, "\n"); TextNum++;
            swprintf(TextNum, GlobalText[1123], g_iKalimaLevel[startIndex][0]);  TextListColor[TextNum] = TEXT_COLOR_DARKRED; TextNum++;
        }
    }
    break;

    case ITEM_SYMBOL_OF_KUNDUN:
    {
        swprintf(TextNum, GlobalText[1181], ip.Durability, 5); TextNum++;
        if (ip.Durability >= 5)
        {
            swprintf(TextNum, GlobalText[1182]); TextListColor[TextNum] = TEXT_COLOR_YELLOW; TextNum++;
        }
        else
        {
            swprintf(TextNum, GlobalText[1183], (5 - ip.Durability)); TextListColor[TextNum] = TEXT_COLOR_YELLOW; TextNum++;
        }
    }
    break;
    }

    return TextNum;
}

    private static bool IsPartChargeItem(SourceItem pItem)
{
    if ((pItem.Type >= ITEM_HELPER + 46 && pItem.Type <= ITEM_HELPER + 48)
        || (pItem.Type == ITEM_POTION + 54)
        || (pItem.Type >= ITEM_POTION + 58 && pItem.Type <= ITEM_POTION + 62)
        || (pItem.Type >= ITEM_POTION + 145 && pItem.Type <= ITEM_POTION + 150)
        || (pItem.Type >= ITEM_HELPER + 125 && pItem.Type <= ITEM_HELPER + 127)
        || pItem.Type == ITEM_POTION + 53
        || (pItem.Type >= ITEM_HELPER + 43 && pItem.Type <= ITEM_HELPER + 45)
        || (pItem.Type >= ITEM_POTION + 70 && pItem.Type <= ITEM_POTION + 71)
        || (pItem.Type >= ITEM_POTION + 72 && pItem.Type <= ITEM_POTION + 77)
        || (pItem.Type == ITEM_HELPER + 59)
        || (pItem.Type >= ITEM_HELPER + 54 && pItem.Type <= ITEM_HELPER + 58)
        || (pItem.Type >= ITEM_POTION + 78 && pItem.Type <= ITEM_POTION + 82)
        || (pItem.Type == ITEM_HELPER + 60)
        || (pItem.Type == ITEM_HELPER + 61)
        || (pItem.Type == ITEM_POTION + 91)
        || (pItem.Type >= ITEM_POTION + 92 && pItem.Type <= ITEM_POTION + 93)
        || (pItem.Type == ITEM_POTION + 95)
        || (pItem.Type == ITEM_POTION + 94)
        || (pItem.Type >= ITEM_HELPER + 62 && pItem.Type <= ITEM_HELPER + 63)
        || (pItem.Type >= ITEM_POTION + 97 && pItem.Type <= ITEM_POTION + 98)
        || (pItem.Type == ITEM_POTION + 96)
        || (pItem.Type == ITEM_DEMON || pItem.Type == ITEM_SPIRIT_OF_GUARDIAN)
        || (pItem.Type == ITEM_HELPER + 69)
        || (pItem.Type == ITEM_HELPER + 70)
        || pItem.Type == ITEM_HELPER + 81
        || pItem.Type == ITEM_HELPER + 82
        || pItem.Type == ITEM_HELPER + 93
        || pItem.Type == ITEM_HELPER + 94
        || pItem.Type == ITEM_HELPER + 107
        || pItem.Type == ITEM_HELPER + 104
        || pItem.Type == ITEM_HELPER + 105
        || pItem.Type == ITEM_HELPER + 103
        || pItem.Type == ITEM_POTION + 133
        || pItem.Type == ITEM_HELPER + 109
        || pItem.Type == ITEM_HELPER + 110
        || pItem.Type == ITEM_HELPER + 111
        || pItem.Type == ITEM_HELPER + 112
        || pItem.Type == ITEM_HELPER + 113
        || pItem.Type == ITEM_HELPER + 114
        || pItem.Type == ITEM_HELPER + 115
        || pItem.Type == ITEM_POTION + 112
        || pItem.Type == ITEM_POTION + 113
        || pItem.Type == ITEM_POTION + 120
        || pItem.Type == ITEM_POTION + 123
        || pItem.Type == ITEM_POTION + 124
        || pItem.Type == ITEM_POTION + 134
        || pItem.Type == ITEM_POTION + 135
        || pItem.Type == ITEM_POTION + 136
        || pItem.Type == ITEM_POTION + 137
        || pItem.Type == ITEM_POTION + 138
        || pItem.Type == ITEM_POTION + 139
        || pItem.Type == ITEM_WING + 130
        || pItem.Type == ITEM_WING + 131
        || pItem.Type == ITEM_WING + 132
        || pItem.Type == ITEM_WING + 133
        || pItem.Type == ITEM_WING + 134
        || pItem.Type == ITEM_WING + 135
        || pItem.Type == ITEM_HELPER + 116
        || pItem.Type == ITEM_PET_UNICORN
        || pItem.Type == ITEM_HELPER + 124
        || pItem.Type == ITEM_POTION + 114
        || pItem.Type == ITEM_POTION + 115
        || pItem.Type == ITEM_POTION + 116
        || pItem.Type == ITEM_POTION + 117
        || pItem.Type == ITEM_POTION + 118
        || pItem.Type == ITEM_POTION + 119
        || pItem.Type == ITEM_POTION + 126
        || pItem.Type == ITEM_POTION + 127
        || pItem.Type == ITEM_POTION + 128
        || pItem.Type == ITEM_POTION + 129
        || pItem.Type == ITEM_POTION + 130
        || pItem.Type == ITEM_POTION + 131
        || pItem.Type == ITEM_POTION + 132
        || pItem.Type == ITEM_HELPER + 121
        || pItem.Type == ITEM_POTION + 140
        )
    {
        return true;
    }

    return false;
}

    private static bool IsPersonalShopBan(SourceItem pItem)
{
    if (pItem == null)
    {
        return false;
    }


    if ((!pItem.bPeriodItem) &&
        pItem.Type == ITEM_DEMON
        || pItem.Type == ITEM_SPIRIT_OF_GUARDIAN
        || pItem.Type == ITEM_PET_PANDA
        || pItem.Type == ITEM_PANDA_TRANSFORMATION_RING
        || pItem.Type == ITEM_SKELETON_TRANSFORMATION_RING
        || pItem.Type == ITEM_PET_SKELETON
        || (pItem.Type == ITEM_WIZARDS_RING && pItem.Level == 0)
        )
    {
        return false;
    }

    if (pItem.Type == ITEM_MOONSTONE_PENDANT
        || pItem.Type == ITEM_ELITE_TRANSFER_SKELETON_RING
        || (pItem.Type == ITEM_POTION + 21 && pItem.Level != 3)
        || (pItem.Type >= ITEM_SCROLL_OF_EMPEROR_RING_OF_HONOR && pItem.Type <= ITEM_SOUL_SHARD_OF_WIZARD)
        || pItem.Type == ITEM_WEAPON_OF_ARCHANGEL
        || (pItem.Type == ITEM_BOX_OF_LUCK && pItem.Level == 13)
        || (pItem.Type >= ITEM_HELPER + 43 && pItem.Type <= ITEM_HELPER + 45)
        || (pItem.Type == ITEM_WIZARDS_RING && pItem.Level != 0)
        || pItem.Type == ITEM_FLAME_OF_DEATH_BEAM_KNIGHT
        || pItem.Type == ITEM_HORN_OF_HELL_MAINE
        || pItem.Type == ITEM_FEATHER_OF_DARK_PHOENIX
        || pItem.Type == ITEM_EYE_OF_ABYSSAL
        || IsPartChargeItem(pItem)
        || pItem.Type == ITEM_HELPER + 97
        || pItem.Type == ITEM_HELPER + 98
        || pItem.Type == ITEM_POTION + 91
        || pItem.Type == ITEM_HELPER + 99
        || pItem.Type == ITEM_PET_PANDA
        || pItem.Type == ITEM_PANDA_TRANSFORMATION_RING
        || pItem.Type == ITEM_SKELETON_TRANSFORMATION_RING
        || pItem.Type == ITEM_PET_SKELETON
        || (pItem.Type == ITEM_POTION + 96)
        || pItem.Type == ITEM_HELPER + 109
        || pItem.Type == ITEM_HELPER + 110
        || pItem.Type == ITEM_HELPER + 111
        || pItem.Type == ITEM_HELPER + 112
        || pItem.Type == ITEM_HELPER + 113
        || pItem.Type == ITEM_HELPER + 114
        || pItem.Type == ITEM_HELPER + 115
        )
    {
        return true;
    }
    if (Check_ItemAction(pItem, eITEM_PERSONALSHOP))	return true;

    return false;
}

    private static bool IsSellingBan(SourceItem pItem)
{
    int Level = pItem.Level;

    if (true == false
        || pItem.Type == ITEM_POTION + 112
        || pItem.Type == ITEM_POTION + 113
        || pItem.Type == ITEM_POTION + 121
        || pItem.Type == ITEM_POTION + 122
        || pItem.Type == ITEM_POTION + 123
        || pItem.Type == ITEM_POTION + 124
        || pItem.Type == ITEM_WING + 130
        || pItem.Type == ITEM_WING + 131
        || pItem.Type == ITEM_WING + 132
        || pItem.Type == ITEM_WING + 133
        || pItem.Type == ITEM_WING + 134
        || pItem.Type == ITEM_WING + 135
        || pItem.Type == ITEM_PET_PANDA
        || pItem.Type == ITEM_PANDA_TRANSFORMATION_RING
        || pItem.Type == ITEM_SKELETON_TRANSFORMATION_RING
        || pItem.Type == ITEM_PET_SKELETON
        || pItem.Type == ITEM_DEMON
        || pItem.Type == ITEM_SPIRIT_OF_GUARDIAN
        || pItem.Type == ITEM_HELPER + 109
        || pItem.Type == ITEM_HELPER + 110
        || pItem.Type == ITEM_HELPER + 111
        || pItem.Type == ITEM_HELPER + 112
        || pItem.Type == ITEM_HELPER + 113
        || pItem.Type == ITEM_HELPER + 114
        || pItem.Type == ITEM_HELPER + 115
        || ((pItem.Type == ITEM_WIZARDS_RING) && (Level == 0))
        || (pItem.Type == ITEM_PET_UNICORN)
        || (pItem.Type == ITEM_HELPER + 107)
        )
    {
        if (true == pItem.bPeriodItem && true == pItem.bExpiredPeriod)
        {
            return false;
        }
    }

    if (pItem.Type == ITEM_BOX_OF_LUCK
        || (pItem.Type == ITEM_POTION + 21 && Level == 1)
        || ((pItem.bPeriodItem == true) && (pItem.bExpiredPeriod == false) && (pItem.Type == ITEM_WIZARDS_RING) && (Level == 0))
        || (pItem.Type == ITEM_WIZARDS_RING && (Level == 1 || Level == 2))
        || pItem.Type == ITEM_WEAPON_OF_ARCHANGEL
        || (pItem.Type == ITEM_POTION + 20 && Level >= 1 && Level <= 5)
        || IsPartChargeItem(pItem)
        || ((pItem.Type >= ITEM_TYPE_CHARM_MIXWING + EWS_BEGIN)
            && (pItem.Type <= ITEM_TYPE_CHARM_MIXWING + EWS_END))
        || pItem.Type == ITEM_PET_PANDA
        || pItem.Type == ITEM_PANDA_TRANSFORMATION_RING
        || pItem.Type == ITEM_SKELETON_TRANSFORMATION_RING
        || pItem.Type == ITEM_PET_SKELETON
        || (pItem.Type == ITEM_PET_UNICORN)
        || (pItem.Type == ITEM_HELPER + 107)
        )
    {
        return true;
    }

    if (Check_ItemAction(pItem, eITEM_SELL))	return true;

    return false;
}
}
}
