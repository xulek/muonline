using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;
using Client.Main.Scenes;
using MUnique.OpenMU.Network;
using MUnique.OpenMU.Network.Packets;
using MUnique.OpenMU.Network.Packets.ClientToServer;
using MUnique.OpenMU.Network.Packets.ServerToClient;
using MUnique.OpenMU.Network.Xor;

namespace Client.Main.Networking
{
    public class RealLoginClient
    {
        private readonly IConnection _connection;
        private readonly Xor3Encryptor _xor3Encryptor = new Xor3Encryptor(0);

        // Delegate for handling packets
        private delegate void PacketHandler(Span<byte> packet);

        private readonly IDictionary<byte, PacketHandler> _packetHandlers = new Dictionary<byte, PacketHandler>();

        public event Action<LoginResponse> LoginResponseReceived;
        public event Action<GameServerEntered> GameServerResponseReceived;


        public RealLoginClient(IConnection connection)
        {
            _connection = connection;
            _connection.PacketReceived += OnPacketReceived;
            RegisterPacketHandlers();
        }

        private void RegisterPacketHandlers()
        {
            // Register packet handlers by packet type
            _packetHandlers.Add(GameServerEntered.Code, HandleLoginLogout);
        }

        private void HandleLoginLogout(Span<byte> packet)
        {
            // Route packet to the appropriate handler based on its subtype
            var subType = packet.GetPacketSubType();
            if (subType == GameServerEntered.SubCode)
            {
                HandleGameServerEntered(packet);
            }
            else if (subType == LoginResponse.SubCode)
            {
                HandleLoginResponse(packet);
            }
            else if (subType == LogoutResponse.SubCode)
            {
                HandleLogoutResponse(packet);
            }
        }

        private void HandleLogoutResponse(Span<byte> packet)
        {
            // Process logout response packet
            var logoutResponse = new LogoutResponse(packet);
            Console.WriteLine($"Logout response: {logoutResponse.Type}");
        }

        private void OnPacketReceived(object sender, ReadOnlySequence<byte> packetSequence)
        {
            // Rent memory and copy the received packet for processing
            using var memoryOwner = MemoryPool<byte>.Shared.Rent((int)packetSequence.Length);
            var packet = memoryOwner.Memory.Slice(0, (int)packetSequence.Length).Span;
            packetSequence.CopyTo(packet);

            if (_packetHandlers.TryGetValue(packet.GetPacketType(), out var handler))
            {
                handler(packet);
            }
        }

        private void HandleGameServerEntered(Span<byte> packet)
        {
            // Process GameServerEntered packet
            var response = new GameServerEntered(packet);
            GameServerResponseReceived?.Invoke(response);
        }

        private void HandleLoginResponse(Span<byte> packet)
        {
            // Process LoginResponse packet and notify subscribers
            var response = new LoginResponse(packet);
            LoginResponseReceived?.Invoke(response);

            if (response.Success == LoginResponse.LoginResult.Okay)
            {
                Console.WriteLine($"Login passed, reason: {response.Success}");
                MuGame.Instance.ChangeScene<SelectCharacterScene>();
            }
            else
            {
                Console.WriteLine($"Login failed, reason: {response.Success}");
            }
        }

        private void HandleCharacterList(Span<byte> packet)
        {
            // Process CharacterList packet and auto-select the first character
            var characterList = new CharacterList(packet);
            if (characterList.CharacterCount > 0)
            {
                string characterName = characterList[0].Name;
                _connection.SendSelectCharacter(characterName);
            }
        }

        public void SendLoginPacket(string username, string password)
        {
            // Start writing a login packet using the connection
            using var writer = _connection.StartWriteLoginLongPassword();
            var loginPacket = writer.Packet;

            // Write username and password using ASCII encoding
            loginPacket.Username.WriteString(username, Encoding.ASCII);
            loginPacket.Password.WriteString(password, Encoding.ASCII);

            // Encrypt username and password
            _xor3Encryptor.Encrypt(loginPacket.Username);
            _xor3Encryptor.Encrypt(loginPacket.Password);

            // Set additional fields
            loginPacket.TickCount = (uint)Environment.TickCount;

            // Commit the packet to send it
            writer.Commit();
        }
    }
}
