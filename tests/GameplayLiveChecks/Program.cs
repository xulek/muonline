// Live checks for daily gameplay packets that can be triggered without special items:
// key configuration (F3/30) persistence across logins and the vault protection state (83) when opening the vault.
// Usage: MU_TEST_USER=<account> MU_TEST_PASSWORD=<password> dotnet run --project tests/GameplayLiveChecks
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

// 1. Key configuration: received after entering, saved, received again after the next login.
var (a, aState) = await EnterGameAsync(Env("MU_TEST_USER"), Env("MU_TEST_PASSWORD"));
Report(await WaitFor(() => aState.ServerKeyConfiguration != null, 10), "ApplyKeyConfiguration (F3/30) after entering the game",
    aState.ServerKeyConfiguration != null ? Convert.ToHexString(aState.ServerKeyConfiguration.ToBytes()) : "not received");
var original = aState.ServerKeyConfiguration?.ToBytes() ?? new KeyConfiguration().ToBytes();
var changed = KeyConfiguration.Parse(original)!;
changed.SkillHotkeys[9] = changed.SkillHotkeys[9] == 0x0013 ? (ushort)0x0014 : (ushort)0x0013;
changed.PotionHotkeys[3] = (byte)(changed.PotionHotkeys[3] == 2 ? 3 : 2);
changed.PotionLevels[0] = (byte)(changed.PotionLevels[0] == 1 ? 2 : 1);
changed.PotionLevels[3] = (byte)(changed.PotionLevels[3] == 7 ? 8 : 7);
await a.GetCharacterService().SendSaveKeyConfigurationAsync(changed.ToBytes());
await WaitFor(() => false, 2);
await a.DisposeAsync();
await Task.Delay(3000);

var (b, bState) = await EnterGameAsync(Env("MU_TEST_USER"), Env("MU_TEST_PASSWORD"));
Report(await WaitFor(() => bState.ServerKeyConfiguration != null, 10), "ApplyKeyConfiguration (F3/30) after the next login");
var reloaded = bState.ServerKeyConfiguration?.ToBytes() ?? Array.Empty<byte>();
// OpenMU returns the first 28 configuration bytes (skills, options, Q/W/E/R items, Q/W levels).
int kept = Math.Min(28, reloaded.Length);
Report(kept >= 28 && reloaded.AsSpan(0, kept).SequenceEqual(changed.ToBytes().AsSpan(0, kept)), "Saved key configuration survives a login (SaveKeyConfiguration F3/30)",
    $"{reloaded.Length} bytes: {Convert.ToHexString(reloaded)}");
await b.GetCharacterService().SendSaveKeyConfigurationAsync(original);

// 2. Vault: talk to the vault NPC (240) in Lorencia → VaultProtectionInformation (83).
if (bState.MapId != 0)
{
    await b.GetCharacterService().SendWarpCommandRequestAsync(2);
    await WaitFor(() => bState.MapId == 0, 10);
    await b.SendClientReadyAfterMapChangeAsync();
}
NpcScopeObject? vault = null;
for (int attempt = 0; attempt < 4 && vault == null; attempt++)
{
    await WaitFor(() => (vault = b.GetScopeManager().GetScopeItems(ScopeObjectType.Npc).OfType<NpcScopeObject>().FirstOrDefault(n => n.TypeNumber == 240)) != null, 5);
    if (vault == null)
    {
        // Lorencia spawns at random positions; warp again to get next to a vault keeper.
        await b.GetCharacterService().SendWarpCommandRequestAsync(2);
        await WaitFor(() => false, 3);
        await b.SendClientReadyAfterMapChangeAsync();
    }
}
Report(vault != null, "Vault NPC (240) in scope", vault != null ? $"[{vault.PositionX},{vault.PositionY}], player [{bState.PositionX},{bState.PositionY}]" : $"player [{bState.PositionX},{bState.PositionY}]");
if (vault != null)
{
    bool protectionReceived = false;
    bState.VaultProtectionChanged += _ => protectionReceived = true;
    await b.GetCharacterService().SendTalkToNpcRequestAsync(vault.Id);
    Report(await WaitFor(() => protectionReceived, 10), "VaultProtectionInformation (83) when opening the vault", $"state {bState.VaultProtectionState}");
    await b.GetCharacterService().SendCloseNpcRequestAsync();
}

await WaitFor(() => false, 1);
var unhandled = ((Client.Main.Networking.PacketHandling.PacketRouter)typeof(NetworkManager)
    .GetField("_packetRouter", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(b)!).GetUnhandledPacketCounts();
Console.WriteLine($"Unhandled packet kinds this session: {(unhandled.Count == 0 ? "none" : string.Join(", ", unhandled.Select(u => $"{u.Key.Code:X2}-{u.Key.SubCode:X2} x{u.Value}")))}");
await b.DisposeAsync();
Console.WriteLine(failures == 0 ? "Gameplay live checks passed." : $"Gameplay live checks: {failures} failure(s).");
return failures == 0 ? 0 : 1;
