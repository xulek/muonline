using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Threading.Tasks;
using Client.Data.ATT;
using Client.Main.Controls;
using Client.Main.Controls.UI;
using Client.Main.Controls.UI.Game;
using Client.Main.Core.Client;
using Client.Main.Networking.Services;
using Client.Main.Scenes;
using Client.Main.Core.Utilities;
using Microsoft.Extensions.Logging;
using MUnique.OpenMU.Network.Packets.ServerToClient;

namespace Client.Main.Networking.PacketHandling.Handlers
{
    /// <summary>
    /// Handles packets shared by the mini game events (Blood Castle, Chaos Castle, Devil Square, ...).
    /// </summary>
    public class MiniGameHandler : IGamePacketHandler
    {
        private readonly ILogger<MiniGameHandler> _logger;
        private readonly CharacterState _characterState;
        private readonly CharacterService _characterService;

        public MiniGameHandler(ILoggerFactory loggerFactory, CharacterState characterState, CharacterService characterService)
        {
            _logger = loggerFactory.CreateLogger<MiniGameHandler>();
            _characterState = characterState;
            _characterService = characterService;
        }

        private static string GetMiniGameName(byte type) => type switch
        {
            1 => "Devil Square",
            2 => "Blood Castle",
            3 => "Cursed Temple",
            4 => "Chaos Castle",
            5 => "Illusion Temple",
            6 => "Doppelganger",
            _ => "The event",
        };

        /// <summary>
        /// MiniGameOpeningState (0x91): answer to MiniGameOpeningStateRequest, e.g. after using an event ticket.
        /// Chaos Castle carries the minutes in two bytes and, when open, asks to enter with the pending ticket
        /// (WSclient.cpp ReceiveMatchTimer-like handling of 0x91, CChaosCastleTimeCheckMsgBoxLayout).
        /// </summary>
        [PacketHandler(0x91, PacketRouter.NoSubCode)]
        public Task HandleMiniGameOpeningStateAsync(Memory<byte> packet)
        {
            var span = packet.Span;
            if (span.Length < 7) return Task.CompletedTask;

            byte type = span[3];
            byte users = span[5];
            int minutes = type == 4 ? (span[4] << 8) | span[6] : span[4];
            string name = GetMiniGameName(type);
            var ticket = _characterState.PendingEventTicket;
            _characterState.PendingEventTicket = null;

            MuGame.ScheduleOnMainThread(() =>
            {
                _characterState.RaiseMiniGameOpeningState(type, minutes, users);
                if (minutes == 0xFFFF || (type != 4 && minutes == 0xFF))
                {
                    RequestDialog.ShowInfo($"{name} is not scheduled on this server.");
                    return;
                }

                if (minutes > 0)
                {
                    RequestDialog.ShowInfo(minutes >= 60
                        ? $"{name} opens in {minutes / 60} hour(s) {minutes % 60} minute(s)."
                        : $"{name} opens in {minutes} minute(s).");
                    return;
                }

                if (type == 4 && ticket is { Type: 4 } cc)
                {
                    RequestDialog.Show(
                        $"Chaos Castle is open ({users}/100 participants). Do you want to enter?",
                        onAccept: () => _ = _characterService.SendChaosCastleEnterRequestAsync(cc.Level, cc.Slot));
                    return;
                }

                RequestDialog.ShowInfo($"{name} is open now.");
            });
            return Task.CompletedTask;
        }

        /// <summary>ChaosCastleEnterResult (0xAF/0x01).</summary>
        [PacketHandler(0xAF, 0x01)]
        public Task HandleChaosCastleEnterResultAsync(Memory<byte> packet)
        {
            if (packet.Length < 5) return Task.CompletedTask;
            byte result = packet.Span[4];
            if (result == 0) return Task.CompletedTask; // the server warps the player
            string text = result switch
            {
                1 => "You can't enter Chaos Castle: check the ticket and your level.",
                2 => "Chaos Castle is not open.",
                5 => "Chaos Castle is full.",
                7 => "You don't have enough Zen for the entrance fee.",
                8 => "Player killers can't enter Chaos Castle.",
                _ => $"Entering Chaos Castle failed ({result}).",
            };
            MuGame.ScheduleOnMainThread(() => RequestDialog.ShowInfo(text));
            return Task.CompletedTask;
        }

        /// <summary>
        /// 0x93: BloodCastleScore (Type 0xFF, 29 bytes) or MiniGameScoreTable (Devil Square / Chaos Castle ranking).
        /// </summary>
        [PacketHandler(0x93, PacketRouter.NoSubCode)]
        public Task HandleScoreAsync(Memory<byte> packet)
        {
            var span = packet.Span;
            if (span.Length < 5) return Task.CompletedTask;

            if (span[4] == 0xFF && span.Length >= 29)
            {
                bool success = span[3] != 0;
                uint score = BinaryPrimitives.ReadUInt32LittleEndian(span[17..]);
                uint exp = BinaryPrimitives.ReadUInt32LittleEndian(span[21..]);
                uint zen = BinaryPrimitives.ReadUInt32LittleEndian(span[25..]);
                MuGame.ScheduleOnMainThread(() => BloodCastleResultControl.Instance?.ShowResult(success, exp, (int)zen, (int)score));
                return Task.CompletedTask;
            }

            int rank = span[3];
            int count = span[4];
            var entries = new List<MiniGameScoreWindow.Entry>(count);
            for (int i = 0; i < count && 5 + (i + 1) * 24 <= span.Length; i++)
            {
                var item = span.Slice(5 + i * 24, 24);
                int end = item[..10].IndexOf((byte)0);
                string name = System.Text.Encoding.UTF8.GetString(end < 0 ? item[..10] : item[..end]);
                entries.Add(new MiniGameScoreWindow.Entry(name,
                    BinaryPrimitives.ReadUInt32LittleEndian(item[12..]),
                    BinaryPrimitives.ReadUInt32LittleEndian(item[16..]),
                    BinaryPrimitives.ReadUInt32LittleEndian(item[20..])));
            }

            ushort map = _characterState.MapId;
            string title = map is >= 18 and <= 23 or 53 ? "CHAOS CASTLE" : map is 9 or 32 ? "DEVIL SQUARE" : "EVENT RESULT";
            MuGame.ScheduleOnMainThread(() => (MuGame.Instance?.ActiveScene as GameScene)?.MiniGameScore?.ShowResults(title, rank, entries));
            return Task.CompletedTask;
        }

        /// <summary>
        /// Handles ChangeTerrainAttributes (0x46): the server sets or clears a terrain attribute
        /// on rectangular areas, e.g. opening the Blood Castle gates or collapsing the Chaos Castle floor.
        /// </summary>
        [PacketHandler(0x46, PacketRouter.NoSubCode)]
        public Task HandleChangeTerrainAttributesAsync(Memory<byte> packet)
        {
            try
            {
                if (packet.Length < 7)
                {
                    _logger.LogWarning("ChangeTerrainAttributes packet too short: {Length}", packet.Length);
                    return Task.CompletedTask;
                }

                var message = new ChangeTerrainAttributes(packet);
                int areaCount = Math.Min((int)message.AreaCount, (packet.Length - 7) / ChangeTerrainAttributes.TerrainArea.Length);
                if (areaCount < message.AreaCount)
                {
                    _logger.LogWarning("ChangeTerrainAttributes announces {Announced} areas but carries only {Actual}.", message.AreaCount, areaCount);
                }

                var areas = new List<(int, int, int, int)>(areaCount);
                for (int i = 0; i < areaCount; i++)
                {
                    ChangeTerrainAttributes.TerrainArea area = message[i];
                    areas.Add((area.StartX, area.StartY, area.EndX, area.EndY));
                }

                // Server attribute values (1, 2, 4, 8, 16) are the low bits of TWFlags.
                var flag = (TWFlags)(byte)message.Attribute;
                bool remove = message.RemoveAttribute;

                MuGame.ScheduleOnMainThread(() =>
                {
                    if (MuGame.Instance?.ActiveScene?.World is not WorldControl world || world.Terrain == null)
                    {
                        _logger.LogDebug("ChangeTerrainAttributes ignored: no active world.");
                        return;
                    }

                    int changed = world.Terrain.ApplyAttributeChange(flag, remove, areas);
                    _logger.LogInformation(
                        "Terrain attribute {Flag} {Action} on {Areas} area(s), {Changed} tile(s) changed.",
                        flag, remove ? "removed" : "added", areas.Count, changed);
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling ChangeTerrainAttributes packet.");
            }

            return Task.CompletedTask;
        }
    }
}
