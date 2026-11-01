using Client.Main.Core.Client;
using Client.Main.Core.Utilities;
using Client.Main.Networking.PacketHandling.Handlers;
using Microsoft.Extensions.Logging.Abstractions;
using Client.Main.Controls.UI.Game.Inventory;
using Client.Main.Networking.PacketHandling;
using System.Reflection;
using System.Buffers;
using MUnique.OpenMU.Network.Packets;

var closeWriter = new ArrayBufferWriter<byte>();
var closeLength = PacketBuilder.BuildPlayerShopClosePacket(closeWriter);
if (!closeWriter.WrittenMemory.IsEmpty) throw new Exception("PacketBuilder must leave advancing to the connection.");
var closeBytes = closeWriter.GetMemory(closeLength).Span[..closeLength];
if (!closeBytes.SequenceEqual(new byte[] { 0xC3, 4, 0x3F, 0x03 }))
    throw new Exception("The shop close request did not initialize its protocol header.");
var moveWriter = new ArrayBufferWriter<byte>();
var moveLength = PacketBuilder.BuildPlayerShopMovePacket(moveWriter, 12, 204);
var moveBytes = moveWriter.GetMemory(moveLength).Span[..moveLength];
if (!moveBytes.SequenceEqual(new byte[] { 0xC3, 7, 0x24, 0, 12, 4, 204 }))
    throw new Exception("Shop move did not use the extended Season 6 protocol and absolute slots.");
var returnWriter = new ArrayBufferWriter<byte>();
var returnLength = PacketBuilder.BuildPlayerShopMovePacket(returnWriter, 204, 12);
if (!returnWriter.GetMemory(returnLength).Span[..returnLength].SequenceEqual(new byte[] { 0xC3, 7, 0x24, 4, 204, 0, 12 }))
    throw new Exception("Returning a shop item used the wrong source or destination storage.");

foreach (var (methodName, expected) in new[] { ("HandlePlayerShopListAsync", (byte)0x05), ("HandlePlayerShopListUpdateAsync", (byte)0x13), ("HandlePlayerShopBuyResultAsync", (byte)0x06) })
{
    var attribute = typeof(ShopHandler).GetMethod(methodName)!.GetCustomAttribute<PacketHandlerAttribute>();
    if (attribute == null || attribute.MainCode != 0x3F || attribute.SubCode != expected)
        throw new Exception($"Incorrect router registration for {methodName}.");
}

var state = new CharacterState(NullLoggerFactory.Instance);
var handler = new ShopHandler(NullLoggerFactory.Instance, state, null, TargetProtocolVersion.Season6);
byte[] success = [0xC1, 13, 0x3F, 0x06, 0x34, 0x12, 1, 25, 0x44, 0, 0, 0, 0];
await handler.HandlePlayerShopBuyResultAsync(success);
if (!state.GetInventoryItems().TryGetValue(25, out var item) || !item.SequenceEqual(success[8..]))
    throw new Exception("Successful purchase did not add the server item to the inventory.");
var paddedSuccess = new byte[23];
success.CopyTo(paddedSuccess, 0);
paddedSuccess[1] = (byte)paddedSuccess.Length;
await handler.HandlePlayerShopBuyResultAsync(paddedSuccess);
if (!state.GetInventoryItems().TryGetValue(25, out item) || !item.SequenceEqual(success[8..]))
    throw new Exception("Fixed-size purchase response did not preserve the actual item data.");
byte[] failure = [0xC1, 8, 0x3F, 0x06, 0x34, 0x12, 7, 0];
await handler.HandlePlayerShopBuyResultAsync(failure);
await handler.HandlePlayerShopBuyResultAsync(success[..12]);
if (state.GetInventoryItems().Count != 1)
    throw new Exception("Failed or malformed response changed the inventory.");
var legacyState = new CharacterState(NullLoggerFactory.Instance);
var legacyHandler = new ShopHandler(NullLoggerFactory.Instance, legacyState, null, TargetProtocolVersion.Version097);
byte[] legacyPurchase = new byte[21];
legacyPurchase[0] = 0xC1; legacyPurchase[1] = 21; legacyPurchase[2] = 0x3F; legacyPurchase[3] = 6;
legacyPurchase[4] = 1; legacyPurchase[5] = 0x12; legacyPurchase[6] = 0x34; legacyPurchase[7] = 0x44; legacyPurchase[20] = 30;
await legacyHandler.HandlePlayerShopBuyResultAsync(legacyPurchase);
if (!legacyState.GetInventoryItems().TryGetValue(30, out var legacyItem) || legacyItem.Length != 12 || legacyItem[0] != 0x44)
    throw new Exception("Legacy purchase was interpreted as a Season 6 response.");
var window = new PlayerShopControl();
var offers = new List<(byte Slot, byte[] ItemData, uint Price)> { (204, [0x44, 0, 0, 0, 0], 100) };
window.ExpectShop(10);
if (window.AcceptList(11, "Other", "Other", offers, false))
    throw new Exception("An unsolicited shop response opened a different seller's offer.");
if (!window.AcceptList(10, "Seller", "Shop", offers, false) || !window.Visible)
    throw new Exception("Requested shop did not open.");
if (window.AcceptList(11, "Other", "Other", offers, true))
    throw new Exception("An unrelated update replaced the visible shop.");
window.PurchaseFinished(11, true);
if (!window.Visible) throw new Exception("Unrelated purchase closed the shop.");
window.PurchaseFinished(10, true);
if (window.Visible) throw new Exception("Successful purchase left a stale offer open.");
if (window.AcceptList(10, "Seller", "Shop", offers, true))
    throw new Exception("Late update reopened a closed shop.");

var inventoryHandler = new InventoryHandler(NullLoggerFactory.Instance, state, null, TargetProtocolVersion.Season6);
byte[] movingItem = [0x44, 0, 0, 0, 0];
state.AddOrUpdateInventoryItem(12, movingItem);
if (!state.TryBeginPlayerShopMove(12, 204)) throw new Exception("Could not begin shop move.");
if (state.TryBeginPlayerShopMove(13, 205)) throw new Exception("Concurrent shop move accepted.");
await inventoryHandler.HandleItemMovedOrFailedAsync(new byte[] { 0xC3, 5, 0x24, 0, 204 });
if (!state.IsPlayerShopMovePending || !state.GetInventoryItems().ContainsKey(12))
    throw new Exception("A truncated acknowledgement consumed the move or lost the source item.");
byte[] moveAck = [0xC3, 10, 0x24, 0, 204, 0x44, 0, 0, 0, 0];
await inventoryHandler.HandleItemMovedOrFailedAsync(moveAck);
if (state.GetInventoryItems().ContainsKey(12) || !state.GetInventoryItems().ContainsKey(204))
    throw new Exception("Confirmed move did not transfer the item to the shop slot.");
if (!state.TryBeginPlayerShopMove(204, 12)) throw new Exception("Could not begin returning item.");
byte[] rejectMove = [0xC3, 9, 0x24, 0xFF, 0x44, 0, 0, 0, 0];
await inventoryHandler.HandleItemMovedOrFailedAsync(rejectMove);
if (!state.GetInventoryItems().ContainsKey(204) || state.GetInventoryItems().ContainsKey(12))
    throw new Exception("Rejected move changed the authoritative item location.");
if (!state.TryBeginPlayerShopMove(204, 12)) throw new Exception("Rejected move did not release pending state.");
byte[] returnAck = [0xC3, 10, 0x24, 0, 12, 0x44, 0, 0, 0, 0];
await inventoryHandler.HandleItemMovedOrFailedAsync(returnAck);
if (state.GetInventoryItems().ContainsKey(204) || !state.GetInventoryItems().ContainsKey(12))
    throw new Exception("Confirmed return did not restore the inventory item.");
var results = new List<(byte From, byte To, uint Price, bool Success)>();
state.PlayerShopPriceResult += (from, to, price, ok) => results.Add((from, to, price, ok));
if (state.TryBeginPlayerShopPrice(12, 204, 0)) throw new Exception("Zero price accepted.");
if (!state.TryBeginPlayerShopPrice(12, 204, 123456)) throw new Exception("Could not begin pricing.");
if (state.TryBeginPlayerShopPrice(12, 205, 100)) throw new Exception("Concurrent pricing accepted.");
await handler.HandlePlayerShopPriceResponseAsync(new byte[] { 0xC3, 6, 0x3F, 0x01, 1, 13 });
if (results.Count != 0) throw new Exception("Price acknowledgement for another item was accepted.");
await handler.HandlePlayerShopPriceResponseAsync(new byte[] { 0xC3, 5, 0x3F, 0x01, 1 });
if (results.Count != 0) throw new Exception("Truncated price acknowledgement was accepted.");
await handler.HandlePlayerShopPriceResponseAsync(new byte[] { 0xC3, 6, 0x3F, 0x01, 1, 12 });
if (results.Count != 1 || results[0] != (12, 204, 123456u, true)) throw new Exception("Price confirmation did not preserve the intended move.");
if (!state.TryBeginPlayerShopPrice(12, 204, 9)) throw new Exception("Price confirmation did not release pending state.");
await handler.HandlePlayerShopPriceResponseAsync(new byte[] { 0xC3, 6, 0x3F, 0x01, 3, 12 });
if (results.Count != 2 || results[1].Success) throw new Exception("Price rejection was not reported.");
state.Id = 0x1234;
await handler.HandlePlayerShopOpenResponseAsync(new byte[] { 0xC1, 5, 0x3F, 2, 1 });
if (!state.IsPlayerShopOpen) throw new Exception("Successful shop opening not reflected in state.");
if (state.TryBeginPlayerShopPrice(12, 204, 100)) throw new Exception("An open shop permitted a price change.");
if (state.TryBeginPlayerShopMove(12, 204)) throw new Exception("An open shop permitted an item move.");
await handler.HandlePlayerShopClosedAsync(new byte[] { 0xC1, 7, 0x3F, 3, 1, 0x12, 0x35 });
if (!state.IsPlayerShopOpen) throw new Exception("Closing another player's shop closed our shop.");
await handler.HandlePlayerShopClosedAsync(new byte[] { 0xC1, 7, 0x3F, 3, 0, 0x12, 0x34 });
if (!state.IsPlayerShopOpen) throw new Exception("Failed close changed shop state.");
await handler.HandlePlayerShopClosedAsync(new byte[] { 0xC1, 7, 0x3F, 3, 1, 0x12, 0x34 });
if (state.IsPlayerShopOpen) throw new Exception("Successful shop close not reflected in state.");
using (var sellerWindow = new OwnPlayerShopControl(state))
{
    if (sellerWindow.Visible) throw new Exception("Seller window unexpectedly starts open.");
    sellerWindow.Show();
    if (!sellerWindow.Visible) throw new Exception("Seller window did not open.");
}
using (var emptySeller = new OwnPlayerShopControl(new CharacterState(NullLoggerFactory.Instance)))
{
    var canPlace = typeof(OwnPlayerShopControl).GetMethod("CanPlace", BindingFlags.Instance | BindingFlags.NonPublic)!;
    bool Fits(int x, int y, int width, int height) => (bool)canPlace.Invoke(emptySeller, new object[] { x, y, width, height })!;
    if (!Fits(6, 2, 2, 2) || Fits(7, 2, 2, 2) || Fits(6, 3, 2, 2) || Fits(0, 0, 0, 1))
        throw new Exception("Shop placement accepted an item outside the grid or rejected a valid boundary placement.");
}
var shopUi = typeof(OwnPlayerShopControl).Assembly.GetType("Client.Main.Controls.UI.Game.Inventory.PlayerShopUi")!;
var tryParseZen = shopUi.GetMethod("TryParseZen", BindingFlags.Static | BindingFlags.Public)!;
uint? ParseZen(string text)
{
    var args = new object[] { text, 0u };
    return (bool)tryParseZen.Invoke(null, args)! ? (uint)args[1] : null;
}
if (ParseZen("1500") != 1500 || ParseZen("2,000,000") != 2_000_000 || ParseZen("2 000 000") != 2_000_000 ||
    ParseZen("500k") != 500_000 || ParseZen("1.5kk") != 1_500_000 || ParseZen("1,5kk") != 1_500_000 ||
    ParseZen("2m") != 2_000_000 || ParseZen("1kkk") != 1_000_000_000 || ParseZen("2147483647") != int.MaxValue)
    throw new Exception("Valid Zen amounts were parsed incorrectly.");
if (ParseZen("") != null || ParseZen("0") != null || ParseZen("k") != null || ParseZen("abc") != null ||
    ParseZen("2147483648") != null || ParseZen("3kkk") != null || ParseZen("0.0001k") != null)
    throw new Exception("Invalid Zen amounts were accepted.");
var formatShort = shopUi.GetMethod("FormatZenShort", BindingFlags.Static | BindingFlags.Public)!;
string Short(uint value) => (string)formatShort.Invoke(null, new object[] { value })!;
if (Short(950) != "950" || Short(25_000) != "25k" || Short(1_500_000) != "1.5kk" || Short(1_999_999) != "1.9kk" || Short(2_000_000_000) != "2kkk")
    throw new Exception("Compact Zen formatting is wrong.");
Console.WriteLine("Player shop integration checks passed.");
