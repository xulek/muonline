using System.Reflection;
using System.Buffers;
using Client.Data.BMD;
using Client.Main.Content;
using Client.Main.Networking.PacketHandling;
using Client.Main.Objects;
using Client.Main.Objects.Effects.Joints;
using Client.Main.Objects.Effects.Skills;
using Client.Main.Objects.Player;
using Client.Main.Models;
using Microsoft.Xna.Framework;

int failures = 0;
void Check(bool ok, string name)
{
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {name}");
    if (!ok) failures++;
}

Check(SkillDefinitions.GetSkillType(236) == SkillType.Area && SkillDefinitions.GetSkillAnimation(236) == 184,
    "Flame Strike uses the area cast and the original animation");
Check(string.IsNullOrEmpty(SkillDefinitions.GetSkillSound(236)), "Flame Strike sound is reserved for release");
foreach (int skillId in new[] { 230, 456 })
{
    Check(SkillDefinitions.GetSkillType(skillId) == SkillType.Area && SkillDefinitions.GetSkillAnimation(skillId) == 185,
        $"Lightning Shock {skillId} uses the area cast and original animation");
    Check(string.IsNullOrEmpty(SkillDefinitions.GetSkillSound(skillId)),
        $"Lightning Shock {skillId} sound is reserved for release");
}
var casterTileMethod = typeof(Client.Main.Scenes.GameScene).Assembly
    .GetType("Client.Main.Scenes.GameSceneSkillController")!
    .GetMethod("UsesCasterTileForAreaRequest", BindingFlags.Static | BindingFlags.NonPublic)!;
foreach (ushort skillId in new ushort[] { 230, 456, 8, 236, 76 })
    Check((bool)casterTileMethod.Invoke(null, new object[] { skillId })!, $"Area skill {skillId} sends the caster tile");
Check(!(bool)casterTileMethod.Invoke(null, new object[] { (ushort)38 })!, "Decay keeps its selected destination tile");
foreach (int skillId in new[] { 214, 458 })
    Check(SkillDefinitions.GetSkillType(skillId) == SkillType.Area && SkillDefinitions.GetSkillAnimation(skillId) == 168,
        $"Drain Life {skillId} uses a target-bearing area request and its original animation");
var drainTargetMethod = typeof(Client.Main.Scenes.GameScene).Assembly
    .GetType("Client.Main.Scenes.GameSceneSkillController")!
    .GetMethod("IsDrainLife", BindingFlags.Static | BindingFlags.NonPublic)!;
Check((bool)drainTargetMethod.Invoke(null, new object[] { (ushort)214 })! &&
      (bool)drainTargetMethod.Invoke(null, new object[] { (ushort)458 })! &&
      !(bool)drainTargetMethod.Invoke(null, new object[] { (ushort)38 })!,
    "Drain Life requires a selected target while ordinary area spells may target the ground");
var areaCastState = new Client.Main.Core.Client.CharacterState(Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance)
{
    LastAreaSkillId = 214, LastAreaSkillTargetId = 0x42, LastAreaSkillSentAtMs = 1000
};
Check(areaCastState.GetRecentAreaSkillTarget(214, 1250) == 0x42 &&
      areaCastState.GetRecentAreaSkillTarget(458, 1250) == 0 &&
      areaCastState.GetRecentAreaSkillTarget(214, 2501) == 0 &&
      areaCastState.GetRecentAreaSkillTarget(214, 999) == 0,
    "Drain Life recovers only the recent target for the matching local cast");
Check(string.IsNullOrEmpty(SkillDefinitions.GetSkillSound(214)) && string.IsNullOrEmpty(SkillDefinitions.GetSkillSound(458)),
    "Drain Life sound is reserved for the timed effect release");
Type drainEffectType = typeof(PlayerObject).Assembly.GetType("Client.Main.Objects.Effects.Summoner.DrainLifeEffect")!;
var drainEffect = Activator.CreateInstance(drainEffectType, new object[] { new PlayerObject(), new PlayerObject() })!;
var drainFlags = BindingFlags.Instance | BindingFlags.NonPublic;
drainEffectType.GetMethod("SpawnEnergy", drainFlags)!.Invoke(drainEffect, new object[] { Vector3.Zero, Vector3.Zero });
var drainJoints = (Array)drainEffectType.GetField("s_joints", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
int drainJointIndex = -1;
for (int i = 0; i < drainJoints.Length; i++)
    if (ReferenceEquals(drainJoints.GetValue(i)!.GetType().GetField("Owner")!.GetValue(drainJoints.GetValue(i)), drainEffect)) { drainJointIndex = i; break; }
drainEffectType.GetMethod("MoveJoints", drainFlags)!.Invoke(drainEffect, new object[] { 1f, true });
var drainJoint = drainJoints.GetValue(drainJointIndex)!;
Type drainJointType = drainJoint.GetType();
Check((Vector3)drainJointType.GetField("Position")!.GetValue(drainJoint)! == new Vector3(0f, -3f, 6f) &&
      (float)drainJointType.GetField("Life")!.GetValue(drainJoint)! == 119f &&
      ((Vector3)drainJointType.GetField("Angle")!.GetValue(drainJoint)!).Z == 10f,
    "Drain Life energy first moves and rises before it starts homing");
drainEffectType.GetMethod("DisposeEffect", drainFlags)!.Invoke(drainEffect, null);
Check(drainJointType.GetField("Owner")!.GetValue(drainJoints.GetValue(drainJointIndex)) == null,
    "Drain Life releases its shared joint slot when the cast is disposed");
var shockCaster = new PlayerObject();
var shockWorld = (Client.Main.Controls.WalkableWorldControl)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Client.Main.Worlds.AcheronWorld));
var shock = new LightningShockSkillEffect().CreateEffect(new SkillEffectContext { Caster = shockCaster, World = shockWorld, SkillId = 230 })!;
Type shockType = shock.GetType();
var shockFlags = BindingFlags.Instance | BindingFlags.NonPublic;
var shockTick = shockType.GetMethod("OnTick", shockFlags)!;
shockType.GetField("_orbLife", shockFlags)!.SetValue(shock, 1f);
shockTick.Invoke(shock, new object[] { 1f });
Check(!(bool)shockType.GetField("_orbAlive", shockFlags)!.GetValue(shock)! &&
      (float)shockType.GetField("_impactLife", shockFlags)!.GetValue(shock)! == 0f,
    "Lightning Shock expires without creating a ground impact when no collision occurs");
var shockImpact = new LightningShockSkillEffect().CreateEffect(new SkillEffectContext { Caster = shockCaster, World = shockWorld, SkillId = 230 })!;
shockType.GetField("_orbAlive", shockFlags)!.SetValue(shockImpact, false);
shockType.GetMethod("StartImpact", shockFlags)!.Invoke(shockImpact, new object[] { 1f });
shockTick.Invoke(shockImpact, new object[] { 1f });
Check(Math.Abs((float)shockType.GetField("_markScale", shockFlags)!.GetValue(shockImpact)! - .95f) < .0001f &&
      Math.Abs((float)shockType.GetField("_flashScale", shockFlags)!.GetValue(shockImpact)! - .6f) < .0001f,
    "Lightning Shock impact marks and flashes grow according to their source lifetimes");
for (int i = 0; i < 80; i++) shockTick.Invoke(shockImpact, new object[] { 1f });
Check(!(bool)shockType.GetMethod("HasActiveContent", shockFlags)!.Invoke(shockImpact, null)!,
    "Lightning Shock impact and its particles eventually release the effect");
Check(SkillDefinitions.GetSkillType(16) == SkillType.Target, "Soul Barrier can target party members as well as self");
foreach (ushort target in new ushort[] { 0x42, 0x43 })
{
    var writer = new ArrayBufferWriter<byte>();
    int size = PacketBuilder.BuildSkillRequestPacket(writer, 16, target);
    writer.Advance(size);
    var packet = new MUnique.OpenMU.Network.Packets.ClientToServer.TargetedSkill(writer.WrittenSpan.ToArray());
    Check(packet.SkillId == 16 && packet.TargetId == target, $"Soul Barrier request retains target {target:X4}");
}
foreach (ushort target in new ushort[] { 0x42, 0x43 })
{
    var writer = new ArrayBufferWriter<byte>();
    int size = PacketBuilder.BuildSkillRequestPacket(writer, 71, target);
    writer.Advance(size);
    var packet = new MUnique.OpenMU.Network.Packets.ClientToServer.TargetedSkill(writer.WrittenSpan.ToArray());
    Check(packet.SkillId == 71 && packet.TargetId == target, $"Cancel Invisibility request retains target {target:X4}");
}
foreach (ushort target in new ushort[] { 0x42, 0x43 })
{
    var writer = new ArrayBufferWriter<byte>();
    int size = PacketBuilder.BuildSkillRequestPacket(writer, 72, target);
    writer.Advance(size);
    var packet = new MUnique.OpenMU.Network.Packets.ClientToServer.TargetedSkill(writer.WrittenSpan.ToArray());
    Check(packet.SkillId == 72 && packet.TargetId == target, $"Abolish Magic request retains target {target:X4}");
}
Vector3 rotated = SourceJointMath.Rotate(new Vector3(0, -1, 0), new Vector3(0, 0, 90));
Check(Vector3.Distance(rotated, Vector3.UnitX) < 0.0001f, "Source rotation sends forward to +X at yaw 90");

var fenrirCaster = new PlayerObject { NetworkId = 0x42 };
var fenrirIndex = typeof(PlayerObject).GetField("_currentVehicleIndex", BindingFlags.Instance | BindingFlags.NonPublic)!;
var ridingField = typeof(PlayerObject).GetField("_isRiding", BindingFlags.Instance | BindingFlags.NonPublic)!;
foreach (var (vehicleIndex, expectedAction) in new (short, PlayerAction)[]
{
    (7, PlayerAction.PlayerSkillDrainLifeUni), (8, PlayerAction.PlayerSkillDrainLifeDino),
    (14, PlayerAction.PlayerSkillDrainLifeFenrir)
})
{
    ridingField.SetValue(fenrirCaster, true);
    fenrirIndex.SetValue(fenrirCaster, vehicleIndex);
    Check(fenrirCaster.GetPacketSkillAnimation(214) == (int)expectedAction &&
          fenrirCaster.GetPacketSkillAnimation(458) == (int)expectedAction,
        $"Drain Life selects mount animation for vehicle {vehicleIndex}");
}
ridingField.SetValue(fenrirCaster, false);
var plasmaWorld = (Client.Main.Controls.WalkableWorldControl)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Client.Main.Worlds.AcheronWorld));
var plasmaFactory = new PlasmaStormSkillEffect();
foreach (var (vehicleIndex, expectedType) in new (short, int)[] { (11, 0), (12, 2), (13, 3), (14, 1), (15, 0), (16, 2), (17, 3), (18, 1) })
{
    fenrirIndex.SetValue(fenrirCaster, vehicleIndex);
    var plasma = plasmaFactory.CreateEffect(new SkillEffectContext
    {
        Caster = fenrirCaster, World = plasmaWorld, SkillId = 76, TargetId = 0, TargetPosition = Vector3.Zero
    })!;
    var plasmaType = plasma.GetType();
    Check(ReferenceEquals(plasmaType.GetField("_mainTarget", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(plasma), fenrirCaster) &&
          (int)plasmaType.GetField("_fenrirType", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(plasma)! == expectedType,
        $"Plasma Storm area packet uses the caster and Fenrir variant {vehicleIndex}");
}
fenrirIndex.SetValue(fenrirCaster, (short)14);
var plasmaPair = plasmaFactory.CreateEffect(new SkillEffectContext
{
    Caster = fenrirCaster, World = plasmaWorld, SkillId = 76, TargetPosition = Vector3.Zero
})!;
Type plasmaTypeForTest = plasmaPair.GetType();
plasmaTypeForTest.GetMethod("SpawnPair", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(plasmaPair, new object[] { fenrirCaster, 4 });
var plasmaJoints = (Array)plasmaTypeForTest.GetField("_joints", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(plasmaPair)!;
object firstPlasmaJoint = plasmaJoints.GetValue(0)!;
object secondPlasmaJoint = plasmaJoints.GetValue(1)!;
Type plasmaJointType = firstPlasmaJoint.GetType();
Check((Vector3)plasmaJointType.GetField("Angle")!.GetValue(firstPlasmaJoint)! ==
      (Vector3)plasmaJointType.GetField("Angle")!.GetValue(secondPlasmaJoint)! &&
      (int)plasmaJointType.GetField("SubType")!.GetValue(firstPlasmaJoint)! == 1 &&
      (int)plasmaJointType.GetField("SubType")!.GetValue(secondPlasmaJoint)! == 4,
    "Plasma Storm creates each lightning pair with the same source angle and correct subtypes");
plasmaTypeForTest.GetMethod("SpawnCoil", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(plasmaPair, null);
object redCoil = plasmaJoints.GetValue(2)!;
float initialCoilWait = (float)plasmaJointType.GetField("Weapon")!.GetValue(redCoil)!;
object[] coilMoveArgs = { redCoil, 0.5f };
plasmaTypeForTest.GetMethod("MoveCoil", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, coilMoveArgs);
Check((float)plasmaJointType.GetField("Weapon")!.GetValue(coilMoveArgs[0])! == initialCoilWait - 0.5f,
    "Plasma Storm coil countdown follows the frame factor");
fenrirIndex.SetValue(fenrirCaster, (short)13);
var goldPlasma = plasmaFactory.CreateEffect(new SkillEffectContext
{
    Caster = fenrirCaster, World = plasmaWorld, SkillId = 76, TargetPosition = Vector3.Zero
})!;
plasmaTypeForTest.GetMethod("SpawnCoil", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(goldPlasma, null);
var goldJoints = (Array)plasmaTypeForTest.GetField("_joints", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(goldPlasma)!;
object goldJoint = goldJoints.GetValue(0)!;
Check((int)plasmaJointType.GetField("SubType")!.GetValue(goldJoint)! == 14 &&
      plasmaJointType.GetField("Trail")!.GetValue(goldJoint) == null &&
      (float)plasmaJointType.GetField("Life")!.GetValue(goldJoint)! == 15f,
    "gold Fenrir subtype 14 follows the source branch without inventing a spiral");
Check(fenrirCaster.GetSkillAction(76, false) == PlayerAction.PlayerFenrirSkill,
    "unarmed Plasma Storm uses the Fenrir skill animation");
fenrirCaster.CharacterClass = MUnique.OpenMU.Network.Packets.CharacterClassNumber.RageFighter;
Check(fenrirCaster.GetSkillAction(76, false) == PlayerAction.PlayerRageFenrir,
    "unarmed Rage Fighter uses its Fenrir skill animation");
fenrirCaster.Weapon2.Model = new BMD();
Check(fenrirCaster.GetSkillAction(76, false) == PlayerAction.PlayerRageFenrirOneRight,
    "Plasma Storm selects the right-hand Rage Fighter animation");
fenrirCaster.Weapon1.Model = new BMD();
Check(fenrirCaster.GetSkillAction(76, false) == PlayerAction.PlayerRageFenrirTwoSword,
    "Plasma Storm selects the two-hand Rage Fighter animation");

Assembly assembly = typeof(ModelObject).Assembly;
Type rendererType = assembly.GetType("Client.Main.Objects.Effects.Joints.SourceModelRenderer")!;
object renderer = Activator.CreateInstance(rendererType, "unused.bmd", -2, -1, true)!;
var setV = rendererType.GetMethod("SetMeshScrollV")!;
var getDeformer = rendererType.GetMethod("GetVertexDeformerForMesh", BindingFlags.Instance | BindingFlags.NonPublic)!;
setV.Invoke(renderer, new object[] { 1, 0.5f });
var mesh0 = (ITexCoordDeformer?)getDeformer.Invoke(renderer, new object[] { 0 });
var mesh1 = (ITexCoordDeformer?)getDeformer.Invoke(renderer, new object[] { 1 });
Check(mesh0 == null && mesh1 != null && mesh1.DeformTexCoord(0.1f, 0.2f) == new Vector2(0.1f, 0.7f),
    "mesh 1 animates vertically without shifting mesh 0");
rendererType.GetProperty("ScrollU")!.SetValue(renderer, 0.25f);
mesh0 = (ITexCoordDeformer?)getDeformer.Invoke(renderer, new object[] { 0 });
mesh1 = (ITexCoordDeformer?)getDeformer.Invoke(renderer, new object[] { 1 });
Check(mesh0 != null && mesh1 != null &&
      Vector2.Distance(mesh0.DeformTexCoord(0, 0), new Vector2(0.25f, 0)) < 0.0001f &&
      Vector2.Distance(mesh1.DeformTexCoord(0, 0), new Vector2(0.25f, 0.5f)) < 0.0001f,
    "shared U scroll still composes with mesh-specific V scroll");

Type blurType = assembly.GetType("Client.Main.Objects.Effects.Joints.SourceObjectBlur")!;
object blur = Activator.CreateInstance(blurType, nonPublic: true)!;
var add = blurType.GetMethod("Add")!;
var move = blurType.GetMethod("Move")!;
var count = blurType.GetField("_count", BindingFlags.Instance | BindingFlags.NonPublic)!;
for (int i = 0; i < 610; i++) add.Invoke(blur, new object[] { Vector3.Zero, Vector3.One });
Check((int)count.GetValue(blur)! == 599, "object blur respects the original 599-tail limit");
for (int i = 0; i < 30; i++) move.Invoke(blur, null);
Check((int)count.GetValue(blur)! == 0, "object blur expires after 30 rendered frames");
add.Invoke(blur, new object[] { Vector3.Zero, Vector3.One });
Check((int)count.GetValue(blur)! == 1, "an expired blur can be reused for a new cast");
return failures == 0 ? 0 : 1;
