#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using MUnique.OpenMU.Network.Packets.ServerToClient;

namespace Client.Main.Core.Client
{
    /// <summary>
    /// Guild member info from the guild list (0x52).
    /// </summary>
    public sealed class GuildMemberInfo
    {
        public string Name { get; init; } = string.Empty;
        public GuildMemberRole Role { get; init; } = GuildMemberRole.NormalMember;

        /// <summary>Game server the member is connected to, or 0xFF when offline.</summary>
        public byte ServerId { get; init; } = 0xFF;

        public bool IsOnline => ServerId != 0xFF;
    }

    /// <summary>
    /// Cached guild data (0x66 GuildInformation), equivalent to the original <c>MARK_t</c> entries of <c>GuildCache</c>.
    /// </summary>
    public sealed class GuildInfo
    {
        public const int MarkSize = 8;

        public uint Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public string AllianceName { get; init; } = string.Empty;
        public byte Type { get; init; }

        /// <summary>8 x 8 palette indices (0 = transparent), row major.</summary>
        public byte[] Mark { get; init; } = new byte[MarkSize * MarkSize];

        /// <summary>Unpacks the 32-byte protocol logo (two pixels per byte, high nibble first).</summary>
        public static byte[] UnpackMark(ReadOnlySpan<byte> packed)
        {
            var mark = new byte[MarkSize * MarkSize];
            for (int i = 0; i < mark.Length && i / 2 < packed.Length; i++)
                mark[i] = (byte)(i % 2 == 0 ? (packed[i / 2] >> 4) & 0x0F : packed[i / 2] & 0x0F);
            return mark;
        }

        /// <summary>Packs 64 palette indices into the 32-byte protocol logo.</summary>
        public static byte[] PackMark(ReadOnlySpan<byte> mark)
        {
            var packed = new byte[MarkSize * MarkSize / 2];
            for (int i = 0; i < packed.Length; i++)
                packed[i] = (byte)(((mark[i * 2] & 0x0F) << 4) | (mark[i * 2 + 1] & 0x0F));
            return packed;
        }
    }

    /// <summary>
    /// Guild state of the local character plus the guild cache and guild assignments of the
    /// players in scope. Equivalent to SourceMain <c>GuildManager</c> + <c>GuildCache</c> + <c>GuildMark[]</c>.
    /// </summary>
    public sealed class GuildManager
    {
        public const int MaxGuildNameLength = 8;

        private readonly ILogger<GuildManager> _logger;
        private readonly ConcurrentDictionary<uint, GuildInfo> _guilds = new();
        private readonly ConcurrentDictionary<uint, byte> _requestedGuilds = new();
        private readonly ConcurrentDictionary<ushort, (uint GuildId, GuildMemberRole Role)> _players = new();
        private readonly object _membersLock = new();
        private List<GuildMemberInfo> _members = new();

        public GuildManager(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<GuildManager>();
        }

        /// <summary>Raised when the own guild (membership, name or mark) changed.</summary>
        public event Action? GuildInfoChanged;

        /// <summary>Raised when a new guild list (0x52) arrived.</summary>
        public event Action? GuildMemberListUpdated;

        /// <summary>Raised when the server wants the guild master NPC dialog shown (0x54).</summary>
        public event Action? GuildMasterDialogRequested;

        /// <summary>Raised when the server wants the guild creation window shown (0x55).</summary>
        public event Action? GuildCreationDialogRequested;

        /// <summary>A player asks the (own) guild master to join the guild (0x50): (requester id, requester name).</summary>
        public event Action<ushort, string>? GuildJoinRequestReceived;

        /// <summary>Result of the own join request (0x51), GuildJoinRequestResult value.</summary>
        public event Action<byte>? GuildJoinResult;

        /// <summary>Result of kick / leave / disband (0x53), GuildKickSuccess value.</summary>
        public event Action<byte>? GuildKickResult;

        /// <summary>Result of the guild creation (0x56): (success, error).</summary>
        public event Action<bool, byte>? GuildCreationResult;

        /// <summary>Guild of the local character, 0 when none.</summary>
        public uint OwnGuildId { get; private set; }
        public GuildMemberRole OwnRole { get; private set; } = GuildMemberRole.Undefined;
        public bool HasGuild => OwnGuildId != 0;
        public bool IsGuildMaster => HasGuild && OwnRole == GuildMemberRole.GuildMaster;
        public GuildInfo? OwnGuild => HasGuild && _guilds.TryGetValue(OwnGuildId, out var guild) ? guild : null;

        public uint TotalScore { get; private set; }
        public byte CurrentScore { get; private set; }
        public string RivalGuildName { get; private set; } = string.Empty;

        public IReadOnlyList<GuildMemberInfo> GetMembers()
        {
            lock (_membersLock) return _members;
        }

        // ─────────────────────────── Guild cache ───────────────────────────

        public bool TryGetGuild(uint guildId, out GuildInfo guild) => _guilds.TryGetValue(guildId, out guild!);

        public void StoreGuild(GuildInfo guild)
        {
            _guilds[guild.Id] = guild;
            _requestedGuilds.TryRemove(guild.Id, out _);
            _logger.LogDebug("Guild cached: {Id} '{Name}'", guild.Id, guild.Name);
            if (guild.Id == OwnGuildId)
                GuildInfoChanged?.Invoke();
        }

        /// <summary>
        /// Returns true once per unknown guild id, i.e. when the caller should send a GuildInfoRequest (0x66).
        /// </summary>
        public bool ShouldRequestGuild(uint guildId)
            => guildId != 0 && !_guilds.ContainsKey(guildId) && _requestedGuilds.TryAdd(guildId, 0);

        // ─────────────────────────── Players in scope ───────────────────────────

        /// <summary>Assigns a player (masked id) to a guild (0x65).</summary>
        public void AssignPlayer(ushort playerId, uint guildId, GuildMemberRole role, bool isSelf)
        {
            _players[playerId] = (guildId, role);
            if (isSelf && (OwnGuildId != guildId || OwnRole != role))
            {
                OwnGuildId = guildId;
                OwnRole = role;
                GuildInfoChanged?.Invoke();
            }
        }

        /// <summary>Removes the guild assignment of a player (0x5D or scope removal).</summary>
        public void RemovePlayer(ushort playerId, bool isSelf)
        {
            _players.TryRemove(playerId, out _);
            if (isSelf)
                ClearOwnGuild();
        }

        /// <summary>Forgets the assignments of all players in scope, optionally keeping the hero.</summary>
        public void ClearPlayers(ushort? keepPlayerId = null)
        {
            foreach (ushort id in _players.Keys)
            {
                if (keepPlayerId.HasValue && id == keepPlayerId.Value)
                    continue;
                _players.TryRemove(id, out _);
            }
        }

        public bool TryGetPlayerGuild(ushort playerId, out GuildInfo? guild, out GuildMemberRole role)
        {
            guild = null;
            role = GuildMemberRole.Undefined;
            if (_players.IsEmpty || !_players.TryGetValue(playerId, out var relation))
                return false;

            role = relation.Role;
            _guilds.TryGetValue(relation.GuildId, out guild);
            return true;
        }

        public uint GetPlayerGuildId(ushort playerId)
            => _players.TryGetValue(playerId, out var relation) ? relation.GuildId : 0;

        /// <summary>Resolves the guild name of a player in scope, e.g. for the trade window.</summary>
        public string GetPlayerGuildName(ushort playerId)
            => TryGetPlayerGuild(playerId, out var guild, out _) && guild != null ? guild.Name : string.Empty;

        public string GetGuildName(uint guildId)
            => guildId != 0 && _guilds.TryGetValue(guildId, out var guild) ? guild.Name : string.Empty;

        // ─────────────────────────── Own guild ───────────────────────────

        public void UpdateMemberList(bool isInGuild, uint totalScore, byte currentScore, string rivalGuildName, IEnumerable<GuildMemberInfo> members)
        {
            var list = isInGuild
                ? members.OrderByDescending(m => RoleRank(m.Role)).ThenByDescending(m => m.IsOnline).ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList()
                : new List<GuildMemberInfo>();
            lock (_membersLock) _members = list;
            TotalScore = totalScore;
            CurrentScore = currentScore;
            RivalGuildName = rivalGuildName ?? string.Empty;
            GuildMemberListUpdated?.Invoke();
        }

        public void ClearOwnGuild()
        {
            bool changed = OwnGuildId != 0;
            OwnGuildId = 0;
            OwnRole = GuildMemberRole.Undefined;
            lock (_membersLock) _members = new List<GuildMemberInfo>();
            TotalScore = 0;
            CurrentScore = 0;
            RivalGuildName = string.Empty;
            if (changed)
                GuildInfoChanged?.Invoke();
            GuildMemberListUpdated?.Invoke();
        }

        /// <summary>Drops everything tied to the current session (disconnect / character change).</summary>
        public void Reset()
        {
            _players.Clear();
            _requestedGuilds.Clear();
            ClearOwnGuild();
        }

        internal void RaiseGuildMasterDialog() => GuildMasterDialogRequested?.Invoke();
        internal void RaiseGuildJoinRequest(ushort requesterId, string requesterName) => GuildJoinRequestReceived?.Invoke(requesterId, requesterName);
        internal void RaiseGuildJoinResult(byte result) => GuildJoinResult?.Invoke(result);
        internal void RaiseGuildKickResult(byte result) => GuildKickResult?.Invoke(result);
        internal void RaiseGuildCreationResult(bool success, byte error) => GuildCreationResult?.Invoke(success, error);
        internal void RaiseGuildCreationDialog() => GuildCreationDialogRequested?.Invoke();

        public static int RoleRank(GuildMemberRole role) => role switch
        {
            GuildMemberRole.GuildMaster => 3,
            (GuildMemberRole)64 => 2, // assistant guild master (G_SUB_MASTER), not sent by OpenMU 0.9.8
            GuildMemberRole.BattleMaster => 1,
            _ => 0,
        };

        /// <summary>Role caption as in the original client (GlobalText 1300-1302, 1330).</summary>
        public static string GetRoleName(GuildMemberRole role) => role switch
        {
            GuildMemberRole.GuildMaster => "Guild Master",
            (GuildMemberRole)64 => "Assistant G.M.",
            GuildMemberRole.BattleMaster => "Battle Master",
            GuildMemberRole.NormalMember => "Guild Member",
            _ => string.Empty,
        };
    }
}
