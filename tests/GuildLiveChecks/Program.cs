// Live guild checks against a running OpenMU server: guild master NPC dialog, creation, join, member list,
// kick and disband, plus the guild assignment (0x65) and guild info (0x66) seen by both players.
// Usage (from the repository root):
//   MU_TEST_USER=<account> MU_TEST_PASSWORD=<password> MU_TEST_FRIEND_USER=<account> MU_TEST_FRIEND_PASSWORD=<password> \
//   dotnet run --project tests/GuildLiveChecks
// The first account needs a level 100+ character (guild creation), the second one level 6+.
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
using MUnique.OpenMU.Network.Packets.ServerToClient;

string Env(string name) => Environment.GetEnvironmentVariable(name) ?? throw new Exception($"{name} is required.");
using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(Environment.CurrentDirectory, "Client.Main", "appsettings.json")));
var settings = config.RootElement.GetProperty("MuOnlineSettings").Deserialize<MuOnlineSettings>()!;
var dispatcher = (MainThreadDispatcher)typeof(MuGame).GetField("_mainThreadDispatcher", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
ILoggerFactory logs = Environment.GetEnvironmentVariable("MU_TEST_VERBOSE") == "1"
    ? LoggerFactory.Create(b => b.AddSimpleConsole().SetMinimumLevel(LogLevel.Debug))
    : NullLoggerFactory.Instance;

const ushort GuildMasterNpc = 241;
const ushort DeviasWarp = 4;
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

async Task<(NetworkManager Network, CharacterState State, string Name)> EnterGameAsync(string user, string password, string? preferred)
{
    var state = new CharacterState(logs);
    var network = new NetworkManager(logs, settings, state, new ScopeManager(logs, state));
    ushort? serverId = null;
    bool loggedIn = false, failed = false;
    string? character = null;
    network.ServerListReceived += (_, servers) => { if (servers.Count > 0) serverId = servers[0].ServerId; };
    network.LoginSuccess += (_, _) => loggedIn = true;
    network.LoginFailed += (_, _) => failed = true;
    network.CharacterListReceived += (_, characters) => character = characters.Any(c => c.Name == preferred) ? preferred : characters.OrderByDescending(c => c.Level).FirstOrDefault().Name;
    await network.ConnectToConnectServerAsync();
    if (!await WaitFor(() => serverId.HasValue)) throw new Exception($"{user}: no server list.");
    await network.RequestGameServerConnectionAsync(serverId!.Value);
    if (!await WaitFor(() => network.CurrentState == ClientConnectionState.ConnectedToGameServer)) throw new Exception($"{user}: no game server.");
    await network.SendLoginRequestAsync(user, password);
    if (!await WaitFor(() => loggedIn || failed) || failed) throw new Exception($"{user}: login failed.");
    if (!await WaitFor(() => character != null) || string.IsNullOrEmpty(character)) throw new Exception($"{user}: no character.");
    await network.SendSelectCharacterRequestAsync(character);
    if (!await WaitFor(() => network.CurrentState == ClientConnectionState.InGame)) throw new Exception($"{user}: character selection failed.");
    await network.SendClientReadyAfterMapChangeAsync();
    await WaitFor(() => false, 2); // let scope and guild assignment packets arrive
    return (network, state, character);
}

// Only leaves a guild as a normal member; existing guilds of other players are never disbanded.
async Task LeaveGuildIfMemberAsync(NetworkManager network, string name)
{
    var guilds = network.GetGuildManager();
    if (!guilds.HasGuild || guilds.IsGuildMaster) return;
    Console.WriteLine($"{name} is a member of guild {guilds.OwnGuildId}; leaving for a clean start.");
    await network.GetCharacterService().SendGuildKickPlayerAsync(name, string.Empty);
    await WaitFor(() => !guilds.HasGuild);
}

async Task<bool> WarpToDeviasAsync(NetworkManager network, CharacterState state)
{
    if (state.MapId == 2) return true;
    await network.GetCharacterService().SendWarpCommandRequestAsync(DeviasWarp);
    if (!await WaitFor(() => state.MapId == 2, 10)) return false;
    await network.SendClientReadyAfterMapChangeAsync();
    await WaitFor(() => false, 2);
    return true;
}

var (a, aState, aName) = await EnterGameAsync(Env("MU_TEST_USER"), Env("MU_TEST_PASSWORD"), Environment.GetEnvironmentVariable("MU_TEST_CHARACTER"));
var (b, bState, bName) = await EnterGameAsync(Env("MU_TEST_FRIEND_USER"), Env("MU_TEST_FRIEND_PASSWORD"), Environment.GetEnvironmentVariable("MU_TEST_FRIEND_CHARACTER"));
var aGuilds = a.GetGuildManager();
var bGuilds = b.GetGuildManager();
var aService = a.GetCharacterService();
var bService = b.GetCharacterService();
Console.WriteLine($"A={aName} lvl {aState.Level} map {aState.MapId} zen {aState.InventoryZen}; B={bName} lvl {bState.Level} map {bState.MapId} zen {bState.InventoryZen}");

Report(aState.Level > 1 && bState.Level > 1, "Character level kept from the character list (CharacterInformation has none)", $"A {aState.Level}, B {bState.Level}");
await LeaveGuildIfMemberAsync(b, bName);
if (aGuilds.HasGuild || bGuilds.HasGuild)
{
    Console.WriteLine($"A guild {aGuilds.OwnGuildId} ({aGuilds.OwnRole}), B guild {bGuilds.OwnGuildId} ({bGuilds.OwnRole}): pick characters without a guild (MU_TEST_CHARACTER / MU_TEST_FRIEND_CHARACTER).");
    Report(false, "Both characters start without a guild");
    return 1;
}
Report(true, "Both characters start without a guild");
if (aState.Level < 100) Console.WriteLine("Warning: A is below level 100; the server will refuse guild creation.");

Report(await WarpToDeviasAsync(a, aState), "A is in Devias", $"map {aState.MapId} [{aState.PositionX},{aState.PositionY}]");
Console.WriteLine($"B stays on map {bState.MapId} [{bState.PositionX},{bState.PositionY}].");

// 1. Guild master NPC: talk (0x30) → ShowGuildMasterDialog (0x54).
NpcScopeObject? master = null;
await WaitFor(() => (master = a.GetScopeManager().GetScopeItems(ScopeObjectType.Npc).OfType<NpcScopeObject>().FirstOrDefault(n => n.TypeNumber == GuildMasterNpc)) != null, 5);
Report(master != null, "Guild Master NPC (241) in A's scope", master != null ? $"id {master.Id:X4} at [{master.PositionX},{master.PositionY}]" : "");
bool masterDialog = false, creationDialog = false;
aGuilds.GuildMasterDialogRequested += () => masterDialog = true;
aGuilds.GuildCreationDialogRequested += () => creationDialog = true;
(bool Success, byte Error)? creation = null;
aGuilds.GuildCreationResult += (ok, error) => creation = (ok, error);
string guildName = "T" + DateTime.Now.ToString("mmss") + "G";

if (master != null)
{
    await aService.SendTalkToNpcRequestAsync(master.Id);
    Report(await WaitFor(() => masterDialog), "ShowGuildMasterDialog (0x54) after talking to the NPC");

    // 2. Accept: CloseNpcRequest (0x31) + GuildMasterAnswer(true) → ShowGuildCreationDialog (0x55).
    await aService.SendCloseNpcRequestAsync();
    await aService.SendGuildMasterAnswerAsync(true);
    Report(await WaitFor(() => creationDialog), "ShowGuildCreationDialog (0x55) after accepting (close-NPC workaround)");

    // 3. Create (0x55 C2S) → GuildCreationResult (0x56), AssignCharacterToGuild (0x65) for self, GuildInformation (0x66).
    var mark = new byte[64];
    for (int i = 0; i < mark.Length; i++) mark[i] = (byte)(1 + (i + i / 8) % 15);
    await aService.SendGuildCreateAsync(guildName, GuildInfo.PackMark(mark));
    Report(await WaitFor(() => creation.HasValue), "GuildCreationResult (0x56)", $"{creation}");
    Report(creation?.Success == true, "Guild created", guildName);
    Report(await WaitFor(() => aGuilds.HasGuild && aGuilds.IsGuildMaster), "AssignCharacterToGuild (0x65): A is guild master", $"guild {aGuilds.OwnGuildId}, role {aGuilds.OwnRole}");
    Report(await WaitFor(() => aGuilds.OwnGuild?.Name == guildName), "GuildInformation (0x66): own guild name cached", aGuilds.OwnGuild?.Name ?? "-");
    Report(aGuilds.OwnGuild?.Mark.SequenceEqual(mark) == true, "Guild mark round trip (pack/unpack, nibble order)");
}

// 4. B sees A's guild (0x65 + 0x66 on B's side). B may be too low for the Devias warp: A joins B's map instead.
if (aGuilds.HasGuild && aState.MapId != bState.MapId)
{
    ushort warp = bState.MapId == 0 ? (ushort)2 : DeviasWarp; // Lorencia / Devias
    await aService.SendWarpCommandRequestAsync(warp);
    await WaitFor(() => aState.MapId == bState.MapId, 10);
    await a.SendClientReadyAfterMapChangeAsync();
    await WaitFor(() => false, 3);
    Report(aState.MapId == bState.MapId, "A joins B's map", $"A map {aState.MapId} [{aState.PositionX},{aState.PositionY}], B map {bState.MapId} [{bState.PositionX},{bState.PositionY}]");
}
if (aGuilds.HasGuild)
{
    ushort aIdOnB = 0;
    await WaitFor(() => (aIdOnB = b.GetScopeManager().GetScopeItems(ScopeObjectType.Player).OfType<PlayerScopeObject>().FirstOrDefault(p => p.Name == aName)?.Id ?? 0) != 0, 10);
    Report(aIdOnB != 0, "A is in B's scope", $"id {aIdOnB:X4}; B sees players [{string.Join(", ", b.GetScopeManager().GetScopeItems(ScopeObjectType.Player).OfType<PlayerScopeObject>().Select(p => $"{p.Name}@{p.PositionX},{p.PositionY}"))}], A sees [{string.Join(", ", a.GetScopeManager().GetScopeItems(ScopeObjectType.Player).OfType<PlayerScopeObject>().Select(p => p.Name))}]");
    Report(await WaitFor(() => bGuilds.TryGetPlayerGuild(aIdOnB, out var g, out _) && g?.Name == guildName), "B sees A's guild name (0x65 + 0x66)",
        bGuilds.GetPlayerGuildName(aIdOnB));

    // 5. Join: B → GuildJoinRequest (0x50) to A, A accepts, B gets GuildJoinResponse (0x51) and 0x65.
    (ushort Id, string Name)? joinRequest = null;
    aGuilds.GuildJoinRequestReceived += (id, name) => joinRequest = (id, name);
    byte? joinResult = null;
    bGuilds.GuildJoinResult += r => joinResult = r;
    await bService.SendGuildJoinRequestAsync(aIdOnB);
    Report(await WaitFor(() => joinRequest.HasValue), "GuildJoinRequest (0x50) reaches the guild master", $"{joinRequest}");
    if (joinRequest is { } request)
    {
        await aService.SendGuildJoinResponseAsync(true, request.Id);
        Report(await WaitFor(() => joinResult.HasValue), "GuildJoinResponse (0x51) on the requester", $"result={joinResult}");
        Report(await WaitFor(() => bGuilds.HasGuild && bGuilds.OwnGuildId == aGuilds.OwnGuildId), "B is assigned to A's guild (0x65)", $"role {bGuilds.OwnRole}");
    }

    // 6. Member list (0x52).
    await aService.SendGuildListRequestAsync();
    Report(await WaitFor(() => aGuilds.GetMembers().Any(m => m.Name == bName) && aGuilds.GetMembers().Any(m => m.Name == aName && m.Role == GuildMemberRole.GuildMaster)),
        "GuildList (0x52) contains master and member",
        string.Join(", ", aGuilds.GetMembers().Select(m => $"{m.Name}:{m.Role}:{(m.IsOnline ? "on" : "off")}")));

    // 7. Kick B (0x53 → 0x53 result, 0x5D for B).
    byte? kickResult = null, kickedResult = null;
    aGuilds.GuildKickResult += r => kickResult = r;
    bGuilds.GuildKickResult += r => kickedResult = r;
    await aService.SendGuildKickPlayerAsync(bName, string.Empty);
    Report(await WaitFor(() => !bGuilds.HasGuild), "GuildMemberLeftGuild (0x5D): B is no longer in the guild");
    Report(await WaitFor(() => kickedResult.HasValue), "GuildKickResponse (0x53) KickSucceeded on the kicked player", $"result={kickedResult}");
    Report(await WaitFor(() => aGuilds.GetMembers().All(m => m.Name != bName)), "Master's member list refreshed after the kick",
        string.Join(", ", aGuilds.GetMembers().Select(m => m.Name)));

    // 8. Disband (master kicks himself).
    kickResult = null;
    await aService.SendGuildKickPlayerAsync(aName, string.Empty);
    Report(await WaitFor(() => kickResult.HasValue), "GuildKickResponse (0x53) for disbanding", $"result={kickResult}");
    Report(await WaitFor(() => !aGuilds.HasGuild), "Guild disbanded: A is no longer in a guild");
}

await a.DisposeAsync();
await b.DisposeAsync();
Console.WriteLine(failures == 0 ? "Guild live checks passed." : $"Guild live checks: {failures} failure(s).");
return failures == 0 ? 0 : 1;
