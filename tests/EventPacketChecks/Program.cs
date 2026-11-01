// Offline checks for the event packets (Chaos Castle, mini game opening state and scores): packets built from the ServerPackets specification are routed
// through the real PacketRouter (so code/sub-code registration is covered) and the resulting client state is checked.
using System.Buffers;
using System.Buffers.Binary;
using System.Reflection;
using System.Text;
using Client.Main;
using Client.Main.Controls.UI.Game;
using Client.Main.Configuration;
using Client.Main.Controllers;
using Client.Main.Core.Client;
using Client.Main.Core.Models;
using Client.Main.Networking;
using Client.Main.Networking.PacketHandling;
using Microsoft.Extensions.Logging.Abstractions;

var logs = NullLoggerFactory.Instance;
var settings = new MuOnlineSettings { ProtocolVersion = "Season6" };
var state = new CharacterState(logs) { Id = 0x0042, Name = "Tester" };
var network = new NetworkManager(logs, settings, state, new ScopeManager(logs, state));
var router = (PacketRouter)typeof(NetworkManager).GetField("_packetRouter", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(network)!;
var dispatcher = (MainThreadDispatcher)typeof(MuGame).GetField("_mainThreadDispatcher", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
router.SetRoutingMode(false);

int failures = 0;
void Check(bool ok, string name, string detail = "")
{
    if (!ok) failures++;
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {name}{(detail.Length > 0 ? " - " + detail : "")}");
}

async Task Route(byte[] packet)
{
    await router.RoutePacketAsync(new ReadOnlySequence<byte>(packet));
    dispatcher.ProcessPending();
}

byte[] C1(params byte[] body)
{
    var data = new byte[body.Length + 2];
    data[0] = 0xC1;
    data[1] = (byte)data.Length;
    body.CopyTo(data, 2);
    return data;
}

byte[] C2(byte code, byte[] body)
{
    var data = new byte[body.Length + 4];
    data[0] = 0xC2;
    BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(1), (ushort)data.Length);
    data[3] = code;
    body.CopyTo(data, 4);
    return data;
}

// 0x91 MiniGameOpeningState: Chaos Castle open in 75 minutes (two-byte minutes), Devil Square open now.
(byte Type, int Minutes, byte Users)? opening = null;
state.MiniGameOpeningStateReceived += (t, m, u) => opening = (t, m, u);
await Route(C1(0x91, 4, 0, 12, 75));
Check(opening == (4, 75, 12), "91 Chaos Castle opening state (minutes high/low bytes)", $"{opening}");
await Route(C1(0x91, 1, 0, 3, 0));
Check(opening == (1, 0, 3), "91 Devil Square opening state", $"{opening}");

// 0x9B with Chaos Castle states drives the time HUD in Chaos Castle mode.
var hud = BloodCastleTimeControl.Instance;
var cc = new byte[13];
cc[0] = 0xC1; cc[1] = 13; cc[2] = 0x9B; cc[3] = 5;
BinaryPrimitives.WriteUInt16LittleEndian(cc.AsSpan(4), 600);
BinaryPrimitives.WriteUInt16LittleEndian(cc.AsSpan(6), 100);
BinaryPrimitives.WriteUInt16LittleEndian(cc.AsSpan(8), 42);
cc[10] = 0xFF; cc[11] = 0xFF; cc[12] = 0xFF;
await Route(cc);
Check(hud.Visible, "9B ChaosCastleStarted shows the time HUD");
cc[3] = 9;
await Route(cc);
Check(hud.Visible, "9B ChaosCastleStageTwo keeps the HUD");
cc[3] = 7;
await Route(cc);
Check(!hud.Visible, "9B ChaosCastleEnded hides the HUD");

// AF/01 ChaosCastleEnterResult and 0x93 score packets must reach their handlers.
await Route(C1(0xAF, 0x01, 2));
var bcScore = new byte[29];
bcScore[0] = 0xC1; bcScore[1] = 29; bcScore[2] = 0x93; bcScore[3] = 1; bcScore[4] = 0xFF;
Encoding.UTF8.GetBytes("Tester").CopyTo(bcScore, 5);
BinaryPrimitives.WriteUInt32LittleEndian(bcScore.AsSpan(17), 1500);
await Route(bcScore);
Check(BloodCastleResultControl.Instance.Visible, "93 BloodCastleScore shows the Blood Castle result");
var table = new byte[5 + 2 * 24];
table[0] = 0xC1; table[1] = (byte)table.Length; table[2] = 0x93; table[3] = 2; table[4] = 2;
Encoding.UTF8.GetBytes("Winner").CopyTo(table, 5);
BinaryPrimitives.WriteUInt32LittleEndian(table.AsSpan(5 + 12), 900);
Encoding.UTF8.GetBytes("Tester").CopyTo(table, 29);
BinaryPrimitives.WriteUInt32LittleEndian(table.AsSpan(29 + 12), 400);
await Route(table);

var unhandled = router.GetUnhandledPacketCounts();
Check(unhandled.Count == 0, "All event packets were routed to a handler", string.Join(", ", unhandled.Select(u => $"{u.Key.Code:X2}-{u.Key.SubCode:X2}")));
Console.WriteLine(failures == 0 ? "Event packet checks passed." : $"Event packet checks: {failures} failure(s).");
return failures == 0 ? 0 : 1;
