using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MUnique.OpenMU.Network.Packets.ServerToClient;

namespace Client.Main.Networking
{
    /// <summary>
    /// Watches the connection and coordinates automatic recovery attempts.
    /// Health checks are optional; transport disconnects can also trigger recovery.
    /// </summary>
    public sealed class ConnectionHealthMonitor : IAsyncDisposable
    {
        private readonly ILogger<ConnectionHealthMonitor> _logger;
        private readonly NetworkManager _networkManager;
        private readonly TimeSpan _healthCheckInterval;
        private readonly TimeSpan _fastCheckInterval;
        private readonly TimeSpan _connectionTimeout;
        private readonly TimeSpan _baseReconnectDelay;
        private readonly int _failureThreshold;
        private readonly int _maxReconnectAttempts;
        private readonly bool _enableFastDetection;
        private readonly bool _healthChecksEnabled;

        private readonly SemaphoreSlim _recoveryGate = new(1, 1);

        private CancellationTokenSource _monitorCts;
        private CancellationTokenSource _recoveryCts;
        private Task _monitorTask;

        private int _consecutiveFailures;
        private bool _fastMode;
        private bool _disposed;

        public event EventHandler<ConnectionHealthEventArgs> ConnectionHealthChanged;
        public event EventHandler<ReconnectionEventArgs> ReconnectionAttempted;
        public event EventHandler ConnectionRestored;
        public event EventHandler<ConnectionLossEventArgs> ConnectionLost;
        public event EventHandler<string> ReconnectionFailed;

        public bool IsMonitoring => _monitorTask != null && !_monitorTask.IsCompleted;
        public bool IsRecovering { get; private set; }
        public bool IsHealthy => !IsRecovering && _consecutiveFailures == 0;
        public DateTime LastSuccessfulCheck { get; private set; } = DateTime.UtcNow;

        public ConnectionHealthMonitor(
            ILoggerFactory loggerFactory,
            NetworkManager networkManager,
            TimeSpan? healthCheckInterval = null,
            TimeSpan? connectionTimeout = null,
            int maxReconnectAttempts = 5,
            TimeSpan? reconnectDelay = null,
            int failureThreshold = 2,
            bool enableFastDetection = true,
            TimeSpan? fastCheckInterval = null,
            bool healthChecksEnabled = true)
        {
            _logger = loggerFactory?.CreateLogger<ConnectionHealthMonitor>()
                      ?? throw new ArgumentNullException(nameof(loggerFactory));
            _networkManager = networkManager ?? throw new ArgumentNullException(nameof(networkManager));

            _healthCheckInterval = healthCheckInterval ?? TimeSpan.FromSeconds(5);
            _connectionTimeout = connectionTimeout ?? TimeSpan.FromSeconds(3);
            _maxReconnectAttempts = Math.Max(1, maxReconnectAttempts);
            _baseReconnectDelay = reconnectDelay ?? TimeSpan.FromSeconds(2);
            _failureThreshold = Math.Max(1, failureThreshold);
            _enableFastDetection = enableFastDetection;
            _fastCheckInterval = fastCheckInterval ?? TimeSpan.FromSeconds(1);
            _healthChecksEnabled = healthChecksEnabled;
        }

        public void StartMonitoring()
        {
            if (IsMonitoring)
            {
                _logger.LogDebug("Connection health monitoring already active.");
                return;
            }

            _monitorCts = new CancellationTokenSource();
            _monitorTask = Task.Run(() => MonitorLoopAsync(_monitorCts.Token), CancellationToken.None);
            _logger.LogInformation("Connection health monitoring started.");
        }

        public async Task StopMonitoringAsync()
        {
            if (!IsMonitoring)
            {
                return;
            }

            _logger.LogInformation("Stopping connection health monitoring...");

            _monitorCts?.Cancel();
            _recoveryCts?.Cancel();

            try
            {
                if (_monitorTask != null)
                {
                    await _monitorTask.ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // expected when shutting down
            }

            CleanupMonitorResources();
            ResetHealth();
        }

        /// <summary>
        /// Called by the network manager when the transport reports a disconnect.
        /// </summary>
        public void NotifyTransportDisconnected()
        {
            if (!IsMonitoring)
            {
                return;
            }

            _logger.LogDebug("Health monitor notified about transport disconnect.");
            _ = TriggerRecoveryAsync(ConnectionLossTrigger.TransportClosed);
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            await StopMonitoringAsync().ConfigureAwait(false);
            _recoveryGate.Dispose();
            _disposed = true;
        }

        private async Task MonitorLoopAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    var interval = _fastMode && _enableFastDetection ? _fastCheckInterval : _healthCheckInterval;

                    try
                    {
                        await Task.Delay(interval, token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        break;
                    }

                    if (IsRecovering)
                    {
                        continue;
                    }

                    if (!_networkManager.IsConnected)
                    {
                        _logger.LogDebug("Health monitor observed IsConnected=false; scheduling recovery.");
                        await TriggerRecoveryAsync(ConnectionLossTrigger.TransportClosed).ConfigureAwait(false);
                        continue;
                    }

                    if (!_healthChecksEnabled)
                    {
                        continue; // rely on transport notifications only
                    }

                    var pingResult = await _networkManager.PingServerAsync((int)_connectionTimeout.TotalMilliseconds)
                        .ConfigureAwait(false);

                    if (pingResult.HasValue)
                    {
                        RegisterHealthy(pingResult.Value);
                    }
                    else
                    {
                        RegisterFailure(ConnectionLossTrigger.HealthCheckTimeout);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error inside health monitoring loop.");
            }
        }

        private void RegisterHealthy(int pingMs)
        {
            var wasUnhealthy = _consecutiveFailures > 0;
            _consecutiveFailures = 0;
            _fastMode = false;
            LastSuccessfulCheck = DateTime.UtcNow;

            ConnectionHealthChanged?.Invoke(this, new ConnectionHealthEventArgs
            {
                IsHealthy = true,
                PingMs = pingMs,
                ConsecutiveFailures = 0,
                LastCheck = LastSuccessfulCheck
            });

            if (wasUnhealthy && !IsRecovering)
            {
                _logger.LogInformation("Connection health restored. Ping: {Ping}ms", pingMs);
                ConnectionRestored?.Invoke(this, EventArgs.Empty);
            }
        }

        private void RegisterFailure(ConnectionLossTrigger trigger)
        {
            _consecutiveFailures++;
            if (_enableFastDetection)
            {
                _fastMode = true;
            }

            ConnectionHealthChanged?.Invoke(this, new ConnectionHealthEventArgs
            {
                IsHealthy = false,
                PingMs = null,
                ConsecutiveFailures = _consecutiveFailures,
                LastCheck = DateTime.UtcNow
            });

            if (_consecutiveFailures >= _failureThreshold)
            {
                _ = TriggerRecoveryAsync(trigger);
            }
        }

        private async Task TriggerRecoveryAsync(ConnectionLossTrigger trigger)
        {
            if (IsRecovering)
            {
                return;
            }

            var failureCount = Math.Max(_consecutiveFailures, _failureThreshold);
            var lossArgs = new ConnectionLossEventArgs
            {
                FailureCount = failureCount,
                Trigger = trigger,
                Message = trigger switch
                {
                    ConnectionLossTrigger.TransportClosed => "Connection to server closed.",
                    ConnectionLossTrigger.HealthCheckTimeout => "Connection timed out.",
                    _ => "Connection lost."
                }
            };

            ConnectionHealthChanged?.Invoke(this, new ConnectionHealthEventArgs
            {
                IsHealthy = false,
                PingMs = null,
                ConsecutiveFailures = failureCount,
                LastCheck = DateTime.UtcNow
            });

            if (!_networkManager.CanAutoReconnect)
            {
                _logger.LogWarning("Auto-reconnect disabled. Reporting connection loss without recovery attempt.");
                ConnectionLost?.Invoke(this, lossArgs);
                return;
            }

            if (!await _recoveryGate.WaitAsync(0).ConfigureAwait(false))
            {
                return; // another recovery is already in flight
            }

            IsRecovering = true;
            ConnectionLost?.Invoke(this, lossArgs);

            _recoveryCts?.Cancel();
            _recoveryCts?.Dispose();
            _recoveryCts = CancellationTokenSource.CreateLinkedTokenSource(_monitorCts.Token);

            _ = Task.Run(() => RecoveryLoopAsync(_recoveryCts.Token), CancellationToken.None);
        }

        private async Task RecoveryLoopAsync(CancellationToken token)
        {
            try
            {
                const int MaxAttempts = 60;

                for (int attempt = 1; attempt <= MaxAttempts; attempt++)
                {
                    if (token.IsCancellationRequested)
                    {
                        return;
                    }

                    ReconnectionAttempted?.Invoke(this, new ReconnectionEventArgs
                    {
                        Attempt = attempt,
                        MaxAttempts = MaxAttempts
                    });

                    var result = await _networkManager.AttemptGameReconnectionAsync(token).ConfigureAwait(false);

                    if (result.IsSuccess)
                    {
                        _logger.LogInformation("Reconnected successfully on attempt {Attempt}.", attempt);
                        _consecutiveFailures = 0;
                        _fastMode = false;
                        LastSuccessfulCheck = DateTime.UtcNow;
                        ConnectionRestored?.Invoke(this, EventArgs.Empty);
                        return;
                    }

                    if (result.IsCancelled)
                    {
                        _logger.LogInformation("Reconnection attempt cancelled.");
                        return;
                    }

                    var reason = result.FailureReason ?? "Unknown error";
                    _logger.LogWarning("Reconnection attempt {Attempt}/{Max} failed: {Reason}", attempt, MaxAttempts, reason);

                    if (result.LoginFailure == LoginResponse.LoginResult.AccountAlreadyConnected)
                    {
                        _logger.LogInformation("Account still marked as connected on the server. Waiting additional time before retry.");
                    }

                    if (attempt >= MaxAttempts)
                    {
                        ReconnectionFailed?.Invoke(this, reason);
                        return;
                    }

                    var delay = DetermineRetryDelay(result);
                    try
                    {
                        await Task.Delay(delay, token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during reconnection attempts.");
                ReconnectionFailed?.Invoke(this, ex.Message);
            }
            finally
            {
                IsRecovering = false;
                _fastMode = false;
                _recoveryCts?.Dispose();
                _recoveryCts = null;
                _recoveryGate.Release();
            }
        }

        private TimeSpan DetermineRetryDelay(ReconnectionAttemptResult result)
        {
            // AccountAlreadyConnected means the server still has the old session active
            // We need to wait for the server to detect the dead TCP connection and release it
            // This typically takes 10-30 seconds depending on TCP timeout settings
            if (result.LoginFailure == LoginResponse.LoginResult.AccountAlreadyConnected)
            {
                return TimeSpan.FromSeconds(15); // Wait for server TCP timeout to detect dead connection
            }

            // Session release is no longer used - this branch shouldn't be hit
            if (_networkManager.IsSessionReleasePending)
            {
                return TimeSpan.FromSeconds(3);
            }

            // Standard delay for other failures
            return TimeSpan.FromSeconds(3);
        }

        private void CleanupMonitorResources()
        {
            _monitorTask = null;
            _monitorCts?.Dispose();
            _monitorCts = null;

            _recoveryCts?.Dispose();
            _recoveryCts = null;
        }

        private void ResetHealth()
        {
            IsRecovering = false;
            _consecutiveFailures = 0;
            _fastMode = false;
        }
    }

    public sealed class ConnectionHealthEventArgs : EventArgs
    {
        public bool IsHealthy { get; init; }
        public int? PingMs { get; init; }
        public int ConsecutiveFailures { get; init; }
        public DateTime LastCheck { get; init; }
    }

    public sealed class ConnectionLossEventArgs : EventArgs
    {
        public int FailureCount { get; init; }
        public ConnectionLossTrigger Trigger { get; init; }
        public string Message { get; init; }
    }

    public sealed class ReconnectionEventArgs : EventArgs
    {
        public int Attempt { get; init; }
        public int MaxAttempts { get; init; }
    }

    public enum ConnectionLossTrigger
    {
        TransportClosed,
        HealthCheckTimeout,
        Manual
    }
}
