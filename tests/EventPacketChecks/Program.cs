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
using Client.Main.Core.Utilities;
using Client.Main.Networking;
using Client.Main.Networking.PacketHandling;
using Client.Main.Networking.PacketHandling.Handlers;
using Client.Main.Models;
using Client.Main.Objects;
using Client.Main.Objects.Monsters;
using Client.Main.Objects.Worlds.Events;
using Microsoft.Xna.Framework;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xna.Framework.Audio;

var logs = NullLoggerFactory.Instance;
var settings = new MuOnlineSettings { ProtocolVersion = "Season6" };
var state = new CharacterState(logs) { Id = 0x0042, Name = "Tester" };
var scope = new ScopeManager(logs, state);
var network = new NetworkManager(logs, settings, state, scope);
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

await Route(C1(0x16, 0, 1, 0, 42, 0, 0));
Check(state.Experience == 42 && !scope.GetScopeItems(ScopeObjectType.Npc).Any(),
      "16 routes experience gain rather than spawning NPCs");

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

// Event audio state changes are routed on the main thread. Disable actual audio
// so these protocol checks run without an audio device or game assets.
bool effectsEnabled = Constants.SOUND_EFFECTS;
bool musicEnabled = Constants.BACKGROUND_MUSIC;
Constants.SOUND_EFFECTS = false;
Constants.BACKGROUND_MUSIC = false;
state.AddOrUpdateChaosMachineItem(0, [0xE0, 13, 0, 0, 0]);
int beforeMix = dispatcher.PendingCount;
await router.RoutePacketAsync(new ReadOnlySequence<byte>(C2(0x31, [3, 0])));
Check(dispatcher.PendingCount == beforeMix + 1 && !state.GetChaosMachineItems().Any(),
      "31 empty chaos mix result clears the box and schedules its sounds");
dispatcher.ProcessPending();
beforeMix = dispatcher.PendingCount;
await router.RoutePacketAsync(new ReadOnlySequence<byte>(C2(0x31, [5, 0])));
Check(dispatcher.PendingCount == beforeMix + 1, "31 resurrection failure schedules mix sounds");
dispatcher.ProcessPending();
beforeMix = dispatcher.PendingCount;
await router.RoutePacketAsync(new ReadOnlySequence<byte>(C2(0x31, [0, 0])));
Check(dispatcher.PendingCount == beforeMix, "31 normal empty store does not schedule mix sounds");
dispatcher.ProcessPending();
var modifiedItemSound = typeof(InventoryHandler).GetMethod("GetModifiedItemSound", BindingFlags.Static | BindingFlags.NonPublic)!;
string? ModifiedItemSound(params byte[] item) => (string?)modifiedItemSound.Invoke(null, [item]);
Check(ModifiedItemSound(0xE0, 28, 0, 0, 0) == "Sound/kundunitem.wav"
      && ModifiedItemSound(0xE0, 111, 0, 0, 0) == "Sound/kundunitem.wav"
      && ModifiedItemSound(0xE0, 13, 0, 0, 0) == "Sound/eGem.wav"
      && ModifiedItemSound(0xD0, 28, 0, 0, 0) == "Sound/eGem.wav"
      && ModifiedItemSound(0xE0) == null,
      "F3:14 modified item selects the reference sound by group and number");
int beforeModifiedItem = dispatcher.PendingCount;
await router.RoutePacketAsync(new ReadOnlySequence<byte>(C1(0xF3, 0x14, 12, 0xE0, 28, 0, 0, 0)));
Check(dispatcher.PendingCount == beforeModifiedItem + 1
      && state.GetInventoryItems().TryGetValue(12, out var modifiedItem) && modifiedItem[1] == 28,
      "F3:14 updates the item and schedules its sound");
dispatcher.ProcessPending();
state.MapId = 45;
var musicRequestId = typeof(SoundController).GetField("_backgroundMusicRequestId", BindingFlags.Instance | BindingFlags.NonPublic)!;
int beforeTempleEnd = (int)musicRequestId.GetValue(SoundController.Instance)!;
await Route(C1(0xBF, 0x09, 0, 3));
Check((int)musicRequestId.GetValue(SoundController.Instance)! == beforeTempleEnd,
      "Illusion Temple end leaves the current music request untouched");
// PRECEIVE_REPAIR_GOLD and PRECEIVE_PLAY_SOUND_EFFECT use native fields;
// exercise both supported header layouts, plus truncated packets, through the router.
int beforeRepairSound = dispatcher.PendingCount;
await router.RoutePacketAsync(new ReadOnlySequence<byte>(C1(0x34, 0x78, 0x56, 0x34, 0x12)));
Check(dispatcher.PendingCount == beforeRepairSound + 1, "34 nonzero repair schedules its sound");
dispatcher.ProcessPending();
Check(state.InventoryZen == 0x12345678, "34 packed repair gold is little-endian");
await Route(C1(0x34, 0x00, 0xEF, 0xCD, 0xAB, 0x09));
Check(state.InventoryZen == 0x09ABCDEF, "34 aligned repair gold is little-endian");
beforeRepairSound = dispatcher.PendingCount;
await router.RoutePacketAsync(new ReadOnlySequence<byte>(C1(0x34, 0, 0, 0, 0)));
Check(dispatcher.PendingCount == beforeRepairSound, "34 zero result does not schedule repair sound");
dispatcher.ProcessPending();
Check(state.InventoryZen == 0x09ABCDEF, "34 zero result preserves gold");
await Route(C1(0x34, 0x11, 0x22));
Check(state.InventoryZen == 0x09ABCDEF, "34 truncated result preserves gold");
async Task CheckSoundPacket(byte[] packet, bool expected, string name)
{
    int pending = dispatcher.PendingCount;
    await router.RoutePacketAsync(new ReadOnlySequence<byte>(packet));
    Check(dispatcher.PendingCount - pending == (expected ? 1 : 0), name);
    dispatcher.ProcessPending();
}
await CheckSoundPacket(C1(0x9E, 0, 0), true, "9E packed ready effect schedules playback");
await CheckSoundPacket(C1(0x9E, 1, 0), true, "9E packed known effect schedules playback");
await CheckSoundPacket(C1(0x9E, 0, 2, 0), true, "9E aligned known effect schedules playback");
await CheckSoundPacket(C1(0x9E, 3, 0), false, "9E unknown effect is ignored");
await CheckSoundPacket(C1(0x9E, 0), false, "9E truncated effect is ignored");
var eventSoundPath = typeof(MiscGamePacketHandler).GetMethod("GetEventSoundPath", BindingFlags.Static | BindingFlags.NonPublic)!;
string? EventSound(ushort effect) => (string?)eventSoundPath.Invoke(null, [effect]);
Check(EventSound(0) == "Sound/iEvent3min.wav" && EventSound(1) == "Sound/iEventStart.wav"
      && EventSound(2) == "Sound/iEventEnd.wav" && EventSound(3) == null,
      "9E ready, start and end effects select reference sound files");
await Route(C1(0x69, 0, 63, 0x42, 0, 0x43, 0));
await Route(C1(0x69, 0, 63, 0, 0x42, 0, 0x43, 0));
await Route(C1(0x69, 0));
var parseMonsterSkill = typeof(ScopeHandler).GetMethod("ParseMonsterSkill", BindingFlags.Static | BindingFlags.NonPublic)!;
object? ParseMonsterSkill(byte[] bytes) => parseMonsterSkill.Invoke(null, [bytes.AsMemory()]);
Check(ParseMonsterSkill(C1(0x69, 0x01, 0x3F, 0x34, 0x12, 0x78, 0xD6)) is ValueTuple<ushort, ushort, ushort> packed69
      && packed69 == (0x013F, 0x1234, 0x5678), "69 packed fields and target success flag are decoded");
Check(ParseMonsterSkill(C1(0x69, 0x01, 0x3F, 0xAA, 0x34, 0x12, 0x78, 0xD6)) is ValueTuple<ushort, ushort, ushort> aligned69
      && aligned69 == (0x013F, 0x1234, 0x5678), "69 aligned fields are decoded");
Check(ParseMonsterSkill(C1(0x69, 0)) == null, "69 truncated fields are rejected");
var resolveMonsterSkill = typeof(ScopeHandler).GetMethod("ResolveImperialMonsterSkill", BindingFlags.Static | BindingFlags.NonPublic)!;
var resolveKanturuSkill = typeof(ScopeHandler).GetMethod("ResolveKanturuMonsterSkill", BindingFlags.Static | BindingFlags.NonPublic)!;
var resolveRaklionSkill = typeof(ScopeHandler).GetMethod("ResolveRaklionMonsterSkill", BindingFlags.Static | BindingFlags.NonPublic);
var selupan = new SelupanBoss();
Check(NpcDatabase.TryGetNpcType(459, out var selupanType) && selupanType == typeof(SelupanBoss),
      "Selupan is instantiated by normal monster scope registration");
var selupanActions = new[]
{
    MonsterActionType.Attack2, MonsterActionType.Attack1, MonsterActionType.Attack3,
    MonsterActionType.Appear, MonsterActionType.Attack4, MonsterActionType.Attack4,
    MonsterActionType.Attack4, (MonsterActionType?)null, MonsterActionType.Attack4,
};
Check(resolveRaklionSkill != null && selupanActions.Select((action, i) =>
        (MonsterActionType?)resolveRaklionSkill.Invoke(null, [selupan, (ushort)(34 + i)]) == action).All(x => x)
      && resolveRaklionSkill.Invoke(null, [new Nightmare(), (ushort)35]) == null,
      "69 Raklion skills select Selupan phases, including appearance and the unused slot");
Check(resolveRaklionSkill != null && new (MonsterObject Actor, ushort Skill)[]
      {
          (new IceWalker(), 29), (new GiantMammoth(), 30), (new IceGiant(), 31),
          (new Coolutin(), 32), (new IronKnight(), 33),
      }.All(s => (MonsterActionType?)resolveRaklionSkill.Invoke(null, [s.Actor, s.Skill]) == MonsterActionType.Attack2),
      "69 selects the Raklion field skill animations by monster and skill");
var resolveCrywolfSkill = typeof(ScopeHandler).GetMethod("ResolveCrywolfMonsterSkill", BindingFlags.Static | BindingFlags.NonPublic);
Check(resolveCrywolfSkill != null &&
      (MonsterActionType?)resolveCrywolfSkill.Invoke(null, [new Balgass(), (ushort)12]) == MonsterActionType.Attack1 &&
      (MonsterActionType?)resolveCrywolfSkill.Invoke(null, [new Balgass(), (ushort)13]) == MonsterActionType.Attack2 &&
      resolveCrywolfSkill.Invoke(null, [new Balgass(), (ushort)14]) == null,
      "69 uses the supplied Balgass slots without borrowing the Kanturu skill 14");
Check((MonsterActionType?)resolveKanturuSkill.Invoke(null, [new Nightmare(), (ushort)18]) == MonsterActionType.Attack1
      && (MonsterActionType?)resolveKanturuSkill.Invoke(null, [new Nightmare(), (ushort)19]) == MonsterActionType.Attack2
      && (MonsterActionType?)resolveKanturuSkill.Invoke(null, [new Nightmare(), (ushort)20]) == MonsterActionType.Attack3
      && (MonsterActionType?)resolveKanturuSkill.Invoke(null, [new Nightmare(), (ushort)21]) == MonsterActionType.Attack4
      && resolveKanturuSkill.Invoke(null, [new Nightmare(), (ushort)22]) == null,
      "69 Nightmare skill slots select attacks one through four");
var applyMonsterSkill = typeof(MonsterObject).GetMethod("ApplyMonsterSkill", BindingFlags.Instance | BindingFlags.NonPublic)!;
var recordingMonster = new RecordingSkillMonster();
bool callbacksMatch = true;
foreach (var (action, expectedAttack) in new[]
{
    (MonsterActionType.Attack1, 1), (MonsterActionType.Attack2, 2),
    (MonsterActionType.Attack3, 3), (MonsterActionType.Attack4, 4),
})
{
    callbacksMatch &= applyMonsterSkill.Invoke(recordingMonster, [action, (ushort)(17 + expectedAttack), (ushort)0x1234, 1.0, null]) is true
        && recordingMonster.LastAttack == expectedAttack && recordingMonster.CurrentAction == (int)action
        && recordingMonster.LastAttackTargetId == 0x1234;
}
applyMonsterSkill.Invoke(recordingMonster, [MonsterActionType.Appear, (ushort)59, (ushort)0x4321, 1.5, null]);
Check(callbacksMatch && recordingMonster.AppearCount == 1 && recordingMonster.LastAttack == 4
      && recordingMonster.CurrentAction == (int)MonsterActionType.Appear && recordingMonster.LastAttackTargetId == 0x4321,
      "69 applies attack variants and appearance through the actual monster callbacks");
recordingMonster.PlayAction((ushort)MonsterActionType.Die, fromServer: true);
applyMonsterSkill.Invoke(recordingMonster, [MonsterActionType.Attack2, (ushort)19, (ushort)0x5678, 1.7, null]);
Check(recordingMonster.CurrentAction == (int)MonsterActionType.Die && recordingMonster.LastAttack == 4
      && recordingMonster.LastAttackTargetId == 0x4321,
      "69 rejected during death does not trigger attack callback or replace target");

var applyNormalMonsterAttack = typeof(MonsterObject).GetMethod("ApplyNormalAttack", BindingFlags.Instance | BindingFlags.NonPublic);
var applyTargetedMonsterAttack = typeof(MonsterObject).GetMethod("ApplyTargetedAttack", BindingFlags.Instance | BindingFlags.NonPublic);
bool NormalAttack(RecordingSkillMonster monster, double time, ushort target = 0x8042) =>
    applyNormalMonsterAttack?.Invoke(monster, [target, time, Client.Main.Models.Direction.South]) is true;
bool TargetedAttack(RecordingSkillMonster monster, double time, ushort target = 0x8042) =>
    applyTargetedMonsterAttack?.Invoke(monster, [(ushort)17, target, time, null]) is true;

var ordinaryAttacker = new RecordingSkillMonster();
Check(NormalAttack(ordinaryAttacker, 1.0) && ordinaryAttacker.LastAttack == 1
      && NormalAttack(ordinaryAttacker, 1.05) && ordinaryAttacker.LastAttack == 2
      && NormalAttack(ordinaryAttacker, 1.10) && ordinaryAttacker.LastAttack == 2
      && NormalAttack(ordinaryAttacker, 1.15) && ordinaryAttacker.LastAttack == 1
      && ordinaryAttacker.AttackCount == 4 && ordinaryAttacker.LastAttackTargetId == 0x42,
      "18 varies basic attacks deterministically and keeps distinct rapid attacks and the masked target");

var spellAttacker = new RecordingSkillMonster();
Check(NormalAttack(spellAttacker, 2.0) && TargetedAttack(spellAttacker, 2.04)
      && spellAttacker.AttackCount == 1 && spellAttacker.LastAttack == 1
      && NormalAttack(spellAttacker, 2.10) && TargetedAttack(spellAttacker, 2.11)
      && spellAttacker.AttackCount == 2 && spellAttacker.LastAttack == 2,
      "18 followed by 19 enriches each spell attack without replaying animation or attack audio");

var reverseOrderCaster = new RecordingSkillMonster();
Check(TargetedAttack(reverseOrderCaster, 3.0) && NormalAttack(reverseOrderCaster, 3.04)
      && reverseOrderCaster.AttackCount == 1 && NormalAttack(reverseOrderCaster, 3.09)
      && reverseOrderCaster.AttackCount == 2 && TargetedAttack(reverseOrderCaster, 3.10, 0x8043)
      && reverseOrderCaster.AttackCount == 3 && reverseOrderCaster.LastAttackTargetId == 0x43,
      "reverse packet order uses one companion and a different spell target starts an independent attack");

var interruptedAttacker = new RecordingSkillMonster();
Check(NormalAttack(interruptedAttacker, 4.0)
      && NormalAttack(interruptedAttacker, 4.25)
      && TargetedAttack(interruptedAttacker, 4.65) && interruptedAttacker.AttackCount == 3,
      "a late spell does not attach to the previous basic attack");
interruptedAttacker.PlayAction((ushort)MonsterActionType.Die, fromServer: true);
Check(!NormalAttack(interruptedAttacker, 4.70, 0x8043) && !TargetedAttack(interruptedAttacker, 4.71, 0x8043)
      && interruptedAttacker.AttackCount == 3 && interruptedAttacker.LastAttackTargetId == 0x42,
      "18 and 19 during death cannot set a new target or trigger attack audio");

var advanceMonsterAttack = typeof(MonsterObject).GetMethod("AdvanceAttack", BindingFlags.Instance | BindingFlags.NonPublic)!;
void AdvanceAttack(RecordingSkillMonster monster, double time, double frame = 0, bool completed = false) =>
    advanceMonsterAttack.Invoke(monster, [time, frame, completed]);

bool equalMarkersAtEachRate = true;
foreach (var fps in new[] { 30, 60, 144 })
{
    var attacker = new RecordingSkillMonster();
    NormalAttack(attacker, 10.0);
    for (int frame = 0; frame <= fps / 2; frame++)
        AdvanceAttack(attacker, 10.0 + frame / (double)fps);
    equalMarkersAtEachRate &= attacker.Markers.SequenceEqual(new[] { 1, 5, 9 });
}
Check(equalMarkersAtEachRate, "single attack markers are independent of 30, 60 and 144 FPS");

var slowFrameAttacker = new RecordingSkillMonster();
NormalAttack(slowFrameAttacker, 12.0);
AdvanceAttack(slowFrameAttacker, 12.32, frame: 5);
AdvanceAttack(slowFrameAttacker, 12.32, frame: 5);
Check(slowFrameAttacker.Markers.SequenceEqual(new[] { 1, 5, 9, 100 }) && slowFrameAttacker.FrameMarkerCount == 1,
      "a skipped time or animation window emits crossed markers in order once");

var lateSkillAttacker = new RecordingSkillMonster();
NormalAttack(lateSkillAttacker, 13.0);
AdvanceAttack(lateSkillAttacker, 13.0);
TargetedAttack(lateSkillAttacker, 13.04);
AdvanceAttack(lateSkillAttacker, 13.08);
AdvanceAttack(lateSkillAttacker, 13.10);
Check(lateSkillAttacker.AttackCount == 1 && lateSkillAttacker.SkillStarts.SequenceEqual(new ushort[] { 17 }),
      "a companion spell received after the first update releases its skill marker once");

var iceCaster = new RecordingSkillMonster();
bool iceStartsAction = applyTargetedMonsterAttack?.Invoke(iceCaster, [(ushort)7, (ushort)0x8042, 13.5d, null]) is true;
bool animationCompletesIcePair = NormalAttack(iceCaster, 13.55);
AdvanceAttack(iceCaster, 13.6);
Check(iceStartsAction && animationCompletesIcePair && iceCaster.AttackCount == 1 &&
      iceCaster.SkillStarts.SequenceEqual(new ushort[] { 7 }),
      "Ice 7 starts a monster attack and the companion animation does not start a second one");

var cancelledAttack = new RecordingSkillMonster();
NormalAttack(cancelledAttack, 14.0);
AdvanceAttack(cancelledAttack, 14.0);
cancelledAttack.PlayAction((ushort)MonsterActionType.Shock, fromServer: true);
AdvanceAttack(cancelledAttack, 14.40, frame: 7);
Check(cancelledAttack.Markers.SequenceEqual(new[] { 1 }), "shock cancels markers from the interrupted attack");

var hydraModel = new Client.Data.BMD.BMD
{
    Bones = [Client.Data.BMD.BMDTextureBone.Dummy],
    Actions = Enumerable.Range(0, 7).Select(_ => new Client.Data.BMD.BMDTextureAction { NumAnimationKeys = 2 }).ToArray(),
    Meshes = Enumerable.Range(0, 7).Select(_ => new Client.Data.BMD.BMDTextureMesh()).ToArray(),
};
hydraModel.Meshes[0].Vertices = [new() { Position = new(-20f, -50f, 0f) }, new() { Position = new(20f, 50f, 80f) }];
hydraModel.Meshes[5].Vertices = [new() { Position = new(0f, -1500f, 0f) }];
hydraModel.Meshes[6].Vertices = [new() { Position = new(0f, 1500f, 0f) }];
var setStaticPose = typeof(ModelObject).GetMethod("SetStaticAnimationPose", BindingFlags.Instance | BindingFlags.NonPublic)!;
var hydraBounds = new BoundingBox(new Vector3(-20f, -50f, 0f), new Vector3(20f, 50f, 80f));
using var idleHydra = new HydraAnimationProbe { Model = hydraModel };
using var attackingHydra = new HydraAnimationProbe { Model = hydraModel };
setStaticPose.Invoke(idleHydra, [(int)MonsterActionType.Stop1, 0]);
setStaticPose.Invoke(attackingHydra, [(int)MonsterActionType.Attack2, 0]);
Check(idleHydra.BoundingBoxLocal == hydraBounds && attackingHydra.BoundingBoxLocal == hydraBounds,
      "Hydra uses its body bounds for picking and culling even while the laser is visible");
var hydraFrame = new GameTime(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(0.1));
attackingHydra.Update(hydraFrame);
bool hydraShowsLaser = attackingHydra.HiddenMesh != 5 && attackingHydra.BlendMeshLight > 0f;
setStaticPose.Invoke(attackingHydra, [(int)MonsterActionType.Shock, 0]);
attackingHydra.Update(hydraFrame);
Check(idleHydra.HiddenMesh == 5 && hydraShowsLaser && attackingHydra.HiddenMesh == 5 && attackingHydra.BlendMeshLight == 0f,
      "Hydra shows its laser mesh during attack and hides it as soon as the attack is interrupted");

using var totem = new TotemGolem();
Check(NpcDatabase.TryGetNpcType(423, out var totemType) && totemType == typeof(TotemGolem) &&
      MathF.Abs(totem.Scale - 0.1615f) < 0.0001f,
      "Elbeland spawn 423 uses the authored world scale for the oversized Totem Golem model");

var unavailableAction = new RecordingSkillMonster
{
    Model = new Client.Data.BMD.BMD
    {
        Actions = new Client.Data.BMD.BMDTextureAction[5]
    }
};
unavailableAction.Model.Actions[(int)MonsterActionType.Attack1] = new Client.Data.BMD.BMDTextureAction { NumAnimationKeys = 2 };
Check(NormalAttack(unavailableAction, 15.0) && NormalAttack(unavailableAction, 15.1) && unavailableAction.LastAttack == 1,
      "a one-attack BMD keeps using its available animation on later basic attacks");

var applyAreaMonsterAttack = typeof(MonsterObject).GetMethod("ApplyAreaAttack", BindingFlags.Instance | BindingFlags.NonPublic);
var resolveAttackPosition = typeof(MonsterObject).GetMethod("TryGetAttackTargetPosition", BindingFlags.Instance | BindingFlags.NonPublic)!;
var areaAttacker = new RecordingSkillMonster();
Vector3 attackPoint = new(350f, 650f, 45f);
object[] attackPosition = [Vector3.Zero];
Check(applyAreaMonsterAttack?.Invoke(areaAttacker, [(ushort)50, attackPoint, 16.0, 90f]) is true
      && areaAttacker.LastAttackTargetId == 0 && resolveAttackPosition.Invoke(areaAttacker, attackPosition) is true
      && (Vector3)attackPosition[0] == attackPoint && NormalAttack(areaAttacker, 17.0)
      && resolveAttackPosition.Invoke(areaAttacker, attackPosition) is false,
      "1E monster casts keep the packet's terrain point and the next attack clears it");

var switchedWorldAttacker = new RecordingSkillMonster();
NormalAttack(switchedWorldAttacker, 18.0);
AdvanceAttack(switchedWorldAttacker, 18.0);
var worldChanged = typeof(MonsterObject).GetMethod("OnWorldChanged", BindingFlags.Instance | BindingFlags.NonPublic)!;
worldChanged.Invoke(switchedWorldAttacker, [null, null]);
AdvanceAttack(switchedWorldAttacker, 18.4, frame: 5);
Check(switchedWorldAttacker.Markers.SequenceEqual(new[] { 1 }),
      "map change cancels markers from the old monster action");

var throttleCombat = typeof(SoundController).GetMethod("ShouldThrottleCombatSound", BindingFlags.Instance | BindingFlags.NonPublic);
bool CombatSoundLimited(string sample, double timeMs) =>
    throttleCombat?.Invoke(SoundController.Instance, [sample, timeMs]) is true;
Check(throttleCombat != null && !CombatSoundLimited("combined-attack.wav", 0d) &&
      !CombatSoundLimited("combined-attack.wav", 1d) && !CombatSoundLimited("combined-attack.wav", 2d) &&
      CombatSoundLimited("combined-attack.wav", 3d) && !CombatSoundLimited("other-attack.wav", 3d) &&
      !CombatSoundLimited("combined-attack.wav", 120d),
      "nearby attacks sharing a WAV can overlap, with a bounded burst that opens again after the interval");

using (var monsterAnimation = new AnimationController(new Nightmare()))
{
    monsterAnimation.PlayAnimation((ushort)MonsterActionType.Attack3, fromServer: true);
    bool attack3OneShot = monsterAnimation.GetAnimationType((ushort)MonsterActionType.Attack3) == AnimationType.Attack
        && monsterAnimation.IsOneShotPlaying;
    monsterAnimation.PlayAnimation((ushort)MonsterActionType.Attack4, fromServer: true);
    Check(attack3OneShot && monsterAnimation.GetAnimationType((ushort)MonsterActionType.Attack4) == AnimationType.Attack
          && monsterAnimation.IsOneShotPlaying,
          "Monster attack 3 and 4 animations are treated as one-shot attacks");
}
Check((MonsterActionType?)resolveKanturuSkill.Invoke(null, [new MayaHandLeft(), (ushort)23]) == MonsterActionType.Attack1
      && (MonsterActionType?)resolveKanturuSkill.Invoke(null, [new MayaHandLeft(), (ushort)24]) == MonsterActionType.Attack2
      && (MonsterActionType?)resolveKanturuSkill.Invoke(null, [new MayaHandRight(), (ushort)23]) == MonsterActionType.Attack1
      && (MonsterActionType?)resolveKanturuSkill.Invoke(null, [new MayaHandRight(), (ushort)24]) == MonsterActionType.Attack2,
      "69 Maya hand skill slots select their two attacks");
Check((MonsterActionType?)resolveMonsterSkill.Invoke(null, [new Erkanne(), (ushort)63]) == MonsterActionType.Attack3
      && (MonsterActionType?)resolveMonsterSkill.Invoke(null, [new Erkanne(), (ushort)62]) == MonsterActionType.Attack2
      && resolveMonsterSkill.Invoke(null, [new Erkanne(), (ushort)99]) == null,
       "69 Erkanne skills select the reference attack variants");
NpcDatabase.TryGetNpcType(516, out var assassinType);
var masterAssassin = (MonsterObject)Activator.CreateInstance(assassinType!, nonPublic: true)!;
Check((MonsterActionType?)resolveMonsterSkill.Invoke(null, [new CombatInstructor(), (ushort)47]) == MonsterActionType.Attack2
      && (MonsterActionType?)resolveMonsterSkill.Invoke(null, [new GaionKharein(), (ushort)67]) == MonsterActionType.Attack1
      && (MonsterActionType?)resolveMonsterSkill.Invoke(null, [new GaionKharein(), (ushort)64]) == MonsterActionType.Attack2
      && (MonsterActionType?)resolveMonsterSkill.Invoke(null, [new GaionKharein(), (ushort)65]) == MonsterActionType.Attack3
      && (MonsterActionType?)resolveMonsterSkill.Invoke(null, [new GaionKharein(), (ushort)66]) == MonsterActionType.Attack4
      && (MonsterActionType?)resolveMonsterSkill.Invoke(null, [new GaionKharein(), (ushort)59]) == MonsterActionType.Appear
      && (MonsterActionType?)resolveMonsterSkill.Invoke(null, [new Jerint(), (ushort)55]) == MonsterActionType.Attack2
      && (MonsterActionType?)resolveMonsterSkill.Invoke(null, [new Jerint(), (ushort)61]) == MonsterActionType.Attack3
      && (MonsterActionType?)resolveMonsterSkill.Invoke(null, [new Jerint(), (ushort)59]) == MonsterActionType.Appear
      && (MonsterActionType?)resolveMonsterSkill.Invoke(null, [new CombatInstructor(), (ushort)49]) == MonsterActionType.Appear
      && (MonsterActionType?)resolveMonsterSkill.Invoke(null, [new KnightCommander(), (ushort)47]) == MonsterActionType.Attack2
      && (MonsterActionType?)resolveMonsterSkill.Invoke(null, [new KnightCommander(), (ushort)50]) == MonsterActionType.Appear
      && (MonsterActionType?)resolveMonsterSkill.Invoke(null, [new ShieldBearer(), (ushort)45]) == MonsterActionType.Attack2
      && (MonsterActionType?)resolveMonsterSkill.Invoke(null, [new ShieldBearer(), (ushort)44]) == MonsterActionType.Appear
      && (MonsterActionType?)resolveMonsterSkill.Invoke(null, [new GrandWizard(), (ushort)51]) == MonsterActionType.Attack2
      && (MonsterActionType?)resolveMonsterSkill.Invoke(null, [new GrandWizard(), (ushort)52]) == MonsterActionType.Attack2
      && (MonsterActionType?)resolveMonsterSkill.Invoke(null, [new Forsaker(), (ushort)46]) == MonsterActionType.Attack2
      && (MonsterActionType?)resolveMonsterSkill.Invoke(null, [new Gallia(), (ushort)58]) == MonsterActionType.Attack2
      && (MonsterActionType?)resolveMonsterSkill.Invoke(null, [new Gallia(), (ushort)60]) == MonsterActionType.Attack3
      && (MonsterActionType?)resolveMonsterSkill.Invoke(null, [new Gallia(), (ushort)59]) == MonsterActionType.Appear
      && (MonsterActionType?)resolveMonsterSkill.Invoke(null, [new CavalryCaptain(), (ushort)54]) == MonsterActionType.Attack2
      && (MonsterActionType?)resolveMonsterSkill.Invoke(null, [new CavalryCaptain(), (ushort)55]) == MonsterActionType.Appear
      && (MonsterActionType?)resolveMonsterSkill.Invoke(null, [new CavalryCaptain(), (ushort)56]) == MonsterActionType.Attack3
      && (MonsterActionType?)resolveMonsterSkill.Invoke(null, [masterAssassin, (ushort)47]) == MonsterActionType.Attack2
      && (MonsterActionType?)resolveMonsterSkill.Invoke(null, [masterAssassin, (ushort)53]) == MonsterActionType.Attack3,
      "69 Imperial Guardian subordinate skills select reference actions");
var misc = typeof(PacketRouter).GetField("_miscGamePacketHandler", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(router)!;
var crywolfResult = misc.GetType().GetField("_lastCrywolfResult", BindingFlags.Instance | BindingFlags.NonPublic)!;
var kanturuSuccess = misc.GetType().GetField("_kanturuSuccessMap", BindingFlags.Instance | BindingFlags.NonPublic)!;
state.MapId = 34;
await Route(C1(0xBD, 0x00, 2, 5));
Check((int)crywolfResult.GetValue(misc)! == -1, "Crywolf end waits while occupation is war");
await Route(C1(0xBD, 0x00, 1, 5));
Check((int)crywolfResult.GetValue(misc)! == 1, "Crywolf end records occupied as defeat");
await Route(C1(0xBD, 0x00, 0, 5));
Check((int)crywolfResult.GetValue(misc)! == 0, "Crywolf end recognizes a late peace result");
await Route(C1(0xBD, 0x00, 2, 4));
Check((int)crywolfResult.GetValue(misc)! == -1, "Crywolf result resets for a new phase");
state.MapId = 39;
await Route(C1(0xD1, 0x00, 4, 1, 0, 0, 0, 0, 0, 0));
Check(!(bool)kanturuSuccess.GetValue(misc)!, "Kanturu entrance information does not change boss map state");
await Route(C1(0xD1, 0x02, 4, 1));
Check((bool)kanturuSuccess.GetValue(misc)!, "Kanturu tower activation enters success state");
await Route(C1(0xD1, 0x03, 4, 2));
Check((bool)kanturuSuccess.GetValue(misc)!, "Kanturu tower notification retains success state");
await Route(C1(0xD1, 0x03, 4, 3));
Check(!(bool)kanturuSuccess.GetValue(misc)!, "Kanturu tower close exits success state");
Constants.SOUND_EFFECTS = effectsEnabled;
Constants.BACKGROUND_MUSIC = musicEnabled;
Check(NpcDatabase.TryGetNpcType(541, out var chest) && chest == typeof(DoppelgangerBox)
      && NpcDatabase.TryGetNpcType(542, out var goldenChest) && goldenChest == typeof(DoppelgangerGoldenBox),
       "Doppelganger compensation boxes are registered by server ID");
Check(NpcDatabase.TryGetNpcType(413, out var lunarRabbit) && lunarRabbit == typeof(LunarRabbit)
      && NpcDatabase.TryGetNpcType(463, out var flameGhost) && flameGhost == typeof(FireFlameGhost),
      "Seasonal Lunar Rabbit and Fire Flame Ghost are registered by server ID");
Check(NpcDatabase.TryGetNpcType(361, out var nightmare) && nightmare == typeof(Nightmare)
      && Math.Abs(new Nightmare().Scale - 1.6f) < 0.001f
      && Math.Abs(new MayaHandLeft().Scale - 2.28f) < 0.001f
      && Math.Abs(new MayaHandRight().Scale - 2.28f) < 0.001f,
      "Kanturu boss IDs and scales match the reference");
var nightmareTeleport = typeof(ScopeHandler).GetMethod("IsNightmareTeleportSpawn", BindingFlags.Static | BindingFlags.NonPublic)!;
bool NightmareTeleport(ushort raw, ushort type, int map) => (bool)nightmareTeleport.Invoke(null, [raw, type, map])!;
Check(NightmareTeleport(0x4042, 361, 39) && !NightmareTeleport(0xC042, 361, 39)
      && !NightmareTeleport(0x0042, 361, 39) && !NightmareTeleport(0x4042, 362, 39)
      && !NightmareTeleport(0x4042, 361, 38),
      "Nightmare teleport sound requires its map, type and teleport-only spawn flag");
var viewportNpcBytes = new byte[MUnique.OpenMU.Network.Packets.ServerToClient.AddNpcsToScope.NpcData.Length];
viewportNpcBytes[0] = 0x40;
viewportNpcBytes[1] = 0x42;
var viewportNpc = new MUnique.OpenMU.Network.Packets.ServerToClient.AddNpcsToScope.NpcData(viewportNpcBytes);
Check(viewportNpc.Id == 0x4042, "Season 6 NPC scope decoder preserves Nightmare teleport flag");
state.MapId = 39;
await Route(C2(0x13, [1, 0x40, 0x42, 0x01, 0x69, 79, 142, 79, 142, 0, 0]));
Check(scope.GetScopeObjectByMaskedId(0x4042) is NpcScopeObject scopedNightmare
      && scopedNightmare.RawId == 0x4042 && scopedNightmare.TypeNumber == 361,
      "Synthetic 13 scope packet preserves Nightmare type and teleport flag through router");
await Route(C2(0x13, [2,
    0x00, 0x70, 0x81, 0x6A, 79, 142, 79, 142, 0, 2, 0x11, 0x22,
    0x40, 0x43, 0x01, 0x69, 80, 142, 80, 142, 0, 0]));
Check(scope.GetScopeObjectByMaskedId(0x0070) is NpcScopeObject firstBuffed && firstBuffed.TypeNumber == 362
      && scope.GetScopeObjectByMaskedId(0x4043) is NpcScopeObject secondNightmare
      && secondNightmare.TypeNumber == 361 && secondNightmare.RawId == 0x4043,
      "13 scope packet masks type flags and skips effects before next NPC");
await Route(C2(0x13, [1, 0, 0x71, 1, 0x69, 80, 142, 80, 142, 0, 2, 0x11]));
Check(scope.GetScopeObjectByMaskedId(0x0071) == null,
      "13 scope packet rejects an NPC with truncated effects");
var truncatedSummon = new byte[21];
truncatedSummon[0] = 1; truncatedSummon[2] = 0x72; truncatedSummon[4] = 0x69; truncatedSummon[20] = 2;
await Route(C2(0x1F, truncatedSummon));
Check(scope.GetScopeObjectByMaskedId(0x0072) == null, "1F scope packet rejects a summon with truncated effects");
var truncatedTransform = new byte[39];
truncatedTransform[0] = 1; truncatedTransform[2] = 0x73; truncatedTransform[5] = 1; truncatedTransform[6] = 0x69; truncatedTransform[38] = 2;
await Route(C2(0x45, truncatedTransform));
Check(scope.GetScopeObjectByMaskedId(0x0073) == null, "45 scope packet rejects a transformed player with truncated effects");
var trapSound = typeof(SoundMonsterObject).GetProperty("AttackSoundPath", BindingFlags.Instance | BindingFlags.NonPublic)!;
Check(NpcDatabase.TryGetNpcType(100, out var lanceTrap) && Activator.CreateInstance(lanceTrap!) is SoundMonsterObject lance
      && (string?)trapSound.GetValue(lance) == "Sound/aGrate.wav"
      && NpcDatabase.TryGetNpcType(101, out var ironTrap) && Activator.CreateInstance(ironTrap!) is SoundMonsterObject iron
      && (string?)trapSound.GetValue(iron) == "Sound/aGrate.wav",
      "Blood Castle mechanical traps use the reference attack sound");
Check(Enumerable.Range(386, 18).All(id => NpcDatabase.TryGetNpcType((ushort)id, out var spiritType)
      && typeof(IllusionTempleSpirit).IsAssignableFrom(spiritType)
      && (id % 3 == 2 ? typeof(RedIllusionSorcereSpirit) : id % 3 == 0
          ? typeof(BlueIllusionSorcereSpirit) : typeof(IllusionSorcereSpirit3)).IsAssignableFrom(spiritType)
      && Activator.CreateInstance(spiritType) is IllusionTempleSpirit),
      "Illusion Temple spirit IDs select the reference model families");
var crossedSpiritFrame = typeof(IllusionTempleSpirit).GetMethod("CrossedSoundFrame", BindingFlags.Static | BindingFlags.NonPublic)!;
bool SpiritCrossed(double previous, double current) => (bool)crossedSpiritFrame.Invoke(null, [previous, current])!;
Check(SpiritCrossed(0.2, 0.8) && !SpiritCrossed(0.8, 1.2)
      && SpiritCrossed(4.0, 0.8) && !SpiritCrossed(4.0, 0.2)
      && SpiritCrossed(-1, 0.8) && !SpiritCrossed(-1, 3.0),
      "Illusion Temple spirit sounds follow animation frame crossings and loops");
Check(NpcDatabase.TryGetNpcType(504, out var gaion) && gaion == typeof(GaionKharein)
      && NpcDatabase.TryGetNpcType(505, out var jerint) && jerint == typeof(Jerint)
      && NpcDatabase.TryGetNpcType(506, out var raymond) && raymond == typeof(Raymond)
      && NpcDatabase.TryGetNpcType(507, out var erkanne) && erkanne == typeof(Erkanne)
      && NpcDatabase.TryGetNpcType(509, out var vermont) && vermont == typeof(Vermont)
      && NpcDatabase.TryGetNpcType(510, out var kato) && kato.Name == "Kato"
      && NpcDatabase.TryGetNpcType(511, out var gallia) && gallia == typeof(Gallia)
      && NpcDatabase.TryGetNpcType(512, out var quarterMaster) && quarterMaster == typeof(Quartermaster)
      && NpcDatabase.TryGetNpcType(513, out var instructor) && instructor == typeof(CombatInstructor)
      && NpcDatabase.TryGetNpcType(514, out var commander) && commander == typeof(KnightCommander)
      && NpcDatabase.TryGetNpcType(515, out var wizard) && wizard == typeof(GrandWizard)
      && NpcDatabase.TryGetNpcType(516, out var assassin) && assassin.Name == "MasterAssassin"
      && NpcDatabase.TryGetNpcType(517, out var cavalry) && cavalry == typeof(CavalryCaptain)
      && NpcDatabase.TryGetNpcType(518, out var shield) && shield == typeof(ShieldBearer)
      && NpcDatabase.TryGetNpcType(519, out var forsaker) && forsaker == typeof(Forsaker),
      "Imperial Guardian monster types are registered by server ID");
var crossedWalkFrame = typeof(SoundMonsterObject).GetMethod("CrossedWalkFrame", BindingFlags.Static | BindingFlags.NonPublic)!;
bool Crossed(double previous, double current, double frame) =>
    (bool)crossedWalkFrame.Invoke(null, [previous, current, frame])!;
Check(Crossed(0.5, 1.5, 1) && !Crossed(1.5, 1.9, 1) && Crossed(6.5, 7.5, 7),
      "Imperial footsteps fire once on frame crossings");
Check(Crossed(8, 1.5, 1) && !Crossed(8, 0.5, 1) && Crossed(6, 0.5, 7)
      && Crossed(-1, 1.2, 1) && !Crossed(-1, 5, 1),
      "Imperial footsteps handle wraparound and initial observation");
var keepNearest = typeof(ObjectAmbientSoundSystem).GetMethod("KeepNearestSource", BindingFlags.Static | BindingFlags.NonPublic)!;
var nearbySources = new Dictionary<int, MapTileObject>();
var farSource = new MapTileObject { Type = 9, Position = new Vector3(700, 0, 0) };
var nearSource = new MapTileObject { Type = 9, Position = new Vector3(100, 0, 0) };
var otherSource = new MapTileObject { Type = 31, Position = new Vector3(400, 0, 0) };
foreach (var source in new[] { farSource, nearSource, otherSource, farSource })
    keepNearest.Invoke(null, [nearbySources, source, Vector3.Zero]);
Check(nearbySources.Count == 2 && ReferenceEquals(nearbySources[9], nearSource)
      && ReferenceEquals(nearbySources[31], otherSource),
      "Object ambience selects the nearest source independently for each sound type");

// The Season 20 Chaos Castle height map (World19) has no BMP header: it must load as a flat 256 x 256 map.
var ccHeight = Path.Combine(@"C:\Games\MU_Red_1_20_61_Full\Data", "World19", "TerrainHeight.OZB");
if (File.Exists(ccHeight))
{
    var ozb = await new Client.Data.OZB.OZBReader().Load(ccHeight);
    Check(ozb.Width == 256 && ozb.Height == 256 && ozb.Data.Length == 65536, "Chaos Castle height map (World19) loads without a BMP header", $"{ozb.Width}x{ozb.Height}");
}

var nightmareModelPath = Path.Combine(@"C:\Games\MU_Red_1_20_61_Full\Data", "Monster", "Monster122.bmd");
if (File.Exists(nightmareModelPath))
{
    var nightmareModel = await new Client.Data.BMD.BMDReader().Load(nightmareModelPath);
    Check(nightmareModel.Meshes.Length > 0 && nightmareModel.Bones.Length > 0
          && nightmareModel.Actions.Length > (int)MonsterActionType.Attack4
          && new[] { MonsterActionType.Attack1, MonsterActionType.Attack2, MonsterActionType.Attack3, MonsterActionType.Attack4 }
              .All(action => nightmareModel.Actions[(int)action]?.NumAnimationKeys > 0),
          "Nightmare game model decodes with meshes, bones and four attack slots",
          $"meshes={nightmareModel.Meshes.Length}, bones={nightmareModel.Bones.Length}, actions={nightmareModel.Actions.Length}");
    var nightmareActor = new Nightmare { Model = nightmareModel };
    var actorController = (AnimationController)typeof(WalkerObject)
        .GetField("_animationController", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(nightmareActor)!;
    foreach (var action in new[] { MonsterActionType.Attack3, MonsterActionType.Attack4 })
    {
        nightmareActor.PlayAction((ushort)action, fromServer: true);
        bool started = nightmareActor.IsOneShotPlaying && nightmareActor.CurrentAction == (int)action;
        actorController.Update(100f);
        Check(started && !nightmareActor.IsOneShotPlaying && nightmareActor.CurrentAction == (int)MonsterActionType.Stop1,
              $"Nightmare {action} starts and returns to idle using model duration");
    }
}
else
    Console.WriteLine("[SKIP] Nightmare model: game file not available");

foreach (string handFile in new[] { "Monster119.bmd", "Monster120.bmd" })
{
    string path = Path.Combine(@"C:\Games\MU_Red_1_20_61_Full\Data", "Monster", handFile);
    if (!File.Exists(path))
    {
        Console.WriteLine($"[SKIP] Maya hand model {handFile}: game file not available");
        continue;
    }
    var model = await new Client.Data.BMD.BMDReader().Load(path);
    Check(model.Meshes.Length > 0 && model.Actions.Length > (int)MonsterActionType.Attack2
          && model.Actions[(int)MonsterActionType.Attack1]?.NumAnimationKeys > 0
          && model.Actions[(int)MonsterActionType.Attack2]?.NumAnimationKeys > 0,
          $"Maya hand model {handFile} decodes with both attack animations");
}

var imperialModels = new (string File, MonsterActionType[] Actions)[]
{
    ("Monster165.bmd", [MonsterActionType.Attack1, MonsterActionType.Attack2, MonsterActionType.Attack3, MonsterActionType.Attack4, MonsterActionType.Appear]),
    ("Monster166.bmd", [MonsterActionType.Attack2, MonsterActionType.Attack3, MonsterActionType.Appear]),
    ("Monster167.bmd", [MonsterActionType.Attack2, MonsterActionType.Attack3, MonsterActionType.Appear]),
    ("Monster168.bmd", [MonsterActionType.Attack2, MonsterActionType.Attack3, MonsterActionType.Appear]),
    ("Monster169.bmd", [MonsterActionType.Attack2, MonsterActionType.Attack3, MonsterActionType.Appear]),
    ("Monster170.bmd", [MonsterActionType.Attack2, MonsterActionType.Attack3, MonsterActionType.Appear]),
    ("Monster171.bmd", [MonsterActionType.Attack2, MonsterActionType.Attack3, MonsterActionType.Appear]),
    ("Monster172.bmd", [MonsterActionType.Attack2, MonsterActionType.Attack3, MonsterActionType.Appear]),
    ("Monster174.bmd", [MonsterActionType.Attack2, MonsterActionType.Appear]),
    ("Monster175.bmd", [MonsterActionType.Attack2, MonsterActionType.Appear]),
    ("Monster176.bmd", [MonsterActionType.Attack2]),
    ("Monster177.bmd", [MonsterActionType.Attack2, MonsterActionType.Attack3]),
    ("Monster178.bmd", [MonsterActionType.Attack2, MonsterActionType.Attack3, MonsterActionType.Appear]),
    ("Monster179.bmd", [MonsterActionType.Attack2, MonsterActionType.Appear]),
    ("Monster180.bmd", [MonsterActionType.Attack2]),
};
foreach (var (file, expectedActions) in imperialModels)
{
    string path = Path.Combine(@"C:\Games\MU_Red_1_20_61_Full\Data", "Monster", file);
    if (!File.Exists(path))
    {
        Console.WriteLine($"[SKIP] Imperial model {file}: game file not available");
        continue;
    }
    var model = await new Client.Data.BMD.BMDReader().Load(path);
    Check(expectedActions.All(action => (int)action < model.Actions.Length && model.Actions[(int)action]?.NumAnimationKeys > 0),
          $"Imperial model {file} has mapped skill animations",
          string.Join(", ", expectedActions.Select(action => $"{action}={((int)action < model.Actions.Length ? model.Actions[(int)action]?.NumAnimationKeys : null)}")));
}

var monsterSkillPath = Path.Combine(@"C:\Games\MU_Red_1_20_61_Full\Data", "Local", "monsterskill.bmd");
if (File.Exists(monsterSkillPath))
{
    byte[] skillFile = await File.ReadAllBytesAsync(monsterSkillPath);
    const int skillRecordSize = 48; // monster ID, ten skill IDs, slot; all int32
    int count = skillFile.Length >= 4 ? BinaryPrimitives.ReadInt32LittleEndian(skillFile) : -1;
    bool valid = count >= 0 && skillFile.Length == 4L + count * skillRecordSize;
    Check(valid, "MonsterSkill game script matches reference record layout", $"records={count}, bytes={skillFile.Length}");
    if (valid)
    {
        bool foundNightmare = false;
        var foundHands = new HashSet<int>();
        for (int i = 0; i < count; i++)
        {
            byte[] record = Client.Data.BuxCryptor.Convert(skillFile.AsSpan(4 + i * skillRecordSize, skillRecordSize).ToArray());
            int monsterId = BinaryPrimitives.ReadInt32LittleEndian(record);
            if (monsterId is 362 or 363)
            {
                var handSkills = Enumerable.Range(0, 10).Select(index => BinaryPrimitives.ReadInt32LittleEndian(record.AsSpan(4 + 4 * index, 4))).ToArray();
                foundHands.Add(monsterId);
                Check(handSkills.Take(2).SequenceEqual(new[] { 23, 24 }), $"Maya hand {monsterId} skill mappings match supplied game data");
                Console.WriteLine($"[INFO] Maya hand {monsterId} MonsterSkill slots: {string.Join(", ", handSkills)}");
            }
            if (monsterId == 361)
            {
                var skills = Enumerable.Range(0, 10).Select(index => BinaryPrimitives.ReadInt32LittleEndian(record.AsSpan(4 + 4 * index, 4))).ToArray();
                foundNightmare = true;
                Check(skills.Take(4).SequenceEqual(new[] { 18, 19, 20, 21 }), "Nightmare skill mappings match supplied game data");
                Console.WriteLine($"[INFO] Nightmare MonsterSkill slots: {string.Join(", ", skills)}");
            }
        }
        Check(foundNightmare, "Nightmare exists in supplied MonsterSkill script");
        Check(foundHands.SetEquals(new[] { 362, 363 }), "Both Maya hands exist in supplied MonsterSkill script");
    }
}
else
    Console.WriteLine("[SKIP] MonsterSkill script: game file not available");

// Exercise the client's actual PCM decoders on supplied game data without an audio device.
void CheckDecoder(string methodName, string relativePath)
{
    string path = Path.Combine(@"C:\Games\MU_Red_1_20_61_Full\Data", relativePath);
    if (!File.Exists(path))
    {
        Console.WriteLine($"[SKIP] {methodName}: game file not available");
        return;
    }
    var decoder = typeof(SoundController).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)!;
    object?[] arguments = [path, 0, 0];
    var pcm = (byte[]?)decoder.Invoke(SoundController.Instance, arguments);
    int rate = (int)arguments[1]!;
    int channels = (int)arguments[2]!;
    bool valid = pcm is { Length: > 0 } && rate > 0 && channels is 1 or 2
        && pcm.Length % (channels * sizeof(short)) == 0 && pcm.Any(b => b != 0);
    Check(valid, $"{methodName} decodes game audio to non-silent PCM", $"rate={rate}, channels={channels}, bytes={pcm?.Length ?? 0}");
}
CheckDecoder("LoadOggPcmData", "Music/crywolf_ready-02.ogg");
CheckDecoder("LoadMp3PcmData", "Music/KanturuMayaBattle.mp3");

if (Environment.GetEnvironmentVariable("MU_TEST_AUDIO_PLAYBACK") == "1")
{
    var loadSound = typeof(SoundController).GetMethod("LoadSoundEffectData", BindingFlags.Instance | BindingFlags.NonPublic)!;
    foreach (string relativePath in new[] { "Sound/aChaosEnd.wav", "Music/crywolf_ready-02.ogg", "Music/KanturuMayaBattle.mp3" })
    {
        string path = Path.Combine(@"C:\Games\MU_Red_1_20_61_Full\Data", relativePath);
        if (!File.Exists(path))
        {
            Console.WriteLine($"[SKIP] Playback {relativePath}: game file not available");
            continue;
        }
        try
        {
            using var sound = (SoundEffect?)loadSound.Invoke(SoundController.Instance, [path]);
            if (sound == null)
            {
                Check(false, $"Playback {relativePath}", "SoundController could not load the file");
                continue;
            }
            using var instance = sound.CreateInstance();
            instance.Volume = 0f;
            instance.Play();
            Check(instance.State == SoundState.Playing, $"Playback {relativePath}", $"duration={sound.Duration}");
            instance.Stop();
        }
        catch (Exception ex)
        {
            Check(false, $"Playback {relativePath}", ex.GetBaseException().Message);
        }
    }

    string previousDataPath = Constants.DataPath;
    bool previousMusicEnabled = Constants.BACKGROUND_MUSIC;
    var previousMusicVolume = Constants.BACKGROUND_MUSIC_VOLUME;
    Constants.DataPath = @"C:\Games\MU_Red_1_20_61_Full\Data";
    Constants.BACKGROUND_MUSIC = true;
    Constants.BACKGROUND_MUSIC_VOLUME = 0;
    var controller = SoundController.Instance;
    var currentPathField = typeof(SoundController).GetField("_currentBackgroundMusicPath", BindingFlags.Instance | BindingFlags.NonPublic)!;
    var activeMusicField = typeof(SoundController).GetField("_activeBackgroundMusicInstance", BindingFlags.Instance | BindingFlags.NonPublic)!;
    var fadingMusicField = typeof(SoundController).GetField("_fadingOutBackgroundMusicInstance", BindingFlags.Instance | BindingFlags.NonPublic)!;
    var fadeField = typeof(SoundController).GetField("_backgroundMusicFade", BindingFlags.Instance | BindingFlags.NonPublic)!;
    async Task<bool> WaitForMusic(Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            dispatcher.ProcessPending();
            controller.Update();
            if (predicate()) return true;
            await Task.Delay(25);
        }
        return false;
    }
    try
    {
        const string first = "Music/crywolf_ready-02.ogg";
        const string second = "Music/KanturuMayaBattle.mp3";
        controller.PlayBackgroundMusic(first);
        bool started = await WaitForMusic(() => (string?)currentPathField.GetValue(controller) == first);
        Check(started, "SoundController starts requested background music");
        bool fadedIn = started && await WaitForMusic(() => (float)fadeField.GetValue(controller)! >= 0.99f);
        Check(fadedIn, "SoundController completes background music fade-in");
        var firstInstance = activeMusicField.GetValue(controller);
        controller.PlayBackgroundMusic(first);
        Check(started && ReferenceEquals(firstInstance, activeMusicField.GetValue(controller)),
              "SoundController retains the instance when the same track is requested");
        controller.PlayBackgroundMusic(second);
        bool switched = await WaitForMusic(() => (string?)currentPathField.GetValue(controller) == second);
        Check(switched && ReferenceEquals(firstInstance, fadingMusicField.GetValue(controller)),
              "SoundController starts crossfade to another track");
        Check(switched && await WaitForMusic(() => fadingMusicField.GetValue(controller) == null),
              "SoundController disposes the old track after crossfade");
    }
    finally
    {
        controller.StopBackgroundMusic();
        Constants.DataPath = previousDataPath;
        Constants.BACKGROUND_MUSIC = previousMusicEnabled;
        Constants.BACKGROUND_MUSIC_VOLUME = previousMusicVolume;
    }
}

var unhandled = router.GetUnhandledPacketCounts();
Check(unhandled.Count == 0, "All event packets were routed to a handler", string.Join(", ", unhandled.Select(u => $"{u.Key.Code:X2}-{u.Key.SubCode:X2}")));
Console.WriteLine(failures == 0 ? "Event packet checks passed." : $"Event packet checks: {failures} failure(s).");
return failures == 0 ? 0 : 1;

sealed class RecordingSkillMonster : MonsterObject
{
    public int LastAttack { get; private set; }
    public int AttackCount { get; private set; }
    public int AppearCount { get; private set; }
    public List<int> Markers { get; } = new();
    public int FrameMarkerCount { get; private set; }
    public List<ushort> SkillStarts { get; } = new();
    public override void OnPerformAttack(int attackType = 1)
    {
        LastAttack = attackType;
        AttackCount++;
    }

    protected override void OnAttackProgress(in AttackProgress progress)
    {
        if (progress.CrossedTick(1)) Markers.Add(1);
        if (progress.CrossedTick(5)) Markers.Add(5);
        if (progress.CrossedTick(9)) Markers.Add(9);
        if (progress.CrossedFrame(3.7))
        {
            FrameMarkerCount++;
            Markers.Add(100);
        }
        if (progress.SkillStarted)
            SkillStarts.Add(progress.SkillId);
    }
    public override void OnAppearAnimationStart() => AppearCount++;
}

sealed class HydraAnimationProbe : Hydra
{
    public HydraAnimationProbe() => Status = GameControlStatus.Ready;

    protected override void OnIdle() { }
}
