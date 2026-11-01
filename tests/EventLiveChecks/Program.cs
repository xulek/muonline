// Live event checks: MiniGameOpeningState (0x91) for Chaos Castle and the server answer for Illusion Temple.
// Usage: MU_TEST_USER=<account> MU_TEST_PASSWORD=<password> dotnet run --project tests/EventLiveChecks
using System.Reflection;
using System.Text.Json;
using Client.Main;
using Client.Main.Configuration;
using Client.Main.Controllers;
using Client.Main.Core.Client;
using Client.Main.Core.Models;
using Client.Main.Networking;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

string Env(string name) => Environment.GetEnvironmentVariable(name) ?? throw new Exception($"{name} is required.");
using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(Environment.CurrentDirectory, "Client.Main", "appsettings.json")));
var settings = json.RootElement.GetProperty("MuOnlineSettings").Deserialize<MuOnlineSettings>()!;
var dispatcher = (MainThreadDispatcher)typeof(MuGame).GetField("_mainThreadDispatcher", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
ILoggerFactory logs = Environment.GetEnvironmentVariable("MU_TEST_VERBOSE") == "1"
    ? LoggerFactory.Create(b => b.AddSimpleConsole().SetMinimumLevel(LogLevel.Debug))
    : NullLoggerFactory.Instance;

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

async Task<(NetworkManager Network, CharacterState State)> EnterGameAsync(string user, string password)
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
    if (!await WaitFor(() => serverId.HasValue)) throw new Exception("no server list");
    await network.RequestGameServerConnectionAsync(serverId!.Value);
    if (!await WaitFor(() => network.CurrentState == ClientConnectionState.ConnectedToGameServer)) throw new Exception("no game server");
    await network.SendLoginRequestAsync(user, password);
    if (!await WaitFor(() => loggedIn || failed) || failed) throw new Exception("login failed");
    if (!await WaitFor(() => character != null)) throw new Exception("no character");
    await network.SendSelectCharacterRequestAsync(character!);
    if (!await WaitFor(() => network.CurrentState == ClientConnectionState.InGame)) throw new Exception("selection failed");
    await network.SendClientReadyAfterMapChangeAsync();
    return (network, state);
}

var (a, aState) = await EnterGameAsync(Env("MU_TEST_USER"), Env("MU_TEST_PASSWORD"));
(byte Type, int Minutes, byte Users)? opening = null;
aState.MiniGameOpeningStateReceived += (t, m, u) => opening = (t, m, u);
await a.GetCharacterService().SendMiniGameOpeningStateRequestAsync(4, 1);
Report(await WaitFor(() => opening.HasValue), "MiniGameOpeningState (0x91) for Chaos Castle", opening is { } o ? $"opens in {o.Minutes} min, {o.Users} participants" : "no answer");

var tickets = aState.GetInventoryItems().Where(e => e.Value.Length > 0 && Client.Main.Core.Utilities.ItemDatabase.GetItemDefinition(e.Value) is { Group: 13, Id: 29 or 121 }).ToList();
Console.WriteLine($"Chaos Castle tickets in the inventory: {tickets.Count} (zen {aState.InventoryZen}, level {aState.Level}, map {aState.MapId} [{aState.PositionX},{aState.PositionY}])");
if (tickets.Count > 0 && opening is { Minutes: 0 } && Environment.GetEnvironmentVariable("MU_TEST_ENTER_CC") == "1")
{
    ushort before = aState.MapId;
    await a.GetCharacterService().SendChaosCastleEnterRequestAsync(1, tickets[0].Key);
    Report(await WaitFor(() => aState.MapId != before, 15), "Entering Chaos Castle with a ticket (AF/01)", $"map {before} -> {aState.MapId}");
}

opening = null;
await a.GetCharacterService().SendMiniGameOpeningStateRequestAsync(5, 0);
bool itAnswered = await WaitFor(() => opening.HasValue, 4);
Report(!itAnswered, "Illusion Temple: no opening state (server: event not implemented)", itAnswered ? $"{opening}" : "server answered with a message only");

await WaitFor(() => false, 1);
var unhandled = ((Client.Main.Networking.PacketHandling.PacketRouter)typeof(NetworkManager)
    .GetField("_packetRouter", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(a)!).GetUnhandledPacketCounts();
Console.WriteLine($"Unhandled packet kinds this session: {(unhandled.Count == 0 ? "none" : string.Join(", ", unhandled.Select(u => $"{u.Key.Code:X2}-{u.Key.SubCode:X2} x{u.Value}")))}");
await a.DisposeAsync();
Console.WriteLine(failures == 0 ? "Event live checks passed." : $"Event live checks: {failures} failure(s).");
return failures == 0 ? 0 : 1;
