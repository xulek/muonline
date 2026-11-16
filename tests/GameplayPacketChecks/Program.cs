// Offline checks for the daily gameplay packets: packets built from the ServerPackets specification are routed
// through the real PacketRouter (so code/sub-code registration is covered) and the resulting client state is checked.
using System.Buffers;
using System.Buffers.Binary;
using System.Reflection;
using System.Text;
using Client.Main;
using Client.Main.Configuration;
using Client.Main.Controllers;
using Client.Main.Core.Client;
using Client.Main.Core.Models;
using Client.Main.Networking;
using Client.Main.Networking.PacketHandling;
using Client.Main.Objects;
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

// A hit packet can contain both HP and shield fractions; updating those values
// must not play hit audio in addition to the packet's single hit reaction.
var hitProbe = new HitReactionProbe();
hitProbe.UpdateHealthFractions(0.75f, 0.50f, 10, 5);
Check(hitProbe.ReactionCount == 0, "HP and shield updates do not duplicate monster hit reactions");

// F3/51 MasterCharacterLevelUpdate (normal, 20 bytes): level 7, +1 point, 5 points, max HP 1234.
var master = C1(0xF3, 0x51, 7, 0, 1, 0, 5, 0, 200, 0, 0xD2, 0x04, 0x2C, 0x01, 0x64, 0, 0x32, 0);
await Route(master);
Check(state.MasterLevel == 7 && state.MasterLevelUpPoints == 5 && state.MaximumHealth == 1234, "F3/51 master level update (normal)",
    $"level {state.MasterLevel}, points {state.MasterLevelUpPoints}, max HP {state.MaximumHealth}");
var masterExt = new byte[28];
masterExt[0] = 0xC1; masterExt[1] = 28; masterExt[2] = 0xF3; masterExt[3] = 0x51;
BinaryPrimitives.WriteUInt16LittleEndian(masterExt.AsSpan(4), 8);
BinaryPrimitives.WriteUInt16LittleEndian(masterExt.AsSpan(8), 6);
BinaryPrimitives.WriteUInt32LittleEndian(masterExt.AsSpan(12), 70000);
await Route(masterExt);
Check(state.MasterLevel == 8 && state.MaximumHealth == 70000, "F3/51 master level update (extended)", $"level {state.MasterLevel}, max HP {state.MaximumHealth}");

// 2C FruitConsumptionResponse: +5 Energy.
state.UpdateStats(100, 100, 100, 100, 0);
await Route(C1(0x2C, 0, 5, 0, 0));
Check(state.Energy == 105, "2C fruit adds energy", $"energy {state.Energy}");
await Route(C1(0x2C, 3, 2, 0, 3));
Check(state.Strength == 98, "2C fruit removes strength", $"strength {state.Strength}");

// 2D EffectItemConsumption: add effect 90 (experience rate) for 600 seconds, then remove.
var effect = new byte[17];
effect[0] = 0xC1; effect[1] = 17; effect[2] = 0x2D; effect[4] = 2; effect[6] = 6; effect[8] = 0;
BinaryPrimitives.WriteUInt32LittleEndian(effect.AsSpan(12), 600);
effect[16] = 90;
await Route(effect);
var buff = state.GetActiveBuffs().FirstOrDefault(b => b.EffectId == 90);
Check(buff != null && buff.Duration == TimeSpan.FromSeconds(600) && buff.Name == "Experience Rate", "2D item effect added with server duration",
    $"{buff?.Name} {buff?.Duration}");
effect[8] = 1;
await Route(effect);
Check(state.GetActiveBuffs().All(b => b.EffectId != 90), "2D item effect removed");

// C3 29 ConsumeItemWithEffect: Ale for 80 seconds (C3 header, same layout as C1 after decryption).
var ale = new byte[] { 0xC3, 6, 0x29, 0, 80, 0 };
await Route(ale);
var aleBuff = state.GetActiveBuffs().FirstOrDefault(b => b.Name == "Ale");
Check(aleBuff != null && aleBuff.Duration == TimeSpan.FromSeconds(80), "29 Ale effect with duration", $"{aleBuff?.Duration}");

// 83 VaultProtectionInformation: locked, then unlocked.
await Route(C1(0x83, 1));
Check(state.VaultProtectionState == 1, "83 vault locked");
await Route(C1(0x83, 12));
Check(state.VaultProtectionState == 12, "83 vault unlocked");

// A9 PetInfoResponse: Dark Horse in inventory slot 20, level 12, 3456 exp, 80 % life.
var pet = new byte[13];
pet[0] = 0xC1; pet[1] = 13; pet[2] = 0xA9; pet[3] = 1; pet[4] = 0; pet[5] = 20; pet[6] = 12;
BinaryPrimitives.WriteUInt32LittleEndian(pet.AsSpan(8), 3456);
pet[12] = 80;
await Route(pet);
Check(state.TryGetPetInfo(0, 20, out var petInfo) && petInfo == (12, 3456u, 80), "A9 pet info stored", $"{petInfo}");

// F3/20 SummonHealthUpdate and F3/07 PoisonDamage.
await Route(C1(0xF3, 0x20, 55));
Check(state.SummonHealthPercent == 55, "F3/20 summon health");
state.UpdateCurrentHealthShield(500, 100);
await Route(C1(0xF3, 0x07, 0, 30, 0, 90));
Check(state.CurrentHealth == 470 && state.CurrentShield == 90, "F3/07 poison damage", $"HP {state.CurrentHealth}, SD {state.CurrentShield}");

// F3/30 ApplyKeyConfiguration and KeyConfiguration round trip.
var config = new KeyConfiguration();
config.SkillHotkeys[0] = 0x0013; config.SkillHotkeys[9] = 0x012C;
config.PotionHotkeys[0] = 3; config.PotionHotkeys[3] = 6; config.PotionLevels[3] = 1;
config.GameOptions = KeyConfiguration.AutoAttackOn | KeyConfiguration.WhisperSoundOn;
var keyPacket = C1(new byte[] { 0xF3, 0x30 }.Concat(config.ToBytes()).ToArray());
await Route(keyPacket);
var received = state.ServerKeyConfiguration;
Check(received != null && received.ToBytes().SequenceEqual(config.ToBytes()), "F3/30 key configuration parsed");
Check(keyPacket[4] == 0x00 && keyPacket[5] == 0x13 && keyPacket[25] == 3 && keyPacket[29] == 6, "Key configuration byte layout (skill big-endian, Q at data 21, R at data 25)");

// 1F AddSummonedMonstersToScope: one summon (id 0x1234, Goblin type 26) owned by "Elfie".
var summon = new byte[1 + 20];
summon[0] = 1;
summon[1] = 0x12; summon[2] = 0x34; summon[3] = 0; summon[4] = 26; summon[5] = 130; summon[6] = 120; summon[7] = 130; summon[8] = 120; summon[9] = 0x30;
Encoding.UTF8.GetBytes("Elfie").CopyTo(summon, 10);
await Route(C2(0x1F, summon));
var summonEntry = network.GetScopeManager().GetScopeItems(ScopeObjectType.Npc).OfType<NpcScopeObject>().FirstOrDefault(n => n.Id == 0x1234);
Check(summonEntry != null && summonEntry.TypeNumber == 26 && summonEntry.Name.Contains("Elfie") && summonEntry.IsSummoned,
    "1F preserves summon ownership and friendly classification", $"{summonEntry?.Name}");

// Each elf summon keeps the monster type supplied by the server; pooled scope objects
// must also lose the summon flag before they represent a normal hostile monster.
foreach (ushort type in new ushort[] { 32, 21, 20, 10, 150, 151 })
{
    summon[4] = (byte)type;
    await Route(C2(0x1F, summon));
    var entry = (NpcScopeObject)network.GetScopeManager().GetScopeObjectByMaskedId(0x1234);
    Check(entry.TypeNumber == type && entry.IsSummoned, $"1F retains summon type {type}");
}
network.GetScopeManager().RemoveObjectFromScope(0x1234);
network.GetScopeManager().AddOrUpdateNpcInScope(0x1235, 0x1235, 130, 120, 26);
var reusedNpc = (NpcScopeObject)network.GetScopeManager().GetScopeObjectByMaskedId(0x1235);
Check(!reusedNpc.IsSummoned, "reused scope entry classifies normal monsters as hostile");

// Distinct elf summon skills must reach the server unchanged, rather than all
// being encoded as the Goblin skill.
for (ushort skillId = 30; skillId <= 36; skillId++)
{
    var writer = new ArrayBufferWriter<byte>();
    int size = PacketBuilder.BuildSkillRequestPacket(writer, skillId, state.Id);
    writer.Advance(size);
    var request = new MUnique.OpenMU.Network.Packets.ClientToServer.TargetedSkill(writer.WrittenSpan.ToArray());
    Check(request.SkillId == skillId && request.TargetId == state.Id, $"summon request preserves skill {skillId}");
}

// 45 AddTransformedCharactersToScope: player "Ringer" (id 0x0101) shown as skin 2 (budge dragon).
var transformed = new byte[1 + 38 + 1];
transformed[0] = 1;
transformed[1] = 0x01; transformed[2] = 0x01; transformed[3] = 140; transformed[4] = 125; transformed[5] = 0; transformed[6] = 2;
Encoding.UTF8.GetBytes("Ringer").CopyTo(transformed, 7);
transformed[38] = 1; transformed[39] = 7; // one effect
await Route(C2(0x45, transformed));
var ringer = network.GetScopeManager().GetScopeItems(ScopeObjectType.Npc).OfType<NpcScopeObject>().FirstOrDefault(n => n.Id == 0x0101);
Check(ringer != null && ringer.TypeNumber == 2 && ringer.Name == "Ringer", "45 transformed player in scope as its skin", $"{ringer?.Name} type {ringer?.TypeNumber}");

// Soul Barrier is effect 4 on the wire. Its activation and removal must reach
// the shared visual manager even when the server sets the high bit of the player ID.
var buffManager = network.GetBuffManager();
var buffEvents = new List<BuffStateChangedEventArgs>();
buffManager.BuffStateChanged += (_, e) => buffEvents.Add(e);
await Route(C1(0x07, 1, 0x80, 0x42, 4));
Check(buffManager.HasBuff(0x42, BuffEffectId.ManaShield) &&
      !buffManager.HasBuff(0x42, BuffEffectId.SwellLife) &&
      buffEvents.LastOrDefault()?.EffectId == BuffEffectId.ManaShield,
    "07 Soul Barrier emits the shield event rather than Swell Life");
await Route(C1(0x07, 0, 0x00, 0x42, 4));
Check(!buffManager.HasBuff(0x42, BuffEffectId.ManaShield) &&
      buffEvents.LastOrDefault() is { EffectId: BuffEffectId.ManaShield, IsActive: false },
    "07 Soul Barrier removal clears the same masked player");
await Route(C1(0x07, 1, 0x00, 0x42, 8));
Check(buffManager.HasBuff(0x42, BuffEffectId.SwellLife) && !buffManager.HasBuff(0x42, BuffEffectId.ManaShield),
    "07 Swell Life remains distinct from Soul Barrier");
await Route(C1(0x07, 1, 0x00, 0x42, 1));
await Route(C1(0x07, 1, 0x00, 0x42, 2));
await Route(C1(0x07, 1, 0x00, 0x42, 4));
var networkProperty = typeof(MuGame).GetProperty(nameof(MuGame.Network))!;
var previousNetwork = networkProperty.GetValue(null);
networkProperty.SetValue(null, network);
var cloakedPlayer = new Client.Main.Objects.Player.PlayerObject { NetworkId = 0x8042 };
cloakedPlayer.EquippedWings.Hidden = false;
var isCloaked = typeof(Client.Main.Objects.Player.PlayerObject).GetProperty("IsCloaked", BindingFlags.Instance | BindingFlags.NonPublic)!;
await Route(C1(0x07, 1, 0x00, 0x42, 18));
Check(buffManager.HasBuff(0x42, BuffEffectId.Invisible) &&
      buffManager.HasBuff(0x42, BuffEffectId.GreaterDamage) &&
      buffManager.HasBuff(0x42, BuffEffectId.GreaterDefense) &&
      buffManager.HasBuff(0x42, BuffEffectId.ManaShield),
    "Invisibility preserves attack, defense and Soul Barrier statuses");
Check((bool)isCloaked.GetValue(cloakedPlayer)! && !cloakedPlayer.Weapon1.Hidden && !cloakedPlayer.EquippedWings.Hidden,
    "cloaking uses the masked player ID without changing equipment state");
var effectsAssembly = typeof(ModelObject).Assembly;
var orbitType = effectsAssembly.GetType("Client.Main.Objects.Effects.SpearSkillOrbitEffect")!;
var elfOrbit = (WorldObject)Activator.CreateInstance(orbitType, cloakedPlayer, 20f, new Microsoft.Xna.Framework.Vector3(0.4f, 0.8f, 0.2f))!; // sub 4 light
var shieldOrbit = (WorldObject)Activator.CreateInstance(orbitType, cloakedPlayer, 50f, Microsoft.Xna.Framework.Vector3.One)!;
cloakedPlayer.Children.Add(elfOrbit);
cloakedPlayer.Children.Add(shieldOrbit);
var castType = effectsAssembly.GetType("Client.Main.Objects.Effects.InvisibilityCastEffect")!;
var cloakCast = (WorldObject)Activator.CreateInstance(castType, cloakedPlayer)!;
castType.GetMethod("OnStart", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(cloakCast, null);
Check(!cloakedPlayer.Children.Contains(elfOrbit) && cloakedPlayer.Children.Contains(shieldOrbit),
    "Invisibility release removes the elf orbit without removing Soul Barrier");
await Route(C1(0x07, 0, 0x00, 0x42, 18));
Check(!buffManager.HasBuff(0x42, BuffEffectId.Invisible) &&
      buffManager.HasBuff(0x42, BuffEffectId.GreaterDamage) &&
      buffManager.HasBuff(0x42, BuffEffectId.GreaterDefense) &&
      buffManager.HasBuff(0x42, BuffEffectId.ManaShield) &&
      buffEvents.LastOrDefault() is { EffectId: BuffEffectId.Invisible, IsActive: false },
    "attack, defense and Soul Barrier remain after Invisibility ends");
Check(!(bool)isCloaked.GetValue(cloakedPlayer)! && !cloakedPlayer.Weapon1.Hidden && !cloakedPlayer.EquippedWings.Hidden,
    "equipment returns when cloaking ends without changing equipment state");
await Route(C1(0x07, 1, 0x00, 0x42, 18));
await Route(C1(0x19, 0x00, 71, 0x00, 0x43, 0x00, 0x42));
Check(buffManager.HasBuff(0x42, BuffEffectId.Invisible),
    "failed Cancel Invisibility leaves the cloak active");
await Route(C1(0x19, 0x00, 71, 0x00, 0x43, 0x80, 0x42));
Check(!buffManager.HasBuff(0x42, BuffEffectId.Invisible) &&
      buffManager.HasBuff(0x42, BuffEffectId.GreaterDamage) &&
      buffManager.HasBuff(0x42, BuffEffectId.GreaterDefense) &&
      buffManager.HasBuff(0x42, BuffEffectId.ManaShield),
    "successful Cancel Invisibility clears only the target cloak");
foreach (byte buffId in new byte[] { 1, 2, 4, 5, 8, 9, 18 })
    await Route(C1(0x07, 1, 0x00, 0x43, buffId));
await Route(C1(0x19, 0x00, 72, 0x00, 0x42, 0x00, 0x43));
Check(buffManager.HasBuff(0x43, BuffEffectId.ManaShield) && buffManager.HasBuff(0x43, BuffEffectId.GreaterDamage),
    "failed Abolish Magic preserves the target buffs");
await Route(C1(0x19, 0x00, 72, 0x00, 0x42, 0x80, 0x43));
Check(!buffManager.HasBuff(0x43, BuffEffectId.GreaterDamage) &&
      !buffManager.HasBuff(0x43, BuffEffectId.GreaterDefense) &&
      !buffManager.HasBuff(0x43, BuffEffectId.ManaShield) &&
      !buffManager.HasBuff(0x43, BuffEffectId.SwellLife) &&
      buffManager.HasBuff(0x43, BuffEffectId.Invisible) &&
      buffManager.HasBuff(0x42, BuffEffectId.ManaShield),
    "successful Abolish Magic removes the original target buff set only");
await Route(C1(0x07, 1, 0x00, 0x42, 18));
await Route(C1(0x07, 1, 0x00, 0x42, 55));
await Route(C1(0x07, 1, 0x00, 0x42, 81));
var removeBuffType = effectsAssembly.GetType("Client.Main.Objects.Effects.RemoveBuffEffect")!;
var abolishCast = (WorldObject)Activator.CreateInstance(removeBuffType, cloakedPlayer)!;
removeBuffType.GetMethod("OnStart", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(abolishCast, null);
var abolishJoints = (Array)removeBuffType.GetField("_joints", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(abolishCast)!;
var abolishJointType = abolishJoints.GetValue(0)!.GetType();
var starts = new float[] { 100, 90, 80, 80, 70, 60 };
var yaws = new float[] { 45, 135, 225, 90, 180, 270 };
bool correctSpawns = true;
for (int i = 0; i < starts.Length; i++)
{
    object joint = abolishJoints.GetValue(i)!;
    var start = (Microsoft.Xna.Framework.Vector3)abolishJointType.GetField("Start")!.GetValue(joint)!;
    var angle = (Microsoft.Xna.Framework.Vector3)abolishJointType.GetField("Angle")!.GetValue(joint)!;
    correctSpawns &= start.Z == starts[i] && angle.Z == yaws[i] &&
                     (int)abolishJointType.GetField("SubType")!.GetValue(joint)! == 5 + i % 3;
}
Check(correctSpawns, "Abolish Magic starts all six source joints at their original heights and angles");
removeBuffType.GetMethod("OnTick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(abolishCast, new object[] { 1f });
object firstJoint = abolishJoints.GetValue(0)!;
var firstAngle = (Microsoft.Xna.Framework.Vector3)abolishJointType.GetField("Angle")!.GetValue(firstJoint)!;
var firstDirection = (Microsoft.Xna.Framework.Vector3)abolishJointType.GetField("Direction")!.GetValue(firstJoint)!;
Check(firstAngle.Z == 75f && Math.Abs(firstDirection.Y - 800f * MathF.Pow(0.95f, 3f)) < 0.01f,
    "Abolish Magic advances three source spiral steps per frame");
Check(!buffManager.HasBuff(0x42, BuffEffectId.ManaShield) &&
      !buffManager.HasBuff(0x42, BuffEffectId.Invisible) &&
      !buffManager.HasBuff(0x42, BuffEffectId.Poison) &&
      buffManager.HasBuff(0x42, (BuffEffectId)81),
    "Abolish Magic release clears only the original caster buff and debuff set");
await Route(C1(0x07, 0, 0x00, 0x42, 81));
await Route(C1(0x07, 0, 0x00, 0x43, 18));
networkProperty.SetValue(null, previousNetwork);
await Route(C1(0x07, 0, 0x00, 0x42, 1));
await Route(C1(0x07, 0, 0x00, 0x42, 2));
await Route(C1(0x07, 0, 0x00, 0x42, 4));
await Route(C1(0x07, 1, 0x80, 0x42, 85));
Check(buffManager.HasBuff(0x42, BuffEffectId.LightningShockDamage) &&
      buffEvents.LastOrDefault() is { EffectId: BuffEffectId.LightningShockDamage, IsActive: true },
    "Lightning Shock target status is routed with its masked player ID");
var shockHitType = effectsAssembly.GetType("Client.Main.Objects.Effects.LightningShockHitEffect")!;
var shockHit = (WorldObject)Activator.CreateInstance(shockHitType, cloakedPlayer)!;
shockHitType.GetMethod("OnStart", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(shockHit, null);
var shockCracks = (Array)shockHitType.GetField("_cracks", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(shockHit)!;
bool validShockCracks = shockCracks.Length == 3;
foreach (object crack in shockCracks)
{
    var crackType = crack.GetType();
    float scale = (float)crackType.GetField("Scale")!.GetValue(crack)!;
    float angle = (float)crackType.GetField("Angle")!.GetValue(crack)!;
    validShockCracks &= scale >= 0.5f && scale <= 1.25f && angle >= 0f && angle < 360f;
}
Check(validShockCracks, "Lightning Shock target impact creates three source-sized cracks");
shockHitType.GetMethod("OnTick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(shockHit, new object[] { 0.5f });
float shockAlpha = (float)shockHitType.GetField("_alpha", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(shockHit)!;
Check(Math.Abs(shockAlpha - MathF.Pow(0.9f, 0.5f)) < 0.0001f,
    "Lightning Shock cracks fade according to the frame factor");
var shockParticleArray = (Array)shockHitType.GetField("_particles", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(shockHit)!;
Type shockParticleType = shockParticleArray.GetType().GetElementType()!;
object smokeParticle = Activator.CreateInstance(shockParticleType)!;
var kindField = shockParticleType.GetField("Kind")!;
kindField.SetValue(smokeParticle, Enum.Parse(kindField.FieldType, "Smoke58"));
shockParticleType.GetField("Life")!.SetValue(smokeParticle, 50f);
shockParticleType.GetField("Scale")!.SetValue(smokeParticle, 1f);
shockParticleType.GetField("Velocity")!.SetValue(smokeParticle, new Microsoft.Xna.Framework.Vector3(0, -10, 0));
shockParticleArray.SetValue(smokeParticle, 0);
var activeParticles = (int[])shockHitType.GetField("_activeParticles", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(shockHit)!;
activeParticles[0] = 0;
shockHitType.GetField("_activeCount", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(shockHit, 1);
shockHitType.GetField("_freeCount", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(shockHit, 2999);
shockHitType.GetField("_emitterLife", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(shockHit, 0f);
shockHitType.GetField("_life", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(shockHit, 0f);
shockHitType.GetMethod("OnTick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(shockHit, new object[] { 0.5f });
smokeParticle = shockParticleArray.GetValue(0)!;
var smokePos = (Microsoft.Xna.Framework.Vector3)shockParticleType.GetField("Position")!.GetValue(smokeParticle)!;
var smokeVelocity = (Microsoft.Xna.Framework.Vector3)shockParticleType.GetField("Velocity")!.GetValue(smokeParticle)!;
float smokeScale = (float)shockParticleType.GetField("Scale")!.GetValue(smokeParticle)!;
Check(smokePos.Y == -5f && smokeVelocity.Y == -4f && Math.Abs(smokeScale - 1.025f) < 0.0001f &&
      (bool)shockHitType.GetMethod("HasActiveContent", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(shockHit, null)!,
    "Lightning Shock smoke keeps moving and remains alive after the cracks end");
await Route(C1(0x07, 0, 0x00, 0x42, 85));
await Route(C1(0x07, 1, 0x00, 0x42, 8));
buffManager.Clear();
Check(!buffManager.HasBuff(0x42, BuffEffectId.SwellLife) &&
      buffEvents.LastOrDefault() is { EffectId: BuffEffectId.SwellLife, IsActive: false },
    "disconnect cleanup clears active buffs and emits removal");

// Nothing of the above may end up as unhandled.
var unhandled = router.GetUnhandledPacketCounts();
Check(unhandled.Count == 0, "All packets were routed to a handler", string.Join(", ", unhandled.Select(u => $"{u.Key.Code:X2}-{u.Key.SubCode:X2}")));

Console.WriteLine(failures == 0 ? "Gameplay packet checks passed." : $"Gameplay packet checks: {failures} failure(s).");
return failures == 0 ? 0 : 1;

sealed class HitReactionProbe : MonsterObject
{
    public int ReactionCount { get; private set; }
    public override void OnReceiveDamage() => ReactionCount++;
}
