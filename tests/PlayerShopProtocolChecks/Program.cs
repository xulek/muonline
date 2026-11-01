using System.Buffers.Binary;
using System.Text;
using Client.Main.Networking.PacketHandling;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

static byte[] Header(int length, byte count)
{
    var data = new byte[length];
    data[0] = 0xC2;
    BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(1), (ushort)length);
    data[3] = 0x3F; data[4] = 0x05; data[5] = 1;
    BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(6), 0x1234);
    Encoding.UTF8.GetBytes("Seller").CopyTo(data, 8);
    Encoding.UTF8.GetBytes("Shop").CopyTo(data, 18);
    data[54] = count;
    return data;
}

var legacy = Header(95, 2);
legacy[55] = 204; legacy[56] = 0x42;
BinaryPrimitives.WriteUInt32LittleEndian(legacy.AsSpan(71), 123456);
legacy[75] = 205; legacy[76] = 0x43;
BinaryPrimitives.WriteUInt32LittleEndian(legacy.AsSpan(91), 876543);
Check(PlayerShopListParser.TryParse(legacy, false, out var shop), "classic packet rejected");
Check(shop!.OwnerId == 0x1234 && shop.OwnerName == "Seller" && shop.ShopName == "Shop", "owner identity lost");
Check(shop.Items.Count == 2 && shop.Items[0].Slot == 204 && shop.Items[0].Price == 123456 && shop.Items[1].Price == 876543, "classic slots or prices corrupted");
legacy[75] = 204;
Check(!PlayerShopListParser.TryParse(legacy, false, out _), "duplicate slots accepted");
legacy[75] = 205;
Check(!PlayerShopListParser.TryParse(legacy.AsSpan(0, 94), false, out _), "truncated packet accepted");
legacy[4] = 0x13;
Check(PlayerShopListParser.TryParse(legacy, false, out var update) && update!.IsUpdate, "shop update not recognized");
legacy[5] = 0;
Check(!PlayerShopListParser.TryParse(legacy, false, out _), "failed shop response accepted");
var empty = Header(55, 0);
Check(PlayerShopListParser.TryParse(empty, false, out var emptyShop) && emptyShop!.Items.Count == 0, "empty shop rejected");
var extended = Header(69, 1);
BinaryPrimitives.WriteUInt32LittleEndian(extended.AsSpan(55), 98765);
extended[63] = 207; extended[64] = 0x44; // five-byte extended item with no flags
Check(PlayerShopListParser.TryParse(extended, true, out var ext), "extended packet rejected");
Check(ext!.Items[0].Slot == 207 && ext.Items[0].Price == 98765 && ext.Items[0].ItemData.Length == 5, "extended item parsed incorrectly");
extended[59] = 1;
Check(!PlayerShopListParser.TryParse(extended, true, out _), "unsupported item currency accepted as Zen");
extended[59] = 0;
extended[68] = 0x80;
Check(!PlayerShopListParser.TryParse(extended, true, out _), "truncated socket item accepted");
var result = new byte[] { 0xC1, 13, 0x3F, 0x06, 0x34, 0x12, 1, 25, 0x44, 0, 0, 0, 0 };
Check(PlayerShopBuyResultParser.TryParse(result, true, out var purchase), "purchase response rejected");
Check(purchase!.SellerId == 0x1234 && purchase.Success && purchase.InventorySlot == 25 && purchase.ItemData.Length == 5, "purchase response misinterpreted");
Check(!PlayerShopBuyResultParser.TryParse(result.AsSpan(0, 12), true, out _), "truncated purchase accepted");
var paddedResult = new byte[23];
result.CopyTo(paddedResult, 0);
paddedResult[1] = (byte)paddedResult.Length;
Check(PlayerShopBuyResultParser.TryParse(paddedResult, true, out var paddedPurchase) && paddedPurchase!.ItemData.SequenceEqual(result.AsSpan(8).ToArray()), "fixed-size server purchase response rejected or item data corrupted");
paddedResult[^1] = 1;
Check(!PlayerShopBuyResultParser.TryParse(paddedResult, true, out _), "nonzero trailing purchase data accepted");
var failure = new byte[] { 0xC1, 8, 0x3F, 0x06, 0x34, 0x12, 7, 0 };
Check(PlayerShopBuyResultParser.TryParse(failure, true, out var rejected) && !rejected!.Success && rejected.Result == 7, "rejection lost");
Console.WriteLine("Player shop packet checks passed.");
