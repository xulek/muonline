#nullable enable
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MUnique.OpenMU.Network;
using MUnique.OpenMU.Network.SimpleModulus;
using MUnique.OpenMU.Network.Xor;
using Pipelines.Sockets.Unofficial;

namespace Client.Main.Networking
{
    /// <summary>
    /// One private chat room on the chat server (original: CUIChatWindow + its own Connection to port 55980,
    /// UIWindows.cpp:1388). The game server sends ChatRoomConnectionInfo (0xCA) with the address, room id and
    /// token; the client connects, authenticates (C1 00) and exchanges ChatMessage (C1 04) packets.
    /// Client-to-server packets use the game client encryption; the chat server answers unencrypted.
    /// </summary>
    public sealed class ChatRoomSession : IAsyncDisposable
    {
        public const int DefaultPort = 55980;
        private const int TokenOffset = 6;
        private const int MessageOffset = 5;
        private const int MaxMessageBytes = 60;
        private static readonly TimeSpan KeepAliveInterval = TimeSpan.FromSeconds(30);

        private readonly ILogger _logger;
        private readonly ILoggerFactory _loggerFactory;
        private readonly Dictionary<byte, string> _clients = new();
        private readonly object _lock = new();
        private SocketConnection? _socket;
        private IConnection? _connection;
        private CancellationTokenSource? _keepAliveCts;
        private bool _leaving;

        public ChatRoomSession(ILoggerFactory loggerFactory, ushort roomId, uint token, string friendName)
        {
            _logger = loggerFactory.CreateLogger<ChatRoomSession>();
            _loggerFactory = loggerFactory;
            RoomId = roomId;
            Token = token;
            FriendName = friendName;
        }

        public ushort RoomId { get; }
        public uint Token { get; }
        public string FriendName { get; }
        public bool IsConnected => _connection?.Connected == true;

        /// <summary>(sender name, message). Raised on a background thread.</summary>
        public event Action<string, string>? MessageReceived;

        /// <summary>(name, joined). Raised on a background thread.</summary>
        public event Action<string, bool>? ClientChanged;

        public event Action? Disconnected;

        public IReadOnlyList<string> GetClientNames()
        {
            lock (_lock) return _clients.Values.ToList();
        }

        public async Task<bool> ConnectAsync(string host, int port = DefaultPort)
        {
            try
            {
                var address = IPAddress.TryParse(host, out var parsed)
                    ? parsed
                    : (await Dns.GetHostAddressesAsync(host)).FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
                if (address == null)
                {
                    _logger.LogError("Chat server address {Host} could not be resolved.", host);
                    return false;
                }

                _socket = await SocketConnection.ConnectAsync(new IPEndPoint(address, port), new PipeOptions());
                IDuplexPipe pipe = _socket;
                var simpleModulus = new PipelinedSimpleModulusEncryptor(pipe.Output, PipelinedSimpleModulusEncryptor.DefaultClientKey);
                var xor32 = new PipelinedXor32Encryptor(simpleModulus.Writer);
                var connection = new Connection(pipe, null, xor32, _loggerFactory.CreateLogger<Connection>());
                connection.PacketReceived += OnPacketReceivedAsync;
                connection.Disconnected += OnDisconnectedAsync;
                _connection = connection;
                _ = connection.BeginReceiveAsync();

                await SendAuthenticateAsync();
                StartKeepAlive();
                _logger.LogInformation("Connected to chat room {Room} on {Host}:{Port}.", RoomId, host, port);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not connect to the chat server {Host}:{Port}.", host, port);
                await DisposeAsync();
                return false;
            }
        }

        private Task SendAuthenticateAsync()
        {
            var connection = _connection!;
            return connection.SendAsync(() =>
            {
                var span = connection.Output.GetSpan(16)[..16];
                span.Clear();
                span[0] = 0xC1;
                span[1] = 16;
                span[2] = 0x00;
                span[4] = (byte)(RoomId & 0xFF);
                span[5] = (byte)(RoomId >> 8);
                var token = Encoding.ASCII.GetBytes(Token.ToString());
                token.AsSpan(0, Math.Min(token.Length, 10)).CopyTo(span[TokenOffset..]);
                Xor3(span[TokenOffset..]);
                return 16;
            }).AsTask();
        }

        /// <summary>Sends a message to the room; the server echoes it to every member, the sender included.</summary>
        public async Task<bool> SendMessageAsync(string message)
        {
            if (_connection is not { Connected: true } connection || string.IsNullOrWhiteSpace(message))
                return false;

            var bytes = Encoding.UTF8.GetBytes(message);
            int count = Math.Min(bytes.Length, MaxMessageBytes);
            try
            {
                await connection.SendAsync(() =>
                {
                    int length = MessageOffset + count;
                    var span = connection.Output.GetSpan(length)[..length];
                    span[0] = 0xC1;
                    span[1] = (byte)length;
                    span[2] = 0x04;
                    span[3] = 0; // sender index, set by the server
                    span[4] = (byte)count;
                    bytes.AsSpan(0, count).CopyTo(span[MessageOffset..]);
                    Xor3(span[MessageOffset..]);
                    return length;
                });
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not send chat message.");
                return false;
            }
        }

        /// <summary>Leaves the room (C1 01) and closes the connection.</summary>
        public async Task LeaveAsync()
        {
            if (_leaving) return;
            _leaving = true;
            if (_connection is { Connected: true } connection)
            {
                try
                {
                    await connection.SendAsync(() =>
                    {
                        var span = connection.Output.GetSpan(3)[..3];
                        span[0] = 0xC1;
                        span[1] = 3;
                        span[2] = 0x01;
                        return 3;
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Could not send LeaveChatRoom.");
                }
            }

            await DisposeAsync();
        }

        private void StartKeepAlive()
        {
            _keepAliveCts = new CancellationTokenSource();
            var token = _keepAliveCts.Token;
            _ = Task.Run(async () =>
            {
                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        await Task.Delay(KeepAliveInterval, token);
                        if (_connection is not { Connected: true } connection) break;
                        await connection.SendAsync(() =>
                        {
                            var span = connection.Output.GetSpan(3)[..3];
                            span[0] = 0xC1;
                            span[1] = 3;
                            span[2] = 0x05;
                            return 3;
                        });
                    }
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Chat keep-alive stopped.");
                }
            }, token);
        }

        private ValueTask OnPacketReceivedAsync(ReadOnlySequence<byte> sequence)
        {
            try
            {
                HandlePacket(sequence.ToArray());
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Invalid chat server packet.");
            }

            return ValueTask.CompletedTask;
        }

        private void HandlePacket(byte[] data)
        {
            if (data.Length < 3) return;
            switch (data[0], data[0] == 0xC2 ? data[3] : data[2])
            {
                case (0xC1, 0x04) when data.Length >= MessageOffset: // ChatMessage
                {
                    int count = Math.Min(data[4], data.Length - MessageOffset);
                    var message = data.AsSpan(MessageOffset, count).ToArray();
                    Xor3(message);
                    string sender;
                    lock (_lock) sender = _clients.TryGetValue(data[3], out var name) ? name : "?";
                    MessageReceived?.Invoke(sender, Encoding.UTF8.GetString(message).TrimEnd('\0'));
                    break;
                }
                case (0xC1, 0x01) when data.Length >= 15: // ChatRoomClientJoined (00) / Left (01)
                {
                    string name = ReadString(data.AsSpan(5, 10));
                    bool joined = data[3] == 0x00;
                    lock (_lock)
                    {
                        if (joined) _clients[data[4]] = name;
                        else _clients.Remove(data[4]);
                    }
                    ClientChanged?.Invoke(name, joined);
                    break;
                }
                case (0xC2, 0x02) when data.Length >= 8: // ChatRoomClients
                {
                    int count = data[6];
                    lock (_lock)
                    {
                        _clients.Clear();
                        for (int i = 0; i < count && 8 + (i + 1) * 11 <= data.Length; i++)
                            _clients[data[8 + i * 11]] = ReadString(data.AsSpan(9 + i * 11, 10));
                    }
                    ClientChanged?.Invoke(string.Empty, true);
                    break;
                }
            }
        }

        private ValueTask OnDisconnectedAsync()
        {
            _logger.LogInformation("Chat room {Room} disconnected.", RoomId);
            Disconnected?.Invoke();
            return ValueTask.CompletedTask;
        }

        private static string ReadString(ReadOnlySpan<byte> data)
        {
            int end = data.IndexOf((byte)0);
            return Encoding.UTF8.GetString(end < 0 ? data : data[..end]);
        }

        /// <summary>The "commonly known" 3-byte XOR (FC CF AB) used for chat tokens and messages.</summary>
        private static void Xor3(Span<byte> data)
        {
            var keys = DefaultKeys.Xor3Keys;
            for (int i = 0; i < data.Length; i++)
                data[i] ^= keys[i % 3];
        }

        public async ValueTask DisposeAsync()
        {
            _keepAliveCts?.Cancel();
            _keepAliveCts = null;
            var connection = _connection;
            _connection = null;
            if (connection != null)
            {
                connection.PacketReceived -= OnPacketReceivedAsync;
                try { await connection.DisconnectAsync(); } catch { /* ignore */ }
                connection.Dispose();
            }
            _socket?.Dispose();
            _socket = null;
        }
    }
}
