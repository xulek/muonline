// Offline check of the SourceMain5.2 RenderItemInfo port: builds tooltips for sample items and prints them.
using Client.Main.Controls.UI.Game.Common;
using Client.Main.Core.Client;
using Client.Main.Core.Utilities;
using Client.Main.Core.Utilities.ItemTooltip;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xna.Framework;
using MUnique.OpenMU.Network.Packets;

int failures = 0;
void Check(bool ok, string name) { if (!ok) failures++; Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {name}"); }

var state = new CharacterState(NullLoggerFactory.Instance)
{
    Class = CharacterClassNumber.SoulMaster, Level = 150,
    Strength = 40, Agility = 40, Vitality = 60, Energy = 500,
};

// Extended item data (OpenMU): group/number, level, durability, flags, [option], [excellent], [ancient], [harmony]
byte[] Item(int group, int number, int level, int dur, bool luck = false, bool skill = false, int option = 0, int exc = 0, int ancient = 0, byte[] sockets = null, int socketBonus = 0x0F)
{
    byte flags = 0;
    var tail = new List<byte>();
    if (option > 0) { flags |= 0x01; tail.Add((byte)option); }
    if (luck) flags |= 0x02;
    if (skill) flags |= 0x04;
    if (exc > 0) { flags |= 0x08; tail.Add((byte)exc); }
    if (ancient > 0) { flags |= 0x10; tail.Add((byte)ancient); }
    if (sockets != null) { flags |= 0x80; tail.Add((byte)((socketBonus << 4) | sockets.Length)); tail.AddRange(sockets); }
    var data = new List<byte> { (byte)((group << 4) | ((number >> 8) & 0x0F)), (byte)(number & 0xFF), (byte)level, (byte)dur, flags };
    data.AddRange(tail);
    return data.ToArray();
}

List<(string text, Color color)> Show(string title, byte[] raw, ItemTooltipContext ctx = null)
{
    var def = ItemDatabase.GetItemDefinition(raw);
    var item = new InventoryItem(def, Point.Zero, raw, ItemDatabase.GetItemDurability(raw));
    ctx ??= new ItemTooltipContext();
    ctx.Character = state;
    var lines = ItemUiHelper.BuildTooltipLines(item, ctx);
    Console.WriteLine($"--- {title} ---");
    foreach (var (text, color) in lines)
        Console.WriteLine(text.Length == 0 ? "" : $"  {text}   [{color.R},{color.G},{color.B}]");
    return lines;
}

Check(GlobalText.Get(71) == "Durability: [%d/%d]", "GlobalText 71");
Check(GlobalText.Format("Luck (critical damage rate +5%%)") == "Luck (critical damage rate +5%)", "printf %%");
Check(GlobalText.Format("%d,%03d", 12, 5) == "12,005", "printf %03d");

var staff = Show("Legendary Staff +9 luck/skill/+12", Item(5, 5, 9, 60, luck: true, skill: true, option: 3));
Check(staff.Any(l => l.text.StartsWith("Legendary Staff +9")), "staff name");
Check(staff.Any(l => l.text == "Additional Wizardry Dmg +12"), "staff +12 option");
Check(staff.Any(l => l.text.StartsWith("Durability: [")), "staff durability");

var armor = Show("Excellent Dragon Armor +5 (HP, Mana)", Item(8, 1, 5, 50, option: 1, exc: 0x30));
Check(armor.Any(l => l.text.StartsWith("Excellent Dragon Armor +5")), "excellent name");
Check(armor.Any(l => l.text == GlobalText.Format(GlobalText.Get(622))), "excellent HP option");

var bless = Show("Jewel of Bless", Item(14, 13, 0, 1));
Check(bless.Count >= 2 && bless.Any(l => l.text == GlobalText.Get(572)), "bless description");

var box = Show("Box of Luck +2", Item(14, 11, 2, 1));
Check(box.Count > 0 && box[0].text == GlobalText.Get(106), "box of luck +2 name");

Show("Wizard's Ring +0", Item(13, 20, 0, 255));
Show("Jewel of Bless in NPC shop", Item(14, 13, 0, 1), new ItemTooltipContext { Type = ItemTooltipType.NpcShop, NpcShopOpen = true });
var anc = Show("Ancient item (set A, +5 stamina option)", Item(7, 3, 6, 40, ancient: 0x11));
Check(anc.Any(l => l.text == GlobalText.Get(1089) + " " + GlobalText.Get(159)), "ancient set option header");

var sock = Show("Socket item: Flamberge with Fire seed sphere lv1 + empty socket", Item(0, 26, 0, 50, sockets: new byte[] { 0, 0xFE }));
Check(sock.Any(l => l.text == GlobalText.Get(2650) + " " + GlobalText.Get(159)), "socket option header");
Check(sock.Any(l => l.text.Contains(GlobalText.Get(2652))), "empty socket row");

Show("Seed Sphere (Fire) level 1", Item(12, 100, 0, 1));

var armorItem = new InventoryItem(ItemDatabase.GetItemDefinition(Item(8, 1, 5, 20, option: 1, exc: 0x30)), Point.Zero, Item(8, 1, 5, 20, option: 1, exc: 0x30), 20);
var buy = ItemPriceCalculator.CalculateBuyPrice(armorItem);
var sell = ItemPriceCalculator.CalculateSellPrice(armorItem);
Console.WriteLine($"--- ItemValue Excellent Dragon Armor +5 (dur 20): buy {buy}, sell {sell}, NPC repair {ItemPriceCalculator.CalculateRepairPrice(armorItem, true)}, self repair {ItemPriceCalculator.CalculateRepairPrice(armorItem, false)} ---");
Check(buy > 0 && sell > 0 && sell < buy, "ItemValue buy/sell");
var blessItem = new InventoryItem(ItemDatabase.GetItemDefinition(Item(14, 13, 0, 1)), Point.Zero, Item(14, 13, 0, 1), 1);
Check(ItemPriceCalculator.CalculateBuyPrice(blessItem) == 9000000, "ItemValue Jewel of Bless = 9,000,000");
Check(ItemPriceCalculator.CalculateSellPrice(blessItem) == 3000000, "ItemValue Jewel of Bless sell = 3,000,000");

var repair = ItemUiHelper.BuildRepairTooltipLines(armorItem, npcRepairShop: true, new ItemTooltipContext { Character = state });
Console.WriteLine("--- Repair tooltip (NPC) ---");
foreach (var (text, color) in repair) Console.WriteLine(text.Length == 0 ? "" : $"  {text}   [{color.R},{color.G},{color.B}] bold={repair.GetStyle(repair.IndexOf((text, color))).Bold}");
Check(repair.Any(l => l.text.StartsWith(GlobalText.Format(GlobalText.Get(238), "").TrimEnd())), "repair price row (GlobalText 238)");

var raven = Show("Dark Raven with pet info (level 10)", Item(13, 5, 0, 255), new ItemTooltipContext { PetInfo = new SourcePetInfo { m_dwPetType = SourcePetInfo.PET_TYPE_DARK_SPIRIT, m_wLevel = 10, m_dwExp1 = 12345, m_wLife = 200 } });
Check(raven.Any(l => l.text == GlobalText.Get(1214)), "pet tooltip header");

var ancStyled = ItemUiHelper.BuildTooltipLines(new InventoryItem(ItemDatabase.GetItemDefinition(Item(7, 3, 6, 40, ancient: 0x11)), Point.Zero, Item(7, 3, 6, 40, ancient: 0x11), 40), new ItemTooltipContext { Character = state });
Check(ancStyled.GetStyle(0).Background.HasValue && ancStyled.GetStyle(0).Bold, "ancient name row: bold with GREEN_BLUE background");

Console.WriteLine(failures == 0 ? "All item tooltip checks passed." : $"{failures} check(s) failed.");
return failures == 0 ? 0 : 1;
