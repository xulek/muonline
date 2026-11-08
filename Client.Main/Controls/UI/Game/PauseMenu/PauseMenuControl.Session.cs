using System;
using System.Linq;
using System.Threading.Tasks;
using Client.Main.Networking;
using Client.Main.Scenes;
using Microsoft.Extensions.Logging;
using MUnique.OpenMU.Network.Packets; // LogOutType

namespace Client.Main.Controls.UI.Game.PauseMenu
{
    // Session flows started from the game menu: back to character select, back to server select, exit.
    public partial class PauseMenuControl
    {
        private readonly ILogger _logger = MuGame.AppLoggerFactory?.CreateLogger<PauseMenuControl>();
        private EventHandler<System.Collections.Generic.List<(string Name, CharacterClassNumber Class, ushort Level, byte[] Appearance)>> _characterListHandler;
        private EventHandler<LogOutType> _logoutResponseHandler;
        private bool _returnInProgress;
        private bool _exitInProgress;

        private bool IsBusy => _returnInProgress || _exitInProgress;

        private async Task ReturnToCharacterSelectAsync()
        {
            if (_returnInProgress) return;
            _returnInProgress = true;
            try
            {
                CharacterSelectClicked?.Invoke(this, EventArgs.Empty);
                await HandleReturnToCharacterSelectAsync();
            }
            finally
            {
                _returnInProgress = false;
            }
        }

        private async Task ExitAsync()
        {
            if (_exitInProgress) return;
            _exitInProgress = true;
            try
            {
                ExitClicked?.Invoke(this, EventArgs.Empty);
                await HandleExitAsync();
            }
            finally
            {
                _exitInProgress = false;
            }
        }

        private async Task HandleReturnToCharacterSelectAsync()
        {
            try
            {
                Visible = false;
                CloseSettings();

                // Close NPC/Vault before switching
                try
                {
                    NpcShopControl.Instance.Visible = false;
                    VaultControl.Instance.Visible = false;
                    var svc = MuGame.Network?.GetCharacterService();
                    if (svc != null)
                        _ = svc.SendCloseNpcRequestAsync();
                    MuGame.Network?.GetCharacterState()?.ClearShopItems();
                }
                catch { }

                var net = MuGame.Network;
                if (net == null || !net.IsConnected)
                {
                    MuGame.Instance.ChangeScene(new LoginScene());
                    return;
                }

                UnsubscribeCharacterListHandler(net);
                UnsubscribeLogoutHandler(net);

                var characterListTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                void CharacterListHandler(object sender, System.Collections.Generic.List<(string Name, CharacterClassNumber Class, ushort Level, byte[] Appearance)> list)
                {
                    try
                    {
                        var next = new SelectCharacterScene(list, net);
                        MuGame.Instance.ChangeScene(next);
                    }
                    finally
                    {
                        try { net.CharacterListReceived -= CharacterListHandler; } catch { }
                        _characterListHandler = null;
                        characterListTcs.TrySetResult(true);
                    }
                }
                _characterListHandler = CharacterListHandler;
                net.CharacterListReceived += _characterListHandler;

                var logoutTcs = new TaskCompletionSource<LogOutType>(TaskCreationOptions.RunContinuationsAsynchronously);
                void LogoutHandler(object sender, LogOutType type)
                {
                    logoutTcs.TrySetResult(type);
                }
                _logoutResponseHandler = LogoutHandler;
                net.LogoutResponseReceived += _logoutResponseHandler;

                _logger?.LogInformation("PauseMenu: Sending logout request (BackToCharacterSelection). Current state: {State}", net.CurrentState);
                await net.GetCharacterService().SendLogoutRequestAsync(LogOutType.BackToCharacterSelection);

                var logoutCompleted = await Task.WhenAny(logoutTcs.Task, Task.Delay(6000));
                if (logoutCompleted != logoutTcs.Task)
                {
                    _logger?.LogWarning("Logout response timed out. Staying in game.");
                    UnsubscribeLogoutHandler(net);
                    UnsubscribeCharacterListHandler(net);
                    Visible = true;
                    return;
                }

                var logoutResult = await logoutTcs.Task;
                UnsubscribeLogoutHandler(net);

                if (logoutResult != LogOutType.BackToCharacterSelection)
                {
                    _logger?.LogInformation("Logout returned type {Type}; aborting character selection flow.", logoutResult);
                    UnsubscribeCharacterListHandler(net);

                    if (logoutResult == LogOutType.BackToServerSelection)
                    {
                        MuGame.Instance.ChangeScene(new LoginScene());
                    }
                    else
                    {
                        Visible = true;
                    }
                    return;
                }

                // Wait for the refreshed character list which is requested after logout response.
                var listCompleted = await Task.WhenAny(characterListTcs.Task, Task.Delay(6000));
                if (listCompleted != characterListTcs.Task)
                {
                    UnsubscribeCharacterListHandler(net);
                    _logger?.LogWarning("Character list response timed out after logout. Staying in game.");

                    var cached = net.GetCachedCharacterList();
                    if (cached != null && cached.Count > 0)
                    {
                        try
                        {
                            _logger?.LogInformation("Using cached character list as fallback after timeout.");
                            MuGame.Instance.ChangeScene(new SelectCharacterScene(cached.ToList(), net));
                            return;
                        }
                        catch { /* if anything fails, reopen menu below */ }
                    }

                    Visible = true;
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error while returning to character select");
                // Keep the current scene; allow user to retry instead of forcing LoginScene
                Visible = true;

            }
        }

        private async Task HandleReturnToServerSelectAsync()
        {
            try
            {
                Visible = false;
                CloseSettings();

                // Close NPC/Vault before switching
                try
                {
                    NpcShopControl.Instance.Visible = false;
                    VaultControl.Instance.Visible = false;
                    var svc = MuGame.Network?.GetCharacterService();
                    if (svc != null)
                        _ = svc.SendCloseNpcRequestAsync();
                    MuGame.Network?.GetCharacterState()?.ClearShopItems();
                }
                catch { }

                var net = MuGame.Network;
                if (net == null || !net.IsConnected)
                {
                    MuGame.Instance.ChangeScene(new LoginScene());
                    return;
                }

                UnsubscribeCharacterListHandler(net);
                UnsubscribeLogoutHandler(net);

                var logoutTcs = new TaskCompletionSource<LogOutType>(TaskCreationOptions.RunContinuationsAsynchronously);
                void LogoutHandler(object sender, LogOutType type)
                {
                    logoutTcs.TrySetResult(type);
                }
                _logoutResponseHandler = LogoutHandler;
                net.LogoutResponseReceived += _logoutResponseHandler;

                _logger?.LogInformation("PauseMenu: Sending logout request (BackToServerSelection). Current state: {State}", net.CurrentState);
                await net.GetCharacterService().SendLogoutRequestAsync(LogOutType.BackToServerSelection);

                var completed = await Task.WhenAny(logoutTcs.Task, Task.Delay(6000));
                if (completed != logoutTcs.Task)
                {
                    _logger?.LogWarning("Logout response timed out. Staying in game.");
                    UnsubscribeLogoutHandler(net);
                    Visible = true;
                    return;
                }

                var logoutResult = await logoutTcs.Task;
                UnsubscribeLogoutHandler(net);

                if (logoutResult != LogOutType.BackToServerSelection)
                {
                    _logger?.LogInformation("Logout returned type {Type}; keeping player in current scene.", logoutResult);
                    Visible = true;
                    return;
                }

                try
                {
                    _ = net.ConnectToConnectServerAsync();
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "PauseMenu: Failed to initiate connect server reconnect after logout.");
                }

                MuGame.Instance.ChangeScene(new LoginScene());
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error while returning to server select");
                MuGame.Instance.ChangeScene(new LoginScene());
            }
        }

        private async Task HandleExitAsync()
        {
            try
            {
                Visible = false;
                CloseSettings();

                var net = MuGame.Network;
                if (net != null && net.IsConnected)
                {
                    UnsubscribeLogoutHandler(net);

                    var logoutTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    void LogoutHandler(object sender, LogOutType type)
                    {
                        if (type == LogOutType.CloseGame)
                        {
                            logoutTcs.TrySetResult(true);
                        }
                    }

                    _logoutResponseHandler = LogoutHandler;
                    net.LogoutResponseReceived += _logoutResponseHandler;

                    _logger?.LogInformation("PauseMenu: Sending logout request (CloseGame). Current state: {State}", net.CurrentState);
                    try
                    {
                        await net.GetCharacterService().SendLogoutRequestAsync(LogOutType.CloseGame);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "PauseMenu: Logout request (CloseGame) failed, proceeding with local shutdown.");
                        logoutTcs.TrySetResult(false);
                    }

                    await Task.WhenAny(logoutTcs.Task, Task.Delay(3000));

                    UnsubscribeLogoutHandler(net);
                }

                MuGame.ScheduleOnMainThread(() =>
                {
#if !IOS
                    MuGame.Instance.Exit();
#endif
                });
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "PauseMenu: Error while exiting the game. Forcing shutdown.");
#if !IOS
                MuGame.ScheduleOnMainThread(() => MuGame.Instance.Exit());
#endif
            }
        }

        public override void Dispose()
        {
            try
            {
                var net = MuGame.Network;
                UnsubscribeCharacterListHandler(net);
                UnsubscribeLogoutHandler(net);
            }
            finally
            {
                base.Dispose();
            }
        }

        private void UnsubscribeCharacterListHandler(NetworkManager net)
        {
            if (net != null && _characterListHandler != null)
            {
                try { net.CharacterListReceived -= _characterListHandler; } catch { }
            }
            _characterListHandler = null;
        }

        private void UnsubscribeLogoutHandler(NetworkManager net)
        {
            if (net != null && _logoutResponseHandler != null)
            {
                try { net.LogoutResponseReceived -= _logoutResponseHandler; } catch { }
            }
            _logoutResponseHandler = null;
        }
    }
}
