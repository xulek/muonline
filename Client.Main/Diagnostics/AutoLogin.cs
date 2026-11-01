using System;
using System.Collections.Generic;
using System.Linq;
using Client.Main.Core.Client;
using Client.Main.Core.Models;
using Client.Main.Models;
using Client.Main.Scenes;

namespace Client.Main.Diagnostics
{
    /// <summary>
    /// Diagnostics only: logs into a real server through the normal scenes (server list, login, character
    /// selection) so rendering can be checked with live data. MU_AUTOLOGIN_USER / MU_AUTOLOGIN_PASS (never
    /// stored), optional MU_AUTOLOGIN_CHAR (default: first character). In game it opens the inventory after
    /// MU_AUTOLOGIN_WAIT seconds (default 8), saves a screenshot to MU_STRESS_SHOTS and exits when
    /// MU_AUTOLOGIN_EXIT=1.
    /// </summary>
    public static class AutoLogin
    {
        public static bool Enabled { get; } =
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MU_AUTOLOGIN_USER"));

        private static List<ServerInfo> _servers;
        private static List<string> _characters;
        private static bool _serverRequested;
        private static bool _loginSent;
        private static bool _characterSelected;
        private static DateTime _selectSceneSeenAt = DateTime.MaxValue;
        private static DateTime _enteredGameAt = DateTime.MaxValue;
        private static DateTime _inventoryOpenedAt = DateTime.MaxValue;
        private static bool _done;

        public static void Attach(MuGame game)
        {
            var network = MuGame.Network;
            if (!Enabled || network == null)
                return;

            network.ServerListReceived += (_, servers) => _servers = servers;
            network.CharacterListReceived += (_, characters) => _characters = characters?.Select(c => c.Name).ToList();
            network.LoginFailed += (_, result) => Console.WriteLine($"[autologin] login failed: {result}");
        }

        public static void OnFrame(MuGame game)
        {
            var network = MuGame.Network;
            if (!Enabled || _done || network == null)
                return;

            var scene = game.ActiveScene;
            var state = network.CurrentState;

            if (scene is LoginScene && scene.Status == GameControlStatus.Ready)
            {
                if (!_serverRequested && state == ClientConnectionState.ReceivedServerList && _servers is { Count: > 0 })
                {
                    _serverRequested = true;
                    Console.WriteLine($"[autologin] connecting to server {_servers[0].ServerId}");
                    _ = network.RequestGameServerConnectionAsync(_servers[0].ServerId);
                }
                else if (!_loginSent && state == ClientConnectionState.ConnectedToGameServer)
                {
                    _loginSent = true;
                    Console.WriteLine("[autologin] sending login");
                    _ = network.SendLoginRequestAsync(
                        Environment.GetEnvironmentVariable("MU_AUTOLOGIN_USER"),
                        Environment.GetEnvironmentVariable("MU_AUTOLOGIN_PASS") ?? string.Empty);
                }
                return;
            }

            if (scene is SelectCharacterScene select && select.Status == GameControlStatus.Ready && !_characterSelected)
            {
                if (_selectSceneSeenAt == DateTime.MaxValue)
                    _selectSceneSeenAt = DateTime.UtcNow;
                if (DateTime.UtcNow - _selectSceneSeenAt < TimeSpan.FromSeconds(2) || _characters is not { Count: > 0 })
                    return;

                string wanted = Environment.GetEnvironmentVariable("MU_AUTOLOGIN_CHAR");
                string name = _characters.FirstOrDefault(n => !string.IsNullOrEmpty(wanted) &&
                                                              string.Equals(n, wanted, StringComparison.OrdinalIgnoreCase))
                              ?? _characters[0];
                _characterSelected = true;
                Console.WriteLine($"[autologin] characters: {string.Join(", ", _characters)}; selecting {name}");
                select.CharacterSelected(name);
                return;
            }

            if (scene is GameScene gameScene && gameScene.Status == GameControlStatus.Ready)
            {
                if (_enteredGameAt == DateTime.MaxValue)
                {
                    _enteredGameAt = DateTime.UtcNow;
                    Console.WriteLine("[autologin] in game");
                }

                double wait = double.TryParse(Environment.GetEnvironmentVariable("MU_AUTOLOGIN_WAIT"), out double w) ? w : 8;
                if (_inventoryOpenedAt == DateTime.MaxValue &&
                    DateTime.UtcNow - _enteredGameAt > TimeSpan.FromSeconds(wait) &&
                    gameScene.InventoryControl != null)
                {
                    gameScene.InventoryControl.Show();
                    _inventoryOpenedAt = DateTime.UtcNow;
                    Console.WriteLine("[autologin] inventory opened");
                }

                if (DateTime.UtcNow - _inventoryOpenedAt > TimeSpan.FromSeconds(3))
                {
                    StressHarness.SaveScreenshot(game, "autologin_inventory");
                    Console.WriteLine("[autologin] screenshot saved");
                    _done = true;
                    if (Environment.GetEnvironmentVariable("MU_AUTOLOGIN_EXIT") == "1")
                        game.Exit();
                }
            }
        }
    }
}
