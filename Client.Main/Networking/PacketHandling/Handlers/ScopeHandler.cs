using Microsoft.Extensions.Logging;
using MUnique.OpenMU.Network.Packets.ServerToClient;
using Client.Main.Core.Utilities;
using Client.Main.Core.Models;
using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using MUnique.OpenMU.Network.Packets;
using Client.Main.Controls;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using Client.Main.Models;
using Client.Main.Objects;
using Client.Main.Objects.Player;
using Client.Main.Objects.Effects;
using Client.Main.Core.Client;
using Client.Main.Configuration;
using Client.Main.Scenes;
using Client.Main.Controllers;
using System.Threading;
using Client.Data.ATT;

namespace Client.Main.Networking.PacketHandling.Handlers
{
    /// <summary>
    /// Handles packets related to objects entering or leaving scope, moving, and dying.
    /// </summary>
    public class ScopeHandler : IGamePacketHandler
    {
        private static readonly string[] _recentHitPackets = new string[12];
        private static int _recentHitPacketIndex = -1;

        internal static string[] RecentHitPackets => _recentHitPackets;
        internal static int RecentHitPacketIndex => System.Threading.Volatile.Read(ref _recentHitPacketIndex);

        // ─────────────────────────── Fields ───────────────────────────
        private readonly ILogger<ScopeHandler> _logger;
        private readonly ScopeManager _scopeManager;
        private readonly CharacterState _characterState;
        private readonly NetworkManager _networkManager;
        private readonly PartyManager _partyManager;
        private readonly TargetProtocolVersion _targetVersion;
        private readonly ILoggerFactory _loggerFactory;
        private readonly bool _useExtendedWalkFormat;
        private readonly bool _useExtendedCharacterScopeFormat;
        private readonly Dictionary<byte, byte> _serverToClientDirMap;

        private static readonly List<NpcScopeObject> _pendingNpcsMonsters = new List<NpcScopeObject>();
        private static readonly List<PlayerScopeObject> _pendingPlayers = new List<PlayerScopeObject>();
        private static readonly HashSet<ushort> _pendingNpcMonsterIds = new();
        private static readonly HashSet<ushort> _pendingPlayerIds = new();
        private static readonly ConcurrentQueue<NpcSpawnRequest> _npcSpawnQueue = new();
        private static readonly ConcurrentQueue<PlayerSpawnRequest> _playerSpawnQueue = new();
        private static readonly ConcurrentQueue<DroppedItemWorkItem> _droppedItemQueue = new();
        private static readonly ConcurrentDictionary<ushort, int> _npcSpawnGenerations = new();
        private static int _npcSpawnsInFlight;
        private static int _playerSpawnWorkerRunning;
        private static int _droppedItemWorkerRunning;
        private const int MaxNpcSpawnsPerFrame = 8;
        private const int MaxConcurrentNpcSpawns = 8;
        private static ScopeHandler _activeInstance;

        // ─────────────────────── Constructors ────────────────────────
        public ScopeHandler(
            ILoggerFactory loggerFactory,
            ScopeManager scopeManager,
            CharacterState characterState,
            NetworkManager networkManager,
            PartyManager partyManager,
            TargetProtocolVersion targetVersion,
            MuOnlineSettings settings)
        {
            _logger = loggerFactory.CreateLogger<ScopeHandler>();
            _scopeManager = scopeManager;
            _characterState = characterState;
            _networkManager = networkManager;
            _partyManager = partyManager;
            _targetVersion = targetVersion;
            _loggerFactory = loggerFactory;
            _activeInstance = this;
            int clientVersionMajorMinor = ParseClientVersionMajorMinor(settings.ClientVersion);

            // Determine if server sends ObjectWalkedExtended based on client version.
            // OpenMU uses [MinimumClient(106, 3)] for Extended format (version >= 1.06.3).
            _useExtendedWalkFormat = targetVersion >= TargetProtocolVersion.Season6
                                    && clientVersionMajorMinor >= 107;

            // Open Source client 2.04d (mapped by OpenMU to client 106.3) uses the extended single-character
            // scope packet layout for code 0x12.
            _useExtendedCharacterScopeFormat = targetVersion >= TargetProtocolVersion.Season6
                                            && clientVersionMajorMinor >= 204;

            // Build server→client direction map (inverse of the client→server DirectionMap).
            _serverToClientDirMap = new Dictionary<byte, byte>();
            var clientToServer = networkManager.GetDirectionMap();
            if (clientToServer != null)
            {
                foreach (var kvp in clientToServer)
                {
                    _serverToClientDirMap[kvp.Value] = kvp.Key;
                }
            }

            _logger.LogInformation(
                "ScopeHandler: UseExtendedWalkFormat={ExtendedWalk}, UseExtendedCharacterScopeFormat={ExtendedScope}, ServerToClientDirMap entries={Count}",
                _useExtendedWalkFormat,
                _useExtendedCharacterScopeFormat,
                _serverToClientDirMap.Count);
        }

        /// <summary>
        /// Parses "X.YYz" client version string to major*100+minor (e.g. "2.04d" → 204, "1.04d" → 104).
        /// </summary>
        private static int ParseClientVersionMajorMinor(string clientVersion)
        {
            if (string.IsNullOrEmpty(clientVersion) || clientVersion.Length < 4)
                return 0;

            // Format: "X.YYz" where X=season, YY=episode, z=patch letter
            if (int.TryParse(clientVersion.AsSpan(0, 1), out int season)
                && int.TryParse(clientVersion.AsSpan(2, 2), out int episode))
            {
                return season * 100 + episode;
            }

            return 0;
        }

        private static int BumpNpcSpawnGeneration(ushort maskedId)
        {
            return _npcSpawnGenerations.AddOrUpdate(maskedId, 1, static (_, previous) => unchecked(previous + 1));
        }

        private static bool IsCurrentNpcSpawnGeneration(ushort maskedId, int generation)
        {
            return _npcSpawnGenerations.TryGetValue(maskedId, out int currentGeneration) &&
                   currentGeneration == generation;
        }

        private static void InvalidateNpcSpawnGeneration(ushort maskedId)
        {
            _npcSpawnGenerations.AddOrUpdate(maskedId, 1, static (_, previous) => unchecked(previous + 1));
        }

        /// <summary>
        /// Maps a server direction byte (0-7) to the client Direction enum using the inverse direction map.
        /// </summary>
        private Client.Main.Models.Direction MapServerDirection(byte serverDirection)
        {
            if (serverDirection > 7)
                return Client.Main.Models.Direction.South;

            if (_serverToClientDirMap.TryGetValue(serverDirection, out byte clientDir))
                return (Client.Main.Models.Direction)clientDir;

            return (Client.Main.Models.Direction)serverDirection;
        }

        /// <summary>
        /// Maps a client-facing direction (0-7) back to server-encoded direction.
        /// Used for re-queueing pending spawns through the unified scope pipeline.
        /// </summary>
        private byte MapClientDirectionToServer(byte clientDirection)
        {
            if (clientDirection > 7)
                return 0;

            foreach (var kvp in _serverToClientDirMap)
            {
                if (kvp.Value == clientDirection)
                    return kvp.Key;
            }

            return clientDirection;
        }

        private static void RecordHitPacket(ReadOnlySpan<byte> packetSpan)
        {
            try
            {
                var hex = BitConverter.ToString(packetSpan.ToArray()).Replace("-", " ");
                var entry = $"Len={packetSpan.Length} Data={hex}";
                var index = Interlocked.Increment(ref _recentHitPacketIndex);
                _recentHitPackets[index % _recentHitPackets.Length] = entry;
            }
            catch
            {
                // Diagnostic helper should never throw into caller.
            }
        }

        // ───────────────────── Internal API ────────────────────────
        /// <summary>
        /// Retrieves and clears pending player spawns.
        /// </summary>
        internal static List<PlayerScopeObject> TakePendingPlayers()
        {
            lock (_pendingPlayers)
            {
                var copy = new List<PlayerScopeObject>(_pendingPlayers);
                _pendingPlayers.Clear();
                _pendingPlayerIds.Clear();
                return copy;
            }
        }

        /// <summary>
        /// Retrieves and clears pending NPC and monster spawns.
        /// </summary>
        internal static List<NpcScopeObject> TakePendingNpcsMonsters()
        {
            lock (_pendingNpcsMonsters)
            {
                var copy = new List<NpcScopeObject>(_pendingNpcsMonsters);
                _pendingNpcsMonsters.Clear();
                _pendingNpcMonsterIds.Clear();
                return copy;
            }
        }

        /// <summary>
        /// Requeues pending NPC/monster descriptors into the normal spawn queue so all lifecycle checks
        /// (generation, scope validity, deduplication) run through one path.
        /// </summary>
        internal static void EnqueuePendingNpcsMonsters(IReadOnlyList<NpcScopeObject> pending)
        {
            var handler = _activeInstance;
            if (handler == null || pending == null || pending.Count == 0)
                return;

            ushort mapId = handler._characterState.MapId;
            var world = MuGame.Instance?.ActiveScene?.World as WalkableWorldControl;

            for (int i = 0; i < pending.Count; i++)
            {
                var npc = pending[i];
                if (npc == null)
                    continue;

                ushort maskedId = (ushort)(npc.Id & 0x7FFF);
                if (!handler._scopeManager.ScopeContains(maskedId))
                    continue;

                if (world != null && world.FindWalkerById(maskedId) != null)
                    continue;

                int spawnGeneration = BumpNpcSpawnGeneration(maskedId);
                byte serverDirection = handler.MapClientDirectionToServer(npc.Direction);
                _npcSpawnQueue.Enqueue(new NpcSpawnRequest(
                    maskedId,
                    npc.RawId,
                    npc.PositionX,
                    npc.PositionY,
                    serverDirection,
                    npc.TypeNumber,
                    npc.Name,
                    mapId,
                    spawnGeneration));
            }
        }

        // ───────────────────── Packet Handlers ──────────────────────

        [PacketHandler(0x12, PacketRouter.NoSubCode)] // AddCharacterToScope
        public Task HandleAddCharacterToScopeAsync(Memory<byte> packet)
        {
            try
            {
                ParseAndAddCharactersToScope(packet);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing AddCharactersToScope (0x12).");
            }
            return Task.CompletedTask;
        }

        private void ParseAndAddCharactersToScope(Memory<byte> packet)
        {
            bool looksLegacy = IsLikelyLegacyCharactersScopePacket(packet.Span);
            bool looksExtended = IsLikelyExtendedCharacterScopePacket(packet.Span);
            bool parsed = false;

            if (looksLegacy)
            {
                parsed = TryParseLegacyCharactersScopePacket(packet);
                if (!parsed)
                {
                    _logger.LogWarning("Legacy-looking AddCharacterToScope packet failed to parse. Length={Length}", packet.Length);
                }
            }

            if (!parsed && looksExtended)
            {
                parsed = TryParseExtendedCharacterScopePacket(packet);
                if (!parsed)
                {
                    _logger.LogWarning("Extended-looking AddCharacterToScope packet failed to parse. Length={Length}", packet.Length);
                }
            }

            if (!parsed && !looksLegacy && !looksExtended)
            {
                _logger.LogDebug(
                    "AddCharacterToScope packet layout not recognized. UseExtendedCharacterScopeFormat={UseExtended}. Length={Length}",
                    _useExtendedCharacterScopeFormat,
                    packet.Length);

                if (_useExtendedCharacterScopeFormat)
                {
                    parsed = TryParseExtendedCharacterScopePacket(packet);
                    if (!parsed)
                    {
                        parsed = TryParseLegacyCharactersScopePacket(packet);
                    }
                }
                else
                {
                    parsed = TryParseLegacyCharactersScopePacket(packet);
                    if (!parsed && _targetVersion >= TargetProtocolVersion.Season6)
                    {
                        parsed = TryParseExtendedCharacterScopePacket(packet);
                    }
                }
            }

            if (!parsed)
            {
                _logger.LogWarning("Failed to parse AddCharacterToScope packet (0x12). Length={Length}", packet.Length);
            }
        }

        private bool TryParseLegacyCharactersScopePacket(Memory<byte> packet)
        {
            try
            {
                if (!IsLikelyLegacyCharactersScopePacket(packet.Span))
                {
                    return false;
                }

                var scope = new AddCharactersToScopeRef(packet.Span);

                for (int i = 0; i < scope.CharacterCount; i++)
                {
                    var c = scope[i];
                    ushort raw = c.Id;

                    if (c.EffectCount > 0)
                    {
                        for (int e = 0; e < c.EffectCount; e++)
                        {
                            byte effectId = c[e].Id;
                            _characterState.ActivateBuff(effectId, raw);
                            ElfBuffEffectManager.Instance?.HandleBuff(effectId, raw, true);
                        }
                    }

                    UpsertAndSpawnRemotePlayer(
                        raw,
                        c.CurrentPositionX,
                        c.CurrentPositionY,
                        c.Name,
                        ClassFromStandardAppearance(c.Appearance),
                        c.Appearance);
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Legacy AddCharactersToScope parse failed.");
                return false;
            }
        }

        private bool TryParseExtendedCharacterScopePacket(Memory<byte> packet)
        {
            try
            {
                if (!TryParseExtendedPacketMetadata(packet.Span, out byte serverClassValue, out int effectCount))
                {
                    return false;
                }

                var character = new AddCharacterToScopeExtended(packet);
                ushort raw = character.Id;
                var appearanceAndEffects = character.AppearanceAndEffects;
                var appearance = appearanceAndEffects.Slice(2, 25);

                for (int i = 0; i < effectCount; i++)
                {
                    byte effectId = appearanceAndEffects[28 + i];
                    _characterState.ActivateBuff(effectId, raw);
                    ElfBuffEffectManager.Instance?.HandleBuff(effectId, raw, true);
                }

                UpsertAndSpawnRemotePlayer(
                    raw,
                    character.CurrentPositionX,
                    character.CurrentPositionY,
                    character.Name,
                    MapClassValueToEnum(serverClassValue),
                    appearance);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Extended AddCharacterToScope parse failed.");
                return false;
            }
        }

        private void UpsertAndSpawnRemotePlayer(
            ushort rawId,
            byte x,
            byte y,
            string name,
            CharacterClassNumber cls,
            ReadOnlySpan<byte> appearance)
        {
            ushort maskedId = (ushort)(rawId & 0x7FFF);

            // Always update the manager, even for the local player.
            _scopeManager.AddOrUpdatePlayerInScope(maskedId, rawId, x, y, name);

            if (maskedId == _characterState.Id)
            {
                return;
            }

            var appearanceBytes = appearance.ToArray();

            // Spawn remote players immediately if the world is ready, otherwise buffer for later.
            if (MuGame.Instance.ActiveScene?.World is WalkableWorldControl w
                && w.Status == GameControlStatus.Ready)
            {
                SpawnRemotePlayerIntoWorld(w, maskedId, rawId, x, y, name, cls, appearanceBytes);
                return;
            }

            lock (_pendingPlayers)
            {
                if (_pendingPlayerIds.Add(maskedId))
                {
                    _pendingPlayers.Add(new PlayerScopeObject(maskedId, rawId, x, y, name, cls, appearanceBytes));
                }
            }
        }

        private static CharacterClassNumber ClassFromStandardAppearance(ReadOnlySpan<byte> app)
        {
            if (app.Length == 0) return CharacterClassNumber.DarkWizard;
            return MapClassValueToEnum((app[0] >> 3) & 0b1_1111);
        }

        private static bool IsLikelyLegacyCharactersScopePacket(ReadOnlySpan<byte> packet)
        {
            if (packet.Length < 5)
            {
                return false;
            }

            int characterCount = packet[4];
            int offset = 5;
            for (int i = 0; i < characterCount; i++)
            {
                if (offset + 36 > packet.Length)
                {
                    return false;
                }

                if (!IsLikelyLegacyCharacterName(packet.Slice(offset + 22, 10)))
                {
                    return false;
                }

                int effectCount = packet[offset + 35];
                offset += 36 + effectCount;
                if (offset > packet.Length)
                {
                    return false;
                }
            }

            return offset == packet.Length;
        }

        private static bool IsLikelyLegacyCharacterName(ReadOnlySpan<byte> rawNameBytes)
        {
            bool seenNonZeroByte = false;
            bool foundTerminator = false;

            for (int i = 0; i < rawNameBytes.Length; i++)
            {
                byte b = rawNameBytes[i];
                if (b == 0)
                {
                    foundTerminator = true;
                    continue;
                }

                if (foundTerminator)
                {
                    return false;
                }

                if (b < 0x20 || b == 0x7F)
                {
                    return false;
                }

                seenNonZeroByte = true;
            }

            return seenNonZeroByte;
        }

        private static bool IsLikelyExtendedCharacterScopePacket(ReadOnlySpan<byte> packet)
        {
            return TryParseExtendedPacketMetadata(packet, out _, out _);
        }

        private static bool TryParseExtendedPacketMetadata(ReadOnlySpan<byte> packet, out byte serverClassValue, out int effectCount)
        {
            serverClassValue = 0;
            effectCount = 0;

            if (packet.Length < 54)
            {
                return false;
            }

            var appearanceAndEffects = packet[26..];
            if (appearanceAndEffects.Length < 28)
            {
                return false;
            }

            byte flags = appearanceAndEffects[1];
            if ((flags & 0xC0) != 0)
            {
                return false;
            }

            serverClassValue = appearanceAndEffects[0];
            if (!IsKnownServerClassValue(serverClassValue))
            {
                return false;
            }

            effectCount = appearanceAndEffects[27];
            return appearanceAndEffects.Length == 28 + effectCount;
        }

        private static CharacterClassNumber MapClassValueToEnum(int value)
        {
            return IsKnownServerClassValue(value)
                ? (CharacterClassNumber)value
                : CharacterClassNumber.DarkWizard;
        }

        private static bool IsKnownServerClassValue(int value)
        {
            return value is 0 or 2 or 3 or 4 or 6 or 7 or 8 or 10 or 11 or 12 or 13 or
                16 or 17 or 20 or 22 or 23 or 24 or 25;
        }

        private void SpawnRemotePlayerIntoWorld(
                WalkableWorldControl world,
                ushort maskedId,
                ushort rawId,
                byte x,
                byte y,
                string name,
                CharacterClassNumber cls,
                ReadOnlyMemory<byte> appearanceData)
        {
            _logger.LogDebug("[Spawn] Received request for {Name} ({MaskedId:X4}).", name, maskedId);
            _playerSpawnQueue.Enqueue(new PlayerSpawnRequest(world, maskedId, rawId, x, y, name, cls, appearanceData));
            TryStartPlayerSpawnWorker();
        }

        private void TryStartPlayerSpawnWorker()
        {
            if (Interlocked.CompareExchange(ref _playerSpawnWorkerRunning, 1, 0) != 0)
                return;

            _ = ProcessPlayerSpawnQueueAsync();
        }

        private async Task ProcessPlayerSpawnQueueAsync()
        {
            try
            {
                while (_playerSpawnQueue.TryDequeue(out var request))
                {
                    try
                    {
                        await ProcessPlayerSpawnAsync(
                            request.World,
                            request.MaskedId,
                            request.RawId,
                            request.X,
                            request.Y,
                            request.Name,
                            request.Class,
                            request.AppearanceData);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "[Spawn] Error processing player spawn for {Name} ({MaskedId:X4}).", request.Name, request.MaskedId);
                    }
                }
            }
            finally
            {
                Volatile.Write(ref _playerSpawnWorkerRunning, 0);
                if (!_playerSpawnQueue.IsEmpty)
                    TryStartPlayerSpawnWorker();
            }
        }

        private async Task ProcessPlayerSpawnAsync(
                WalkableWorldControl world,
                ushort maskedId,
                ushort rawId,
                byte x,
                byte y,
                string name,
                CharacterClassNumber cls,
                ReadOnlyMemory<byte> appearanceData)
        {
            _logger.LogDebug("[Spawn] Starting creation for {Name} ({MaskedId:X4}).", name, maskedId);

            if (MuGame.Instance.ActiveScene?.World != world || world.Status != GameControlStatus.Ready)
            {
                _logger.LogWarning("[Spawn] World changed or not ready. Aborting spawn for {Name}.", name);
                return;
            }

            var p = new PlayerObject(new AppearanceData(appearanceData))
            {
                NetworkId = maskedId,
                CharacterClass = cls,
                Name = name,
                Location = new Vector2(x, y),
                World = world
            };
            _logger.LogDebug("[Spawn] PlayerObject created for {Name}.", name);

            var preloadTask = p.PreloadAppearanceModelsAsync();

            // Load assets in background
            try
            {
                var loadTask = p.Load();
                await Task.WhenAll(preloadTask, loadTask);
                _logger.LogDebug("[Spawn] Assets preloaded and Load() completed for {Name}.", name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Spawn] Error loading assets for {Name} ({MaskedId:X4}).", name, maskedId);
                MuGame.ScheduleOnMainThread(() => p.Dispose());
                return;
            }

            // Add to world on main thread
            MuGame.ScheduleOnMainThread(() =>
            {
                // Double-check world is still valid
                if (MuGame.Instance.ActiveScene?.World != world || world.Status != GameControlStatus.Ready)
                {
                    _logger.LogWarning("[Spawn] World changed or not ready during spawn. Aborting spawn for {Name}.", name);
                    p.Dispose();
                    return;
                }

                if (world.WalkerObjectsById.TryGetValue(maskedId, out WalkerObject existingWalker))
                {
                    _logger.LogWarning("[Spawn] Stale object for {Name} found. Removing before adding new.", name);
                    world.Objects.Remove(existingWalker);
                    existingWalker.Dispose();
                }

                if (world.FindPlayerById(maskedId) != null)
                {
                    _logger.LogWarning("[Spawn] PlayerObject for {Name} already exists. Aborting.", name);
                    p.Dispose();
                    return;
                }

                world.Objects.Add(p);
                _logger.LogDebug("[Spawn] Added {Name} to world.Objects.", name);

                ElfBuffEffectManager.Instance?.EnsureBuffsForPlayer(maskedId);

                // Set final position
                if (p.World != null && p.World.Terrain != null)
                {
                    p.MoveTargetPosition = p.TargetPosition;
                    p.Position = p.TargetPosition;
                }
                else
                {
                    float worldX = p.Location.X * Constants.TERRAIN_SCALE + 0.5f * Constants.TERRAIN_SCALE;
                    float worldY = p.Location.Y * Constants.TERRAIN_SCALE + 0.5f * Constants.TERRAIN_SCALE;
                    p.MoveTargetPosition = new Vector3(worldX, worldY, 0);
                    p.Position = p.MoveTargetPosition;
                }
                _logger.LogDebug("[Spawn] Successfully spawned {Name} ({MaskedId:X4}) into world.", name, maskedId);
            });
        }

        [PacketHandler(0x13, PacketRouter.NoSubCode)] // AddNpcToScope
        public Task HandleAddNpcToScopeAsync(Memory<byte> packet)
        {
            ParseAndQueueNpcSpawns(packet);
            return Task.CompletedTask;
        }

        [PacketHandler(0x16, PacketRouter.NoSubCode)] // AddMonstersToScope
        public Task HandleAddMonstersToScopeAsync(Memory<byte> packet)
        {
            ParseAndQueueNpcSpawns(packet);
            return Task.CompletedTask;
        }

        private void ParseAndQueueNpcSpawns(Memory<byte> packet)
        {
            int npcCount = 0, firstOffset = 0, dataSize = 0;
            Func<Memory<byte>, (ushort id, ushort type, byte x, byte y, byte direction)> readNpc = null!;

            switch (_targetVersion)
            {
                case TargetProtocolVersion.Season6:
                    var s6 = new AddNpcsToScope(packet);
                    npcCount = s6.NpcCount;
                    firstOffset = 5;
                    dataSize = AddNpcsToScope.NpcData.Length;
                    readNpc = m => { var d = new AddNpcsToScope.NpcData(m); return (d.Id, d.TypeNumber, d.CurrentPositionX, d.CurrentPositionY, d.Rotation); };
                    break;
                case TargetProtocolVersion.Version097:
                    var v97 = new AddNpcsToScope095(packet);
                    npcCount = v97.NpcCount;
                    firstOffset = 5;
                    dataSize = AddNpcsToScope095.NpcData.Length;
                    readNpc = m => { var d = new AddNpcsToScope095.NpcData(m); return (d.Id, d.TypeNumber, d.CurrentPositionX, d.CurrentPositionY, d.Rotation); };
                    break;
                case TargetProtocolVersion.Version075:
                    var v75 = new AddNpcsToScope075(packet);
                    npcCount = v75.NpcCount;
                    firstOffset = 5;
                    dataSize = AddNpcsToScope075.NpcData.Length;
                    readNpc = m => { var d = new AddNpcsToScope075.NpcData(m); return (d.Id, d.TypeNumber, d.CurrentPositionX, d.CurrentPositionY, d.Rotation); };
                    break;
                default:
                    _logger.LogWarning("Unsupported protocol version {Version} for AddNpcToScope.", _targetVersion);
                    return;
            }

            _logger.LogDebug("ScopeHandler: AddNpcToScope received {Count} objects.", npcCount);

            int currentPacketOffset = firstOffset;
            ushort currentMapId = _characterState.MapId;

            for (int i = 0; i < npcCount; i++)
            {
                if (currentPacketOffset + dataSize > packet.Length)
                {
                    _logger.LogWarning("ScopeHandler: Packet too short for NPC data at index {Index}.", i);
                    break;
                }

                var (rawId, type, x, y, direction) = readNpc(packet.Slice(currentPacketOffset));
                currentPacketOffset += dataSize;

                ushort maskedId = (ushort)(rawId & 0x7FFF);
                string name = NpcDatabase.GetNpcName(type);

                _scopeManager.AddOrUpdateNpcInScope(maskedId, rawId, x, y, type, name);
                int spawnGeneration = BumpNpcSpawnGeneration(maskedId);

                _npcSpawnQueue.Enqueue(new NpcSpawnRequest(maskedId, rawId, x, y, direction, type, name, currentMapId, spawnGeneration));
            }
        }

        internal static void PumpNpcSpawnQueue(WalkableWorldControl world, int maxPerFrame = MaxNpcSpawnsPerFrame)
        {
            if (world == null || world.Status != GameControlStatus.Ready)
            {
                return;
            }

            var handler = _activeInstance;
            if (handler == null || _npcSpawnQueue.IsEmpty)
            {
                return;
            }

            int startedThisFrame = 0;
            while (startedThisFrame < maxPerFrame
                && Volatile.Read(ref _npcSpawnsInFlight) < MaxConcurrentNpcSpawns
                && _npcSpawnQueue.TryDequeue(out var request))
            {
                if (request.MapId != handler._characterState.MapId)
                {
                    handler._logger.LogDebug("Discarding queued NPC/Monster spawn {SpawnId:X4} for map {RequestMap} after map changed to {CurrentMap}.", request.MaskedId, request.MapId, handler._characterState.MapId);
                    continue;
                }

                startedThisFrame++;
                handler.StartNpcSpawn(request);
            }
        }

        private void StartNpcSpawn(NpcSpawnRequest request)
        {
            if (request.MapId != _characterState.MapId)
            {
                _logger.LogDebug("Skipping queued NPC/Monster spawn {SpawnId:X4} for stale map {RequestMap} (current: {CurrentMap}).", request.MaskedId, request.MapId, _characterState.MapId);
                return;
            }

            if (!IsCurrentNpcSpawnGeneration(request.MaskedId, request.SpawnGeneration))
            {
                _logger.LogDebug("Skipping stale NPC/Monster spawn {SpawnId:X4} (generation {Generation}).", request.MaskedId, request.SpawnGeneration);
                return;
            }

            Interlocked.Increment(ref _npcSpawnsInFlight);

            _ = ProcessNpcSpawnAsync(request).ContinueWith(t =>
            {
                if (t.IsFaulted && t.Exception != null)
                {
                    _logger.LogError(t.Exception, "ScopeHandler: Error processing NPC/Monster spawn {SpawnId:X4}.", request.MaskedId);
                }

                Interlocked.Decrement(ref _npcSpawnsInFlight);
            }, global::System.Threading.Tasks.TaskScheduler.Default);
        }

        private Task ProcessNpcSpawnAsync(NpcSpawnRequest request)
        {
            if (request.MapId != _characterState.MapId)
            {
                _logger.LogDebug("Dropping NPC/Monster spawn {SpawnId:X4} queued for map {RequestMap} after map changed to {CurrentMap}.", request.MaskedId, request.MapId, _characterState.MapId);
                return Task.CompletedTask;
            }

            if (!IsCurrentNpcSpawnGeneration(request.MaskedId, request.SpawnGeneration))
            {
                _logger.LogDebug("Dropping stale NPC/Monster spawn {SpawnId:X4} (generation {Generation}) before load.", request.MaskedId, request.SpawnGeneration);
                return Task.CompletedTask;
            }

            return ProcessNpcSpawnAsync(request.MaskedId, request.RawId, request.X, request.Y, request.Direction, request.Type, request.Name, request.SpawnGeneration);
        }

        private async Task ProcessNpcSpawnAsync(ushort maskedId, ushort rawId, byte x, byte y, byte direction, ushort type, string name, int spawnGeneration)
        {
            if (!IsCurrentNpcSpawnGeneration(maskedId, spawnGeneration))
            {
                _logger.LogDebug("Skipping NPC/Monster spawn {SpawnId:X4} due to outdated generation {Generation}.", maskedId, spawnGeneration);
                return;
            }

            if (MuGame.Instance.ActiveScene?.World is not WalkableWorldControl worldRef || worldRef.Status != GameControlStatus.Ready)
            {
                lock (_pendingNpcsMonsters)
                {
                    if (_pendingNpcMonsterIds.Add(maskedId))
                    {
                        _pendingNpcsMonsters.Add(new NpcScopeObject(maskedId, rawId, x, y, type, name) { Direction = (byte)MapServerDirection(direction) });
                    }
                }
                return;
            }

            if (!NpcDatabase.TryGetNpcType(type, out var npcClassType))
            {
                _logger.LogWarning("ScopeHandler: NPC type not found in NpcDatabase for TypeID {TypeId}.", type);
                return;
            }

            if (!(Activator.CreateInstance(npcClassType) is WalkerObject obj))
            {
                _logger.LogWarning("ScopeHandler: Could not create instance of NPC type {NpcClassType} for TypeID {TypeId}.", npcClassType, type);
                return;
            }

            // Configure the object's properties
            obj.NetworkId = maskedId;
            obj.Location = new Vector2(x, y);
            obj.Direction = MapServerDirection(direction);
            obj.World = worldRef;

            // Load assets in background
            try
            {
                await obj.Load();
                if (obj is ModelObject modelObj)
                {
                    // Skip preloading to avoid blocking
                }

                if (obj.Status != GameControlStatus.Ready)
                {
                    _logger.LogWarning(
                        "ScopeHandler: NPC/Monster {MaskedId:X4} ({WalkerType}) loaded but status is {Status}.",
                        maskedId,
                        obj.GetType().Name,
                        obj.Status);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ScopeHandler: Error loading NPC/Monster {MaskedId:X4} ({WalkerType}).", maskedId, obj.GetType().Name);
                MuGame.ScheduleOnMainThread(() => obj.Dispose());
                return;
            }

            if (!IsCurrentNpcSpawnGeneration(maskedId, spawnGeneration) || !_scopeManager.ScopeContains(maskedId))
            {
                MuGame.ScheduleOnMainThread(() => obj.Dispose());
                return;
            }

            // Add to world on main thread
            MuGame.ScheduleOnMainThread(() =>
            {
                // Double-check world is still valid and object doesn't already exist
                if (MuGame.Instance.ActiveScene?.World != worldRef || worldRef.Status != GameControlStatus.Ready)
                {
                    obj.Dispose();
                    return;
                }

                if (!IsCurrentNpcSpawnGeneration(maskedId, spawnGeneration) || !_scopeManager.ScopeContains(maskedId))
                {
                    obj.Dispose();
                    return;
                }

                // Check and remove stale objects quickly
                if (worldRef.WalkerObjectsById.TryGetValue(maskedId, out WalkerObject existingWalker))
                {
                    _logger.LogWarning(
                        "ScopeHandler: Stale/Duplicate NPC/Monster ID {MaskedId:X4} ({ExistingWalkerType}) found in WalkerObjectsById. Removing it before adding new {Name} (Type: {TypeId}).",
                        maskedId,
                        existingWalker.GetType().Name,
                        name,
                        type);

                    existingWalker.Dispose();
                    worldRef.Objects.Remove(existingWalker);
                }

                // Quick check for duplicates using cached walkers
                if (worldRef.FindWalkerById(maskedId) != null)
                {
                    obj.Dispose();
                    return;
                }

                worldRef.Objects.Add(obj);

                // Set final position
                if (obj.World?.Terrain != null)
                {
                    obj.MoveTargetPosition = obj.TargetPosition;
                    obj.Position = obj.TargetPosition;
                }
                else
                {
                    _logger.LogError(
                        "ScopeHandler: obj.World or obj.World.Terrain is null for NPC/Monster {MaskedId:X4} ({WalkerType}) AFTER loading and adding. This indicates a problem.",
                        maskedId,
                        obj.GetType().Name);
                    float worldX = obj.Location.X * Constants.TERRAIN_SCALE + 0.5f * Constants.TERRAIN_SCALE;
                    float worldY = obj.Location.Y * Constants.TERRAIN_SCALE + 0.5f * Constants.TERRAIN_SCALE;
                    obj.MoveTargetPosition = new Vector3(worldX, worldY, 0);
                    obj.Position = obj.MoveTargetPosition;
                }

                // Play Appear animation for monsters that have MonsterActionType.Appear mapped
                if (obj is MonsterObject monster && obj is ModelObject modelObj)
                {
                    // Check if monster has Appear animation available
                    if (modelObj.Model?.Actions != null &&
                        (int)MonsterActionType.Appear < modelObj.Model.Actions.Length &&
                        modelObj.Model.Actions[(int)MonsterActionType.Appear] != null)
                    {
                        // Play MonsterActionType.Appear animation for dramatic spawn effect
                        monster.PlayAction((ushort)MonsterActionType.Appear);
                    }
                }
            });
        }

        private readonly struct NpcSpawnRequest
        {
            public NpcSpawnRequest(ushort maskedId, ushort rawId, byte x, byte y, byte direction, ushort type, string name, ushort mapId, int spawnGeneration)
            {
                MaskedId = maskedId;
                RawId = rawId;
                X = x;
                Y = y;
                Direction = direction;
                Type = type;
                Name = name;
                MapId = mapId;
                SpawnGeneration = spawnGeneration;
            }

            public ushort MaskedId { get; }
            public ushort RawId { get; }
            public byte X { get; }
            public byte Y { get; }
            public byte Direction { get; }
            public ushort Type { get; }
            public string Name { get; }
            public ushort MapId { get; }
            public int SpawnGeneration { get; }
        }

        private readonly struct PlayerSpawnRequest
        {
            public PlayerSpawnRequest(
                WalkableWorldControl world,
                ushort maskedId,
                ushort rawId,
                byte x,
                byte y,
                string name,
                CharacterClassNumber @class,
                ReadOnlyMemory<byte> appearanceData)
            {
                World = world;
                MaskedId = maskedId;
                RawId = rawId;
                X = x;
                Y = y;
                Name = name;
                Class = @class;
                AppearanceData = appearanceData;
            }

            public WalkableWorldControl World { get; }
            public ushort MaskedId { get; }
            public ushort RawId { get; }
            public byte X { get; }
            public byte Y { get; }
            public string Name { get; }
            public CharacterClassNumber Class { get; }
            public ReadOnlyMemory<byte> AppearanceData { get; }
        }

        private readonly struct DroppedItemWorkItem
        {
            public DroppedItemWorkItem(ScopeObject dropObj, ushort maskedId, string soundPath)
            {
                DropObject = dropObj;
                MaskedId = maskedId;
                SoundPath = soundPath;
            }

            public ScopeObject DropObject { get; }
            public ushort MaskedId { get; }
            public string SoundPath { get; }
        }

        [PacketHandler(0x25, PacketRouter.NoSubCode)]
        public async Task HandleAppearanceChangedAsync(Memory<byte> packet)
        {
            try
            {
                const byte UNEQUIP_MARKER = 0xFF;
                const ushort ID_MASK = 0x7FFF;

                var span = packet.Span;

                // Season 6 servers can send two variants:
                // - Standard AppearanceChanged (length 13): player id + 8 bytes packed item appearance.
                // - AppearanceChangedExtended (length 14): explicit slot/group/number/level fields.
                if (span.Length == 14)
                {
                    const int EXT_PLAYER_ID_OFFSET = 4;
                    const int EXT_ITEM_SLOT_OFFSET = 6;
                    const int EXT_ITEM_GROUP_OFFSET = 7;
                    const int EXT_ITEM_NUMBER_OFFSET = 8;
                    const int EXT_ITEM_LEVEL_OFFSET = 10;
                    const int EXT_EXCELLENT_FLAGS_OFFSET = 11;
                    const int EXT_ANCIENT_DISCRIMINATOR_OFFSET = 12;
                    const int EXT_SET_COMPLETE_OFFSET = 13;

                    ushort extRawKey = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(EXT_PLAYER_ID_OFFSET, 2));
                    ushort extMaskedId = (ushort)(extRawKey & ID_MASK);

                    byte extItemSlot = span[EXT_ITEM_SLOT_OFFSET];
                    byte extItemGroup = span[EXT_ITEM_GROUP_OFFSET];

                    if (extItemGroup == UNEQUIP_MARKER)
                    {
                        await HandleUnequipAsync(extMaskedId, extItemSlot);
                        return;
                    }

                    ushort extItemNumber = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(EXT_ITEM_NUMBER_OFFSET, 2));
                    byte extItemLevel = span[EXT_ITEM_LEVEL_OFFSET];
                    byte extExcellentFlags = span[EXT_EXCELLENT_FLAGS_OFFSET];
                    byte extAncientDiscriminator = span[EXT_ANCIENT_DISCRIMINATOR_OFFSET];
                    bool extIsAncientSetComplete = span[EXT_SET_COMPLETE_OFFSET] != 0;

                    const int EXT_MAX_ITEM_INDEX = 512;
                    int extFinalItemType = (extItemGroup * EXT_MAX_ITEM_INDEX) + extItemNumber;

                    _logger.LogDebug("Parsed AppearanceChangedExtended for ID {Id:X4}: Slot={Slot}, Group={Group}, Number={Number}, Type={Type}, Level={Level}",
                        extMaskedId, extItemSlot, extItemGroup, extItemNumber, extFinalItemType, extItemLevel);

                    _logger.LogDebug("[ScopeHandler] AppearanceChangedExtended ID {Id:X4}: ExcFlags=0x{ExcFlags:X2}, AncDisc=0x{AncDisc:X2}, SetComplete={SetComplete}",
                        extMaskedId, extExcellentFlags, extAncientDiscriminator, extIsAncientSetComplete);

                    await HandleEquipAsync(extMaskedId, extItemSlot, extItemGroup, extItemNumber, extFinalItemType, extItemLevel,
                        itemOptions: 0, extExcellentFlags, extAncientDiscriminator, extIsAncientSetComplete);
                    return;
                }

                // Standard packed variant.
                const int STD_MIN_LENGTH = 7; // header(3) + id(2) + at least 2 bytes of item data
                const int STD_PLAYER_ID_OFFSET = 3;
                const int STD_ITEM_DATA_OFFSET = 5;
                const int WEAPON_SLOT_THRESHOLD = 2;
                const int WEAPON_GROUP = 0;
                const int ARMOR_GROUP_OFFSET = 5;

                if (span.Length < STD_MIN_LENGTH)
                {
                    _logger.LogWarning("AppearanceChanged packet (0x25) too short: {Length}.", span.Length);
                    return;
                }

                ushort stdRawKey = System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(span.Slice(STD_PLAYER_ID_OFFSET, 2));
                ushort stdMaskedId = (ushort)(stdRawKey & ID_MASK);

                var itemData = span.Slice(STD_ITEM_DATA_OFFSET);
                if (itemData.Length < 2)
                {
                    _logger.LogWarning("AppearanceChanged packet (0x25) item data too short: {Length}.", itemData.Length);
                    return;
                }

                byte itemSlot = (byte)((itemData[1] >> 4) & 0x0F);

                if (itemData[0] == UNEQUIP_MARKER)
                {
                    await HandleUnequipAsync(stdMaskedId, itemSlot);
                    return;
                }

                byte glowLevel = (byte)(itemData[1] & 0x0F);

                ushort itemNumber = itemData[0];
                byte itemGroup;

                if (itemSlot < WEAPON_SLOT_THRESHOLD)
                {
                    // For weapon slots the group is encoded in itemData[2] high nibble,
                    // and the item number high bits in its low nibble (same layout as viewport equipment).
                    if (itemData.Length > 2)
                    {
                        itemGroup = (byte)((itemData[2] >> 4) & 0x0F);
                        itemNumber = (ushort)(itemNumber | ((itemData[2] & 0x0F) << 8));
                    }
                    else
                    {
                        itemGroup = (byte)WEAPON_GROUP;
                    }
                }
                else
                {
                    itemGroup = (byte)(itemSlot + ARMOR_GROUP_OFFSET);
                }

                byte itemLevel = ConvertGlowToItemLevel(glowLevel);

                byte itemOptions = itemData.Length > 3 ? itemData[3] : (byte)0;
                byte excellentFlags = itemData.Length > 4 ? itemData[4] : (byte)0;
                byte ancientDiscriminator = itemData.Length > 5 ? itemData[5] : (byte)0;
                bool isAncientSetComplete = itemData.Length > 6 && itemData[6] != 0;

                const int MAX_ITEM_INDEX = 512;
                int finalItemType = (itemGroup * MAX_ITEM_INDEX) + itemNumber;

                _logger.LogDebug("Parsed AppearanceChanged for ID {Id:X4}: Slot={Slot}, Group={Group}, Number={Number}, Type={Type}, Level={Level}",
                    stdMaskedId, itemSlot, itemGroup, itemNumber, finalItemType, itemLevel);

                _logger.LogDebug("[ScopeHandler] AppearanceChanged ID {Id:X4}: ExcFlags=0x{ExcFlags:X2}, AncDisc=0x{AncDisc:X2}, SetComplete={SetComplete}",
                    stdMaskedId, excellentFlags, ancientDiscriminator, isAncientSetComplete);

                await HandleEquipAsync(stdMaskedId, itemSlot, itemGroup, itemNumber, finalItemType, itemLevel,
                    itemOptions, excellentFlags, ancientDiscriminator, isAncientSetComplete);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing AppearanceChanged (0x25).");
            }
        }

        private Task HandleUnequipAsync(ushort maskedId, byte itemSlot)
        {
            MuGame.ScheduleOnMainThread(async () =>
            {
                if (MuGame.Instance.ActiveScene?.World is not WorldControl world)
                {
                    _logger.LogWarning("No world available for unequip operation");
                    return;
                }

                if (world.TryGetWalkerById(maskedId, out var walker) && walker is PlayerObject player)
                {
                    try
                    {
                        await player.UpdateEquipmentSlotAsync(itemSlot, null);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error unequipping item from slot {Slot}", itemSlot);
                    }
                }
                else
                {
                    _logger.LogWarning("Player with ID {Id:X4} not found for unequip operation", maskedId);
                }
            });
            return Task.CompletedTask;
        }

        private Task HandleEquipAsync(ushort maskedId, byte itemSlot, byte itemGroup, ushort itemNumber,
            int finalItemType, byte itemLevel, byte itemOptions, byte excellentFlags,
            byte ancientDiscriminator, bool isAncientSetComplete)
        {
            MuGame.ScheduleOnMainThread(async () =>
            {
                if (MuGame.Instance.ActiveScene?.World is not WorldControl world) return;

                if (world.TryGetWalkerById(maskedId, out var walker) && walker is PlayerObject player)
                {
                    var equipmentData = new EquipmentSlotData
                    {
                        ItemGroup = itemGroup,
                        ItemNumber = itemNumber,
                        ItemType = finalItemType,
                        ItemLevel = itemLevel,
                        ItemOptions = itemOptions,
                        ExcellentFlags = excellentFlags,
                        AncientDiscriminator = ancientDiscriminator,
                        IsAncientSetComplete = isAncientSetComplete
                    };

                    await player.UpdateEquipmentSlotAsync(itemSlot, equipmentData);
                }
                else
                {
                    _logger.LogWarning("Player with ID {Id:X4} not found in scope.", maskedId);
                }
            });
            return Task.CompletedTask;
        }

        [PacketHandler(0x11, PacketRouter.NoSubCode)] // ObjectHit / ObjectGotHit
        public Task HandleObjectHitAsync(Memory<byte> packet)
        {
            try
            {
                RecordHitPacket(packet.Span);

                bool isExtendedPacket = packet.Length >= ObjectHitExtended.Length;
                ushort rawId;
                uint healthDmg;
                uint shieldDmg;
                DamageKind damageKind;
                bool isDoubleDamage;
                bool isTripleDamage;
                byte? healthStatus = null;
                byte? shieldStatus = null;

                if (isExtendedPacket)
                {
                    var extended = new ObjectHitExtended(packet);
                    rawId = extended.ObjectId;
                    healthDmg = extended.HealthDamage;
                    shieldDmg = extended.ShieldDamage;
                    damageKind = extended.Kind;
                    isDoubleDamage = extended.IsDoubleDamage;
                    isTripleDamage = extended.IsTripleDamage;
                    healthStatus = extended.HealthStatus;
                    shieldStatus = extended.ShieldStatus;
                }
                else
                {
                    if (packet.Length < ObjectHit.Length)
                    {
                        _logger.LogWarning("ObjectHit packet (0x11) too short: {Length}", packet.Length);
                        return Task.CompletedTask;
                    }

                    var hitInfo = new ObjectHit(packet);
                    rawId = hitInfo.ObjectId;
                    healthDmg = hitInfo.HealthDamage;
                    shieldDmg = hitInfo.ShieldDamage;
                    damageKind = hitInfo.Kind;
                    isDoubleDamage = hitInfo.IsDoubleDamage;
                    isTripleDamage = hitInfo.IsTripleDamage;
                }

                ushort maskedId = (ushort)(rawId & 0x7FFF);
                uint totalDmg = healthDmg + shieldDmg;

                float? healthFraction = null;
                float? shieldFraction = null;
                const float statusScale = 1f / 255f;

                if (healthStatus is { } hs && hs != byte.MaxValue)
                {
                    healthFraction = Math.Clamp(hs * statusScale, 0f, 1f);
                }
                else if (healthStatus.HasValue)
                {
                    _logger.LogDebug("ObjectHit 0x11: HealthStatus unknown (0xFF) for {Id:X4}.", maskedId);
                }
                else
                {
                    _logger.LogDebug("ObjectHit 0x11: HealthStatus missing (short packet) for {Id:X4}.", maskedId);
                }

                if (shieldStatus is { } ss && ss != byte.MaxValue)
                {
                    shieldFraction = Math.Clamp(ss * statusScale, 0f, 1f);
                }

                // Log damage event with type information
                string objectName = _scopeManager.TryGetScopeObjectName(maskedId, out var nm) ? (nm ?? "Object") : "Object";
                _logger.LogDebug(
                    "💥 {ObjectName} (ID: {Id:X4}) received hit: HP {HpDmg}, SD {SdDmg}, Type: {DamageKind}, 2x: {IsDouble}, 3x: {IsTriple}",
                    objectName, maskedId, healthDmg, shieldDmg, damageKind, isDoubleDamage, isTripleDamage
                );

                // Display floating damage text on the main thread
                MuGame.ScheduleOnMainThread(() =>
                {
                    if (MuGame.Instance.ActiveScene?.World is not WorldControl world)
                    {
                        _logger.LogWarning("Cannot show damage text: Active world is not ready.");
                        return;
                    }

                    WalkerObject target = null;
                    if (maskedId == _characterState.Id && world is WalkableWorldControl walkable)
                    {
                        target = walkable.Walker;
                        if (target == null)
                        {
                            _logger.LogWarning("Local player (ID {Id:X4}) hit but walker is null.", maskedId);
                            return;
                        }
                    }
                    else if (!world.TryGetWalkerById(maskedId, out target))
                    {
                        _logger.LogWarning("Cannot find walker {Id:X4} to show damage text.", maskedId);
                        return;
                    }

                    var headPos = target.WorldPosition.Translation
                                + Vector3.UnitZ * (target.BoundingBoxWorld.Max.Z - target.WorldPosition.Translation.Z + 30f);

                    if (target is MonsterObject monster)
                    {
                        monster.UpdateHealthFractions(healthFraction, shieldFraction, healthDmg, shieldDmg);
                    }

                    // Use server-provided damage type for authentic MU Online colors
                    Color dmgColor;
                    string dmgText;

                    if (totalDmg == 0)
                    {
                        dmgColor = Color.White;
                        dmgText = "Miss";
                    }
                    else
                    {
                        // Local player damage is always red, others use server-provided damage type colors
                        if (maskedId == _characterState.Id)
                        {
                            dmgColor = Color.Red;
                        }
                        else
                        {
                            // Map DamageKind to colors for other players/monsters
                            dmgColor = damageKind switch
                            {
                                DamageKind.NormalRed => Color.Orange,
                                DamageKind.IgnoreDefenseCyan => Color.Cyan,
                                DamageKind.ExcellentLightGreen => Color.LightGreen,
                                DamageKind.CriticalBlue => Color.DeepSkyBlue,
                                DamageKind.LightPink => Color.LightPink,
                                DamageKind.PoisonDarkGreen => Color.DarkGreen,
                                DamageKind.ReflectedDarkPink => Color.DeepPink,
                                DamageKind.White => Color.White,
                                _ => Color.Red // fallback to normal red
                            };
                        }

                        // Add damage multiplier indicators for double/triple damage
                        string multiplier = "";
                        if (isTripleDamage)
                            multiplier = "!!!";
                        else if (isDoubleDamage)
                            multiplier = "!!";

                        dmgText = $"{totalDmg}{multiplier}";
                    }

                    var txt = DamageTextObject.Rent(
                        dmgText,
                        maskedId,
                        dmgColor
                    );
                    world.Objects.Add(txt);
                    _logger.LogDebug("Spawned DamageTextObject '{Text}' for {Id:X4}", txt.Text, maskedId);
                });

                // Update local player's health/shield
                if (maskedId == _characterState.Id)
                {
                    uint currentHpBeforeHit = _characterState.CurrentHealth;
                    uint newHp = (uint)Math.Max(0, (int)_characterState.CurrentHealth - (int)healthDmg);
                    uint newSd = (uint)Math.Max(0, (int)_characterState.CurrentShield - (int)shieldDmg);
                    _characterState.UpdateCurrentHealthShield(newHp, newSd);

                    MuGame.ScheduleOnMainThread(() =>
                    {
                        if (MuGame.Instance.ActiveScene is GameScene gs && gs.Hero != null)
                        {
                            gs.Hero.OnPlayerTookDamage();
                        }
                    });

                    if (newHp == 0 && currentHpBeforeHit > 0)
                    {
                        _logger.LogWarning("💀 Local player (ID: {Id:X4}) died!", maskedId);
                        MuGame.ScheduleOnMainThread(() =>
                        {
                            if (MuGame.Instance.ActiveScene?.World is WalkableWorldControl walkableWorld &&
                                walkableWorld.Walker != null)
                            {
                                var localPlayer = walkableWorld.Walker;

                                if (localPlayer is PlayerObject playerObj)
                                {
                                    playerObj.IsResting = false;
                                    playerObj.IsSitting = false;
                                    playerObj.RestPlaceTarget = null;
                                    playerObj.SitPlaceTarget = null;
                                }

                                localPlayer.PlayAction((ushort)PlayerAction.PlayerDie1);
                                _logger.LogDebug("Triggered PlayerDie1 animation for local player.");
                            }
                        });
                    }
                }
                else
                {
                    // Optionally trigger hit animation for NPCs/monsters
                    MuGame.ScheduleOnMainThread(() =>
                    {
                        if (MuGame.Instance.ActiveScene?.World is WorldControl world
                          && world.TryGetWalkerById(maskedId, out var walker) && walker is MonsterObject monster)
                        {
                            monster.OnReceiveDamage();
                            monster.PlayAction((byte)MonsterActionType.Shock);
                            _logger.LogDebug("Triggering hit animation for {Type} {Id:X4}", walker.GetType().Name, maskedId);
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing ObjectHit (0x11).");
            }
            return Task.CompletedTask;
        }

        [PacketHandler(0x20, PacketRouter.NoSubCode)] // ItemsDropped / MoneyDropped075
        public Task HandleItemsDroppedAsync(Memory<byte> packet)
        {
            try
            {
                ParseAndAddDroppedItemsToScope(packet);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing ItemsDropped (20).");
            }
            return Task.CompletedTask;
        }

        private void ParseAndAddDroppedItemsToScope(Memory<byte> packet)
        {
            const int HeaderSize = 4; // size+code
            const int PrefixSize = HeaderSize + 1; // +count byte

            if (_targetVersion >= TargetProtocolVersion.Season6)
            {
                if (packet.Length < PrefixSize)
                {
                    _logger.LogWarning("ItemsDropped packet too short: {Length}", packet.Length);
                    return;
                }
                byte itemCount = packet.Span[4];
                _logger.LogDebug("Received ItemsDropped (S6+): {Count} items.", itemCount);

                int offset = PrefixSize;
                for (int i = 0; i < itemCount; i++)
                {
                    if (offset + 4 > packet.Length)
                    {
                        _logger.LogWarning("Packet too short for item {Index}.", i);
                        break;
                    }

                    ushort rawId = System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(packet.Span.Slice(offset, 2));
                    ushort maskedId = (ushort)(rawId & 0x7FFF);
                    byte x = packet.Span[offset + 2];
                    byte y = packet.Span[offset + 3];
                    int itemDataOffset = offset + 4;

                    if (itemDataOffset >= packet.Length)
                    {
                        _logger.LogWarning("Packet missing item data for item {Index}.", i);
                        break;
                    }

                    ReadOnlySpan<byte> itemSpan = packet.Span.Slice(itemDataOffset);
                    if (!ItemDataParser.TryGetExtendedItemLength(itemSpan, out int itemLen) || itemDataOffset + itemLen > packet.Length)
                    {
                        itemLen = Math.Min(itemSpan.Length, 12);
                    }

                    var data = itemSpan.Slice(0, itemLen);
                    offset = itemDataOffset + itemLen;

                    bool isMoney = ItemDataParser.TryGetGroupAndNumber(data, out var group, out var number)
                                   && group == 14
                                   && number == 15;
                    ScopeObject dropObj;

                    if (isMoney)
                    {
                        uint amount = (uint)(data.Length >= 5 ? data[4] : 0);
                        dropObj = new MoneyScopeObject(maskedId, rawId, x, y, amount);
                        _scopeManager.AddOrUpdateMoneyInScope(maskedId, rawId, x, y, amount);
                        _logger.LogDebug("Dropped Money: Amount={Amount}, ID={Id:X4}", amount, maskedId);
                        EnqueueDroppedItemProcessing(dropObj, maskedId, "Sound/pDropMoney.wav");
                    }
                    else
                    {
                        byte[] dataCopy = data.ToArray();
                        dropObj = new ItemScopeObject(maskedId, rawId, x, y, dataCopy);
                        _scopeManager.AddOrUpdateItemInScope(maskedId, rawId, x, y, dataCopy);
                        _logger.LogDebug("Dropped Item: ID={Id:X4}, DataLen={Len}", maskedId, data.Length);

                        string itemName = ItemDatabase.GetItemName(dataCopy) ?? string.Empty;
                        string soundPath = itemName.StartsWith("Jewel", StringComparison.OrdinalIgnoreCase)
                            ? "Sound/eGem.wav"
                            : "Sound/pDropItem.wav";

                        EnqueueDroppedItemProcessing(dropObj, maskedId, soundPath);
                    }
                }
            }
            else if (_targetVersion == TargetProtocolVersion.Version075)
            {
                // This block also needs to play sounds, similar to S6+ logic
                if (packet.Length < MoneyDropped075.Length)
                {
                    _logger.LogWarning("Dropped Object packet too short: {Length}", packet.Length);
                    return;
                }
                var legacy = new MoneyDropped075(packet);
                _logger.LogDebug("Received Dropped Object (0.75): Count={Count}.", legacy.ItemCount);

                if (legacy.ItemCount == 1)
                {
                    ushort rawId = legacy.Id;
                    ushort maskedId = (ushort)(rawId & 0x7FFF);
                    byte x = legacy.PositionX;
                    byte y = legacy.PositionY;
                    ScopeObject dropObj;

                    if (legacy.MoneyGroup == 14 && legacy.MoneyNumber == 15) // Money identification
                    {
                        uint amount = legacy.Amount;
                        dropObj = new MoneyScopeObject(maskedId, rawId, x, y, amount);
                        _scopeManager.AddOrUpdateMoneyInScope(maskedId, rawId, x, y, amount);
                        _logger.LogDebug("Dropped Money (0.75): Amount={Amount}, ID={Id:X4}", amount, maskedId);
                        EnqueueDroppedItemProcessing(dropObj, maskedId, "Sound/pDropMoney.wav");
                    }
                    else // Item identification
                    {
                        const int dataOffset = 9, dataLen075 = 7;
                        if (packet.Length >= dataOffset + dataLen075)
                        {
                            var data = packet.Span.Slice(dataOffset, dataLen075).ToArray();
                            dropObj = new ItemScopeObject(maskedId, rawId, x, y, data);
                            _scopeManager.AddOrUpdateItemInScope(maskedId, rawId, x, y, data);
                            _logger.LogDebug("Dropped Item (0.75): ID={Id:X4}, DataLen={Len}", maskedId, dataLen075);
                            string itemName = ItemDatabase.GetItemName(data) ?? string.Empty;
                            string soundPath = itemName.StartsWith("Jewel", StringComparison.OrdinalIgnoreCase)
                                ? "Sound/eGem.wav"
                                : "Sound/pDropItem.wav";

                            EnqueueDroppedItemProcessing(dropObj, maskedId, soundPath);
                        }
                        else
                        {
                            _logger.LogWarning("Cannot extract item data from droppacket (0.75).");
                            return;
                        }
                    }
                }
                else
                {
                    _logger.LogWarning("Multiple items in one packet not handled (Count={Count}).", legacy.ItemCount);
                }
            }
            else
            {
                _logger.LogWarning("Unsupported version for ItemsDropped (0x20): {Version}", _targetVersion);
            }
        }

        [PacketHandler(0x21, PacketRouter.NoSubCode)] // ItemDropRemoved
        public Task HandleItemDropRemovedAsync(Memory<byte> packet)
        {
            try
            {
                ParseAndRemoveDroppedItemsFromScope(packet);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing ItemDropRemoved (0x21).");
            }
            return Task.CompletedTask;
        }

        private void ParseAndRemoveDroppedItemsFromScope(Memory<byte> packet)
        {
            const int headerSize = 4;
            const int prefix = headerSize + 1;   // +count

            if (packet.Length < prefix)
            {
                _logger.LogWarning("ItemDropRemoved packet too short: {Length}", packet.Length);
                return;
            }

            var removed = new ItemDropRemoved(packet);
            byte count = removed.ItemCount;
            _logger.LogDebug("Received ItemDropRemoved: {Count} objects.", count);

            const int idSize = 2;
            int expectedLen = prefix + count * idSize;
            if (packet.Length < expectedLen)
            {
                count = (byte)((packet.Length - prefix) / idSize);
                _logger.LogWarning("Packet shorter than expected – adjusted removal count to {Count}.", count);
            }

            var objectsToRemove = new List<ushort>(count);

            for (int i = 0; i < count; i++)
            {
                try
                {
                    var entry = removed[i];
                    ushort rawId = entry.Id;
                    ushort masked = (ushort)(rawId & 0x7FFF);

                    _scopeManager.RemoveObjectFromScope(masked);
                    objectsToRemove.Add(masked);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing dropped item removal at idx {Idx}.", i);
                }
            }

            // Remove objects on main thread in one batched action.
            MuGame.ScheduleOnMainThread(() =>
            {
                if (MuGame.Instance.ActiveScene?.World is not WalkableWorldControl world) return;

                foreach (var masked in objectsToRemove)
                {
                    var obj = world.FindDroppedItemById(masked);
                    if (obj != null)
                    {
                        world.Objects.Remove(obj);
                        obj.Recycle();
                        _logger.LogDebug("Removed DroppedItemObject {Id:X4} from world (scope gone).", masked);
                    }
                }
            });
        }

        [PacketHandler(0x2F, PacketRouter.NoSubCode)] // MoneyDroppedExtended
        public Task HandleMoneyDroppedExtendedAsync(Memory<byte> packet)
        {
            try
            {
                if (packet.Length < MoneyDroppedExtended.Length)
                {
                    _logger.LogWarning("MoneyDroppedExtended packet too short: {Length}", packet.Length);
                    return Task.CompletedTask;
                }
                var drop = new MoneyDroppedExtended(packet);
                ushort raw = drop.Id;
                ushort masked = (ushort)(raw & 0x7FFF);
                uint amount = drop.Amount;
                byte x = drop.PositionX;
                byte y = drop.PositionY;

                _scopeManager.AddOrUpdateMoneyInScope(masked, raw, x, y, amount);
                _logger.LogDebug("💰 MoneyDroppedExtended: ID={Id:X4}, Amount={Amount}, Pos=({X},{Y})", masked, amount, x, y);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing MoneyDroppedExtended (0x2F).");
            }
            return Task.CompletedTask;
        }

        [PacketHandler(0x14, PacketRouter.NoSubCode)] // MapObjectOutOfScope
        public Task HandleMapObjectOutOfScopeAsync(Memory<byte> packet)
        {
            var outPkt = new MapObjectOutOfScope(packet);
            int count = outPkt.ObjectCount;
            ushort selfId = (ushort)(_characterState.Id & 0x7FFF);
            var objectsToRemove = new List<ushort>(count);

            for (int i = 0; i < count; i++)
            {
                ushort raw = outPkt[i].Id;
                ushort masked = (ushort)(raw & 0x7FFF);
                if (masked == selfId && selfId != 0 && selfId != 0x7FFF)
                {
                    _logger.LogDebug("Ignoring OutOfScope for local player ID {Id:X4}.", masked);
                    continue;
                }

                objectsToRemove.Add(masked);
                InvalidateNpcSpawnGeneration(masked);
                _scopeManager.RemoveObjectFromScope(masked);
            }

            // Remove objects on main thread in one batched action.
            MuGame.ScheduleOnMainThread(() =>
            {
                if (MuGame.Instance.ActiveScene?.World is not WalkableWorldControl world) return;
                var localWalker = world.Walker;

                foreach (var masked in objectsToRemove)
                {
                    if (localWalker != null && localWalker.NetworkId == masked)
                    {
                        _logger.LogWarning("Skipping OutOfScope removal for local walker ID {Id:X4}.", masked);
                        continue;
                    }

                    // ---- 1) Player --------------------------------------------------
                    var player = world.FindPlayerById(masked);
                    if (player != null)
                    {
                        if (localWalker != null && ReferenceEquals(player, localWalker))
                        {
                            _logger.LogWarning("Skipping OutOfScope disposal for local player object ID {Id:X4}.", masked);
                            continue;
                        }

                        world.Objects.Remove(player);
                        player.Dispose();
                        continue;
                    }

                    // ---- 2) Walker / NPC --------------------------------------------
                    var walker = world.FindWalkerById(masked);
                    if (walker != null)
                    {
                        if (localWalker != null && ReferenceEquals(walker, localWalker))
                        {
                            _logger.LogWarning("Skipping OutOfScope disposal for local walker object ID {Id:X4}.", masked);
                            continue;
                        }

                        world.Objects.Remove(walker);
                        walker.Dispose();
                        continue;
                    }

                    // ---- 3) Dropped item --------------------------------------------
                    var drop = world.FindDroppedItemById(masked);
                    if (drop != null)
                    {
                        world.Objects.Remove(drop);
                        drop.Dispose();
                    }
                }
            });

            return Task.CompletedTask;
        }

        [PacketHandler(0x15, PacketRouter.NoSubCode)] // ObjectMoved
        public Task HandleObjectMovedAsync(Memory<byte> packet)
        {
            ushort maskedId = 0xFFFF;
            try
            {
                if (packet.Length < ObjectMoved.Length)
                {
                    _logger.LogWarning("ObjectMoved packet too short: {Length}", packet.Length);
                    return Task.CompletedTask;
                }

                var move = new ObjectMoved(packet);
                ushort raw = move.ObjectId;
                maskedId = (ushort)(raw & 0x7FFF);
                byte x = move.PositionX;
                byte y = move.PositionY;
                _logger.LogDebug("Parsed ObjectMoved: ID={Id:X4}, Pos=({X},{Y})", maskedId, x, y);

                _scopeManager.TryUpdateScopeObjectPosition(maskedId, x, y);

                // Update visual position on the main thread
                MuGame.ScheduleOnMainThread(() =>
                {
                    if (MuGame.Instance.ActiveScene?.World is WalkableWorldControl world)
                    {
                        var objToMove = world.FindWalkerById(maskedId);
                        if (objToMove != null)
                        {
                            objToMove.Location = new Vector2(x, y);
                            _logger.LogDebug("Updated visual position for {Type} {Id:X4}", objToMove.GetType().Name, maskedId);
                        }
                    }
                });

                if (maskedId == _characterState.Id)
                {
                    _logger.LogDebug("🏃‍♂️ Local character moved to ({X},{Y})", x, y);
                    _characterState.UpdatePosition(x, y);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing ObjectMoved (0x15).");
            }
            return Task.CompletedTask;
        }

        [PacketHandler(0xD4, PacketRouter.NoSubCode)] // ObjectWalked
        public Task HandleObjectWalkedAsync(Memory<byte> packet)
        {
            if (packet.Length < 7) return Task.CompletedTask;

            ushort raw;
            ushort maskedId;
            byte x, y;

            if (_useExtendedWalkFormat && packet.Length >= ObjectWalkedExtended.GetRequiredSize(0))
            {
                // Server sends ObjectWalkedExtended for client versions >= 1.06.3 (e.g. 2.04d).
                // Bytes [5-6] = SourceX/Y, [7-8] = TargetX/Y.
                var walkExtended = new ObjectWalkedExtended(packet);
                raw = walkExtended.ObjectId;
                maskedId = (ushort)(raw & 0x7FFF);
                x = walkExtended.TargetX;
                y = walkExtended.TargetY;
            }
            else
            {
                // Server sends ObjectWalked for older client versions (e.g. 1.04d).
                // Bytes [5-6] = TargetX/Y directly.
                var walk = new ObjectWalked(packet);
                raw = walk.ObjectId;
                maskedId = (ushort)(raw & 0x7FFF);
                x = walk.TargetX;
                y = walk.TargetY;
            }

            _scopeManager.TryUpdateScopeObjectPosition(maskedId, x, y);
            if (maskedId == _characterState.Id)
            {
                _characterState.UpdatePosition(x, y);
            }

            MuGame.ScheduleOnMainThread(() =>
            {
                if (MuGame.Instance.ActiveScene?.World is not WalkableWorldControl world)
                    return;

                // ────────────────────────────────────────────────
                //  local player?  → do not override animation
                // ────────────────────────────────────────────────
                if (maskedId == _characterState.Id)
                {

                    var self = world.Walker;

                    if (self != null && self.NetworkId == maskedId)
                    {
                        // Mirror SourceMain behavior: while local movement is in progress,
                        // ignore delayed walk echoes from server to avoid "one-click-behind" movement.
                        if (self.MovementIntent || self.IsMoving)
                        {
                            return;
                        }

                        self.MoveTo(new Vector2(x, y), sendToServer: false, usePathfinding: false);
                        return;
                    }
                }

                if (!world.TryGetWalkerById(maskedId, out var walker) || walker == null)
                {
                    _logger.LogTrace("HandleObjectWalked: Walker {Id:X4} not found.", maskedId);
                    return;
                }

                walker.MoveTo(new Vector2(x, y), sendToServer: false, usePathfinding: false);

                if (walker is PlayerObject player)
                {
                    bool isFemale = PlayerActionMapper.IsCharacterFemale(player.CharacterClass);
                    PlayerAction walkAction;

                    if (world.WorldIndex == 8) // Atlans
                    {
                        var flags = world.Terrain.RequestTerrainFlag(x, y);
                        if (flags.HasFlag(TWFlags.SafeZone))
                        {
                            walkAction = isFemale ? PlayerAction.PlayerWalkFemale : PlayerAction.PlayerWalkMale;
                        }
                        else if (player.HasEquippedWings)
                        {
                            walkAction = PlayerAction.PlayerFly;
                        }
                        else
                        {
                            walkAction = PlayerAction.PlayerRunSwim;
                        }
                    }
                    else if (world.WorldIndex == 11 || (world.WorldIndex == 1 && player.HasEquippedWings && !world.Terrain.RequestTerrainFlag(x, y).HasFlag(TWFlags.SafeZone)))
                    {
                        walkAction = PlayerAction.PlayerFly;
                    }
                    else
                    {
                        walkAction = isFemale ? PlayerAction.PlayerWalkFemale : PlayerAction.PlayerWalkMale;
                    }

                    if (player.CurrentAction != walkAction)
                    {
                        player.PlayAction((ushort)walkAction, fromServer: true);
                    }
                }
                else if (walker is MonsterObject)
                {
                    walker.PlayAction((ushort)MonsterActionType.Walk, fromServer: true);
                }
                else if (walker is NPCObject)
                {
                    const PlayerAction walkAction = PlayerAction.PlayerWalkMale;
                    if (walker.CurrentAction != (int)walkAction)
                        walker.PlayAction((ushort)walkAction, fromServer: true);
                }
            });

            return Task.CompletedTask;
        }


        [PacketHandler(0x17, PacketRouter.NoSubCode)]
        public Task HandleObjectGotKilledAsync(Memory<byte> packet)
        {
            try
            {
                if (packet.Length < ObjectGotKilled.Length)
                {
                    _logger.LogWarning("ObjectGotKilled packet too short: {Length}", packet.Length);
                    return Task.CompletedTask;
                }

                var death = new ObjectGotKilled(packet);
                ushort killed = death.KilledId;
                ushort killer = death.KillerId;

                string killerName = _scopeManager.TryGetScopeObjectName(killer, out var kn) ? (kn ?? "Unknown") : "Unknown";
                string killedName = _scopeManager.TryGetScopeObjectName(killed, out var kd) ? (kd ?? "Unknown") : "Unknown";

                if (killed == _characterState.Id)
                {
                    _logger.LogWarning("💀 You died! Killed by {Killer}", killerName);
                    _characterState.UpdateCurrentHealthShield(0, 0);

                    // CRITICAL: Don't remove local player from scope - let respawn handle it
                    // _scopeManager.RemoveObjectFromScope(killed); // REMOVED THIS LINE
                }
                else
                {
                    _logger.LogInformation("💀 {Killed} died. Killed by {Killer}", killedName, killerName);
                    _scopeManager.RemoveObjectFromScope(killed);
                }

                MuGame.ScheduleOnMainThread(() =>
                {
                    if (MuGame.Instance.ActiveScene?.World is not WorldControl world) return;

                    // Use same lookup as HandleObjectAnimation
                    var player = world.FindPlayerById(killed);

                    WalkerObject walker = null;
                    if (!world.TryGetWalkerById(killed, out walker) && player == null)
                    {
                        _logger.LogTrace("HandleObjectGotKilled: Walker with ID {Id:X4} not found in world.", killed);
                        return;
                    }

                    if (player != null)
                    {
                        walker = player;
                    }

                    if (walker != null)
                    {
                        // Handle local player death differently
                        if (killed == _characterState.Id && walker is PlayerObject localPlayer)
                        {
                            // Reset all animation states
                            localPlayer.IsResting = false;
                            localPlayer.IsSitting = false;
                            localPlayer.RestPlaceTarget = null;
                            localPlayer.SitPlaceTarget = null;

                            // Play death animation but DON'T remove from world
                            localPlayer.PlayAction((ushort)PlayerAction.PlayerDie1);
                            _logger.LogDebug("💀 Local player death animation started - staying in world for respawn");
                            return; // Don't remove local player
                        }

                        // Handle remote player death
                        if (walker is PlayerObject remotePlayer && !remotePlayer.IsMainWalker)
                        {
                            remotePlayer.IsResting = false;
                            remotePlayer.IsSitting = false;
                            remotePlayer.RestPlaceTarget = null;
                            remotePlayer.SitPlaceTarget = null;

                            remotePlayer.PlayAction((ushort)PlayerAction.PlayerDie1);
                            _logger.LogDebug("💀 Remote player {Name} ({Id:X4}) death animation started",
                                            remotePlayer.Name, killed);

                            // Remove after death animation
                            Task.Delay(3000).ContinueWith(_ =>
                            {
                                MuGame.ScheduleOnMainThread(() =>
                                {
                                    if (world.Objects.Contains(walker))
                                    {
                                        world.Objects.Remove(walker);
                                        walker.Dispose();
                                        _logger.LogDebug("💀 Removed dead remote player {Name} after animation",
                                                        remotePlayer.Name);
                                    }
                                });
                            });
                        }
                        // Handle monster death
                        else if (walker is MonsterObject monster)
                        {
                            monster.PlayAction((byte)MonsterActionType.Die);
                            monster.StartDeathFade();
                            _logger.LogDebug("💀 Monster {Id:X4} death animation started", killed);
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing ObjectGotKilled (0x17).");
            }
            return Task.CompletedTask;
        }

        [PacketHandler(0x18, PacketRouter.NoSubCode)] // ObjectAnimation
        public Task HandleObjectAnimationAsync(Memory<byte> packet)
        {
            var anim = new ObjectAnimation(packet);
            ushort rawId = anim.ObjectId;
            ushort maskedId = (ushort)(rawId & 0x7FFF);
            byte serverActionId = anim.Animation;
            byte serverDirection = anim.Direction;
            ushort targetId = anim.TargetId;

            MuGame.ScheduleOnMainThread(() =>
            {
                if (MuGame.Instance.ActiveScene?.World is not WorldControl world) return;

                var player = world.FindPlayerById(maskedId);

                if (!world.TryGetWalkerById(maskedId, out var walker) && player == null)
                {
                    _logger.LogTrace("HandleObjectAnimation: Walker with MaskedID {MaskedId} (RawID {RawId}) not found in world.", maskedId, rawId);
                    return;
                }

                if (player != null)
                {
                    walker = player;
                }

                if (walker == null || walker.Status == GameControlStatus.Disposed)
                {
                    _logger.LogWarning("HandleObjectAnimation: Walker {MaskedId} is null or disposed, cannot animate.", maskedId);
                    return;
                }

                PlayerAction clientActionToPlay;
                string actionNameForLog;
                MonsterActionType? monsterAction = null;

                if (walker is PlayerObject playerToAnimate)
                {
                    CharacterClassNumber playerClass = playerToAnimate.CharacterClass;
                    clientActionToPlay = PlayerActionMapper.GetClientAction(serverActionId, playerClass);
                    actionNameForLog = clientActionToPlay.ToString();
                }
                else if (walker is MonsterObject monsterToAnimate)
                {
                    byte actionIdx = (byte)((serverActionId & 0xE0) >> 5);
                    var action = (MonsterActionType)actionIdx;

                    if (action is MonsterActionType.Attack1 or MonsterActionType.Attack2) // It was always attack1
                    {
                        action = MuGame.Random.Next(2) == 0
                            ? MonsterActionType.Attack1
                            : MonsterActionType.Attack2;
                        actionIdx = (byte)action;
                    }

                    clientActionToPlay = (PlayerAction)action;
                    actionNameForLog = action.ToString();
                    monsterAction = action;

                    if (monsterAction == MonsterActionType.Attack1 || monsterAction == MonsterActionType.Attack2)
                    {
                        monsterToAnimate.LastAttackTargetId = targetId;
                    }
                }
                else
                {
                    _logger.LogWarning("HandleObjectAnimation: Walker {MaskedId} is not PlayerObject or MonsterObject. Type: {WalkerType}", maskedId, walker.GetType().Name);
                    return;
                }

                Client.Main.Models.Direction clientDirection = MapServerDirection(serverDirection);

                if (maskedId == _characterState.Id && walker is PlayerObject localPlayer)
                {
                    localPlayer.Direction = clientDirection;
                    localPlayer.PlayAction((ushort)clientActionToPlay, fromServer: true); // <-- Dodaj fromServer: true
                    _logger.LogDebug("🎞️ Animation (LocalPlayer {Id:X4}): Action: {ActionName} ({ClientAction}), ServerActionID: {ServerActionId}, Dir: {Direction}",
                        maskedId, actionNameForLog, clientActionToPlay, serverActionId, clientDirection);
                }
                else
                {
                    walker.Direction = clientDirection;

                    walker.PlayAction((ushort)clientActionToPlay, fromServer: true);

                    if (walker is MonsterObject monster && monsterAction.HasValue &&
                        (monsterAction == MonsterActionType.Attack1 || monsterAction == MonsterActionType.Attack2))
                    {
                        monster.OnPerformAttack(monsterAction == MonsterActionType.Attack1 ? 1 : 2);
                    }

                    _logger.LogDebug("🎞️ Animation ({WalkerType} {Id:X4}): Action: {ActionName} ({ClientAction}), ServerActionID: {ServerActionId}, Dir: {Direction}",
                       walker.GetType().Name, maskedId, actionNameForLog, clientActionToPlay, serverActionId, clientDirection);
                }
            });

            return Task.CompletedTask;
        }


        [PacketHandler(0x65, PacketRouter.NoSubCode)] // AssignCharacterToGuild
        public Task HandleAssignCharacterToGuildAsync(Memory<byte> packet)
        {
            try
            {
                var assign = new AssignCharacterToGuild(packet);
                _logger.LogDebug("🛡️ AssignCharacterToGuild: {Count} players.", assign.PlayerCount);
                for (int i = 0; i < assign.PlayerCount; i++)
                {
                    var rel = assign[i];
                    ushort rawId = rel.PlayerId;
                    ushort maskedId = (ushort)(rawId & 0x7FFF);
                    _logger.LogDebug(
                        "Player {Player:X4} (Raw: {Raw:X4}) in Guild {GuildId}, Role {Role}",
                        maskedId, rawId, rel.GuildId, rel.Role);
                    // TODO: update guild info in _scopeManager
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing AssignCharacterToGuild (0x65).");
            }
            return Task.CompletedTask;
        }

        [PacketHandler(0x5D, PacketRouter.NoSubCode)] // GuildMemberLeftGuild
        public Task HandleGuildMemberLeftGuildAsync(Memory<byte> packet)
        {
            try
            {
                if (packet.Length < GuildMemberLeftGuild.Length)
                {
                    _logger.LogWarning("GuildMemberLeftGuild packet too short: {Length}", packet.Length);
                    return Task.CompletedTask;
                }
                var left = new GuildMemberLeftGuild(packet);
                ushort rawId = left.PlayerId;
                ushort maskedId = (ushort)(rawId & 0x7FFF);
                _logger.LogDebug(
                    "🚶 Player {Id:X4} left guild (GM: {IsGM}).",
                    maskedId, left.IsGuildMaster
                );
                // TODO: clear guild info in _scopeManager
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing GuildMemberLeftGuild (0x5D).");
            }
            return Task.CompletedTask;
        }

        private void EnqueueDroppedItemProcessing(ScopeObject dropObj, ushort maskedId, string soundPath)
        {
            _droppedItemQueue.Enqueue(new DroppedItemWorkItem(dropObj, maskedId, soundPath));
            TryStartDroppedItemWorker();
        }

        private void TryStartDroppedItemWorker()
        {
            if (Interlocked.CompareExchange(ref _droppedItemWorkerRunning, 1, 0) != 0)
                return;

            _ = ProcessDroppedItemQueueAsync();
        }

        private async Task ProcessDroppedItemQueueAsync()
        {
            try
            {
                while (_droppedItemQueue.TryDequeue(out var workItem))
                {
                    try
                    {
                        await ProcessDroppedItemAsync(workItem.DropObject, workItem.MaskedId, workItem.SoundPath);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing dropped item {Id:X4}", workItem.MaskedId);
                    }
                }
            }
            finally
            {
                Volatile.Write(ref _droppedItemWorkerRunning, 0);
                if (!_droppedItemQueue.IsEmpty)
                    TryStartDroppedItemWorker();
            }
        }

        private async Task ProcessDroppedItemAsync(ScopeObject dropObj, ushort maskedId, string soundPath)
        {
            // Add to world on main thread first, then load assets
            var tcs = new TaskCompletionSource<bool>();

            MuGame.ScheduleOnMainThread(() =>
            {
                ProcessDroppedItemOnMainThread(dropObj, maskedId, soundPath, tcs);
            });

            await tcs.Task;
        }

        private void ProcessDroppedItemOnMainThread(ScopeObject dropObj, ushort maskedId, string soundPath, TaskCompletionSource<bool> tcs)
        {
            try
            {
                if (MuGame.Instance.ActiveScene?.World is not WalkableWorldControl world)
                {
                    tcs.SetResult(false);
                    return;
                }

                // Remove existing visual object if it's already there
                var existing = world.FindDroppedItemById(maskedId);
                if (existing != null)
                {
                    world.Objects.Remove(existing);
                    existing.Recycle();
                }

                var obj = DroppedItemObject.Rent(dropObj, _characterState.Id, _networkManager.GetCharacterService(), _loggerFactory.CreateLogger<DroppedItemObject>());

                // Set World property before adding to world objects
                obj.World = world;

                // Add to world so World.Scene is available
                world.Objects.Add(obj);

                // Queue load to avoid long stalls on the main thread
                bool enqueued = MuGame.TaskScheduler.QueueTask(async () =>
                {
                    try
                    {
                        await obj.Load();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error loading dropped item assets for {MaskedId:X4}", maskedId);
                        world.Objects.Remove(obj);
                        obj.Recycle();
                        tcs.TrySetResult(false);
                        return;
                    }

                    // Play drop sound
                    SoundController.Instance.PlayBufferWithAttenuation(soundPath, obj.Position, world.Walker.Position);

                    // Don't set Hidden immediately - let WorldObject.Update handle visibility checks
                    // The immediate visibility check was causing items to be Hidden incorrectly
                    _logger.LogDebug(
                        "Spawned dropped item ({DisplayName}) at {PosX},{PosY},{PosZ}. RawId: {RawId:X4}, MaskedId: {MaskedId:X4}",
                        obj.DisplayName,
                        obj.Position.X,
                        obj.Position.Y,
                        obj.Position.Z,
                        obj.RawId,
                        obj.NetworkId);
                    tcs.TrySetResult(true);
                }, Controllers.TaskScheduler.Priority.Low);

                if (!enqueued)
                {
                    _logger.LogWarning("Failed to queue dropped item load task for {Id:X4} – scheduler at capacity.", maskedId);
                    world.Objects.Remove(obj);
                    obj.Recycle();
                    tcs.TrySetResult(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing dropped item on main thread for {MaskedId:X4}", maskedId);
                tcs.TrySetResult(false);
            }
        }

        private static byte ConvertGlowToItemLevel(byte glowLevel)
        {
            return glowLevel switch
            {
                0 => 0,
                1 => 3,
                2 => 5,
                3 => 7,
                4 => 9,
                5 => 11,
                6 => 13,
                7 => 15,
                _ => 0
            };
        }
    }
}
