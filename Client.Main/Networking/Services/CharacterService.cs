using Microsoft.Extensions.Logging;
using MUnique.OpenMU.Network;
using Client.Main.Networking.PacketHandling;
using System;
using System.Threading.Tasks;
using MUnique.OpenMU.Network.Packets.ClientToServer;
using MUnique.OpenMU.Network.Packets;
using Client.Main.Core.Client;

namespace Client.Main.Networking.Services
{
    /// <summary>
    /// Manages sending character‐related packets to the game server,
    /// including character list requests, character selection, movement, and animations.
    /// </summary>
    public class CharacterService
    {
        private readonly ConnectionManager _connectionManager;
        private readonly ILogger<CharacterService> _logger;

        public CharacterService(
            ConnectionManager connectionManager,
            ILogger<CharacterService> logger)
        {
            _connectionManager = connectionManager;
            _logger = logger;
        }

        /// <summary>
        /// Requests the list of characters for the current account.
        /// </summary>
        public async Task RequestCharacterListAsync()
        {
            if (!_connectionManager.IsConnected)
            {
                _logger.LogError("Not connected — cannot request character list.");
                return;
            }

            _logger.LogInformation("Sending character list request...");
            try
            {
                await _connectionManager.Connection.SendAsync(() =>
                    PacketBuilder.BuildRequestCharacterListPacket(_connectionManager.Connection.Output));
                _logger.LogInformation("Character list request sent.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending character list request.");
            }
        }

        /// <summary>
        /// Sends a response to a party invitation.
        /// </summary>
        public async Task SendPartyResponseAsync(bool accepted, ushort requesterId)
        {
            if (!_connectionManager.IsConnected)
            {
                _logger.LogError("Not connected — cannot respond to party invite.");
                return;
            }

            _logger.LogInformation("Sending party response: Accepted={Accepted}, RequesterId={Id}", accepted, requesterId);
            try
            {
                await _connectionManager.Connection.SendAsync(() =>
                {
                    var packet = new PartyInviteResponse(_connectionManager.Connection.Output.GetMemory(PartyInviteResponse.Length).Slice(0, PartyInviteResponse.Length));
                    packet.Accepted = accepted;
                    packet.RequesterId = requesterId;
                    return PartyInviteResponse.Length;
                });
                _logger.LogInformation("Party response sent.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending party response.");
            }
        }

        /// <summary>
        /// Sends a request to kick a player from party (or leave party yourself).
        /// </summary>
        /// <param name="playerIndex">Index of player to kick (or your own index to leave party)</param>
        public async Task SendPartyKickRequestAsync(byte playerIndex)
        {
            if (!_connectionManager.IsConnected)
            {
                _logger.LogError("Not connected — cannot send party kick request.");
                return;
            }

            _logger.LogInformation("Sending party kick request for player index {PlayerIndex}", playerIndex);
            try
            {
                await _connectionManager.Connection.SendAsync(() =>
                {
                    var packet = new PartyPlayerKickRequest(_connectionManager.Connection.Output.GetMemory(PartyPlayerKickRequest.Length).Slice(0, PartyPlayerKickRequest.Length));
                    packet.PlayerIndex = playerIndex;
                    return PartyPlayerKickRequest.Length;
                });
                _logger.LogInformation("Party kick request sent.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending party kick request.");
            }
        }

        /// <summary>
        /// Sends a request for the current party list.
        /// </summary>
        public async Task RequestPartyListAsync()
        {
            if (!_connectionManager.IsConnected)
            {
                _logger.LogWarning("Not connected — cannot request party list.");
                return;
            }

            _logger.LogTrace("Sending PartyListRequest..."); // Użyj LogTrace, aby nie zaśmiecać logów
            try
            {
                await _connectionManager.Connection.SendAsync(() =>
                {
                    var packet = new PartyListRequest(_connectionManager.Connection.Output.GetMemory(PartyListRequest.Length).Slice(0, PartyListRequest.Length));
                    return PartyListRequest.Length;
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending PartyListRequest packet.");
            }
        }

        /// <summary>
        /// Sends a response to a guild join invitation.
        /// </summary>
        public async Task SendGuildJoinResponseAsync(bool accepted, ushort requesterId)
        {
            if (!_connectionManager.IsConnected)
            {
                _logger.LogError("Not connected — cannot respond to guild invite.");
                return;
            }

            _logger.LogInformation("Sending guild join response: Accepted={Accepted}, RequesterId={Id}", accepted, requesterId);
            try
            {
                await _connectionManager.Connection.SendAsync(() =>
                {
                    var packet = new GuildJoinResponse(_connectionManager.Connection.Output.GetMemory(GuildJoinResponse.Length).Slice(0, GuildJoinResponse.Length));
                    packet.Accepted = accepted;
                    packet.RequesterId = requesterId;
                    return GuildJoinResponse.Length;
                });
                _logger.LogInformation("Guild join response sent.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending guild join response.");
            }
        }

        /// <summary>
        /// Sends a response to a trade request.
        /// </summary>
        public async Task SendTradeResponseAsync(bool accepted)
        {
            if (!_connectionManager.IsConnected)
            {
                _logger.LogError("Not connected — cannot respond to trade request.");
                return;
            }

            _logger.LogInformation("Sending trade response: Accepted={Accepted}", accepted);
            try
            {
                await _connectionManager.Connection.SendAsync(() =>
                {
                    var packet = new TradeRequestResponse(_connectionManager.Connection.Output.GetMemory(TradeRequestResponse.Length).Slice(0, TradeRequestResponse.Length));
                    packet.TradeAccepted = accepted;
                    return TradeRequestResponse.Length;
                });
                _logger.LogInformation("Trade response sent.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending trade response.");
            }
        }

        /// <summary>
        /// Sends a response to a duel request.
        /// </summary>
        public async Task SendDuelResponseAsync(bool accepted, ushort requesterId, string requesterName)
        {
            if (!_connectionManager.IsConnected)
            {
                _logger.LogError("Not connected — cannot respond to duel request.");
                return;
            }

            _logger.LogInformation("Sending duel response: Accepted={Accepted}, RequesterId={Id}", accepted, requesterId);
            try
            {
                await _connectionManager.Connection.SendAsync(() =>
                {
                    var packet = new DuelStartResponse(_connectionManager.Connection.Output.GetMemory(DuelStartResponse.Length).Slice(0, DuelStartResponse.Length));
                    packet.Response = accepted;
                    packet.PlayerId = requesterId;
                    packet.PlayerName = requesterName;
                    return DuelStartResponse.Length;
                });
                _logger.LogInformation("Duel response sent.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending duel response.");
            }
        }

        public async Task SendWarpCommandRequestAsync(ushort warpInfoIndex, uint commandKey = 0)
        {
            if (!_connectionManager.IsConnected)
            {
                _logger.LogError("Not connected — cannot send warp command request.");
                return;
            }

            _logger.LogInformation("Sending Warp Command Request for index {WarpInfoIndex}...", warpInfoIndex);
            try
            {
                await _connectionManager.Connection.SendAsync(() =>
                {
                    var packet = new WarpCommandRequest(_connectionManager.Connection.Output.GetMemory(WarpCommandRequest.Length).Slice(0, WarpCommandRequest.Length));
                    packet.CommandKey = commandKey;
                    packet.WarpInfoIndex = warpInfoIndex;
                    return WarpCommandRequest.Length;
                });
                _logger.LogInformation("Warp Command Request sent.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending Warp Command Request.");
            }
        }

        public async Task SendClientReadyAfterMapChangeAsync()
        {
            if (!_connectionManager.IsConnected)
            {
                _logger.LogError("Not connected — cannot send ClientReadyAfterMapChange.");
                return;
            }

            _logger.LogInformation("Sending ClientReadyAfterMapChange packet...");
            try
            {
                await _connectionManager.Connection.SendAsync(() =>
                    PacketBuilder.BuildClientReadyAfterMapChangePacket(_connectionManager.Connection.Output));
                _logger.LogInformation("ClientReadyAfterMapChange packet sent.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending ClientReadyAfterMapChange packet.");
            }
        }

        /// <summary>
        /// Selects the specified character on the game server.
        /// </summary>
        public async Task SelectCharacterAsync(string characterName)
        {
            if (!_connectionManager.IsConnected)
            {
                _logger.LogError("Not connected — cannot select character.");
                return;
            }

            _logger.LogInformation("Selecting character '{CharacterName}'...", characterName);
            try
            {
                await _connectionManager.Connection.SendAsync(() =>
                    PacketBuilder.BuildSelectCharacterPacket(_connectionManager.Connection.Output, characterName));
                _logger.LogInformation("Character selection packet sent.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending character selection packet.");
            }
        }

        /// <summary>
        /// Sends an instant move (teleport) request to the given coordinates.
        /// </summary>
        public async Task SendInstantMoveRequestAsync(byte x, byte y)
        {
            if (!_connectionManager.IsConnected)
            {
                _logger.LogError("Not connected — cannot move instantly.");
                return;
            }

            _logger.LogInformation("Sending instant move to ({X}, {Y})...", x, y);
            try
            {
                await _connectionManager.Connection.SendAsync(() =>
                    PacketBuilder.BuildInstantMoveRequestPacket(_connectionManager.Connection.Output, x, y));
                _logger.LogInformation("Instant move request sent.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending instant move request.");
            }
        }

        /// <summary>
        /// Sends an animation request with the specified rotation and animation number.
        /// </summary>
        public async Task SendAnimationRequestAsync(byte rotation, byte animationNumber)
        {
            if (!_connectionManager.IsConnected)
            {
                _logger.LogError("Not connected — cannot send animation request.");
                return;
            }

            _logger.LogInformation(
                "Sending animation request (rotation={Rotation}, animation={AnimationNumber})...",
                rotation, animationNumber);

            try
            {
                await _connectionManager.Connection.SendAsync(() =>
                    PacketBuilder.BuildAnimationRequestPacket(_connectionManager.Connection.Output, rotation, animationNumber));
                _logger.LogInformation("Animation request sent.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending animation request.");
            }
        }

        /// <summary>
        /// Sends a walk request along a path of direction steps.
        /// </summary>
        public async Task SendWalkRequestAsync(byte startX, byte startY, byte[] path)
        {
            if (!_connectionManager.IsConnected)
            {
                _logger.LogError("Not connected — cannot send walk request.");
                return;
            }
            if (path == null || path.Length == 0)
            {
                _logger.LogWarning("Empty path — walk request not sent.");
                return;
            }

            _logger.LogInformation(
                "Sending walk request from ({StartX}, {StartY}) with {Steps} steps...",
                startX, startY, path.Length);

            try
            {
                await _connectionManager.Connection.SendAsync(() =>
                    PacketBuilder.BuildWalkRequestPacket(
                        _connectionManager.Connection.Output,
                        startX, startY, path));
                _logger.LogInformation("Walk request sent.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending walk request.");
            }
        }

        /// <summary>
        /// Sends a hit request packet to the server.
        /// </summary>
        public async Task SendHitRequestAsync(ushort targetId, byte attackAnimation, byte lookingDirection)
        {
            if (!_connectionManager.IsConnected)
            {
                _logger.LogError("Not connected — cannot send hit request.");
                return;
            }

            _logger.LogInformation(
                "Sending hit request for TargetID: {TargetId}, Anim: {Animation}, Dir: {Direction}...",
                targetId, attackAnimation, lookingDirection);

            try
            {
                await _connectionManager.Connection.SendAsync(() =>
                    PacketBuilder.BuildHitRequestPacket(_connectionManager.Connection.Output, targetId, attackAnimation, lookingDirection));
                _logger.LogInformation("Hit request sent.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending hit request.");
            }
        }

        /// <summary>
        /// Sends a request to increase a specific character stat attribute.
        /// </summary>
        /// <param name="attribute">The attribute to be increased.</param>
        public async Task SendIncreaseCharacterStatPointRequestAsync(CharacterStatAttribute attribute)
        {
            if (!_connectionManager.IsConnected)
            {
                _logger.LogError("Not connected — cannot send stat increase request.");
                return;
            }

            _logger.LogInformation("Sending stat increase request for attribute: {Attribute}...", attribute);
            try
            {
                await _connectionManager.Connection.SendAsync(() => PacketBuilder.BuildIncreaseCharacterStatPointPacket(_connectionManager.Connection.Output, attribute));
                _logger.LogInformation("Stat increase request sent for attribute: {Attribute}.", attribute);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending stat increase request for attribute {Attribute}.", attribute);
            }
        }

        /// <summary>
        /// Sends a request to pick up a dropped item or money by its network ID.
        /// </summary>
        public async Task SendPickupItemRequestAsync(ushort itemId, TargetProtocolVersion version)
        {
            ushort itemIdMasked = (ushort)(itemId & 0x7FFF);
            if (!_connectionManager.IsConnected)
            {
                _logger.LogError("Not connected — cannot send pickup item request.");
                return;
            }

            _logger.LogInformation("Sending pickup item request for itemId: {ItemId}...", itemIdMasked);
            try
            {
                // Using the ConnectionExtensions directly based on protocol version
                switch (version)
                {
                    case TargetProtocolVersion.Season6:
                    case TargetProtocolVersion.Version097:
                        await _connectionManager.Connection.SendPickupItemRequestAsync(itemIdMasked);
                        break;
                    case TargetProtocolVersion.Version075:
                        await _connectionManager.Connection.SendPickupItemRequest075Async(itemIdMasked);
                        break;
                }
                _logger.LogInformation("Pickup item request sent for itemId: {ItemId}.", itemIdMasked);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending pickup item request for itemId {ItemId}.", itemIdMasked);
            }
        }

        public async Task SendEnterGateRequestAsync(ushort gateId)
        {
            if (!_connectionManager.IsConnected)
            {
                _logger.LogError("Not connected — cannot send enter gate request.");
                return;
            }

            _logger.LogInformation("Sending enter gate request for gate {GateId}...", gateId);
            try
            {
                await _connectionManager.Connection.SendAsync(() =>
                {
                    var packet = new EnterGateRequest(_connectionManager.Connection.Output.GetMemory(EnterGateRequest.Length).Slice(0, EnterGateRequest.Length));
                    packet.GateNumber = gateId;
                    packet.TeleportTargetX = 0;
                    packet.TeleportTargetY = 0;
                    return EnterGateRequest.Length;
                });
                _logger.LogInformation("Enter gate request sent for gate {GateId}.", gateId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending enter gate request for gate {GateId}.", gateId);
            }
        }
    }
}
