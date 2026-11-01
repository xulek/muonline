// Live messenger checks against a running OpenMU server: friends, online state, letters and the chat server.
// Usage (from the repository root):
//   MU_TEST_USER=<account> MU_TEST_PASSWORD=<password> MU_TEST_FRIEND_USER=<account> MU_TEST_FRIEND_PASSWORD=<password> \
//   dotnet run --project tests/MessengerLiveChecks
using System.Reflection;
using System.Text.Json;
using Client.Main;
using Client.Main.Configuration;
using Client.Main.Controllers;
using Client.Main.Core.Client;
using Client.Main.Networking;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

string Env(string name) => Environment.GetEnvironmentVariable(name) ?? throw new Exception($"{name} is required.");
using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(Environment.CurrentDirectory, "Client.Main", "appsettings.json")));
var settings = config.RootElement.GetProperty("MuOnlineSettings").Deserialize<MuOnlineSettings>()!;
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
    if (!await WaitFor(() => serverId.HasValue)) throw new Exception($"{user}: no server list.");
    await network.RequestGameServerConnectionAsync(serverId!.Value);
    if (!await WaitFor(() => network.CurrentState == ClientConnectionState.ConnectedToGameServer)) throw new Exception($"{user}: no game server.");
    await network.SendLoginRequestAsync(user, password);
    if (!await WaitFor(() => loggedIn || failed) || failed) throw new Exception($"{user}: login failed.");
    if (!await WaitFor(() => character != null) || string.IsNullOrEmpty(character)) throw new Exception($"{user}: no character.");
    await network.SendSelectCharacterRequestAsync(character);
    if (!await WaitFor(() => network.CurrentState == ClientConnectionState.InGame)) throw new Exception($"{user}: character selection failed.");
    await network.SendClientReadyAfterMapChangeAsync();
    return (network, state, character);
}

var (a, aState, aName) = await EnterGameAsync(Env("MU_TEST_USER"), Env("MU_TEST_PASSWORD"));
var (b, bState, bName) = await EnterGameAsync(Env("MU_TEST_FRIEND_USER"), Env("MU_TEST_FRIEND_PASSWORD"));
Console.WriteLine($"In game: A={aName}, B={bName}");
var aFriends = a.GetFriendManager();
var bFriends = b.GetFriendManager();
var aService = a.GetCharacterService();
var bService = b.GetCharacterService();

// 1. Messenger initialization (0xC0) after entering the game.
Report(await WaitFor(() => aFriends.IsInitialized && bFriends.IsInitialized), "MessengerInitialization (0xC0) received by both");

// 2. Friend request round trip (0xC1 → 0xC2 → FriendAddResponse → 0xC4 / 0xC1).
if (aFriends.IsFriend(bName))
{
    await aService.SendFriendDeleteAsync(bName);
    Report(await WaitFor(() => !aFriends.IsFriend(bName)), "FriendDeleted (0xC3/01) for an existing friend (cleanup)");
}
if (bFriends.IsFriend(aName))
{
    await bService.SendFriendDeleteAsync(aName);
    await WaitFor(() => !bFriends.IsFriend(aName));
}

string? requester = null;
bFriends.FriendRequestReceived += name => requester = name;
await aService.SendFriendAddRequestAsync(bName);
Report(await WaitFor(() => aFriends.IsFriend(bName)), "FriendAdded (0xC1/01) on the requester");
Report(await WaitFor(() => requester == aName), "FriendRequest (0xC2) on the receiver", $"requester={requester}");
await bService.SendFriendAddResponseAsync(true, aName);
Report(await WaitFor(() => bFriends.IsFriend(aName)), "Receiver lists the requester after accepting");
Report(await WaitFor(() => aFriends.GetFriends().Any(f => f.Name == bName && f.IsOnline)), "FriendOnlineStateUpdate (0xC4): B online on A's list",
    string.Join(", ", aFriends.GetFriends().Select(f => $"{f.Name}:{f.ServerId}")));

// 3. Online state toggle (SetFriendOnlineState 0xC4).
await bService.SendSetFriendOnlineStateAsync(false);
Report(await WaitFor(() => aFriends.GetFriends().Any(f => f.Name == bName && !f.IsOnline)), "B appears offline after SetFriendOnlineState(false)");
await bService.SendSetFriendOnlineStateAsync(true);
Report(await WaitFor(() => aFriends.GetFriends().Any(f => f.Name == bName && f.IsOnline)), "B appears online again");

// 4. Letters: send (0xC5), receive (0xC6), read (0xC7), delete (0xC8) with index shifting.
// Remove letters left over by earlier runs first.
foreach (var old in bFriends.GetLetters().Where(l => l.Subject.StartsWith("Live test")).OrderByDescending(l => l.Index).ToList())
{
    await bService.SendLetterDeleteRequestAsync(old.Index);
    await WaitFor(() => bFriends.GetLetters().All(l => l.Subject != old.Subject || l.Sender != old.Sender || l.Timestamp != old.Timestamp), 5);
}
byte? sendResult = null;
aFriends.LetterSendResult += r => sendResult = r;
string stamp = DateTime.Now.ToString("HHmmss");
var subjects = new[] { $"Live test {stamp} A", $"Live test {stamp} B" };
var bodies = new[] { "Hello from the messenger live check.\nSecond line.", "Second letter body." };
int lettersBefore = bFriends.GetLetters().Count;
for (int i = 0; i < 2; i++)
{
    sendResult = null;
    await aService.SendLetterAsync(bName, subjects[i], bodies[i]);
    Report(await WaitFor(() => sendResult.HasValue), $"LetterSendResponse (0xC5) for letter {i + 1}", $"result={sendResult}");
}
LetterEntry? first = null, second = null;
Report(await WaitFor(() => (first = bFriends.GetLetters().FirstOrDefault(l => l.Subject == subjects[0])) != null &&
                           (second = bFriends.GetLetters().FirstOrDefault(l => l.Subject == subjects[1])) != null),
    "AddLetter (0xC6) for both letters", $"letters {lettersBefore} -> {bFriends.GetLetters().Count}");
if (first != null && second != null)
{
    Report(first.Sender == aName && !first.IsRead, "Letter header (sender, unread)", $"sender={first.Sender}, read={first.IsRead}, time={first.Timestamp}, index={first.Index}/{second.Index}");
    await bService.SendLetterReadRequestAsync(first.Index);
    Report(await WaitFor(() => first.Message != null), "OpenLetter (0xC7) text received");
    Report(first.Message?.Replace("\r", "") == bodies[0], "Letter text matches", $"\"{first.Message}\"");
    ushort secondIndexBefore = second.Index;
    await bService.SendLetterDeleteRequestAsync(first.Index);
    Report(await WaitFor(() => bFriends.GetLetters().All(l => l.Subject != subjects[0])), "RemoveLetter (0xC8)");
    Report(second.Index == secondIndexBefore - 1, "Later letter index shifted down", $"{secondIndexBefore} -> {second.Index}");
    await bService.SendLetterReadRequestAsync(second.Index);
    Report(await WaitFor(() => second.Message != null) && second.Message == bodies[1], "Reading the shifted letter returns its own text", $"\"{second.Message}\"");
    await bService.SendLetterDeleteRequestAsync(second.Index);
    Report(await WaitFor(() => bFriends.GetLetters().All(l => l.Subject != subjects[1])), "Deleting the shifted letter");
}

// 5. Chat room: create (0xCA), connect to the chat server, exchange messages, leave.
ChatRoomInvite? aInvite = null, bInvite = null;
aFriends.ChatRoomOpened += i => aInvite = i;
bFriends.ChatRoomOpened += i => bInvite = i;
await aService.SendChatRoomCreateRequestAsync(bName);
Report(await WaitFor(() => aInvite != null && bInvite != null), "ChatRoomConnectionInfo (0xCA) on both sides",
    $"A={aInvite}, B={bInvite}");
if (aInvite != null && bInvite != null)
{
    await using var aChat = new ChatRoomSession(logs, aInvite.RoomId, aInvite.Token, aInvite.FriendName);
    await using var bChat = new ChatRoomSession(logs, bInvite.RoomId, bInvite.Token, bInvite.FriendName);
    var aMessages = new List<(string, string)>();
    var bMessages = new List<(string, string)>();
    aChat.MessageReceived += (s, m) => { lock (aMessages) aMessages.Add((s, m)); };
    bChat.MessageReceived += (s, m) => { lock (bMessages) bMessages.Add((s, m)); };
    bool aLeftSeen = false;
    bChat.ClientChanged += (n, joined) => { if (n == aName && !joined) aLeftSeen = true; };

    Report(await aChat.ConnectAsync(aInvite.Host), "A connects to the chat server", aInvite.Host);
    Report(await bChat.ConnectAsync(bInvite.Host), "B connects to the chat server", bInvite.Host);
    Report(await WaitFor(() => aChat.GetClientNames().Count >= 2 && bChat.GetClientNames().Count >= 2), "Chat room member list (C2 02 / C1 01 00)",
        $"A sees [{string.Join(", ", aChat.GetClientNames())}], B sees [{string.Join(", ", bChat.GetClientNames())}]");

    string hello = "hello " + DateTime.Now.Second;
    await aChat.SendMessageAsync(hello);
    Report(await WaitFor(() => { lock (bMessages) return bMessages.Any(m => m.Item2 == hello); }), "B receives A's chat message (XOR-3)",
        string.Join(" | ", bMessages.Select(m => $"{m.Item1}: {m.Item2}")));
    Report(await WaitFor(() => { lock (aMessages) return aMessages.Any(m => m.Item2 == hello && m.Item1 == aName); }), "Server echoes the message to the sender with its name");
    await bChat.SendMessageAsync("reply ąę");
    Report(await WaitFor(() => { lock (aMessages) return aMessages.Any(m => m.Item2 == "reply ąę" && m.Item1 == bName); }), "A receives B's reply (UTF-8)");

    // Chat room invitation (0xCB). A third online account is needed for a successful invite; inviting
    // a character that is not a friend must come back as a failed FriendInvitationResult with our request id.
    (bool Success, uint RequestId)? invitation = null;
    aFriends.ChatInvitationResult += (ok, id) => invitation = (ok, id);
    await aService.SendChatRoomInvitationAsync(Environment.GetEnvironmentVariable("MU_TEST_INVITE") ?? "NoSuchChr", aInvite.RoomId, 4242);
    Report(await WaitFor(() => invitation.HasValue), "FriendInvitationResult (0xCB) for a chat invitation", $"{invitation}");
    if (Environment.GetEnvironmentVariable("MU_TEST_INVITE") is null)
        Report(invitation is { Success: false, RequestId: 4242 }, "Invitation of a non-friend fails and echoes the request id");

    await aChat.LeaveAsync();
    Report(await WaitFor(() => aLeftSeen), "LeaveChatRoom: B sees A leaving (C1 01 01)");
}

// 6. Cleanup: remove the friendship again.
await aService.SendFriendDeleteAsync(bName);
Report(await WaitFor(() => !aFriends.IsFriend(bName)), "Cleanup: friend removed");
await bService.SendFriendDeleteAsync(aName);
await WaitFor(() => !bFriends.IsFriend(aName), 5);

await a.DisposeAsync();
await b.DisposeAsync();
Console.WriteLine(failures == 0 ? "Messenger live checks passed." : $"Messenger live checks: {failures} failure(s).");
return failures == 0 ? 0 : 1;
