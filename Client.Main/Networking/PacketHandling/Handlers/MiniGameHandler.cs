using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Client.Data.ATT;
using Client.Main.Controls;
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

        public MiniGameHandler(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<MiniGameHandler>();
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
