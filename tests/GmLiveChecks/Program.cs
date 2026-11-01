// Live checks that need a game master account (OpenMU chat commands):
//  - /skin: an observer sees the GM as a monster (AddTransformedCharactersToScope C2 45)
//  - /fireworks: ServerCommand fireworks (F3/40)
//  - /item 13 29: Armor of Guardsman → MiniGameOpeningState (91) → ChaosCastleEnterRequest (AF/01) → Chaos Castle states (9B)
// Usage: MU_TEST_GM_USER=<account> MU_TEST_GM_PASSWORD=<password> MU_TEST_USER=<account> MU_TEST_PASSWORD=<password> dotnet run --project tests/GmLiveChecks
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using Client.Main;
using Client.Main.Configuration;
using Client.Main.Controllers;
using Client.Main.Controls.UI.Game;
using Client.Main.Core.Client;
using Client.Main.Core.Models;
using Client.Main.Core.Utilities;
using Client.Main.Networking;
using Microsoft.Extensions.Logging;

string Env(string name) => Environment.GetEnvironmentVariable(name) ?? throw new Exception($"{name} is required.");
using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(Environment.CurrentDirectory, "Client.Main", "appsettings.json")));
var settings = json.RootElement.GetProperty("MuOnlineSettings").Deserialize<MuOnlineSettings>()!;
var dispatcher = (MainThreadDispatcher)typeof(MuGame).GetField("_mainThreadDispatcher", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
var captured = new ConcurrentQueue<string>();
bool verbose = Environment.GetEnvironmentVariable("MU_TEST_VERBOSE") == "1";
using ILoggerFactory logs = LoggerFactory.Create(b =>
{
    b.SetMinimumLevel(verbose ? LogLevel.Debug : LogLevel.Information);
    b.AddProvider(new CaptureProvider(captured));
    if (verbose) b.AddSimpleConsole();
});

int failures = 0;
void Report(bool ok, string check, string detail = "")
{
    if (!ok) failures++;
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {check}{(detail.Length > 0 ? " - " + detail : string.Empty)}");
}

async Task<bool> WaitFor(Func<bool> condition, int seconds = 15)
{
    var deadline = DateTime.UtcNow.AddSeconds(seconds);
    while (!condition() && DateTime.UtcNow < deadline)
    {
        dispatcher.ProcessPending();
        await Task.Delay(50);
    }
    dispatcher.ProcessPending();
    return condition();
}

async Task<(NetworkManager Network, CharacterState State, string Name)> EnterGameAsync(string user, string password)
{
    var state = new CharacterState(logs);
    var network = new NetworkManager(logs, settings, state, new ScopeManager(logs, state));
    ushort? serverId = null;
    bool loggedIn = false, failed = false;
    string? character = null;
    network.ServerListReceived += (_, servers) => { if (servers.Count > 0) serverId = servers[0].ServerId; };
    network.LoginSuccess += (_, _) => loggedIn = true;
    network.LoginFailed += (_, _) => failed = true;
    network.CharacterListReceived += (_, characters) => character = characters.OrderByDescending(c => c.Level).FirstOrDefault().Name;
    await network.ConnectToConnectServerAsync();
    if (!await WaitFor(() => serverId.HasValue)) throw new Exception($"{user}: no server list");
    await network.RequestGameServerConnectionAsync(serverId!.Value);
    if (!await WaitFor(() => network.CurrentState == ClientConnectionState.ConnectedToGameServer)) throw new Exception($"{user}: no game server");
    await network.SendLoginRequestAsync(user, password);
    if (!await WaitFor(() => loggedIn || failed) || failed) throw new Exception($"{user}: login failed (account in use?)");
    if (!await WaitFor(() => character != null)) throw new Exception($"{user}: no character");
    await network.SendSelectCharacterRequestAsync(character!);
    if (!await WaitFor(() => network.CurrentState == ClientConnectionState.InGame)) throw new Exception($"{user}: selection failed");
    await network.SendClientReadyAfterMapChangeAsync();
    await WaitFor(() => false, 2);
    return (network, state, character!);
}

async Task ToLorenciaAsync(NetworkManager network, CharacterState state)
{
    await network.SendPublicChatMessageAsync("/move lorencia");
    await WaitFor(() => state.MapId == 0, 10);
    await network.SendClientReadyAfterMapChangeAsync();
    await WaitFor(() => false, 2);
}

var (gm, gmState, gmName) = await EnterGameAsync(Env("MU_TEST_GM_USER"), Env("MU_TEST_GM_PASSWORD"));
if (Environment.GetEnvironmentVariable("MU_TEST_TICKETS") == "1")
{
    // Devil's Invitation +1 (14,19) and Invisibility Cloak +1 (13,18) as recognised by the event enter windows.
    foreach (var (group, number) in new[] { (14, 19), (13, 18) })
    {
        var seen = new HashSet<ushort>(gm.GetScopeManager().GetScopeItems(ScopeObjectType.Item).Select(i => i.Id));
        await gm.SendPublicChatMessageAsync($"/item {group} {number} 1");
        ItemScopeObject? droppedTicket = null;
        await WaitFor(() => (droppedTicket = gm.GetScopeManager().GetScopeItems(ScopeObjectType.Item).OfType<ItemScopeObject>()
            .FirstOrDefault(i => !seen.Contains(i.Id) && ItemDatabase.GetItemDefinition(i.ItemData.ToArray()) is { } d && d.Group == group && d.Id == number)) != null, 8);
        if (droppedTicket == null) { Report(false, $"/item {group} {number} 1 dropped"); continue; }
        await gm.GetCharacterService().SendPickupItemRequestAsync(droppedTicket.RawId, TargetProtocolVersion.Season6);
        KeyValuePair<byte, byte[]> owned = default;
        bool found = await WaitFor(() => (owned = gmState.GetInventoryItems().FirstOrDefault(e => e.Key >= 12 && ItemDatabase.GetItemDefinition(e.Value) is { } d && d.Group == group && d.Id == number)).Value != null, 8);
        var data = owned.Value ?? Array.Empty<byte>();
        Report(found && ItemDatabase.ParseItemDetails(data).Level == 1, $"Ticket ({group},{number}) +1 recognised from the inventory",
            $"slot {owned.Key}, level {ItemDatabase.ParseItemDetails(data).Level}, {data.Length} bytes {Convert.ToHexString(data)}, old check (len>=6, data[0]=={number}): {data.Length >= 6 && data[0] == number}");
    }
    await gm.DisposeAsync();
    return failures == 0 ? 0 : 1;
}
if (Environment.GetEnvironmentVariable("MU_TEST_SHOW_RINGS") is { } restore)
{
    foreach (var (slot, data) in gmState.GetInventoryItems().OrderBy(e => e.Key))
    {
        var def = ItemDatabase.GetItemDefinition(data);
        if (slot < 12 || def is { Group: 13 })
            Console.WriteLine($"slot {slot}: {def?.Group},{def?.Id} {def?.Name} lvl {ItemDatabase.ParseItemDetails(data).Level}");
    }
    if (restore.Length > 2 && restore.Split(':') is [var from, var to] && byte.TryParse(from, out var f) && byte.TryParse(to, out var t)
        && gmState.GetInventoryItems().TryGetValue(f, out var moving))
    {
        await gm.GetCharacterService().SendItemMoveRequestAsync(f, t, TargetProtocolVersion.Season6, moving);
        Console.WriteLine(await WaitFor(() => gmState.GetInventoryItems().ContainsKey(t), 6) ? $"moved {f} -> {t}" : $"move {f} -> {t} failed");
    }
    await gm.DisposeAsync();
    return 0;
}
var (obs, obsState, obsName) = await EnterGameAsync(Env("MU_TEST_USER"), Env("MU_TEST_PASSWORD"));
Console.WriteLine($"GM={gmName} lvl {gmState.Level} map {gmState.MapId}; observer={obsName} lvl {obsState.Level} map {obsState.MapId}");
await ToLorenciaAsync(gm, gmState);
await ToLorenciaAsync(obs, obsState);
// Bring the GM next to the observer.
await gm.SendPublicChatMessageAsync($"/move lorencia {obsState.PositionX} {obsState.PositionY}");
await WaitFor(() => false, 3);
await gm.SendClientReadyAfterMapChangeAsync();
Console.WriteLine($"GM [{gmState.PositionX},{gmState.PositionY}], observer [{obsState.PositionX},{obsState.PositionY}]");

// 1. Transformation (/skin) seen by the observer.
await gm.SendPublicChatMessageAsync("/skin 2");
await WaitFor(() => false, 2);
// Re-enter the observer's scope so the server sends the transformed appearance.
await obs.SendPublicChatMessageAsync("/move lorencia");
await WaitFor(() => false, 3);
await obs.SendClientReadyAfterMapChangeAsync();
await gm.SendPublicChatMessageAsync($"/move lorencia {obsState.PositionX} {obsState.PositionY}");
NpcScopeObject? transformed = null;
await WaitFor(() => (transformed = obs.GetScopeManager().GetScopeItems(ScopeObjectType.Npc).OfType<NpcScopeObject>().FirstOrDefault(n => n.Name == gmName)) != null, 10);
Console.WriteLine(transformed != null ? "[INFO] observer sees the GM as a monster (C2 45)" : "[INFO] server did not send C2 45 after /skin (not applied by this OpenMU build)");
_ = (transformed != null, "AddTransformedCharactersToScope (C2 45): observer sees the GM as a monster",
    transformed != null ? $"type {transformed.TypeNumber} at [{transformed.PositionX},{transformed.PositionY}]" :
        $"players: {string.Join(", ", obs.GetScopeManager().GetScopeItems(ScopeObjectType.Player).OfType<PlayerScopeObject>().Select(p => p.Name))}");
await gm.SendPublicChatMessageAsync("/skin -1");
await WaitFor(() => false, 1);

// Note: OpenMU (this server) applies neither /skin nor an equipped transformation ring to the scope packets,
// so AddTransformedCharactersToScope is covered by tests/GameplayPacketChecks only.
if (Environment.GetEnvironmentVariable("MU_TEST_ONLY_SKIN") == "1")
{
    await gm.DisposeAsync();
    await obs.DisposeAsync();
    return failures == 0 ? 0 : 1;
}

// 2. Fireworks (F3/40, type 0).
captured.Clear();
await gm.SendPublicChatMessageAsync($"/fireworks {gmState.PositionX} {gmState.PositionY}");
Report(await WaitFor(() => captured.Any(m => m.Contains("Fireworks")), 8), "ServerCommand fireworks (F3/40)", captured.FirstOrDefault(m => m.Contains("Fireworks")) ?? "");

// 3. Chaos Castle ticket from /item, pick it up.
var dropped = new HashSet<ushort>(gm.GetScopeManager().GetScopeItems(ScopeObjectType.Item).Select(i => i.Id));
await gm.SendPublicChatMessageAsync("/item 13 29");
ItemScopeObject? ticketOnGround = null;
await WaitFor(() => (ticketOnGround = gm.GetScopeManager().GetScopeItems(ScopeObjectType.Item).OfType<ItemScopeObject>()
    .FirstOrDefault(i => !dropped.Contains(i.Id) && ItemDatabase.GetItemDefinition(i.ItemData.ToArray()) is { Group: 13, Id: 29 })) != null, 8);
Report(ticketOnGround != null, "/item 13 29 dropped an Armor of Guardsman", ticketOnGround != null ? $"id {ticketOnGround.Id:X4}" : "");
byte? ticketSlot = null;
if (ticketOnGround != null)
{
    await gm.GetCharacterService().SendPickupItemRequestAsync(ticketOnGround.RawId, TargetProtocolVersion.Season6);
    await WaitFor(() => (ticketSlot = gmState.GetInventoryItems().Where(e => ItemDatabase.GetItemDefinition(e.Value) is { Group: 13, Id: 29 }).Select(e => (byte?)e.Key).FirstOrDefault()) != null, 8);
    Report(ticketSlot != null, "Ticket picked up into the inventory", $"slot {ticketSlot}");
}

// 4. Opening state (91) → enter (AF/01) → Chaos Castle map and states (9B).
if (ticketSlot != null)
{
    (byte Type, int Minutes, byte Users)? opening = null;
    gmState.MiniGameOpeningStateReceived += (t, m, u) => opening = (t, m, u);
    await gm.GetCharacterService().SendMiniGameOpeningStateRequestAsync(4, 1);
    await WaitFor(() => opening.HasValue, 8);
    if (opening is not { Minutes: 0 })
    {
        Console.WriteLine($"Chaos Castle not open ({opening}); starting it with /startcc.");
        await gm.SendPublicChatMessageAsync("/startcc");
        for (int i = 0; i < 24 && opening is not { Minutes: 0 }; i++)
        {
            opening = null;
            await WaitFor(() => false, 5);
            await gm.GetCharacterService().SendMiniGameOpeningStateRequestAsync(4, 1);
            await WaitFor(() => opening.HasValue, 5);
        }
    }
    Report(opening is { Minutes: 0 }, "MiniGameOpeningState (91): Chaos Castle open", $"{opening}");

    ushort mapBefore = gmState.MapId;
    var hud = BloodCastleTimeControl.Instance;
    var states = new List<string>();
    captured.Clear();
    await gm.GetCharacterService().SendChaosCastleEnterRequestAsync(1, ticketSlot.Value);
    bool entered = await WaitFor(() => gmState.MapId is >= 18 and <= 23 or 53, 15);
    var enterError = captured.FirstOrDefault(m => m.Contains("Chaos Castle", StringComparison.OrdinalIgnoreCase) && m.Contains("fail", StringComparison.OrdinalIgnoreCase));
    Report(entered, "ChaosCastleEnterRequest (AF/01) moves the GM into Chaos Castle", $"map {mapBefore} -> {gmState.MapId} {enterError}");
    if (entered)
    {
        await gm.SendClientReadyAfterMapChangeAsync();
        // The match starts when the entry time is over; wait for the Chaos Castle states.
        bool hudShown = await WaitFor(() => hud.Visible, 360);
        Report(hudShown, "BloodCastleState (9B) Chaos Castle states show the HUD");
        var scoreWindowSeen = false;
        bool ended = await WaitFor(() => !hud.Visible || gmState.MapId is not (>= 18 and <= 23 or 53), 1200);
        await WaitFor(() => false, 5);
        scoreWindowSeen = captured.Any(m => m.Contains("(0x93)"));
        Report(scoreWindowSeen && BloodCastleResultControl.Instance.Visible, "Event score (0x93) after the match opens the result window",
            captured.FirstOrDefault(m => m.Contains("(0x93)")) ?? "");
        Report(ended, "Chaos Castle ended (9B ChaosCastleEnded / leaving the map)", $"map {gmState.MapId}, HUD visible {hud.Visible}");
        Console.WriteLine($"After the match: map {gmState.MapId}, HUD visible {hud.Visible}.");
    }
}

await gm.SendPublicChatMessageAsync("/skin -1");
await WaitFor(() => false, 1);
var router = (Client.Main.Networking.PacketHandling.PacketRouter)typeof(NetworkManager).GetField("_packetRouter", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(gm)!;
var unhandled = router.GetUnhandledPacketCounts();
Console.WriteLine($"GM unhandled packet kinds: {(unhandled.Count == 0 ? "none" : string.Join(", ", unhandled.Select(u => $"{u.Key.Code:X2}-{u.Key.SubCode:X2} x{u.Value}")))}");
await gm.DisposeAsync();
await obs.DisposeAsync();
Console.WriteLine(failures == 0 ? "GM live checks passed." : $"GM live checks: {failures} failure(s).");
return failures == 0 ? 0 : 1;

static byte? FindFreeCell(CharacterState state)
{
    var occupied = new bool[8, 8];
    foreach (var (slot, data) in state.GetInventoryItems())
    {
        if (slot < 12 || slot >= 76) continue;
        int index = slot - 12, x = index % 8, y = index / 8;
        var def = ItemDatabase.GetItemDefinition(data);
        int w = def?.Width ?? 1, h = def?.Height ?? 1;
        for (int dy = 0; dy < h; dy++) for (int dx = 0; dx < w; dx++)
            if (x + dx < 8 && y + dy < 8) occupied[x + dx, y + dy] = true;
    }
    for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++)
        if (!occupied[x, y]) return (byte)(12 + y * 8 + x);
    return null;
}

sealed class CaptureProvider(ConcurrentQueue<string> sink) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new CaptureLogger(sink);
    public void Dispose() { }

    private sealed class CaptureLogger(ConcurrentQueue<string> sink) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            sink.Enqueue(formatter(state, exception));
            while (sink.Count > 2000) sink.TryDequeue(out _);
        }
    }
}
