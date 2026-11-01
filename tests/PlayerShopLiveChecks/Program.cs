using System.Reflection;
using System.Buffers;
using System.Text.Json;
using Client.Main;
using Client.Main.Configuration;
using Client.Main.Controllers;
using Client.Main.Core.Client;
using Client.Main.Core.Models;
using Client.Main.Networking;
using Client.Main.Networking.PacketHandling;
using Microsoft.Extensions.Logging.Abstractions;
using MUnique.OpenMU.Network;

var username = Environment.GetEnvironmentVariable("MU_TEST_USER") ?? throw new Exception("MU_TEST_USER is required.");
var password = Environment.GetEnvironmentVariable("MU_TEST_PASSWORD") ?? throw new Exception("MU_TEST_PASSWORD is required.");
using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(Environment.CurrentDirectory, "Client.Main", "appsettings.json")));
var settings = config.RootElement.GetProperty("MuOnlineSettings").Deserialize<MuOnlineSettings>()!;
var state = new CharacterState(NullLoggerFactory.Instance);
await using var network = new NetworkManager(NullLoggerFactory.Instance, settings, state, new ScopeManager(NullLoggerFactory.Instance, state));
var buyerUser = Environment.GetEnvironmentVariable("MU_TEST_BUYER_USER");
var buyerPassword = Environment.GetEnvironmentVariable("MU_TEST_BUYER_PASSWORD");
var buyerState = new CharacterState(NullLoggerFactory.Instance);
await using var buyer = string.IsNullOrEmpty(buyerUser) ? null :
    new NetworkManager(NullLoggerFactory.Instance, settings, buyerState, new ScopeManager(NullLoggerFactory.Instance, buyerState));
var dispatcher = (MainThreadDispatcher)typeof(MuGame).GetField("_mainThreadDispatcher", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
ushort? serverId = null;
bool loggedIn = false, loginFailed = false, gotCharacters = false;
int characterCount = 0;
ushort listedLevel = 0;
string? firstCharacter = null;
ushort? sellerIdSeenByBuyer = null;
ushort? buyerIdSeenBySeller = null;
ushort buyerListedLevel = 0;
network.ServerListReceived += (_, servers) => { if (servers.Count > 0) serverId = servers[0].ServerId; };
network.LoginSuccess += (_, _) => loggedIn = true;
network.LoginFailed += (_, _) => loginFailed = true;
network.CharacterListReceived += (_, characters) =>
{
    gotCharacters = true;
    characterCount = characters.Count;
    int.TryParse(Environment.GetEnvironmentVariable("MU_TEST_CHARACTER_INDEX"), out int index);
    var sorted = characters.OrderByDescending(c => c.Level).ToArray();
    if (index >= 0 && index < sorted.Length) { firstCharacter = sorted[index].Name; listedLevel = sorted[index].Level; }
};

async Task WaitFor(Func<bool> condition, string stage)
{
    var deadline = DateTime.UtcNow.AddSeconds(20);
    while (!condition() && DateTime.UtcNow < deadline)
    {
        dispatcher.ProcessPending();
        await Task.Delay(50);
    }
    dispatcher.ProcessPending();
    if (!condition()) throw new Exception($"Timed out at {stage}; state={network.CurrentState}.");
}

await network.ConnectToConnectServerAsync();
await WaitFor(() => serverId.HasValue, "server list");
await network.RequestGameServerConnectionAsync(serverId!.Value);
await WaitFor(() => network.CurrentState == ClientConnectionState.ConnectedToGameServer, "game server handshake");
await network.SendLoginRequestAsync(username, password);
await WaitFor(() => loggedIn || loginFailed, "login");
if (loginFailed) throw new Exception("Server rejected test login.");
await WaitFor(() => gotCharacters, "character list");
Console.WriteLine($"Live login and character-list check passed ({characterCount} character(s)).");
if (Environment.GetEnvironmentVariable("MU_TEST_SELECT_FIRST") == "1")
{
    if (string.IsNullOrWhiteSpace(firstCharacter)) throw new Exception("No character is available for the in-game check.");
    bool inventoryReceived = false;
    state.InventoryChanged += () => inventoryReceived = true;
    await network.SendSelectCharacterRequestAsync(firstCharacter);
    await WaitFor(() => network.CurrentState == ClientConnectionState.InGame, "character selection");
    await WaitFor(() => inventoryReceived, "inventory");
    await Task.Delay(1000);
    dispatcher.ProcessPending();
    await network.SendClientReadyAfterMapChangeAsync();
    var inventory = state.GetInventoryItems();
    int oneCellItems = inventory.Count(entry => entry.Key >= 12 && entry.Key < 76 &&
        Client.Main.Core.Utilities.ItemDatabase.GetItemDefinition(entry.Value) is { Width: 1, Height: 1 });
    Console.WriteLine($"Live character and inventory check passed ({inventory.Count} occupied slots; {inventory.Keys.Count(slot => slot >= 12 && slot < 76)} in the main bag; {inventory.Keys.Count(slot => slot >= 204 && slot < 236)} in the player shop; {oneCellItems} known 1x1 bag items; listed level {listedLevel}, reported level {state.Level}; selected name matches state: {firstCharacter == state.Name}; positive Zen: {state.InventoryZen > 0}).");

    if (buyer != null)
    {
        ushort? buyerServer = null;
        string? buyerCharacter = null;
        bool buyerLogin = false, buyerFailed = false, buyerCharacters = false, buyerInventory = false;
        ushort buyerLevel = 0;
        buyer.ServerListReceived += (_, servers) => { if (servers.Count > 0) buyerServer = servers[0].ServerId; };
        buyer.LoginSuccess += (_, _) => buyerLogin = true;
        buyer.LoginFailed += (_, _) => buyerFailed = true;
        buyer.CharacterListReceived += (_, characters) =>
        {
            int.TryParse(Environment.GetEnvironmentVariable("MU_TEST_BUYER_INDEX"), out int index);
            var sorted = characters.OrderByDescending(c => c.Level).ToArray();
            if (index >= 0 && index < sorted.Length) { buyerCharacter = sorted[index].Name; buyerLevel = sorted[index].Level; }
            Console.WriteLine($"Second account has {sorted.Length} character(s).");
            buyerCharacters = true;
        };
        buyerState.InventoryChanged += () => buyerInventory = true;
        await buyer.ConnectToConnectServerAsync();
        await WaitFor(() => buyerServer.HasValue, "buyer server list");
        if (buyerServer != serverId) throw new Exception("The test accounts resolved to different servers.");
        await buyer.RequestGameServerConnectionAsync(buyerServer!.Value);
        await WaitFor(() => buyer.CurrentState == ClientConnectionState.ConnectedToGameServer, "buyer game server handshake");
        await buyer.SendLoginRequestAsync(buyerUser!, buyerPassword ?? throw new Exception("MU_TEST_BUYER_PASSWORD is required."));
        await WaitFor(() => buyerLogin || buyerFailed, "buyer login");
        if (buyerFailed) throw new Exception("The server rejected the buyer test login.");
        await WaitFor(() => buyerCharacters, "buyer character list");
        if (string.IsNullOrWhiteSpace(buyerCharacter)) throw new Exception("The buyer account has no characters.");
        await buyer.SendSelectCharacterRequestAsync(buyerCharacter);
        await WaitFor(() => buyer.CurrentState == ClientConnectionState.InGame, "buyer character selection");
        await WaitFor(() => buyerInventory, "buyer inventory");
        await Task.Delay(1000);
        dispatcher.ProcessPending();
        await buyer.SendClientReadyAfterMapChangeAsync();
        await Task.Delay(1000);
        dispatcher.ProcessPending();
        if (Environment.GetEnvironmentVariable("MU_TEST_GATHER") == "1" && state.MapId == 2 && buyerState.MapId == 2)
        {
            byte oldX = state.PositionX, oldY = state.PositionY;
            await network.GetCharacterService().SendWarpCommandRequestAsync(4);
            await WaitFor(() => state.PositionX != oldX || state.PositionY != oldY, "seller warp to Devias");
            await network.SendClientReadyAfterMapChangeAsync();
            await Task.Delay(1500);
            dispatcher.ProcessPending();
        }
        Console.WriteLine($"Second account login and character check passed (listed level {buyerLevel}; seller map {state.MapId}, buyer map {buyerState.MapId}; empty shop: {!buyerState.GetInventoryItems().Keys.Any(slot => slot >= 204 && slot < 236)}; positive Zen: {buyerState.InventoryZen > 0}).");
        if (Environment.GetEnvironmentVariable("MU_TEST_AUDIT") == "1")
        {
            foreach (var (label, inspected) in new[] { ("seller", state), ("buyer", buyerState) })
            {
                var items = inspected.GetInventoryItems();
                Console.WriteLine($"Audit {label}: bag slots {string.Join(',', items.Keys.Where(s => s >= 12 && s < 76).Order())}; shop slots {string.Join(',', items.Keys.Where(s => s >= 204 && s < 236).Order())}.");
                if (items.TryGetValue(12, out var item12))
                    Console.WriteLine($"Audit {label} slot 12: {Client.Main.Core.Utilities.ItemDatabase.GetItemName(item12) ?? "unknown"}; data {Convert.ToHexString(item12)}.");
            }
        }
        var seenSeller = buyer.GetScopeManager().GetScopeItems(ScopeObjectType.Player).OfType<PlayerScopeObject>().FirstOrDefault(p => p.Name == firstCharacter);
        sellerIdSeenByBuyer = seenSeller?.Id;
        buyerIdSeenBySeller = network.GetScopeManager().GetScopeItems(ScopeObjectType.Player).OfType<PlayerScopeObject>().FirstOrDefault(p => p.Name == buyerCharacter)?.Id;
        buyerListedLevel = buyerLevel;
        Console.WriteLine($"Seller visible to buyer: {seenSeller != null}; buyer visible to seller: {buyerIdSeenBySeller.HasValue}; seller [{state.PositionX},{state.PositionY}], buyer [{buyerState.PositionX},{buyerState.PositionY}]; map-coordinate distance: {Math.Max(Math.Abs(state.PositionX - buyerState.PositionX), Math.Abs(state.PositionY - buyerState.PositionY))}.");
    }

    if (Environment.GetEnvironmentVariable("MU_TEST_SELLER") == "1")
    {
        if (listedLevel < 6 || inventory.Keys.Any(slot => slot >= 204 && slot < 236))
            throw new Exception("Live seller test requires a level 6+ character and an empty shop.");
        var candidate = inventory.FirstOrDefault(entry => entry.Key >= 12 && entry.Key < 76 &&
            Client.Main.Core.Utilities.ItemDatabase.GetItemDefinition(entry.Value) is { Width: 1, Height: 1 });
        if (candidate.Value == null) throw new Exception("No known 1x1 inventory item is available.");
        if (Environment.GetEnvironmentVariable("MU_TEST_PURCHASE") == "1" &&
            (buyer == null || !sellerIdSeenByBuyer.HasValue || !buyerIdSeenBySeller.HasValue || buyerListedLevel < 6 ||
             buyerState.InventoryZen < 1 || buyerState.GetInventoryItems().Keys.Any(slot => slot >= 204 && slot < 236)))
            throw new Exception("A visible buyer with an empty shop, Zen and level 6+ is required before opening the test shop.");
        if (Environment.GetEnvironmentVariable("MU_TEST_PURCHASE") == "1")
        {
            var occupied = new bool[8, 8];
            foreach (var entry in buyerState.GetInventoryItems().Where(e => e.Key >= 12 && e.Key < 76))
            {
                var def = Client.Main.Core.Utilities.ItemDatabase.GetItemDefinition(entry.Value)
                    ?? throw new Exception("Cannot verify free buyer inventory space because an item is unknown.");
                int start = entry.Key - 12;
                for (int y = start / 8; y < Math.Min(8, start / 8 + def.Height); y++)
                    for (int x = start % 8; x < Math.Min(8, start % 8 + def.Width); x++) occupied[x, y] = true;
            }
            if (occupied.Cast<bool>().All(value => value)) throw new Exception("The buyer's main inventory has no free slot.");
        }
        byte source = candidate.Key, shopSlot = 204;
        byte[] original = candidate.Value.ToArray();
        if (Environment.GetEnvironmentVariable("MU_TEST_PURCHASE") == "1")
        {
            var snapshotPath = Path.Combine(Path.GetTempPath(), $"mu-player-shop-{Guid.NewGuid():N}.json");
            var snapshot = new
            {
                Seller = state.Name,
                Buyer = buyerState.Name,
                SourceSlot = source,
                Item = Convert.ToHexString(original),
                SellerInventory = state.GetInventoryItems().ToDictionary(item => item.Key, item => Convert.ToHexString(item.Value)),
                BuyerInventory = buyerState.GetInventoryItems().ToDictionary(item => item.Key, item => Convert.ToHexString(item.Value)),
            };
            File.WriteAllText(snapshotPath, JsonSerializer.Serialize(snapshot));
            Console.WriteLine($"Pre-transaction inventory snapshot: {snapshotPath}");
        }
        bool priceReceived = false, priceAccepted = false, moveReceived = false, moveAccepted = false;
        state.PlayerShopPriceResult += (_, _, _, ok) => { priceAccepted = ok; priceReceived = true; };
        state.PlayerShopMoveResult += (_, _, ok) => { moveAccepted = ok; moveReceived = true; };
        var service = network.GetCharacterService();
        bool moveSent = false;
        bool openRequested = false;
        bool itemOnBuyer = false;
        bool buyerShopRequested = false;
        uint testPrice = Environment.GetEnvironmentVariable("MU_TEST_PURCHASE") == "1" ? 1u : 2_000_000_000u;
        try
        {
            if (!state.TryBeginPlayerShopPrice(source, shopSlot, testPrice)) throw new Exception("Could not begin pricing.");
            if (!await service.SendPlayerShopSetItemPriceAsync(source, testPrice)) throw new Exception("Could not send the price.");
            await WaitFor(() => priceReceived, "price response");
            if (!priceAccepted) throw new Exception("The server rejected the selected item's price.");
            if (!state.TryBeginPlayerShopMove(source, shopSlot)) throw new Exception("Could not begin shop move.");
            if (!await service.SendPlayerShopMoveAsync(source, shopSlot)) throw new Exception("Could not send the shop move.");
            moveSent = true;
            await WaitFor(() => moveReceived, "move to shop");
            if (!moveAccepted) throw new Exception("The server rejected the shop move.");
            if (!state.GetInventoryItems().ContainsKey(shopSlot) || state.GetInventoryItems().ContainsKey(source))
                throw new Exception("The server-confirmed move was not reflected in the client state.");
            Console.WriteLine("Live price and move-to-shop checks passed.");
            if (Environment.GetEnvironmentVariable("MU_TEST_OPEN") == "1")
            {
                if (state.Id == ushort.MaxValue) throw new Exception("Cannot verify shop close without the local player ID.");
                openRequested = true;
                await service.SendPlayerShopOpenAsync("TEST ONLY DO NOT BUY");
                await WaitFor(() => state.IsPlayerShopOpen, "shop open response");
                if (Environment.GetEnvironmentVariable("MU_TEST_BUYER_LIST") == "1")
                {
                    if (buyer == null || buyerState.MapId != state.MapId || !sellerIdSeenByBuyer.HasValue) throw new Exception("The buyer must see the seller in scope.");
                    var buyerManager = (ConnectionManager)typeof(NetworkManager).GetField("_connectionManager", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(buyer)!;
                    bool listReceived = false, listCorrect = false;
                    string? listError = null;
                    buyerManager.Connection.PacketReceived += sequence =>
                    {
                        var bytes = sequence.ToArray();
                        if (bytes.Length >= 5 && bytes[0] == 0xC2 && bytes[3] == 0x3F && bytes[4] == 5)
                        {
                            listCorrect = PlayerShopListParser.TryParse(bytes, true, out var list) && list!.OwnerId == sellerIdSeenByBuyer.Value &&
                                list.Items.Count == 1 && list.Items[0].Slot == shopSlot && list.Items[0].Price == testPrice;
                            listReceived = true;
                        }
                        else if (bytes.Length >= 4 && (bytes[0] == 0xC1 || bytes[0] == 0xC3) && bytes[2] == 0x0D)
                        {
                            var message = new MUnique.OpenMU.Network.Packets.ServerToClient.ServerMessage(bytes.AsMemory()).Message;
                            if (message.Contains("Store", StringComparison.OrdinalIgnoreCase) || message.Contains("Player", StringComparison.OrdinalIgnoreCase))
                                listError = message;
                        }
                        return ValueTask.CompletedTask;
                    };
                    await buyer.GetCharacterService().SendPlayerStoreListRequestAsync(sellerIdSeenByBuyer.Value, firstCharacter!);
                    await WaitFor(() => listReceived || listError != null, "buyer shop list");
                    if (listError != null) throw new Exception($"Server rejected the shop-list request: {listError}");
                    if (!listCorrect) throw new Exception("The buyer received an invalid shop list or incorrect item price.");
                    Console.WriteLine("Live buyer shop-list response check passed.");
                    if (Environment.GetEnvironmentVariable("MU_TEST_PURCHASE") == "1")
                    {
                        if (!buyerIdSeenBySeller.HasValue || buyerListedLevel < 6 || buyerState.InventoryZen < 1 ||
                            buyerState.GetInventoryItems().Keys.Any(slot => slot >= 204 && slot < 236))
                            throw new Exception("The buyer cannot safely perform the return transaction.");
                        PlayerShopBuyResultData? buyerResult = null;
                        buyerManager.Connection.PacketReceived += sequence =>
                        {
                            var bytes = sequence.ToArray();
                            if (bytes.Length >= 4 && bytes[2] == 0x3F && bytes[3] == 6 &&
                                PlayerShopBuyResultParser.TryParse(bytes, true, out var result)) buyerResult = result;
                            return ValueTask.CompletedTask;
                        };
                        // Once a purchase is attempted, a timeout cannot prove the item is still in the seller's shop.
                        moveSent = false;
                        if (!await buyer.GetCharacterService().SendPlayerShopItemBuyRequestAsync(sellerIdSeenByBuyer.Value, firstCharacter!, shopSlot))
                            throw new Exception("Could not send the buyer's purchase request.");
                        await WaitFor(() => buyerResult != null, "buyer purchase result");
                        if (!buyerResult!.Success)
                        {
                            moveSent = true;
                            throw new Exception($"The server rejected the purchase (result {buyerResult.Result}).");
                        }
                        itemOnBuyer = true;
                        moveSent = false;
                        byte buyerSlot = buyerResult.InventorySlot;
                        await WaitFor(() => buyerState.GetInventoryItems().TryGetValue(buyerSlot, out var data) && data.SequenceEqual(original), "buyer inventory update");
                        Console.WriteLine("Live purchase check passed; item received by buyer.");

                        bool buyerPriceReceived = false, buyerPriceOk = false, buyerMoveReceived = false, buyerMoveOk = false;
                        buyerState.PlayerShopPriceResult += (_, _, _, ok) => { buyerPriceOk = ok; buyerPriceReceived = true; };
                        buyerState.PlayerShopMoveResult += (_, _, ok) => { buyerMoveOk = ok; buyerMoveReceived = true; };
                        var buyerService = buyer.GetCharacterService();
                        if (!buyerState.TryBeginPlayerShopPrice(buyerSlot, shopSlot, 1) || !await buyerService.SendPlayerShopSetItemPriceAsync(buyerSlot, 1))
                            throw new Exception($"Could not price the return item in buyer inventory slot {buyerSlot}.");
                        await WaitFor(() => buyerPriceReceived, "return item price response");
                        if (!buyerPriceOk) throw new Exception($"Return price was rejected; item remains in buyer inventory slot {buyerSlot}.");
                        if (!buyerState.TryBeginPlayerShopMove(buyerSlot, shopSlot) || !await buyerService.SendPlayerShopMoveAsync(buyerSlot, shopSlot))
                            throw new Exception($"Could not move the return item from buyer inventory slot {buyerSlot}.");
                        await WaitFor(() => buyerMoveReceived, "return item move");
                        if (!buyerMoveOk) throw new Exception($"Return move was rejected; inspect buyer inventory slot {buyerSlot}.");
                        buyerShopRequested = true;
                        await buyerService.SendPlayerShopOpenAsync("TEST RETURN ONLY");
                        await WaitFor(() => buyerState.IsPlayerShopOpen, "buyer return shop opening");

                        var sellerManager = (ConnectionManager)typeof(NetworkManager).GetField("_connectionManager", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(network)!;
                        bool returnListReceived = false, returnListCorrect = false;
                        PlayerShopBuyResultData? returnResult = null;
                        sellerManager.Connection.PacketReceived += sequence =>
                        {
                            var bytes = sequence.ToArray();
                            if (bytes.Length >= 5 && bytes[0] == 0xC2 && bytes[3] == 0x3F && bytes[4] == 5)
                            {
                                returnListCorrect = PlayerShopListParser.TryParse(bytes, true, out var list) && list!.OwnerId == buyerIdSeenBySeller.Value &&
                                    list.Items.Count == 1 && list.Items[0].Slot == shopSlot && list.Items[0].Price == 1;
                                returnListReceived = true;
                            }
                            if (bytes.Length >= 4 && bytes[2] == 0x3F && bytes[3] == 6 &&
                                PlayerShopBuyResultParser.TryParse(bytes, true, out var result)) returnResult = result;
                            return ValueTask.CompletedTask;
                        };
                        await service.SendPlayerStoreListRequestAsync(buyerIdSeenBySeller.Value, buyerState.Name);
                        await WaitFor(() => returnListReceived, "return shop list");
                        if (!returnListCorrect) throw new Exception("Return shop list did not match the test item.");
                        if (!await service.SendPlayerShopItemBuyRequestAsync(buyerIdSeenBySeller.Value, buyerState.Name, shopSlot))
                            throw new Exception("Could not request the return purchase.");
                        await WaitFor(() => returnResult != null, "return purchase result");
                        if (!returnResult!.Success) throw new Exception($"Return purchase was rejected (result {returnResult.Result}).");
                        byte returnedSlot = returnResult.InventorySlot;
                        itemOnBuyer = false;
                        await WaitFor(() => state.GetInventoryItems().TryGetValue(returnedSlot, out var data) && data.SequenceEqual(original), "returned inventory item");
                        if (buyerState.IsPlayerShopOpen)
                        {
                            await buyerService.SendPlayerShopCloseAsync();
                            await WaitFor(() => !buyerState.IsPlayerShopOpen, "close return shop");
                        }
                        buyerShopRequested = false;
                        if (returnedSlot != source)
                        {
                            state.StashPendingInventoryMove(returnedSlot, source);
                            await sellerManager.Connection.SendAsync(() => PacketBuilder.BuildItemMoveRequestExtendedPacket(
                                sellerManager.Connection.Output, MUnique.OpenMU.Network.Packets.ItemStorageKind.Inventory, returnedSlot,
                                MUnique.OpenMU.Network.Packets.ItemStorageKind.Inventory, source));
                            await WaitFor(() => state.GetInventoryItems().TryGetValue(source, out var data) && data.SequenceEqual(original) &&
                                !state.GetInventoryItems().ContainsKey(returnedSlot), "restore original inventory slot");
                        }
                        Console.WriteLine("Live return purchase passed; item restored to its original inventory slot.");
                    }
                }
                if (Environment.GetEnvironmentVariable("MU_TEST_OWN_LIST") == "1")
                {
                    var manager = (ConnectionManager)typeof(NetworkManager).GetField("_connectionManager", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(network)!;
                    bool listReceived = false, listCorrect = false;
                    manager.Connection.PacketReceived += sequence =>
                    {
                        var bytes = sequence.ToArray();
                        if (bytes.Length >= 5 && bytes[0] == 0xC2 && bytes[3] == 0x3F && bytes[4] == 5)
                        {
                            listCorrect = PlayerShopListParser.TryParse(bytes, true, out var list) && list!.OwnerId == state.Id &&
                                list.Items.Count == 1 && list.Items[0].Slot == shopSlot && list.Items[0].Price == 2_000_000_000;
                            listReceived = true;
                        }
                        return ValueTask.CompletedTask;
                    };
                    await service.SendPlayerStoreListRequestAsync(state.Id, firstCharacter!);
                    await WaitFor(() => listReceived, "own shop list");
                    if (!listCorrect) throw new Exception("Actual shop list did not match the priced item.");
                    Console.WriteLine("Live shop-list response check passed.");
                }
                if (state.IsPlayerShopOpen)
                {
                    await service.SendPlayerShopCloseAsync();
                    await WaitFor(() => !state.IsPlayerShopOpen, "shop close response");
                }
                openRequested = false;
                Console.WriteLine("Live shop open and close checks passed.");
            }
        }
        finally
        {
            Exception? closeError = null;
            if (buyerShopRequested && buyer != null)
            {
                await buyer.GetCharacterService().SendPlayerShopCloseAsync();
                if (buyerState.IsPlayerShopOpen) await WaitFor(() => !buyerState.IsPlayerShopOpen, "cleanup buyer shop close");
            }
            if (openRequested)
            {
                try
                {
                    await service.SendPlayerShopCloseAsync();
                    if (state.IsPlayerShopOpen) await WaitFor(() => !state.IsPlayerShopOpen, "cleanup shop close");
                }
                catch (Exception ex) { closeError = ex; }
            }
            if (moveSent)
            {
                if (state.IsPlayerShopMovePending) state.TryFinishPlayerShopMove(null, out _);
                moveReceived = false; moveAccepted = false;
                if (!state.TryBeginPlayerShopMove(shopSlot, source) || !await service.SendPlayerShopMoveAsync(shopSlot, source))
                    throw new Exception($"Cleanup failed: could not request return of the item from shop slot {shopSlot} to inventory slot {source}.");
                await WaitFor(() => moveReceived, "return item to inventory");
                if (!moveAccepted || !state.GetInventoryItems().TryGetValue(source, out var returned) || !returned.SequenceEqual(original) || state.GetInventoryItems().ContainsKey(shopSlot))
                    throw new Exception($"Cleanup failed or returned item differed; inspect inventory slot {source} and shop slot {shopSlot}.");
                Console.WriteLine("Live return-to-inventory check passed; original item bytes restored.");
            }
            if (closeError != null) throw new Exception("Could not verify shop closure during cleanup.", closeError);
            if (itemOnBuyer) throw new Exception("The test item remains on the buyer account; inspect its inventory and personal shop before retrying.");
        }
    }
}
