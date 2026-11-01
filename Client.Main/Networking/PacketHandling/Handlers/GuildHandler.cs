using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Client.Main.Controls.UI;
using Client.Main.Core.Client;
using Client.Main.Core.Utilities;
using Client.Main.Models;
using Client.Main.Networking.Services;
using Client.Main.Scenes;
using Microsoft.Extensions.Logging;
using MUnique.OpenMU.Network.Packets.ServerToClient;

namespace Client.Main.Networking.PacketHandling.Handlers
{
    /// <summary>
    /// Handles guild packets: guild assignment of players in scope, guild cache, member list,
    /// creation flow and kick/leave results. Mirrors WSclient.cpp ReceiveGuild* (l.7036-7600).
    /// </summary>
    public class GuildHandler : IGamePacketHandler
    {
        private readonly ILogger<GuildHandler> _logger;
        private readonly CharacterState _characterState;
        private readonly CharacterService _characterService;
        private readonly NetworkManager _networkManager;

        public GuildHandler(ILoggerFactory loggerFactory, CharacterState characterState, CharacterService characterService, NetworkManager networkManager)
        {
            _logger = loggerFactory.CreateLogger<GuildHandler>();
            _characterState = characterState;
            _characterService = characterService;
            _networkManager = networkManager;
        }

        private GuildManager Guilds => _networkManager.GetGuildManager();

        /// <summary>AssignCharacterToGuild (0x65): players in scope that belong to a guild.</summary>
        [PacketHandler(0x65, PacketRouter.NoSubCode)]
        public Task HandleAssignCharacterToGuildAsync(Memory<byte> packet)
        {
            try
            {
                var assign = new AssignCharacterToGuild(packet);
                int count = assign.PlayerCount;
                while (count > 0 && AssignCharacterToGuild.GetRequiredSize(count) > packet.Length)
                    count--;
                for (int i = 0; i < count; i++)
                {
                    var relation = assign[i];
                    ushort playerId = (ushort)(relation.PlayerId & 0x7FFF);
                    bool isSelf = playerId == _characterState.Id;
                    Guilds.AssignPlayer(playerId, relation.GuildId, relation.Role, isSelf);
                    _logger.LogDebug("Player {Player:X4} in guild {GuildId} as {Role}{Self}.",
                        playerId, relation.GuildId, relation.Role, isSelf ? " (self)" : string.Empty);

                    if (Guilds.ShouldRequestGuild(relation.GuildId))
                        _ = _characterService.SendGuildInfoRequestAsync(relation.GuildId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing AssignCharacterToGuild (0x65).");
            }

            return Task.CompletedTask;
        }

        /// <summary>GuildMemberLeftGuild (0x5D): a player (maybe the hero) is no longer a guild member.</summary>
        [PacketHandler(0x5D, PacketRouter.NoSubCode)]
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
                ushort playerId = (ushort)(left.PlayerId & 0x7FFF);
                bool isSelf = playerId == _characterState.Id;
                string guildName = isSelf ? Guilds.OwnGuild?.Name ?? string.Empty : string.Empty;
                Guilds.RemovePlayer(playerId, isSelf);
                _logger.LogDebug("Player {Id:X4} left the guild (GM: {IsGM}).", playerId, left.IsGuildMaster);

                if (isSelf)
                {
                    ShowSystemMessage(string.IsNullOrEmpty(guildName)
                        ? "You are no longer a guild member."
                        : $"You are no longer a member of the guild {guildName}.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing GuildMemberLeftGuild (0x5D).");
            }

            return Task.CompletedTask;
        }

        /// <summary>GuildInformation (0x66): name and mark of a guild, answer to GuildInfoRequest.</summary>
        [PacketHandler(0x66, PacketRouter.NoSubCode)]
        public Task HandleGuildInformationAsync(Memory<byte> packet)
        {
            try
            {
                if (packet.Length < GuildInformation.Length)
                {
                    _logger.LogWarning("GuildInformation packet too short: {Length}", packet.Length);
                    return Task.CompletedTask;
                }

                var info = new GuildInformation(packet);
                Guilds.StoreGuild(new GuildInfo
                {
                    Id = info.GuildId,
                    Name = info.GuildName,
                    AllianceName = info.AllianceGuildName,
                    Type = info.GuildType,
                    Mark = GuildInfo.UnpackMark(info.Logo),
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing GuildInformation (0x66).");
            }

            return Task.CompletedTask;
        }

        /// <summary>GuildList (0x52): members of the own guild, answer to GuildListRequest.</summary>
        [PacketHandler(0x52, PacketRouter.NoSubCode)]
        public Task HandleGuildListAsync(Memory<byte> packet)
        {
            try
            {
                if (packet.Length < 24)
                {
                    _logger.LogWarning("GuildList packet too short: {Length}", packet.Length);
                    return Task.CompletedTask;
                }

                var list = new GuildList(packet);
                int count = list.GuildMemberCount;
                while (count > 0 && GuildList.GetRequiredSize(count) > packet.Length)
                    count--;
                var members = new List<GuildMemberInfo>(count);
                for (int i = 0; i < count; i++)
                {
                    var member = list[i];
                    members.Add(new GuildMemberInfo { Name = member.Name, Role = member.Role, ServerId = member.ServerId });
                }

                Guilds.UpdateMemberList(list.IsInGuild, list.TotalScore, list.CurrentScore, list.RivalGuildName, members);
                _logger.LogDebug("GuildList: InGuild={InGuild}, {Count} members.", list.IsInGuild, members.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing GuildList (0x52).");
            }

            return Task.CompletedTask;
        }

        /// <summary>GuildJoinResponse (0x51): result of the own join request.</summary>
        [PacketHandler(0x51, PacketRouter.NoSubCode)]
        public Task HandleGuildJoinResponseAsync(Memory<byte> packet)
        {
            if (packet.Length < GuildJoinResponse.Length)
                return Task.CompletedTask;

            var result = new GuildJoinResponse(packet).Result;
            string text = result switch
            {
                GuildJoinResponse.GuildJoinRequestResult.Accepted => "You have joined the guild.",
                GuildJoinResponse.GuildJoinRequestResult.Refused => "The guild master refused your request.",
                GuildJoinResponse.GuildJoinRequestResult.GuildFull => "The guild is full.",
                GuildJoinResponse.GuildJoinRequestResult.Disconnected => "The guild master is not connected.",
                GuildJoinResponse.GuildJoinRequestResult.NotTheGuildMaster => "That player is not a guild master.",
                GuildJoinResponse.GuildJoinRequestResult.AlreadyHaveGuild => "You are already in a guild.",
                GuildJoinResponse.GuildJoinRequestResult.GuildMasterOrRequesterIsBusy => "The guild master is busy. Try again later.",
                GuildJoinResponse.GuildJoinRequestResult.MinimumLevel6 => "You need to be at least level 6 to join a guild.",
                _ => $"Guild join failed ({result}).",
            };
            MuGame.ScheduleOnMainThread(() => RequestDialog.ShowInfo(text));
            return Task.CompletedTask;
        }

        /// <summary>GuildKickResponse (0x53): result of kick / leave / disband.</summary>
        [PacketHandler(0x53, PacketRouter.NoSubCode)]
        public Task HandleGuildKickResponseAsync(Memory<byte> packet)
        {
            if (packet.Length < GuildKickResponse.Length)
                return Task.CompletedTask;

            var result = new GuildKickResponse(packet).Result;
            string text = result switch
            {
                GuildKickResponse.GuildKickSuccess.KickSucceeded => "The member has been removed from the guild.",
                GuildKickResponse.GuildKickSuccess.GuildMemberWithdrawn => "You have left the guild.",
                GuildKickResponse.GuildKickSuccess.GuildDisband => "The guild has been disbanded.",
                GuildKickResponse.GuildKickSuccess.FailedPasswordIncorrect => "Incorrect security code.",
                GuildKickResponse.GuildKickSuccess.KickFailedBecausePlayerIsNotGuildMaster => "Only the guild master can do that.",
                _ => "The request failed.",
            };

            if (result == GuildKickResponse.GuildKickSuccess.KickSucceeded && Guilds.HasGuild)
                _ = _characterService.SendGuildListRequestAsync();

            MuGame.ScheduleOnMainThread(() => RequestDialog.ShowInfo(text));
            return Task.CompletedTask;
        }

        /// <summary>ShowGuildMasterDialog (0x54): talking to the guild master NPC.</summary>
        [PacketHandler(0x54, PacketRouter.NoSubCode)]
        public Task HandleShowGuildMasterDialogAsync(Memory<byte> packet)
        {
            MuGame.ScheduleOnMainThread(Guilds.RaiseGuildMasterDialog);
            return Task.CompletedTask;
        }

        /// <summary>ShowGuildCreationDialog (0x55): open the guild creation window.</summary>
        [PacketHandler(0x55, PacketRouter.NoSubCode)]
        public Task HandleShowGuildCreationDialogAsync(Memory<byte> packet)
        {
            MuGame.ScheduleOnMainThread(Guilds.RaiseGuildCreationDialog);
            return Task.CompletedTask;
        }

        /// <summary>GuildCreationResult (0x56).</summary>
        [PacketHandler(0x56, PacketRouter.NoSubCode)]
        public Task HandleGuildCreationResultAsync(Memory<byte> packet)
        {
            if (packet.Length < GuildCreationResult.Length)
                return Task.CompletedTask;

            var result = new GuildCreationResult(packet);
            bool success = result.Success;
            var error = result.Error;
            MuGame.ScheduleOnMainThread(() =>
            {
                (MuGame.Instance?.ActiveScene as GameScene)?.GuildCreate?.OnCreationResult(success);
                RequestDialog.ShowInfo(success
                    ? "Your guild has been created."
                    : error == GuildCreationResult.GuildCreationErrorType.GuildNameAlreadyTaken
                        ? "This guild name is already taken."
                        : "The guild could not be created.");
            });
            return Task.CompletedTask;
        }

        private static void ShowSystemMessage(string text)
        {
            MuGame.ScheduleOnMainThread(() =>
                (MuGame.Instance?.ActiveScene as GameScene)?.ChatLog?.AddMessage("System", text, MessageType.Info));
        }
    }
}
