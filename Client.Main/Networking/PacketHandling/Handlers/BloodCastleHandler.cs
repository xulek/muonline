using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MUnique.OpenMU.Network.Packets.ServerToClient;
using Client.Main.Core.Client;
using Client.Main.Networking;
using Client.Main.Controls.UI;
using Client.Main.Controls.UI.Game;
using Client.Main.Core.Utilities;
using Client.Main.Controls;
using Client.Main.Controllers;
using Client.Main.Objects.Worlds.BloodCastle;
using Client.Main.Worlds;

namespace Client.Main.Networking.PacketHandling.Handlers
{
    /// <summary>
    /// Handles packets related to Blood Castle event.
    /// </summary>
    public class BloodCastleHandler : IGamePacketHandler
    {
        private readonly ILogger<BloodCastleHandler> _logger;
        private readonly CharacterState _characterState;
        private readonly NetworkManager _networkManager;
        private ChaosCastleEventWorldBase _chaosAudioWorld;
        private byte _chaosAudioState = byte.MaxValue;

        public BloodCastleHandler(
            ILoggerFactory loggerFactory,
            CharacterState characterState,
            NetworkManager networkManager)
        {
            _logger = loggerFactory.CreateLogger<BloodCastleHandler>();
            _characterState = characterState;
            _networkManager = networkManager;
        }

        /// <summary>
        /// Handles Blood Castle game state updates (0x9B).
        /// Updates timer, monster count, and quest item carrier.
        /// </summary>
        [PacketHandler(0x9B, PacketRouter.NoSubCode)]
        public Task HandleMatchGameStateAsync(Memory<byte> packet)
        {
            try
            {
                if (packet.Length < 12)
                {
                    _logger.LogWarning("MatchGameState packet too short: {Length}", packet.Length);
                    return Task.CompletedTask;
                }

                var span = packet.Span;
                byte playState = span[3];
                ushort remainSec = (ushort)((span[5] << 8) | span[4]);
                ushort maxKillMonster = (ushort)((span[7] << 8) | span[6]);
                ushort curKillMonster = (ushort)((span[9] << 8) | span[8]);
                ushort questItemCarrierIndex = (ushort)((span[11] << 8) | span[10]);
                byte questItemType = span.Length > 12 ? span[12] : (byte)0xFF;

                _logger.LogInformation("Blood Castle State: PlayState={PlayState}, Time={Time}s, Monsters={Cur}/{Max}, Carrier={Carrier}, ItemType={ItemType}",
                    playState, remainSec, curKillMonster, maxKillMonster, questItemCarrierIndex, questItemType);

                MuGame.ScheduleOnMainThread(() =>
                {
                    switch (playState)
                    {
                        case 0: // Event start
                            _logger.LogInformation("Blood Castle event started!");
                            SoundController.Instance.PlayAmbientSound("Sound/iBloodCastle.wav");
                            break;

                        case 1: // Playing - phase 1
                        case 4: // Playing - phase 2
                            var timeControl = BloodCastleTimeControl.Instance;
                            if (timeControl != null)
                            {
                                if (!timeControl.Visible)
                                {
                                    timeControl.ShowWindow();
                                }
                                timeControl.SetTime(remainSec);
                                timeControl.SetKillMonsterStatus(curKillMonster, maxKillMonster);
                            }

                            // Handle quest item carrier display
                            if (questItemCarrierIndex != 0xFFFF && questItemType != 0xFF)
                            {
                                ushort maskedIndex = (ushort)(questItemCarrierIndex & 0x7FFF);
                                _logger.LogDebug("Quest item carrier: Index={Index}, Type={Type}", maskedIndex, questItemType);
                                // TODO: Display quest item on character model (EtcPart)
                            }
                            break;

                        case 2: // Event end
                            _logger.LogInformation("Blood Castle event ended.");
                            BloodCastleTimeControl.Instance?.HideWindow();
                            SoundController.Instance.StopAmbientSound();
                            break;

                        case 3: // Special state (bridge activation?)
                            _logger.LogInformation("Blood Castle special state (bridge activation?).");
                            if (MuGame.Instance?.ActiveScene?.World is WorldControl world)
                                BloodCastleObject.BeginBridgeOpening(world);
                            break;

                        // Chaos Castle states (ChaosCastleStateViewPlugIn): CurMonster carries the remaining
                        // monsters and players, RemainSecond the time.
                        case 5: // ChaosCastleStarted
                        case 6: // ChaosCastleRunning
                        case 8: // ChaosCastleStageOne
                        case 9: // ChaosCastleStageTwo
                        case 10: // ChaosCastleStageThree
                            BloodCastleTimeControl.Instance?.ShowChaosCastle(remainSec, curKillMonster);
                            if (playState == 5 && MuGame.Instance?.ActiveScene?.World is ChaosCastleEventWorldBase startWorld)
                            {
                                if (!ReferenceEquals(_chaosAudioWorld, startWorld) || _chaosAudioState != 5)
                                {
                                    _chaosAudioWorld = startWorld;
                                    _chaosAudioState = 5;
                                    SoundController.Instance.PlayAmbientSound("Sound/iChaosCastle.wav");
                                }
                            }
                            if (playState == 5 || playState >= 8)
                            {
                                string text = playState switch
                                {
                                    5 => "Chaos Castle has started! Defeat every monster and player.",
                                    8 => "The castle starts to collapse!",
                                    9 => "The castle collapses further!",
                                    _ => "Only the centre of the castle remains!",
                                };
                                (MuGame.Instance?.ActiveScene as Client.Main.Scenes.GameScene)?.ChatLog?.AddMessage("System", text, Models.MessageType.System);
                            }
                            break;

                        case 7: // ChaosCastleEnded
                            BloodCastleTimeControl.Instance?.HideWindow();
                            if (MuGame.Instance?.ActiveScene?.World is ChaosCastleEventWorldBase endWorld &&
                                (!ReferenceEquals(_chaosAudioWorld, endWorld) || _chaosAudioState != 7))
                            {
                                _chaosAudioWorld = endWorld;
                                _chaosAudioState = 7;
                                SoundController.Instance.PlayAmbientSound("Sound/aChaos.wav");
                                SoundController.Instance.PlayBuffer("Sound/aChaosEnd.wav");
                            }
                            break;

                        default:
                            _logger.LogWarning("Unknown Blood Castle play state: {PlayState}", playState);
                            break;
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing MatchGameState packet.");
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Handles Blood Castle enter response (0x9A).
        /// Server response after player attempts to enter Blood Castle.
        /// </summary>
        [PacketHandler(0x9A, PacketRouter.NoSubCode)]
        public Task HandleBloodCastleEnterResponseAsync(Memory<byte> packet)
        {
            try
            {
                if (packet.Length < 4)
                {
                    _logger.LogWarning("BloodCastleEnterResponse packet too short: {Length}", packet.Length);
                    return Task.CompletedTask;
                }

                var span = packet.Span;
                byte result = span[3];

                _logger.LogInformation("Blood Castle Enter Response: Result={Result}", result);

                MuGame.ScheduleOnMainThread(() =>
                {
                    string message = result switch
                    {
                        0 => "Successfully entered Blood Castle!",
                        1 => "Failed to enter Blood Castle.",
                        2 => "Blood Castle ticket not found or invalid level.",
                        3 => "You cannot enter at this time.",
                        4 => "Blood Castle is full.",
                        5 => "Your level is too low.",
                        6 => "Your level is too high.",
                        _ => $"Failed to enter Blood Castle (code: {result})."
                    };

                    if (result == 0)
                    {
                        _logger.LogInformation("Player successfully entered Blood Castle.");
                        // Success - player will be teleported by server
                    }
                    else
                    {
                        _logger.LogWarning("Blood Castle entry failed: {Message}", message);
                        RequestDialog.ShowInfo(message);
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing BloodCastleEnterResponse packet.");
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Handles Blood Castle result (0x9A sub ?).
        /// This might be sent via a different packet structure - placeholder for now.
        /// </summary>
        public void HandleBloodCastleResult(bool success, ulong experience, int zen, int score)
        {
            _logger.LogInformation("Blood Castle Result: Success={Success}, Exp={Exp}, Zen={Zen}, Score={Score}",
                success, experience, zen, score);

            MuGame.ScheduleOnMainThread(() =>
            {
                BloodCastleResultControl.Instance?.ShowResult(success, experience, zen, score);
            });
        }
    }
}
