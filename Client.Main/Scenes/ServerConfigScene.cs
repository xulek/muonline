using System;
using System.Threading.Tasks;
using Client.Main.Controls.UI;
using Client.Main.Controls.UI.Common;
using Client.Main.Controls.UI.Login;
using Client.Main.Controls.UI.Game.Common;
using Microsoft.Xna.Framework;
using Microsoft.Extensions.Logging;
using Client.Main.Controllers;
using Client.Main.Models;
using Client.Main.Helpers;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Scenes
{
    /// <summary>
    /// Simple MonoGame scene to collect server host/port on Android before loading data.
    /// </summary>
    public class ServerConfigScene : BaseScene
    {
        private readonly ILogger _logger;
        private readonly ServerConfigDialog _dialog;
        private Texture2D _backgroundTexture;
        private bool _submitted;

        public ServerConfigScene()
        {
            _logger = MuGame.AppLoggerFactory?.CreateLogger<ServerConfigScene>();
            BackgroundColor = UiThemeManager.CurrentId == UiThemeId.Classic
                ? ModernHudTheme.BgDarkest : new Color(12, 12, 20);

            Controls.Add(_dialog = new ServerConfigDialog());
            _dialog.SubmitRequested += OnSubmit;
            UiThemeManager.ThemeChanged += ServerConfigScene_ThemeChanged;
        }

        protected override async Task LoadSceneContentWithProgress(Action<string, float> progressCallback)
        {
            progressCallback?.Invoke("Preparing background...", 0.2f);

            try
            {
                _backgroundTexture = MuGame.Instance.Content.Load<Texture2D>("Background");
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to load background texture. Using fallback color.");
            }

            progressCallback?.Invoke("Preparing configuration fields...", 0.5f);

            string defaultHost = MuGame.AppSettings?.ConnectServerHost ?? "127.0.0.1";
            int defaultPort = MuGame.AppSettings?.ConnectServerPort ?? 44405;
            _dialog.SetValues(defaultHost, defaultPort.ToString());

            progressCallback?.Invoke("UI ready", 1f);
            await base.Load();
        }

        public override void Update(GameTime gameTime)
        {
            var previousFocus = FocusControl;
            base.Update(gameTime);

            if (World == null && Status == GameControlStatus.Ready)
            {
                if (FocusControl != previousFocus)
                {
                    previousFocus?.OnBlur();
                    FocusControl?.OnFocus();
                }

                Cursor?.BringToFront();
                DebugPanel?.BringToFront();
            }
        }

        private void OnSubmit(object sender, EventArgs e)
        {
            _dialog.ClearError();

            var host = _dialog.Host?.Trim() ?? string.Empty;
            var portText = _dialog.PortText?.Trim();

            if (string.IsNullOrWhiteSpace(host))
            {
                _dialog.SetError("Please enter a valid host.");
                _dialog.FocusHost();
                return;
            }

            if (!int.TryParse(portText, out var port) || port <= 0 || port > 65535)
            {
                _dialog.SetError("Please enter a port between 1 and 65535.");
                _dialog.FocusPort();
                return;
            }

            if (_submitted) return; // prevent double-submit
            _submitted = true;

            _logger?.LogInformation("ServerConfigScene: host={Host}, port={Port}", host, port);

            MuGame.AppSettings.ConnectServerHost = host;
            MuGame.AppSettings.ConnectServerPort = port;
            MuGame.PersistConnectSettings(host, port);

            var network = MuGame.Network;
            if (network != null)
            {
                network.UpdateConnectServerSettings(host, port);
                _ = network.ForceReconnectToConnectServerAsync().ContinueWith(t =>
                {
                    if (t.Exception != null)
                    {
                        _logger?.LogWarning(t.Exception, "Failed to reconnect with updated server settings; continuing to LoginScene.");
                    }
                });
            }

            MuGame.Instance.ChangeScene(new LoginScene());
        }

        public override void Dispose()
        {
            UiThemeManager.ThemeChanged -= ServerConfigScene_ThemeChanged;
            if (_dialog != null)
            {
                _dialog.SubmitRequested -= OnSubmit;
            }
            base.Dispose();
        }

        private void ServerConfigScene_ThemeChanged(object sender, UiThemeChangedEventArgs e)
        {
            BackgroundColor = e.Current.Id == UiThemeId.Classic
                ? ModernHudTheme.BgDarkest : new Color(12, 12, 20);
        }

        public override void Draw(GameTime gameTime)
        {
            GraphicsDevice.Clear(BackgroundColor);

            using (new SpriteBatchScope(
                       GraphicsManager.Instance.Sprite,
                       SpriteSortMode.Deferred,
                       BlendState.AlphaBlend,
                       SamplerState.LinearClamp,
                       DepthStencilState.None,
                       null,
                       null,
                       UiScaler.SpriteTransform))
            {
                if (_backgroundTexture != null)
                {
                    GraphicsManager.Instance.Sprite.Draw(_backgroundTexture,
                        new Rectangle(0, 0, UiScaler.VirtualSize.X, UiScaler.VirtualSize.Y), Color.White);
                }
                else
                {
                    GraphicsManager.Instance.Sprite.Draw(GraphicsManager.Instance.Pixel,
                        new Rectangle(0, 0, UiScaler.VirtualSize.X, UiScaler.VirtualSize.Y), BackgroundColor);
                }
            }

            // Darken the background a bit to make the dialog pop.
            using (new SpriteBatchScope(
                       GraphicsManager.Instance.Sprite,
                       SpriteSortMode.Deferred,
                       BlendState.AlphaBlend,
                       SamplerState.PointClamp,
                       DepthStencilState.None,
                       null,
                       null,
                       UiScaler.SpriteTransform))
            {
                GraphicsManager.Instance.Sprite.Draw(GraphicsManager.Instance.Pixel,
                    new Rectangle(0, 0, UiScaler.VirtualSize.X, UiScaler.VirtualSize.Y),
                    new Color(0, 0, 0, 160));
            }

            using (new SpriteBatchScope(
                       GraphicsManager.Instance.Sprite,
                       SpriteSortMode.Deferred,
                       BlendState.AlphaBlend,
                       SamplerState.LinearClamp,
                       DepthStencilState.None,
                       null,
                       null,
                       UiScaler.SpriteTransform))
            {
                var controls = Controls.GetSnapshot();
                for (int i = 0; i < controls.Count; i++)
                {
                    var ctrl = controls[i];
                    if (ctrl == null || ctrl == World || !ctrl.Visible)
                    {
                        continue;
                    }

                    ctrl.Draw(gameTime);
                }
            }
        }
    }

    internal sealed class ServerConfigDialog : PopupFieldDialog
    {
        private readonly LabelControl _titleLabel;
        private readonly TextureControl _separator;
        private readonly LabelControl _hostLabel;
        private readonly TextFieldControl _hostInput;
        private readonly LabelControl _portLabel;
        private readonly TextFieldControl _portInput;
        private readonly LabelControl _errorLabel;
        private readonly OkButton _okButton;
        private readonly ButtonControl _classicOkButton;

        public string Host
        {
            get => _hostInput.Value;
            set => _hostInput.Value = value ?? string.Empty;
        }

        public string PortText
        {
            get => _portInput.Value;
            set => _portInput.Value = value ?? string.Empty;
        }

        public event EventHandler SubmitRequested;

        public ServerConfigDialog()
        {
            ControlSize = new Point(400, 220);
            Align = ControlAlign.HorizontalCenter | ControlAlign.VerticalCenter;

            Controls.Add(_titleLabel = new LabelControl
            {
                Text = "Connection Settings",
                Align = ControlAlign.HorizontalCenter,
                Y = 12,
                FontSize = 14f,
                TextColor = ModernHudTheme.TextGold
            });

            Controls.Add(_separator = new TextureControl
            {
                TexturePath = "Interface/GFx/popup_line_m.ozd",
                X = 12,
                Y = 40,
                AutoViewSize = false,
                ViewSize = new Point(ControlSize.X - 24, 6),
                Alpha = 0.9f
            });

            Controls.Add(_hostLabel = new LabelControl
            {
                Text = "Host",
                X = 26,
                Y = 72,
                AutoViewSize = false,
                ViewSize = new Point(70, 20),
                TextAlign = HorizontalAlign.Right,
                FontSize = 12f,
                TextColor = ModernHudTheme.TextGray
            });

            _hostInput = TextFieldControl.Create();
            _hostInput.X = 110;
            _hostInput.Y = 68;
            _hostInput.Skin = TextFieldSkin.NineSlice;
            _hostInput.ControlSize = new Point(240, 30);
            _hostInput.FontSize = 12f;
            _hostInput.Interactive = true;

            Controls.Add(_hostInput);

            Controls.Add(_portLabel = new LabelControl
            {
                Text = "Port",
                X = 26,
                Y = 108,
                AutoViewSize = false,
                ViewSize = new Point(70, 20),
                TextAlign = HorizontalAlign.Right,
                FontSize = 12f,
                TextColor = ModernHudTheme.TextGray
            });

            _portInput = TextFieldControl.Create();
            _portInput.X = 110;
            _portInput.Y = 104;
            _portInput.Skin = TextFieldSkin.NineSlice;
            _portInput.ControlSize = new Point(120, 30);
            _portInput.FontSize = 12f;
            _portInput.Interactive = true;

            Controls.Add(_portInput);

            _errorLabel = new LabelControl
            {
                Text = string.Empty,
                X = 26,
                Y = 140,
                AutoViewSize = false,
                ViewSize = new Point(ControlSize.X - 60, 20),
                FontSize = 11f,
                TextColor = ModernHudTheme.Danger,
                Visible = false
            };
            Controls.Add(_errorLabel);

            _okButton = new OkButton
            {
                Align = ControlAlign.HorizontalCenter,
                Y = 170
            };
            _okButton.Click += (_, _) => SubmitRequested?.Invoke(this, EventArgs.Empty);
            Controls.Add(_okButton);

            _classicOkButton = new ButtonControl
            {
                Text = "CONNECT",
                AutoViewSize = false,
                FontSize = 12f,
                BackgroundColor = ModernHudTheme.Success,
                HoverBackgroundColor = Color.Lerp(ModernHudTheme.Success, Color.White, 0.15f),
                PressedBackgroundColor = Color.Lerp(ModernHudTheme.Success, Color.Black, 0.15f),
                TextColor = ModernHudTheme.TextWhite,
                HoverTextColor = ModernHudTheme.TextWhite,
                BorderColor = ModernHudTheme.BorderInner,
                BorderThickness = 1,
                Interactive = true,
                Visible = false
            };
            _classicOkButton.Click += (_, _) => SubmitRequested?.Invoke(this, EventArgs.Empty);
            Controls.Add(_classicOkButton);

            _hostInput.Click += (_, _) => FocusHost();
            _portInput.Click += (_, _) => FocusPort();

            _hostInput.EnterKeyPressed += (_, _) => FocusPort();
            _portInput.EnterKeyPressed += (_, _) => SubmitRequested?.Invoke(this, EventArgs.Empty);

            ApplyThemeLayout();
        }

        protected override void OnThemeChanged(UiThemeChangedEventArgs e)
        {
            base.OnThemeChanged(e);
            ApplyThemeLayout();
        }

        private void ApplyThemeLayout()
        {
            bool classic = UiThemeManager.CurrentId == UiThemeId.Classic;
            ControlSize = classic ? new Point(420, 260) : new Point(400, 220);
            ViewSize = ControlSize;

            _titleLabel.TextColor = classic ? ModernHudTheme.TextWhite : new Color(241, 188, 37);
            _hostLabel.TextColor = classic ? ModernHudTheme.TextWhite : Color.LightGray;
            _portLabel.TextColor = classic ? ModernHudTheme.TextWhite : Color.LightGray;
            _errorLabel.TextColor = classic ? ModernHudTheme.Danger : Color.OrangeRed;
            _separator.Visible = !classic;

            if (classic)
            {
                _titleLabel.Y = 16;
                _separator.X = 14;
                _separator.Y = 46;
                _separator.ViewSize = new Point(ControlSize.X - 28, 1);

                _hostLabel.X = 30;
                _hostLabel.Y = 82;
                _portLabel.X = 30;
                _portLabel.Y = 126;
                _hostInput.X = 120;
                _hostInput.Y = 76;
                _hostInput.ViewSize = new Point(270, 32);
                _portInput.X = 120;
                _portInput.Y = 120;
                _portInput.ViewSize = new Point(150, 32);
                _hostInput.Skin = TextFieldSkin.Flat;
                _portInput.Skin = TextFieldSkin.Flat;
                _hostInput.BackgroundColor = ModernHudTheme.BgDarkest;
                _portInput.BackgroundColor = ModernHudTheme.BgDarkest;
                _hostInput.BorderColor = ModernHudTheme.BorderInner;
                _portInput.BorderColor = ModernHudTheme.BorderInner;
                _hostInput.TextColor = ModernHudTheme.TextWhite;
                _portInput.TextColor = ModernHudTheme.TextWhite;

                _errorLabel.X = 30;
                _errorLabel.Y = 164;
                _errorLabel.ViewSize = new Point(ControlSize.X - 60, 20);
                _okButton.Visible = false;
                _classicOkButton.Visible = true;
                _classicOkButton.X = 135;
                _classicOkButton.Y = 204;
                _classicOkButton.ViewSize = new Point(150, 36);
                _classicOkButton.BackgroundColor = ModernHudTheme.Success;
                _classicOkButton.HoverBackgroundColor = Color.Lerp(ModernHudTheme.Success, Color.White, 0.15f);
                _classicOkButton.PressedBackgroundColor = Color.Lerp(ModernHudTheme.Success, Color.Black, 0.15f);
                _classicOkButton.BorderColor = ModernHudTheme.BorderInner;
            }
            else
            {
                _titleLabel.Y = 12;
                _separator.X = 12;
                _separator.Y = 40;
                _separator.ViewSize = new Point(ControlSize.X - 24, 6);

                _hostLabel.X = 26;
                _hostLabel.Y = 72;
                _portLabel.X = 26;
                _portLabel.Y = 108;
                _hostInput.X = 110;
                _hostInput.Y = 68;
                _hostInput.ControlSize = new Point(240, 30);
                _portInput.X = 110;
                _portInput.Y = 104;
                _portInput.ControlSize = new Point(120, 30);
                _hostInput.Skin = TextFieldSkin.NineSlice;
                _portInput.Skin = TextFieldSkin.NineSlice;
                _hostInput.TextColor = Color.White;
                _portInput.TextColor = Color.White;
                _errorLabel.X = 26;
                _errorLabel.Y = 140;
                _errorLabel.ViewSize = new Point(ControlSize.X - 60, 20);
                _okButton.Visible = true;
                _okButton.Y = 170;
                _classicOkButton.Visible = false;
            }
        }

        public void SetValues(string host, string port)
        {
            Host = host;
            PortText = port;
        }

        public void SetError(string message)
        {
            _errorLabel.Text = message ?? string.Empty;
            _errorLabel.Visible = !string.IsNullOrWhiteSpace(message);
        }

        public void ClearError() => SetError(string.Empty);

        public void FocusHost()
        {
            Scene?.FocusControlIfInteractive(_hostInput);
            _hostInput.OnFocus();
            _portInput.OnBlur();
        }

        public void FocusPort()
        {
            Scene?.FocusControlIfInteractive(_portInput);
            _portInput.OnFocus();
            _hostInput.OnBlur();
        }
    }
}
