using Client.Main.Configuration;
using Client.Main.Controls;
using Client.Main.Core.Client;
using Client.Main.Core.Models;
using Client.Main.Networking.PacketHandling;
using Client.Main.Networking.Services;
using Client.Main.Scenes;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework;
using MUnique.OpenMU.Network;
using MUnique.OpenMU.Network.Packets; // For CharacterClassNumber
using MUnique.OpenMU.Network.Packets.ClientToServer;
using MUnique.OpenMU.Network.Packets.ServerToClient;
using MUnique.OpenMU.Network.SimpleModulus;
using MUnique.OpenMU.Network.Xor;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Client.Main.Networking
{
    public sealed class NetworkManager : IAsyncDisposable
    {
        // Fields
        private static readonly SimpleModulusKeys EncryptKeys = PipelinedSimpleModulusEncryptor.DefaultClientKey;
        private static readonly SimpleModulusKeys DecryptKeys = PipelinedSimpleModulusDecryptor.DefaultClientKey;
        private static readonly byte[] Xor3Keys = DefaultKeys.Xor3Keys;

        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger<NetworkManager> _logger;
        private readonly MuOnlineSettings _settings;
        private readonly ConnectionManager _connectionManager;
        private readonly PacketRouter _packetRouter;
        private readonly LoginService _loginService;
        private readonly CharacterService _characterService;
        private readonly ConnectServerService _connectServerService;
        private readonly CharacterState _characterState;
        private readonly PartyManager _partyManager;
        private readonly ScopeManager _scopeManager;
        private readonly Dictionary<byte, byte> _serverDirectionMap;
        private readonly ConnectionHealthMonitor _healthMonitor;
        private string _selectedCharacterNameForLogin = string.Empty;
        private string _lastLoginUsername = string.Empty;
        private string _lastLoginPassword = string.Empty;
        private bool _sessionReleasePending;

        private ClientConnectionState _currentState = ClientConnectionState.Initial;
        private List<ServerInfo> _serverList = new();
        private List<(string Name, CharacterClassNumber Class, ushort Level, byte[] Appearance)> _lastCharacterList;
        private CancellationTokenSource _managerCts;

        private string _currentHost = string.Empty; // Host of current connection
        private int _currentPort; // Port of current connection

        // Events
        public event EventHandler<ClientConnectionState> ConnectionStateChanged;
        public event EventHandler<List<ServerInfo>> ServerListReceived;
        public event EventHandler<string> ErrorOccurred;
        public event EventHandler<List<(string Name, CharacterClassNumber Class, ushort Level, byte[] Appearance)>> CharacterListReceived;
        public event EventHandler LoginSuccess;
        public event EventHandler EnteredGame;
        public event EventHandler<LoginResponse.LoginResult> LoginFailed;
        public event EventHandler<LogOutType> LogoutResponseReceived;

        // Connection Health Events
        public event EventHandler<ConnectionHealthEventArgs> ConnectionHealthChanged;
        public event EventHandler<ReconnectionEventArgs> ReconnectionAttempted;
        public event EventHandler ConnectionRestored;
        public event EventHandler<int> ConnectionLost;

        // UI Events for Reconnection Dialog
        public event EventHandler<ReconnectionStartedEventArgs> ReconnectionStarted;
        public event EventHandler<ReconnectionProgressEventArgs> ReconnectionProgressChanged;
        public event EventHandler ReconnectionSucceeded;
        public event EventHandler<string> ReconnectionFailed;

        // Properties
        public ClientConnectionState CurrentState => _currentState;
        public bool IsConnected => _connectionManager.IsConnected;
        public CharacterState GetCharacterState() => _characterState;
        public Task SendClientReadyAfterMapChangeAsync()
            => _characterService.SendClientReadyAfterMapChangeAsync();

        public CharacterService GetCharacterService() => _characterService;
        public ScopeManager GetScopeManager() => _scopeManager;
        public PartyManager GetPartyManager() => _partyManager;
        public TargetProtocolVersion TargetVersion => _packetRouter.TargetVersion;
        public string CurrentHost => _currentHost;
        public int CurrentPort => _currentPort;
        public IReadOnlyList<(string Name, CharacterClassNumber Class, ushort Level, byte[] Appearance)> GetCachedCharacterList()
            => _lastCharacterList is null ? Array.Empty<(string, CharacterClassNumber, ushort, byte[])>() : new List<(string, CharacterClassNumber, ushort, byte[])>(_lastCharacterList);

        // Connection Health Properties
        public bool IsHealthMonitoring => _healthMonitor?.IsMonitoring ?? false;
        public bool IsConnectionHealthy => _healthMonitor?.IsHealthy ?? false;
        public DateTime LastHealthCheck => _healthMonitor?.LastSuccessfulCheck ?? DateTime.MinValue;
        internal bool CanAutoReconnect => _settings.ConnectionHealth.EnableAutoReconnect;
        internal bool IsSessionReleasePending => _sessionReleasePending;

        // Constructors
        public NetworkManager(ILoggerFactory loggerFactory, MuOnlineSettings settings, CharacterState characterState, ScopeManager scopeManager)
        {
            _loggerFactory = loggerFactory;
            _logger = loggerFactory.CreateLogger<NetworkManager>();
            _settings = settings;
            _characterState = characterState;
            _scopeManager = scopeManager;
            _partyManager = new PartyManager(_loggerFactory);

            var clientVersionBytes = Encoding.ASCII.GetBytes(settings.ClientVersion);
            var clientSerialBytes = Encoding.ASCII.GetBytes(settings.ClientSerial);
            var targetVersion = System.Enum.Parse<TargetProtocolVersion>(settings.ProtocolVersion, ignoreCase: true);

            _connectionManager = new ConnectionManager(loggerFactory, EncryptKeys, DecryptKeys);

            _loginService = new LoginService(_connectionManager, _loggerFactory.CreateLogger<LoginService>(), clientVersionBytes, clientSerialBytes, Xor3Keys);
            _characterService = new CharacterService(_connectionManager, _loggerFactory.CreateLogger<CharacterService>());
            _connectServerService = new ConnectServerService(_connectionManager, _loggerFactory.CreateLogger<ConnectServerService>());

            _packetRouter = new PacketRouter(loggerFactory, _characterService, _loginService, targetVersion, this, _characterState, _scopeManager, _partyManager, _settings);

            // Initialize connection health monitor
            _healthMonitor = new ConnectionHealthMonitor(
                loggerFactory,
                this,
                healthCheckInterval: TimeSpan.FromSeconds(settings.ConnectionHealth.HealthCheckInterval ?? 5),
                connectionTimeout: TimeSpan.FromSeconds(settings.ConnectionHealth.ConnectionTimeout ?? 3),
                maxReconnectAttempts: settings.ConnectionHealth.MaxReconnectAttempts ?? 10,
                reconnectDelay: TimeSpan.FromSeconds(settings.ConnectionHealth.ReconnectDelay ?? 2),
                failureThreshold: settings.ConnectionHealth.FailureThreshold ?? 2,
                enableFastDetection: settings.ConnectionHealth.EnableFastDetection ?? true,
                fastCheckInterval: TimeSpan.FromSeconds(settings.ConnectionHealth.FastCheckInterval ?? 1),
                healthChecksEnabled: settings.ConnectionHealth.EnableHealthCheck
            );

            // Subscribe to health monitor events
            _healthMonitor.ConnectionHealthChanged += (sender, args) => ConnectionHealthChanged?.Invoke(this, args);
            _healthMonitor.ReconnectionAttempted += OnHealthMonitorReconnectionAttempted;
            _healthMonitor.ConnectionRestored += OnHealthMonitorConnectionRestored;
            _healthMonitor.ConnectionLost += OnHealthMonitorConnectionLost;
            _healthMonitor.ReconnectionFailed += OnHealthMonitorReconnectionFailed;

            _managerCts = new CancellationTokenSource();

            _serverDirectionMap = settings.DirectionMap?.ToDictionary(kv => Convert.ToByte(kv.Key), kv => kv.Value)
                                  ?? new Dictionary<byte, byte>();
        }

        // Public Methods
        public IReadOnlyDictionary<byte, byte> GetDirectionMap()
        {
            return _serverDirectionMap;
        }

        /// <summary>
        /// Sends a public chat message (including party, guild, gens with prefixes) to the server.
        /// </summary>
        /// <param name="message">The message content, potentially including prefixes like ~, @, $.</param>
        public async Task SendPublicChatMessageAsync(string message)
        {
            if (!IsConnected || _currentState < ClientConnectionState.InGame)
            {
                _logger.LogWarning("Cannot send public chat message, not connected or not in game. State: {State}", _currentState);
                return;
            }

            string characterName = _characterState.Name ?? "Unknown";
            if (characterName == "???" || characterName == "Unknown")
            {
                _logger.LogWarning("Cannot send public chat message, character name not available yet.");
                OnErrorOccurred("Cannot send chat message: Character data not loaded.");
                return;
            }

            _logger.LogInformation("✉️ Sending Public Chat: '{Message}'", message);
            try
            {
                await _connectionManager.Connection.SendAsync(() =>
                    PacketBuilder.BuildPublicChatMessagePacket(_connectionManager.Connection.Output, characterName, message)
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "💥 Error sending public chat message.");
                OnErrorOccurred("Error sending chat message.");
            }
        }

        /// <summary>
        /// Sends a whisper message to the specified receiver.
        /// </summary>
        /// <param name="receiver">The name of the character to receive the whisper.</param>
        /// <param name="message">The message content.</param>
        public async Task SendWhisperMessageAsync(string receiver, string message)
        {
            if (!IsConnected || _currentState < ClientConnectionState.InGame)
            {
                _logger.LogWarning("Cannot send whisper message, not connected or not in game. State: {State}", _currentState);
                return;
            }

            _logger.LogInformation("Sending Whisper to '{Receiver}': '{Message}'", receiver, message);
            try
            {
                await _connectionManager.Connection.SendAsync(() =>
                   PacketBuilder.BuildWhisperMessagePacket(_connectionManager.Connection.Output, receiver, message)
               );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "💥 Error sending whisper message to {Receiver}.", receiver);
                OnErrorOccurred($"Error sending whisper to {receiver}.");
            }
        }

        /// <summary>
        /// Gets a read-only view of the currently cached server list.
        /// </summary>
        /// <returns>A read-only list of ServerInfo objects.</returns>
        public IReadOnlyList<ServerInfo> GetCachedServerList()
        {
            // Return a copy or ReadOnly view to prevent external modification.
            // ToList() creates a new list (a copy).
            // Or use ReadOnlyCollection for a more efficient read-only view:
            return new ReadOnlyCollection<ServerInfo>(_serverList);
        }

        public Task SendWalkRequestAsync(byte startX, byte startY, byte[] path)
            => _characterService.SendWalkRequestAsync(startX, startY, path);

        public Task SendInstantMoveRequestAsync(byte x, byte y)
            => _characterService.SendInstantMoveRequestAsync(x, y);

        public Task SendHitRequestAsync(ushort targetId, byte attackAnimation, byte lookingDirection)
            => _characterService.SendHitRequestAsync(targetId, attackAnimation, lookingDirection);

        /// <summary>
        /// Sends a warp command request to the server.
        /// </summary>
        /// <param name="warpInfoIndex">The index of the warp destination.</param>
        /// <param name="commandKey">Optional command key, if required by the server (default is 0).</param>
        public async Task SendWarpRequestAsync(ushort warpInfoIndex, uint commandKey = 0)
        {
            if (!IsConnected || _currentState < ClientConnectionState.InGame)
            {
                _logger.LogWarning("Cannot send warp request, not connected or not in game. State: {State}", _currentState);
                OnErrorOccurred("Cannot warp: Not in game.");
                return;
            }

            await _characterService.SendWarpCommandRequestAsync(warpInfoIndex, commandKey);
        }

        public async Task ConnectToConnectServerAsync()
        {
            if (_connectionManager.IsConnected)
            {
                OnErrorOccurred("Already connected. Disconnect first.");
                return;
            }

            var cancellationToken = _managerCts.Token;
            if (cancellationToken.IsCancellationRequested) return;

            UpdateState(ClientConnectionState.ConnectingToConnectServer);
            _packetRouter.SetRoutingMode(true); // Set routing to CS mode

            if (await _connectionManager.ConnectAsync(_settings.ConnectServerHost, _settings.ConnectServerPort, false, cancellationToken))
            {
                _currentHost = _settings.ConnectServerHost;
                _currentPort = _settings.ConnectServerPort;

                var csConnection = _connectionManager.Connection;
                csConnection.PacketReceived += HandlePacketAsync;
                csConnection.Disconnected += HandleDisconnectAsync;
                _connectionManager.StartReceiving(cancellationToken);
            }
            else
            {
                OnErrorOccurred($"Connection to Connect Server {_settings.ConnectServerHost}:{_settings.ConnectServerPort} failed.");
                UpdateState(ClientConnectionState.Disconnected);
            }
        }

        public async Task RequestServerListAsync()
        {
            if (_currentState != ClientConnectionState.ConnectedToConnectServer && _currentState != ClientConnectionState.ReceivedServerList)
            {
                OnErrorOccurred($"Cannot request server list in state: {_currentState}");
                return;
            }
            UpdateState(ClientConnectionState.RequestingServerList);
            await _connectServerService.RequestServerListAsync();
        }

        public async Task RequestGameServerConnectionAsync(ushort serverId)
        {
            if (_currentState >= ClientConnectionState.RequestingConnectionInfo && _currentState < ClientConnectionState.InGame)
            {
                _logger.LogWarning("RequestGameServerConnectionAsync called while already connecting/connected to GS or requesting info. Ignoring request for ServerId: {ServerId}. CurrentState: {State}", serverId, _currentState);
                return; // Already in progress or this stage is complete, ignore
            }

            _logger.LogDebug("NetworkManager.RequestGameServerConnectionAsync called. ServerId: {ServerId}. CurrentState: {State}", serverId, _currentState);

            if (_currentState != ClientConnectionState.ReceivedServerList)
            {
                OnErrorOccurred($"Cannot initiate game server connection request from state: {_currentState}");
                return;
            }
            if (!_connectionManager.IsConnected)
            {
                OnErrorOccurred("Cannot request game server connection: Not connected to Connect Server.");
                return;
            }

            _logger.LogInformation("Requesting connection info for Server ID: {ServerId}", serverId);
            UpdateState(ClientConnectionState.RequestingConnectionInfo);
            await _connectServerService.RequestConnectionInfoAsync(serverId);
        }

        public async Task RequestServerListAsync(bool initiatedByUi = false)
        {
            _logger.LogDebug("NetworkManager.RequestServerListAsync called. InitiatedByUi: {Initiated}. CurrentState: {State}", initiatedByUi, _currentState);

            if (initiatedByUi && _currentState != ClientConnectionState.ConnectedToConnectServer && _currentState != ClientConnectionState.ReceivedServerList)
            {
                OnErrorOccurred($"Cannot request server list in state: {_currentState}");
                return;
            }
            if (!_connectionManager.IsConnected)
            {
                OnErrorOccurred("Cannot request server list: Not connected.");
                return;
            }

            if (_currentState != ClientConnectionState.RequestingServerList)
            {
                UpdateState(ClientConnectionState.RequestingServerList);
            }
            await _connectServerService.RequestServerListAsync();
        }

        /// <summary>
        /// Sends a login request using the provided username and password.
        /// </summary>
        /// <param name="username">Username from UI.</param>
        /// <param name="password">Password from UI.</param>
        public async Task SendLoginRequestAsync(string username, string password)
        {
            if (_currentState != ClientConnectionState.ConnectedToGameServer && _currentState != ClientConnectionState.Authenticating) // Allow retry in Authenticating state
            {
                OnErrorOccurred($"Cannot send login request in state: {_currentState}");
                return;
            }
            _logger.LogInformation("Sending login request for user: {Username}", username);
            _lastLoginUsername = username;
            _lastLoginPassword = password;
            UpdateState(ClientConnectionState.Authenticating);
            await _loginService.SendLoginRequestAsync(username, password);
        }

        public async Task SendSelectCharacterRequestAsync(string characterName)
        {
            if (_currentState != ClientConnectionState.ConnectedToGameServer)
            {
                OnErrorOccurred($"Cannot select character in state: {_currentState}");
                return;
            }
            _logger.LogInformation("Sending select character request for: {CharacterName}", characterName);
            _selectedCharacterNameForLogin = characterName;
            UpdateState(ClientConnectionState.SelectingCharacter);
            await _characterService.SelectCharacterAsync(characterName);
        }

        // Internal Methods
        internal void ProcessCharacterRespawn(ushort mapId, byte x, byte y, byte direction)
        {
            _logger.LogInformation("ProcessCharacterRespawn: MapID={MapId}, X={X}, Y={Y}, Direction={Direction}", mapId, x, y, direction);

            bool previousInGameStatus = _characterState.IsInGame;
            _characterState.UpdateMap(mapId);
            _characterState.UpdatePosition(x, y);
            _characterState.IsInGame = true;

            MuGame.ScheduleOnMainThread(async () =>
            {
                try
                {
                    var game = MuGame.Instance;
                    // Avoid racing with SelectCharacterScene which will switch to GameScene
                    if (game?.ActiveScene is SelectCharacterScene)
                    {
                        _logger.LogInformation("ProcessCharacterRespawn: Currently in SelectCharacterScene; deferring scene change to it.");
                        return;
                    }
                    if (game?.ActiveScene is not GameScene gs)
                    {
                        _logger.LogWarning("ProcessCharacterRespawn: ActiveScene is not GameScene. Changing scene.");
                        game.ChangeScene(new GameScene());
                        return;
                    }

                    var currentWorld = gs.World as WalkableWorldControl;
                    bool mapChanged = currentWorld == null || currentWorld.WorldIndex != mapId + 1;

                    _logger.LogDebug("ProcessCharacterRespawn: CurrentWorldIndex: {CurrentIdx}, NewMapId: {NewMapId}, MapChanged: {MapChangedFlag}",
                        currentWorld?.WorldIndex, mapId, mapChanged);

                    var hero = gs.Hero;
                    hero.Reset();
                    hero.Location = new Vector2(x, y);
                    hero.Direction = (Client.Main.Models.Direction)direction;

                    if (mapChanged)
                    {
                        _logger.LogInformation("ProcessCharacterRespawn: Map has changed. Requesting world change to map {MapId}", mapId);
                        if (GameScene.MapWorldRegistry.TryGetValue((byte)mapId, out var worldType))
                        {
                            await gs.ChangeMap(worldType);
                        }
                        else
                        {
                            _logger.LogWarning("ProcessCharacterRespawn: Unknown mapId {MapId} for world change. Creating new GameScene.", mapId);
                            game.ChangeScene(new GameScene());
                        }
                    }
                    else
                    {
                        _logger.LogInformation("ProcessCharacterRespawn: Same map ({MapId}). Updating hero position.", mapId);
                        hero.MoveTargetPosition = hero.TargetPosition;
                        hero.Position = hero.TargetPosition;

                        _logger.LogInformation("ProcessCharacterRespawn: Sending ClientReadyAfterMapChange for same map teleport/respawn.");
                        await _characterService.SendClientReadyAfterMapChangeAsync();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in ProcessCharacterRespawn.");
                }
            });
        }

        internal void UpdateState(ClientConnectionState newState)
        {
            if (_currentState != newState)
            {
                var oldState = _currentState;
                _logger.LogInformation(">>> UpdateState: Changing state from {OldState} to {NewState}", oldState, newState);
                _currentState = newState;
                _logger.LogInformation("=== UpdateState: _currentState is now {CurrentState}", _currentState);

                MuGame.ScheduleOnMainThread(() =>
                {
                    _logger.LogTrace("--- UpdateState: Raising ConnectionStateChanged event for state {NewState} on main thread.", newState);
                    try
                    {
                        ConnectionStateChanged?.Invoke(this, newState);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "--- UpdateState: Exception during ConnectionStateChanged event invocation for state {NewState}", newState);
                    }
                    _logger.LogTrace("<<< UpdateState: ConnectionStateChanged event raising scheduled/attempted on main thread.");
                });
            }
            else
            {
                _logger.LogTrace(">>> UpdateState: State {NewState} is the same as current. No change.", newState);
            }
        }

        internal void OnErrorOccurred(string message)
        {
            _logger.LogError("Network Error: {Message}", message);
            MuGame.ScheduleOnMainThread(() =>
            {
                ErrorOccurred?.Invoke(this, message);
            });
        }

        internal void ProcessCharacterList(List<(string Name, CharacterClassNumber Class, ushort Level, byte[] Appearance)> characters)
        {            // If we receive a character list while in-game (e.g., returning to character selection),
            // normalize the state back to ConnectedToGameServer so UI can select a character without rejection.
            if (_currentState == ClientConnectionState.InGame)
            {
                _logger.LogInformation("ProcessCharacterList: Current state is InGame; switching to ConnectedToGameServer for character selection.");
                UpdateState(ClientConnectionState.ConnectedToGameServer);
            }
            // Cache the last received list for potential fallback usage
            try { _lastCharacterList = characters?.Select(c => (c.Name, c.Class, c.Level, c.Appearance?.ToArray() ?? Array.Empty<byte>())).ToList() ?? new(); }
            catch { _lastCharacterList = characters ?? new(); }
            _logger.LogInformation(">>> ProcessCharacterList: Received list with {Count} characters. Raising event on UI thread...", characters?.Count ?? 0);
            MuGame.ScheduleOnMainThread(() =>
            {
                _logger.LogTrace("--- ProcessCharacterList: Raising CharacterListReceived event on main thread.");
                try
                {
                    CharacterListReceived?.Invoke(this, characters ?? new());
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "--- ProcessCharacterList: Exception during CharacterListReceived event invocation.");
                }
                _logger.LogTrace("<<< ProcessCharacterList: CharacterListReceived event raising scheduled/attempted.");
            });
        }

        internal void ProcessHelloPacket()
        {
            _logger.LogInformation("Processing Hello packet. Updating state and requesting server list.");
            UpdateState(ClientConnectionState.ConnectedToConnectServer);
            _ = RequestServerListAsync(initiatedByUi: false);
        }

        internal void StoreServerList(List<ServerInfo> servers)
        {
            _serverList = servers ?? new List<ServerInfo>(); // Ensure it's not null
            UpdateState(ClientConnectionState.ReceivedServerList);
            _logger.LogInformation("Server list received ({Count} servers) and cached.", _serverList.Count);

            MuGame.ScheduleOnMainThread(() =>
            {
                ServerListReceived?.Invoke(this, _serverList.ToList());
            });
        }

        internal async void SwitchToGameServer(string host, int port)
        {
            if (_currentState != ClientConnectionState.RequestingConnectionInfo && _currentState != ClientConnectionState.ReceivedConnectionInfo)
            {
                _logger.LogWarning("Received game server info in unexpected state ({CurrentState}). Ignoring.", _currentState);
                return;
            }
            UpdateState(ClientConnectionState.ReceivedConnectionInfo);

            _logger.LogInformation("Disconnecting from Connect Server...");
            var oldConnection = _connectionManager.Connection;
            if (oldConnection != null)
            {
                try { oldConnection.PacketReceived -= HandlePacketAsync; } catch { /* Ignore */ }
                try { oldConnection.Disconnected -= HandleDisconnectAsync; } catch { /* Ignore */ }
            }
            await _connectionManager.DisconnectAsync();

            _logger.LogInformation("Connecting to Game Server {Host}:{Port}...", host, port);
            UpdateState(ClientConnectionState.ConnectingToGameServer);
            _packetRouter.SetRoutingMode(false);

            if (await _connectionManager.ConnectAsync(host, (ushort)port, true, _managerCts.Token))
            {
                _currentHost = host;
                _currentPort = port;

                var gsConnection = _connectionManager.Connection;
                gsConnection.PacketReceived += HandlePacketAsync;
                gsConnection.Disconnected += HandleDisconnectAsync;
                _connectionManager.StartReceiving(_managerCts.Token);
            }
            else
            {
                OnErrorOccurred($"Connection to Game Server {host}:{port} failed.");
                UpdateState(ClientConnectionState.Disconnected);
            }
        }

        internal void ProcessGameServerEntered()
        {
            _logger.LogInformation(">>> ProcessGameServerEntered: Received welcome packet. Calling UpdateState(ConnectedToGameServer)...");
            UpdateState(ClientConnectionState.ConnectedToGameServer);
            _logger.LogInformation("<<< ProcessGameServerEntered: UpdateState called.");
        }

        internal void ProcessLoginSuccess()
        {
            _logger.LogInformation(">>> ProcessLoginSuccess: Login OK. Updating state back to ConnectedToGameServer and requesting character list...");
            // Change state back to allow character selection
            UpdateState(ClientConnectionState.ConnectedToGameServer);
            // Raise LoginSuccess event for UI
            MuGame.ScheduleOnMainThread(() => LoginSuccess?.Invoke(this, EventArgs.Empty));
            // Send character list request
            _ = _characterService.RequestCharacterListAsync();
            _logger.LogInformation("<<< ProcessLoginSuccess: State updated and CharacterList requested.");
        }

        internal void ProcessLoginResponse(LoginResponse.LoginResult result)
        {
            if (result == LoginResponse.LoginResult.Okay)
            {
                ProcessLoginSuccess();
            }
            else
            {
                string reasonString = result.ToString();
                _logger.LogError("Login failed: {ReasonString}", reasonString);
                OnErrorOccurred($"Login failed: {reasonString}");
                MuGame.ScheduleOnMainThread(() =>
                {
                    _logger.LogDebug("NetworkManager: Invoking LoginFailed event with reason: {ResultEnum} (Value: {ResultByte})", result, (byte)result);
                    LoginFailed?.Invoke(this, result);
                });
                UpdateState(ClientConnectionState.ConnectedToGameServer); // Allow retry
            }
        }

        internal async Task ProcessLogoutResponseAsync(LogOutType type)
        {
            _logger.LogInformation(">>> ProcessLogoutResponse: Received logout response type {Type}", type);
            _characterState.IsInGame = false;

            switch (type)
            {
                case LogOutType.BackToCharacterSelection:
                    UpdateState(ClientConnectionState.ConnectedToGameServer);
                    try
                    {
                        await _characterService.RequestCharacterListAsync();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error requesting character list after logout response.");
                    }
                    break;

                case LogOutType.BackToServerSelection:
                    try
                    {
                        await StopHealthMonitoringAsync();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error while stopping health monitoring during logout to server selection.");
                    }

                    try
                    {
                        await _connectionManager.DisconnectAsync();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error while disconnecting from game server during logout to server selection.");
                    }

                    UpdateState(ClientConnectionState.Disconnected);
                    break;

                case LogOutType.CloseGame:
                    _logger.LogInformation("Logout type CloseGame received. No additional action taken by client.");
                    break;
            }

            MuGame.ScheduleOnMainThread(() =>
            {
                try
                {
                    LogoutResponseReceived?.Invoke(this, type);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error notifying LogoutResponseReceived subscribers.");
                }
            });

            _logger.LogInformation("<<< ProcessLogoutResponse: Handled logout response type {Type}", type);
        }

        internal void ProcessCharacterInformation()
        {
            if (_currentState != ClientConnectionState.SelectingCharacter)
            {
                _logger.LogWarning("ProcessCharacterInformation called in unexpected state ({CurrentState}). Character name might not be set correctly.", _currentState);
            }
            else if (string.IsNullOrEmpty(_selectedCharacterNameForLogin))
            {
                _logger.LogError("ProcessCharacterInformation called, but _selectedCharacterNameForLogin is null or empty. Cannot set character name in state.");
            }
            else
            {
                _characterState.Name = _selectedCharacterNameForLogin;
                _logger.LogInformation("CharacterState.Name set to '{Name}' based on selection.", _characterState.Name);
                _selectedCharacterNameForLogin = string.Empty; // Clear the temporary storage
            }

            _logger.LogInformation(">>> ProcessCharacterInformation: Character selected successfully. Updating state to InGame and raising EnteredGame event...");
            UpdateState(ClientConnectionState.InGame); // Change state (this will schedule ConnectionStateChanged)

            // Start health monitoring when entering the game
            StartHealthMonitoring();

            _logger.LogInformation("--- ProcessCharacterInformation: Raising EnteredGame event directly...");
            try
            {
                EnteredGame?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex) { _logger.LogError(ex, "--- ProcessCharacterInformation: Exception during EnteredGame event invocation."); }
            _logger.LogInformation("<<< ProcessCharacterInformation: State updated and EnteredGame event raised.");
        }

        /// <summary>
        /// Pings the current server.
        /// </summary>
        /// <param name="timeoutMs">The timeout for the ping in milliseconds.</param>
        /// <returns>The round-trip time in milliseconds, or null if the ping failed or host is not set.</returns>
        public async Task<int?> PingServerAsync(int timeoutMs = 1000)
        {
            if (string.IsNullOrWhiteSpace(_currentHost))
                return null;

            try
            {
                using var p = new System.Net.NetworkInformation.Ping();
                var reply = await p.SendPingAsync(_currentHost, timeoutMs);
                return reply.Status == System.Net.NetworkInformation.IPStatus.Success
                    ? (int?)reply.RoundtripTime
                    : null;
            }
            catch
            {
                return null;
            }
        }

        // Connection Health Methods

        /// <summary>
        /// Starts health monitoring if enabled in settings.
        /// </summary>
        public void StartHealthMonitoring()
        {
            if (!_settings.ConnectionHealth.EnableHealthCheck && !_settings.ConnectionHealth.EnableAutoReconnect)
            {
                _logger.LogDebug("Skipping health monitor start (both health checks and auto reconnect disabled).");
                return;
            }

            _healthMonitor.StartMonitoring();
        }

        /// <summary>
        /// Stops health monitoring.
        /// </summary>
        public async Task StopHealthMonitoringAsync()
        {
            await _healthMonitor.StopMonitoringAsync();
        }

        /// <summary>
        /// Attempts to reconnect to the game server automatically.
        /// This method tries to restore the connection and re-authenticate the character.
        /// </summary>
        internal async Task<ReconnectionAttemptResult> AttemptGameReconnectionAsync(CancellationToken cancellationToken)
        {
            if (!CanAutoReconnect)
            {
                _logger.LogDebug("Auto-reconnect disabled; skipping reconnection attempt.");
                return ReconnectionAttemptResult.Failed("Auto-reconnect disabled.");
            }

            if (string.IsNullOrWhiteSpace(_currentHost) || _currentPort == 0)
            {
                _logger.LogWarning("Cannot reconnect: missing server endpoint information.");
                return ReconnectionAttemptResult.Failed("Missing server endpoint information.");
            }

            var characterName = _characterState?.Name;
            var restoreSession = _characterState?.IsInGame == true && !string.IsNullOrEmpty(characterName);

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_managerCts.Token, cancellationToken);

            try
            {
                await EnsureDisconnectedAsync().ConfigureAwait(false);

                UpdateState(ClientConnectionState.Disconnected);

                _logger.LogInformation("Attempting to reconnect to game server {Host}:{Port}...", _currentHost, _currentPort);

                UpdateState(ClientConnectionState.ConnectingToGameServer);
                _packetRouter.SetRoutingMode(false);

                var connected = await _connectionManager.ConnectAsync(_currentHost, _currentPort, true, linkedCts.Token).ConfigureAwait(false);
                if (!connected)
                {
                    _logger.LogError("Failed to reconnect to game server {Host}:{Port}.", _currentHost, _currentPort);
                    _sessionReleasePending = false;
                    UpdateState(ClientConnectionState.Disconnected);
                    return ReconnectionAttemptResult.Failed($"Failed to connect to {_currentHost}:{_currentPort}.");
                }

                var gsConnection = _connectionManager.Connection;
                gsConnection.PacketReceived += HandlePacketAsync;
                gsConnection.Disconnected += HandleDisconnectAsync;
                _connectionManager.StartReceiving(linkedCts.Token);

                var handshakeCompleted = await WaitForStateAsync(ClientConnectionState.ConnectedToGameServer, TimeSpan.FromSeconds(10), linkedCts.Token).ConfigureAwait(false);
                if (!handshakeCompleted)
                {
                    _logger.LogWarning("Handshake with game server timed out during reconnection.");
                    await EnsureDisconnectedAsync().ConfigureAwait(false);
                    _sessionReleasePending = false;
                    UpdateState(ClientConnectionState.Disconnected);
                    return ReconnectionAttemptResult.Failed("Handshake with game server timed out.");
                }

                var hasCredentials = !string.IsNullOrWhiteSpace(_lastLoginUsername) && !string.IsNullOrWhiteSpace(_lastLoginPassword);
                if (hasCredentials)
                {
                    var loginResult = await ReauthenticateAsync(linkedCts.Token).ConfigureAwait(false);
                    if (!loginResult.Success)
                    {
                        _logger.LogWarning("Automatic login failed during reconnection: {Reason}", loginResult.FailureReason);

                        if (loginResult.FailureCode == LoginResponse.LoginResult.AccountAlreadyConnected)
                        {
                            _logger.LogInformation("Account already connected (first attempt). Trying login again to force server to disconnect old session...");

                            // THEORY: Maybe the server needs TWO login attempts:
                            // 1st attempt: Server returns AccountAlreadyConnected
                            // 2nd attempt: Server forcibly disconnects old session and accepts new login
                            _sessionReleasePending = false;

                            // Wait a moment, then try logging in AGAIN
                            await Task.Delay(TimeSpan.FromSeconds(2), linkedCts.Token).ConfigureAwait(false);

                            _logger.LogInformation("Sending second login attempt to force old session disconnect...");
                            var secondLoginResult = await ReauthenticateAsync(linkedCts.Token).ConfigureAwait(false);

                            if (!secondLoginResult.Success)
                            {
                                _logger.LogWarning("Second login attempt also failed: {Reason}", secondLoginResult.FailureReason);
                                await EnsureDisconnectedAsync().ConfigureAwait(false);
                                UpdateState(ClientConnectionState.Disconnected);
                                return ReconnectionAttemptResult.Failed($"Second login failed: {secondLoginResult.FailureReason}", secondLoginResult.FailureCode);
                            }

                            _logger.LogInformation("Second login attempt succeeded! Server accepted the connection.");
                            _sessionReleasePending = false;
                            // Continue with normal flow below (character selection, etc.)
                        }
                        else
                        {
                            _sessionReleasePending = false;
                            await EnsureDisconnectedAsync().ConfigureAwait(false);
                            UpdateState(ClientConnectionState.Disconnected);
                            return ReconnectionAttemptResult.Failed(loginResult.FailureReason, loginResult.FailureCode);
                        }
                    }
                    else
                    {
                        _sessionReleasePending = false;
                    }
                }
                else
                {
                    _sessionReleasePending = false;
                }

                if (!restoreSession)
                {
                    _logger.LogInformation("Transport reconnected successfully; awaiting player input.");
                    return ReconnectionAttemptResult.Succeeded(restoredSession: false);
                }

                _logger.LogInformation("Attempting to restore character session for {CharacterName}.", characterName);
                try
                {
                    await _characterService.RequestCharacterListAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to request character list during reconnection.");
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(1.5), linkedCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (linkedCts.IsCancellationRequested)
                {
                    throw;
                }

                await SendSelectCharacterRequestAsync(characterName).ConfigureAwait(false);

                var sessionRestored = await WaitForStateAsync(ClientConnectionState.InGame, TimeSpan.FromSeconds(15), linkedCts.Token).ConfigureAwait(false);
                if (sessionRestored)
                {
                    _logger.LogInformation("Character session restored successfully for {CharacterName}.", characterName);
                    _sessionReleasePending = false;
                    return ReconnectionAttemptResult.Succeeded(restoredSession: true);
                }

                _logger.LogWarning("Failed to restore character session. Current state: {State}", _currentState);
                await EnsureDisconnectedAsync().ConfigureAwait(false);
                _sessionReleasePending = false;
                UpdateState(ClientConnectionState.Disconnected);
                return ReconnectionAttemptResult.Failed("Failed to restore character session.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || linkedCts.IsCancellationRequested)
            {
                _logger.LogDebug("Reconnection attempt cancelled by caller.");
                return ReconnectionAttemptResult.Cancelled();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during reconnection attempt.");
                await EnsureDisconnectedAsync().ConfigureAwait(false);
                _sessionReleasePending = false;
                UpdateState(ClientConnectionState.Disconnected);
                return ReconnectionAttemptResult.Failed(ex.Message);
            }
        }

        private async Task EnsureDisconnectedAsync()
        {
            if (!_connectionManager.IsConnected)
            {
                return;
            }

            try
            {
                var existingConnection = _connectionManager.Connection;
                existingConnection.PacketReceived -= HandlePacketAsync;
                existingConnection.Disconnected -= HandleDisconnectAsync;
            }
            catch
            {
                // Connection might already be disposed; ignore.
            }

            try
            {
                await _connectionManager.DisconnectAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error while disconnecting prior to reconnection attempt.");
            }
        }

        private async Task<bool> WaitForStateAsync(ClientConnectionState expectedState, TimeSpan timeout, CancellationToken cancellationToken)
        {
            if (_currentState == expectedState)
            {
                return true;
            }

            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            void Handler(object sender, ClientConnectionState state)
            {
                if (state == expectedState)
                {
                    tcs.TrySetResult(true);
                }
                else if (state == ClientConnectionState.Disconnected && expectedState != ClientConnectionState.Disconnected)
                {
                    tcs.TrySetResult(false);
                }
            }

            ConnectionStateChanged += Handler;

            try
            {
                var completed = await Task.WhenAny(tcs.Task, Task.Delay(timeout, cancellationToken)).ConfigureAwait(false);
                if (completed == tcs.Task)
                {
                    return await tcs.Task.ConfigureAwait(false);
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                return false;
            }
            finally
            {
                ConnectionStateChanged -= Handler;
            }
        }

        private async Task<bool> AttemptSessionResumptionAsync(CancellationToken cancellationToken)
        {
            if (!_connectionManager.IsConnected)
            {
                _logger.LogWarning("Cannot resume session - not connected to server.");
                return false;
            }

            try
            {
                // Check if we're reconnecting from an active game session
                var wasInGame = _characterState?.IsInGame == true;
                var characterName = _characterState?.Name;

                if (wasInGame && !string.IsNullOrEmpty(characterName))
                {
                    _logger.LogInformation("Resuming active game session for character: {CharacterName}", characterName);

                    // We were in-game, so the server still has our character in the world
                    // CRITICAL: After AccountAlreadyConnected, the server knows we're trying to reconnect
                    // We just need to wait for the server to bind this new TCP connection to our existing game session
                    // The server should do this automatically and start sending us packets

                    _logger.LogInformation("Waiting for server to bind new connection to existing game session...");

                    // Give server time to detect AccountAlreadyConnected came from same account
                    // and bind the new TCP connection to the existing session
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);

                    // Set state back to InGame - we should start receiving packets now
                    UpdateState(ClientConnectionState.InGame);

                    // Start health monitoring since we're back in game
                    StartHealthMonitoring();

                    _logger.LogInformation("Session resumption complete - server should be sending packets to this connection now.");
                    return true;
                }
                else
                {
                    // We weren't in-game, so do the normal session resumption with character list
                    _logger.LogInformation("Server indicates we're logged in. Requesting character list...");

                    UpdateState(ClientConnectionState.ConnectedToGameServer);

                    // Request character list to resume normal session
                    await _characterService.RequestCharacterListAsync().ConfigureAwait(false);

                    // Give server a moment to send character list
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);

                    _logger.LogInformation("Session resumed - ready for character selection.");
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error during session resumption attempt.");
                return false;
            }
        }

        private async Task<(bool Success, string FailureReason, LoginResponse.LoginResult? FailureCode)> ReauthenticateAsync(CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(_lastLoginUsername) || string.IsNullOrWhiteSpace(_lastLoginPassword))
            {
                return (false, "Stored credentials unavailable.", null);
            }

            var loginTcs = new TaskCompletionSource<LoginResponse.LoginResult>(TaskCreationOptions.RunContinuationsAsynchronously);

            void OnLoginSucceeded(object sender, EventArgs args) => loginTcs.TrySetResult(LoginResponse.LoginResult.Okay);
            void OnLoginFailedHandler(object sender, LoginResponse.LoginResult result) => loginTcs.TrySetResult(result);

            LoginSuccess += OnLoginSucceeded;
            LoginFailed += OnLoginFailedHandler;

            try
            {
                await SendLoginRequestAsync(_lastLoginUsername, _lastLoginPassword).ConfigureAwait(false);

                var completed = await Task.WhenAny(loginTcs.Task, Task.Delay(TimeSpan.FromSeconds(8), cancellationToken)).ConfigureAwait(false);
                if (completed != loginTcs.Task)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                    }

                    return (false, "Login attempt timed out.", null);
                }

                var result = await loginTcs.Task.ConfigureAwait(false);
                if (result == LoginResponse.LoginResult.Okay)
                {
                    return (true, null, null);
                }

                return (false, $"Login failed: {result}", result);
            }
            finally
            {
                LoginSuccess -= OnLoginSucceeded;
                LoginFailed -= OnLoginFailedHandler;
            }
        }

        // Health Monitor Event Handlers

        private void OnHealthMonitorConnectionLost(object sender, ConnectionLossEventArgs args)
        {
            _logger.LogWarning("Health monitor detected connection loss ({Reason}) after {Failures} failures.", args.Trigger, args.FailureCount);
            ConnectionLost?.Invoke(this, args.FailureCount);

            if (!CanAutoReconnect)
            {
                _logger.LogInformation("Auto-reconnect disabled; skipping reconnection dialog.");
                OnErrorOccurred(args.Message ?? "Connection lost. Auto-reconnect is disabled.");
                return;
            }

            ReconnectionStarted?.Invoke(this, new ReconnectionStartedEventArgs
            {
                MaxAttempts = 60,
                Reason = args.Message ?? "Connection lost due to network issues"
            });
        }

        private void OnHealthMonitorReconnectionAttempted(object sender, ReconnectionEventArgs args)
        {
            _logger.LogDebug("Reconnection attempt {Attempt}/{MaxAttempts}.", args.Attempt, args.MaxAttempts);
            ReconnectionAttempted?.Invoke(this, args);

            // Update UI progress
            ReconnectionProgressChanged?.Invoke(this, new ReconnectionProgressEventArgs
            {
                CurrentAttempt = args.Attempt,
                MaxAttempts = args.MaxAttempts,
                Status = $"Reconnection attempt {args.Attempt} of {args.MaxAttempts}..."
            });
        }

        private void OnHealthMonitorConnectionRestored(object sender, EventArgs args)
        {
            _logger.LogInformation("Health monitor confirmed connection restored.");
            ConnectionRestored?.Invoke(this, args);

            // Trigger UI success event
            ReconnectionSucceeded?.Invoke(this, EventArgs.Empty);
        }

        private void OnHealthMonitorReconnectionFailed(object sender, string message)
        {
            _logger.LogError("Automatic reconnection failed: {Message}", message);
            ReconnectionFailed?.Invoke(this, message ?? "Automatic reconnection failed.");
        }

        // Private Methods
        private ValueTask HandlePacketAsync(ReadOnlySequence<byte> sequence)
        {
            return new ValueTask(_packetRouter.RoutePacketAsync(sequence));
        }

        private ValueTask HandleDisconnectAsync()
        {
            var previousState = _currentState;
            _logger.LogWarning("Connection lost. Previous state: {PreviousState}", previousState);
            UpdateState(ClientConnectionState.Disconnected);

            // Allow the health monitor to drive reconnection attempts for unexpected disconnects
            _healthMonitor.NotifyTransportDisconnected();

            return new ValueTask(_packetRouter.OnDisconnected());
        }

        // IAsyncDisposable Implementation
        public async ValueTask DisposeAsync()
        {
            _logger.LogInformation("Disposing NetworkManager...");

            // Stop health monitoring
            await StopHealthMonitoringAsync();

            _managerCts.Cancel();
            await _connectionManager.DisposeAsync();

            // Dispose health monitor
            await _healthMonitor.DisposeAsync();

            _managerCts.Dispose();
            _logger.LogInformation("NetworkManager disposed.");
            GC.SuppressFinalize(this);
        }
    }
}
