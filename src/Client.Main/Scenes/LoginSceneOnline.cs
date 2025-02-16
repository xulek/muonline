using Client.Main.Controllers;
using Client.Main.Controls.UI;
using Client.Main.Controls.UI.Login;
using Client.Main.Models;
using Client.Main.Networking;
using Client.Main.Worlds;
using MUnique.OpenMU.Network;
using MUnique.OpenMU.Network.Packets.ServerToClient;
using MUnique.OpenMU.Network.SimpleModulus;
using MUnique.OpenMU.Network.Xor;
using Pipelines.Sockets.Unofficial;
using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Client.Main.Scenes
{
    public class LoginSceneOnline : BaseScene
    {
        private LoginDialogOnline _loginDialog;
        private ServerGroupSelector _nonEventGroup;
        private ServerGroupSelector _eventGroup;
        private ServerList _serverList;
        private RealLoginClient _realLoginClient;
        private Connection _connection;

        public LoginSceneOnline()
        {
            _loginDialog = new LoginDialogOnline
            {
                Visible = false,
                Align = ControlAlign.HorizontalCenter | ControlAlign.VerticalCenter
            };
            _loginDialog.LoginButtonClicked += LoginButton_Click;
            Controls.Add(_loginDialog);
        }

        private void LoginButton_Click(object sender, EventArgs e)
        {
            // Retrieve credentials from the login dialog and send login packet
            string username = _loginDialog.Username;
            string password = _loginDialog.Password;
            _realLoginClient?.SendLoginPacket(username, password);
        }

        public override async Task Load()
        {
            await ChangeWorldAsync<NewLoginWorld>();
            await base.Load();
            SoundController.Instance.PlayBackgroundMusic("Music/login_theme.mp3");
        }

        public override void AfterLoad()
        {
            base.AfterLoad();
            TryConnect();
        }

        private async void TryConnect()
        {
            try
            {
                // Establish a TCP connection to the login server
                var address = $"{Constants.IPAddress}:{Constants.Port}";
                var socketConnection = await SocketConnection.ConnectAsync(IPEndPoint.Parse(address));
                var encryptor = new PipelinedXor32Encryptor(
                    new PipelinedSimpleModulusEncryptor(socketConnection.Output, PipelinedSimpleModulusEncryptor.DefaultClientKey).Writer);
                var decryptor = new PipelinedSimpleModulusDecryptor(socketConnection.Input, PipelinedSimpleModulusDecryptor.DefaultClientKey);
                var connection = new Connection(socketConnection, decryptor, encryptor);

                // Create the real login client using the established connection
                _realLoginClient = new RealLoginClient(connection);
                _realLoginClient.LoginResponseReceived += OnLoginResponseReceived;
                _realLoginClient.GameServerResponseReceived += OnGameServerResponseReceived;

                InitializeServerSelection();

                await connection.BeginReceive();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
                await HandleDisconnection();
            }
        }

        private void InitializeServerSelection()
        {
            // Initialize non-event server group
            _nonEventGroup = new ServerGroupSelector(false)
            {
                Align = ControlAlign.HorizontalCenter | ControlAlign.VerticalCenter,
                Margin = new Margin { Left = -220 }
            };

            for (byte i = 0; i < 4; i++)
            {
                _nonEventGroup.AddServer(i, $"Server {i + 1}");
            }

            // Initialize event server group
            _eventGroup = new ServerGroupSelector(true)
            {
                Align = ControlAlign.HorizontalCenter | ControlAlign.VerticalCenter,
                Margin = new Margin { Right = -220 }
            };

            for (byte i = 0; i < 3; i++)
            {
                _eventGroup.AddServer(i, $"Event {i + 1}");
            }

            // Initialize server list
            _serverList = new ServerList
            {
                Align = ControlAlign.VerticalCenter | ControlAlign.HorizontalCenter,
                Visible = false
            };

            _serverList.ServerClick += ServerList_ServerClick;

            // Register event handlers for server group selection
            _nonEventGroup.SelectedIndexChanged += NonEventGroup_SelectedIndexChanged;
            _eventGroup.SelectedIndexChanged += EventGroup_SelectedIndexChanged;

            Controls.Add(_nonEventGroup);
            Controls.Add(_eventGroup);
            Controls.Add(_serverList);
        }

        private void NonEventGroup_SelectedIndexChanged(object sender, EventArgs e)
        {
            // Update server list based on non-event group selection
            _serverList.Clear();
            for (var i = 0; i < 10; i++)
            {
                _serverList.AddServer((byte)i, $"Non Event Server {_nonEventGroup.ActiveIndex + 1}", (byte)((i + 1) * 10));
            }
            _serverList.Visible = true;
            _eventGroup.UnselectServer();
        }

        private void EventGroup_SelectedIndexChanged(object sender, EventArgs e)
        {
            // Update server list based on event group selection
            _serverList.Clear();
            for (var i = 0; i < 10; i++)
            {
                _serverList.AddServer((byte)i, $"Event Server {_eventGroup.ActiveIndex + 1}", (byte)((i + 1) * 10));
            }
            _serverList.X = MuGame.Instance.Width / 2 - _serverList.DisplaySize.X / 2;
            _serverList.Y = MuGame.Instance.Height / 2 - _serverList.DisplaySize.Y / 2;
            _serverList.Visible = true;
            _nonEventGroup.UnselectServer();
        }


        private void OnGameServerResponseReceived(GameServerEntered response)
        {
            if (response.Success == true)
            {
                Console.WriteLine("Connected to the GameServer!");
            }
            else
            {
                Console.WriteLine("Error connecting to the GameServer!");
                _ = HandleDisconnection();
            }
        }

        private void OnLoginResponseReceived(LoginResponse response)
        {
            // Process login response and update UI accordingly
            if (response.Success == LoginResponse.LoginResult.Okay)
            {
                Console.WriteLine("Login successful - update UI accordingly.");
                MuGame.Instance.ChangeScene<SelectCharacterScene>();
            }
            else
            {
                Console.WriteLine("Login failed - show error message.");
            }
        }

        private void ServerList_ServerClick(object sender, ServerSelectEventArgs e)
        {
            // Hide server selection UI and show the login dialog
            _eventGroup.Visible = false;
            _nonEventGroup.Visible = false;
            _serverList.Visible = false;
            _loginDialog.Visible = true;
        }

        private async ValueTask HandleDisconnection()
        {
            // Display disconnection message and exit the game
            var dialog = MessageWindow.Show("You are disconnected from server");
            dialog.Closed += (s, e) => MuGame.Instance.Exit();
            await Task.CompletedTask;
        }
    }
}
