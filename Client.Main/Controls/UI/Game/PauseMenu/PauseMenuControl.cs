using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Client.Main.Controls.UI.Common;
using System.Threading.Tasks;
using Client.Main.Scenes;
using Client.Main.Networking;
using Client.Main.Networking.Services;
using Microsoft.Extensions.Logging;
using Client.Main.Core.Client; // ClientConnectionState
using Client.Main;
using Client.Main.Controllers;
using Client.Main.Controls.UI.Game;
using Client.Main.Controls.UI.Game.Common;
using Client.Main.Graphics;
using Client.Main.Helpers;
using Client.Main.Models;
using MUnique.OpenMU.Network.Packets; // LogOutType
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Controls.UI.Game.PauseMenu
{
    public class PauseMenuControl : UIControl
    {
        private readonly ILogger _logger = MuGame.AppLoggerFactory?.CreateLogger<PauseMenuControl>();
        private EventHandler<System.Collections.Generic.List<(string Name, MUnique.OpenMU.Network.Packets.CharacterClassNumber Class, ushort Level, byte[] Appearance)>> _characterListHandler;
        private EventHandler<LogOutType> _logoutResponseHandler;
        private class PausePanelControl : UIControl
        {
            public int HeaderHeight { get; set; } = 96;
            public int ContentTop { get; set; } = 0;
            public bool DrawContentSurface { get; set; }

            public PausePanelControl()
            {
                BackgroundColor = Color.Transparent;
                BorderColor = Color.Transparent;
                BorderThickness = 0;
            }

            public override void Draw(GameTime gameTime)
            {
                if (Status != GameControlStatus.Ready || !Visible)
                    return;

                var sprite = GraphicsManager.Instance.Sprite;
                var pixel = GraphicsManager.Instance.Pixel;
                var rect = DisplayRectangle;
                if (pixel == null)
                {
                    base.Draw(gameTime);
                    return;
                }

                sprite.Draw(pixel, new Rectangle(rect.X + 9, rect.Y + 12, rect.Width, rect.Height), new Color(0, 0, 0, 105));
                sprite.Draw(pixel, new Rectangle(rect.X + 4, rect.Y + 6, rect.Width, rect.Height), new Color(0, 0, 0, 70));

                bool season6 = UiThemeManager.CurrentId == UiThemeId.Season6;
                Color panelTop = season6 ? ModernHudTheme.BgMid : new Color(29, 35, 46, 250);
                Color panelBottom = season6 ? ModernHudTheme.BgDarkest : new Color(8, 11, 17, 252);
                Color headerTop = season6 ? ModernHudTheme.BgLighter : new Color(50, 59, 73, 238);
                Color headerBottom = season6 ? ModernHudTheme.BgMid : new Color(24, 30, 40, 222);

                UiDrawHelper.DrawVerticalGradient(
                    sprite,
                    rect,
                    panelTop,
                    panelBottom,
                    20);

                var headerRect = new Rectangle(rect.X + 1, rect.Y + 1, rect.Width - 2, Math.Min(HeaderHeight, rect.Height - 2));
                UiDrawHelper.DrawVerticalGradient(
                    sprite,
                    headerRect,
                    headerTop,
                    headerBottom,
                    12);

                UiDrawHelper.DrawHorizontalGradient(
                    sprite,
                    new Rectangle(rect.X + 24, rect.Y + HeaderHeight - 2, rect.Width - 48, 2),
                    Color.Transparent,
                    ModernHudTheme.AccentBright,
                    16);
                UiDrawHelper.DrawHorizontalGradient(
                    sprite,
                    new Rectangle(rect.Center.X, rect.Y + HeaderHeight - 2, Math.Max(1, rect.Right - 24 - rect.Center.X), 2),
                    ModernHudTheme.AccentBright,
                    Color.Transparent,
                    16);

                if (DrawContentSurface && ContentTop > 0 && ContentTop < rect.Height - 30)
                {
                    var contentRect = new Rectangle(
                        rect.X + 18,
                        rect.Y + ContentTop,
                        rect.Width - 36,
                        rect.Height - ContentTop - 18);
                    sprite.Draw(pixel, contentRect, season6 ? ModernHudTheme.BgDarkest * 0.7f : new Color(5, 8, 13, 118));
                    UiDrawHelper.DrawBorder(sprite, contentRect, season6 ? ModernHudTheme.BorderInner * 0.7f : new Color(91, 104, 124, 72));
                }

                UiDrawHelper.DrawBorder(sprite, rect, ModernHudTheme.BorderOuter, 2);
                UiDrawHelper.DrawBorder(sprite, new Rectangle(rect.X + 2, rect.Y + 2, rect.Width - 4, rect.Height - 4), ModernHudTheme.BorderInner);
                UiDrawHelper.DrawCornerAccents(sprite, rect, ModernHudTheme.Accent, 18, 2);

                base.Draw(gameTime);
            }
        }

        private sealed class PauseMenuButtonControl : ButtonControl
        {
            public string Subtitle { get; set; }
            public Color AccentColor { get; set; } = ModernHudTheme.Accent;
            public bool IsDanger { get; set; }
            public bool Compact { get; set; }

            public PauseMenuButtonControl()
            {
                BackgroundColor = Color.Transparent;
                HoverBackgroundColor = Color.Transparent;
                PressedBackgroundColor = Color.Transparent;
                TextColor = ModernHudTheme.TextWhite;
                HoverTextColor = ModernHudTheme.TextWhite;
            }

            public override void Draw(GameTime gameTime)
            {
                if (Status != GameControlStatus.Ready || !Visible)
                    return;

                var sprite = GraphicsManager.Instance.Sprite;
                var pixel = GraphicsManager.Instance.Pixel;
                var font = GraphicsManager.Instance.Font;
                if (pixel == null || font == null)
                    return;

                var rect = DisplayRectangle;
                Color accent = IsDanger ? ModernHudTheme.Danger : AccentColor;
                Color top;
                Color bottom;
                bool season6 = UiThemeManager.CurrentId == UiThemeId.Season6;

                if (!Enabled)
                {
                    top = season6 ? ModernHudTheme.BgMid : new Color(25, 29, 36, 205);
                    bottom = season6 ? ModernHudTheme.BgDarkest : new Color(13, 16, 21, 215);
                    accent = ModernHudTheme.TextDark;
                }
                else if (IsMousePressed)
                {
                    top = season6 ? ModernHudTheme.BgLight : new Color(20, 25, 33, 252);
                    bottom = season6 ? ModernHudTheme.BgDark : new Color(8, 11, 16, 252);
                }
                else if (IsMouseOver)
                {
                    top = season6
                        ? (IsDanger ? ModernHudTheme.Danger * 0.65f : ModernHudTheme.BgLighter)
                        : (IsDanger ? new Color(83, 38, 41, 248) : new Color(52, 61, 76, 248));
                    bottom = season6
                        ? (IsDanger ? ModernHudTheme.Danger * 0.35f : ModernHudTheme.BgMid)
                        : (IsDanger ? new Color(42, 20, 24, 250) : new Color(20, 27, 37, 250));
                }
                else
                {
                    top = season6 ? ModernHudTheme.BgLight : new Color(37, 44, 56, 238);
                    bottom = season6 ? ModernHudTheme.BgDark : new Color(16, 21, 29, 244);
                }

                sprite.Draw(pixel, new Rectangle(rect.X + 3, rect.Y + 4, rect.Width, rect.Height), new Color(0, 0, 0, 76));
                UiDrawHelper.DrawVerticalGradient(sprite, rect, top, bottom, 12);

                if (IsMouseOver && Enabled)
                {
                    sprite.Draw(pixel, new Rectangle(rect.X, rect.Y, rect.Width, rect.Height), new Color(accent.R, accent.G, accent.B, (byte)22));
                    sprite.Draw(pixel, new Rectangle(rect.X, rect.Y, 5, rect.Height), accent);
                }
                else
                {
                    sprite.Draw(pixel, new Rectangle(rect.X, rect.Y, 3, rect.Height), new Color(accent.R, accent.G, accent.B, (byte)(Enabled ? 185 : 80)));
                }

                UiDrawHelper.DrawBorder(sprite, rect,
                    IsMouseOver && Enabled
                        ? new Color(accent.R, accent.G, accent.B, (byte)180)
                        : season6 ? ModernHudTheme.BorderInner : new Color(91, 104, 124, 115));
                sprite.Draw(pixel, new Rectangle(rect.X + 10, rect.Bottom - 1, rect.Width - 20, 1), new Color(255, 255, 255, 18));

                float titleScale = (Compact ? 11.5f : 14f) / Constants.BASE_FONT_SIZE;
                float subtitleScale = 9.5f / Constants.BASE_FONT_SIZE;
                Color titleColor = Enabled ? ModernHudTheme.TextWhite : ModernHudTheme.TextDark;
                Color subtitleColor = Enabled ? ModernHudTheme.TextGray : ModernHudTheme.TextDark;

                if (Compact)
                {
                    Vector2 titleSize = font.MeasureString(Text ?? string.Empty) * titleScale;
                    var titlePos = new Vector2(
                        rect.X + (rect.Width - titleSize.X) * 0.5f,
                        rect.Y + (rect.Height - titleSize.Y) * 0.5f);
                    sprite.DrawString(font, Text ?? string.Empty, titlePos + Vector2.One, Color.Black * 0.7f, 0f, Vector2.Zero, titleScale, SpriteEffects.None, 0f);
                    sprite.DrawString(font, Text ?? string.Empty, titlePos, titleColor * Alpha, 0f, Vector2.Zero, titleScale, SpriteEffects.None, 0f);
                    return;
                }

                var titlePosition = new Vector2(rect.X + 18, rect.Y + 9);
                sprite.DrawString(font, Text ?? string.Empty, titlePosition + Vector2.One, Color.Black * 0.75f, 0f, Vector2.Zero, titleScale, SpriteEffects.None, 0f);
                sprite.DrawString(font, Text ?? string.Empty, titlePosition, titleColor * Alpha, 0f, Vector2.Zero, titleScale, SpriteEffects.None, 0f);

                if (!string.IsNullOrWhiteSpace(Subtitle))
                {
                    sprite.DrawString(font, Subtitle, new Vector2(rect.X + 18, rect.Y + 31), subtitleColor * Alpha, 0f, Vector2.Zero, subtitleScale, SpriteEffects.None, 0f);
                }

                string arrow = ">";
                float arrowScale = 13f / Constants.BASE_FONT_SIZE;
                Vector2 arrowSize = font.MeasureString(arrow) * arrowScale;
                Vector2 arrowPosition = new(rect.Right - 20 - arrowSize.X, rect.Y + (rect.Height - arrowSize.Y) * 0.5f);
                sprite.DrawString(font, arrow, arrowPosition, new Color(accent.R, accent.G, accent.B, (byte)(IsMouseOver ? 255 : 150)) * Alpha, 0f, Vector2.Zero, arrowScale, SpriteEffects.None, 0f);
            }
        }

        private sealed class MenuTabButtonControl : ButtonControl
        {
            public bool Active { get; set; }

            public MenuTabButtonControl()
            {
                BackgroundColor = Color.Transparent;
                HoverBackgroundColor = Color.Transparent;
                PressedBackgroundColor = Color.Transparent;
            }

            public override void Draw(GameTime gameTime)
            {
                if (Status != GameControlStatus.Ready || !Visible)
                    return;

                var sprite = GraphicsManager.Instance.Sprite;
                var pixel = GraphicsManager.Instance.Pixel;
                var font = GraphicsManager.Instance.Font;
                if (pixel == null || font == null)
                    return;

                var rect = DisplayRectangle;
                Color fill = Active
                    ? UiThemeManager.CurrentId == UiThemeId.Season6 ? ModernHudTheme.SlotSelected : new Color(64, 55, 34, 225)
                    : IsMouseOver
                        ? UiThemeManager.CurrentId == UiThemeId.Season6 ? ModernHudTheme.SlotHover : new Color(46, 55, 69, 225)
                        : UiThemeManager.CurrentId == UiThemeId.Season6 ? ModernHudTheme.BgDark : new Color(20, 26, 35, 210);
                sprite.Draw(pixel, rect, fill);
                UiDrawHelper.DrawBorder(sprite, rect,
                    Active ? new Color(ModernHudTheme.Accent.R, ModernHudTheme.Accent.G, ModernHudTheme.Accent.B, (byte)190)
                        : UiThemeManager.CurrentId == UiThemeId.Season6 ? ModernHudTheme.BorderInner : new Color(91, 104, 124, 95));

                if (Active)
                    sprite.Draw(pixel, new Rectangle(rect.X + 8, rect.Bottom - 2, rect.Width - 16, 2), ModernHudTheme.AccentBright);

                float scale = 10.5f / Constants.BASE_FONT_SIZE;
                string label = Text ?? string.Empty;
                Vector2 size = font.MeasureString(label) * scale;
                Vector2 position = new(rect.X + (rect.Width - size.X) * 0.5f, rect.Y + (rect.Height - size.Y) * 0.5f);
                Color color = Active ? ModernHudTheme.TextGold : ModernHudTheme.TextGray;
                sprite.DrawString(font, label, position, color * Alpha, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
            }
        }

        private PausePanelControl _panel;
        private LabelControl _titleLabel;
        private LabelControl _subtitleLabel;
        private LabelControl _footerLabel;
        private ButtonControl _btnCharacterSelect;
        private ButtonControl _btnServerSelect;
        private ButtonControl _btnOptions;
        private ButtonControl _btnExit;
        private ButtonControl _btnResume;
        private bool _returnInProgress;
        private bool _exitInProgress;
        private OptionsPanelControl _optionsPanel;

        public event EventHandler ResumeClicked;
        public event EventHandler CharacterSelectClicked;
        public event EventHandler ServerSelectClicked;
        public event EventHandler OptionsClicked;
        public event EventHandler ExitClicked;

        public PauseMenuControl()
        {
            Visible = false;
            Interactive = true;
            AutoViewSize = false;
            ViewSize = new Point(UiScaler.VirtualSize.X, UiScaler.VirtualSize.Y);
            ControlSize = ViewSize;
            BackgroundColor = Color.Transparent;

            _panel = new PausePanelControl
            {
                AutoViewSize = false,
                ControlSize = new Point(430, 500),
                ViewSize = new Point(430, 500),
                Align = Models.ControlAlign.HorizontalCenter | Models.ControlAlign.VerticalCenter,
                HeaderHeight = 98,
                Interactive = true
            };
            Controls.Add(_panel);

            _titleLabel = new LabelControl
            {
                Text = "PAUSE MENU",
                FontSize = 23f,
                TextColor = ModernHudTheme.TextGold,
                IsBold = true,
                X = 0,
                Y = 22,
                Align = Models.ControlAlign.HorizontalCenter
            };
            _panel.Controls.Add(_titleLabel);

            _subtitleLabel = new LabelControl
            {
                Text = "Take a breath. Your adventure is waiting.",
                FontSize = 10.5f,
                TextColor = ModernHudTheme.TextGray,
                HasShadow = false,
                X = 0,
                Y = 58,
                Align = Models.ControlAlign.HorizontalCenter
            };
            _panel.Controls.Add(_subtitleLabel);

            int btnWidth = 342;
            int btnHeight = 56;
            int x = (_panel.ViewSize.X - btnWidth) / 2;
            int y = 111;
            int spacing = 10;

            _btnResume = CreateButton("Continue", "Return to the game", x, y, btnWidth, btnHeight, ModernHudTheme.AccentBright);
            _btnResume.Click += (s, e) =>
            {
                ResumeClicked?.Invoke(this, EventArgs.Empty);
                Visible = false;
                _panel.Visible = true;
                if (_optionsPanel != null)
                    _optionsPanel.Visible = false;
            };
            _panel.Controls.Add(_btnResume);
            y += btnHeight + spacing;

            _btnCharacterSelect = CreateButton("Character Select", "Leave the world and choose another hero", x, y, btnWidth, btnHeight, ModernHudTheme.SecondaryBright);
            _btnCharacterSelect.Click += async (s, e) =>
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
            };
            _panel.Controls.Add(_btnCharacterSelect);
            y += btnHeight + spacing;

            _btnServerSelect = CreateButton("Server Select", "Disconnect and return to the server list", x, y, btnWidth, btnHeight, ModernHudTheme.Secondary);
            _btnServerSelect.Click += async (s, e) =>
            {
                ServerSelectClicked?.Invoke(this, EventArgs.Empty);
                await HandleReturnToServerSelectAsync();
            };
            _panel.Controls.Add(_btnServerSelect);
            y += btnHeight + spacing;

            _btnOptions = CreateButton("Settings", "Graphics, audio and performance options", x, y, btnWidth, btnHeight, new Color(150, 118, 210));
            _btnOptions.Click += (s, e) =>
            {
                OptionsClicked?.Invoke(this, EventArgs.Empty);
                ToggleOptionsPanel();
            };
            _panel.Controls.Add(_btnOptions);
            y += btnHeight + spacing;

            _btnExit = CreateButton("Exit Game", "Close the client", x, y, btnWidth, btnHeight, ModernHudTheme.Danger, isDanger: true);
            _btnExit.Click += async (s, e) =>
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
            };
            _panel.Controls.Add(_btnExit);

            _footerLabel = new LabelControl
            {
                Text = "ESC  ·  close menu",
                FontSize = 9.5f,
                TextColor = ModernHudTheme.TextDark,
                HasShadow = false,
                X = 0,
                Y = 468,
                Align = Models.ControlAlign.HorizontalCenter
            };
            _panel.Controls.Add(_footerLabel);
            ApplyThemeLayout();
        }

        protected override void OnThemeChanged(UiThemeChangedEventArgs e)
        {
            base.OnThemeChanged(e);
            ApplyThemeLayout();
        }

        private void ApplyThemeLayout()
        {
            bool season6 = UiThemeManager.CurrentId == UiThemeId.Season6;
            Point panelSize = UiThemeManager.Current.Metrics.ModalWindowSize;
            _panel.ControlSize = panelSize;
            _panel.ViewSize = panelSize;
            _panel.HeaderHeight = season6 ? 88 : 98;

            int buttonWidth = season6 ? 332 : 342;
            int buttonHeight = season6 ? 58 : 56;
            int x = (panelSize.X - buttonWidth) / 2;
            int y = season6 ? 102 : 111;
            int spacing = season6 ? 9 : 10;

            _titleLabel.Y = season6 ? 18 : 22;
            _subtitleLabel.Y = season6 ? 52 : 58;
            _footerLabel.Y = panelSize.Y - (season6 ? 31 : 32);

            PositionPauseButton(_btnResume, x, y, buttonWidth, buttonHeight);
            y += buttonHeight + spacing;
            PositionPauseButton(_btnCharacterSelect, x, y, buttonWidth, buttonHeight);
            y += buttonHeight + spacing;
            PositionPauseButton(_btnServerSelect, x, y, buttonWidth, buttonHeight);
            y += buttonHeight + spacing;
            PositionPauseButton(_btnOptions, x, y, buttonWidth, buttonHeight);
            y += buttonHeight + spacing;
            PositionPauseButton(_btnExit, x, y, buttonWidth, buttonHeight);

            if (_btnOptions is PauseMenuButtonControl optionsButton)
                optionsButton.AccentColor = ModernHudTheme.Secondary;

            _optionsPanel?.ApplyThemeLayout();
            MarkLayoutDirty();
        }

        private static void PositionPauseButton(ButtonControl button, int x, int y, int width, int height)
        {
            button.X = x;
            button.Y = y;
            button.ControlSize = new Point(width, height);
            button.ViewSize = new Point(width, height);
        }

        public override void Draw(GameTime gameTime)
        {
            if (Status != GameControlStatus.Ready || !Visible)
                return;

            var sprite = GraphicsManager.Instance.Sprite;
            var rect = DisplayRectangle;
            bool season6 = UiThemeManager.CurrentId == UiThemeId.Season6;
            UiDrawHelper.DrawVerticalGradient(sprite, rect,
                season6 ? ModernHudTheme.BgDark * 0.82f : new Color(6, 8, 13, 205),
                season6 ? ModernHudTheme.BgDarkest * 0.94f : new Color(0, 0, 0, 238), 20);

            base.Draw(gameTime);
        }

        private static ButtonControl CreateButton(string text, string subtitle, int x, int y, int width, int height, Color accent, bool isDanger = false)
        {
            return new PauseMenuButtonControl
            {
                Text = text,
                Subtitle = subtitle,
                AccentColor = accent,
                IsDanger = isDanger,
                X = x,
                Y = y,
                ControlSize = new Point(width, height),
                ViewSize = new Point(width, height),
                AutoViewSize = false,
                FontSize = 14f,
                TextColor = ModernHudTheme.TextWhite
            };
        }

        private void ToggleOptionsPanel()
        {
            if (_optionsPanel == null)
            {
                _optionsPanel = new OptionsPanelControl(this)
                {
                    Visible = false
                };
                Controls.Add(_optionsPanel);
                _optionsPanel.BringToFront();
            }

            bool show = !_optionsPanel.Visible;
            _optionsPanel.Visible = show;
            _panel.Visible = !show;

            if (show)
            {
                _optionsPanel.Refresh();
                _optionsPanel.BringToFront();
            }
        }

        // --- Internal handlers (network-aware) ---
        private async Task HandleReturnToCharacterSelectAsync()
        {
            try
            {
                Visible = false;
                if (_optionsPanel != null)
                {
                    _optionsPanel.Visible = false;
                }
                _panel.Visible = true;

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
                void CharacterListHandler(object sender, System.Collections.Generic.List<(string Name, MUnique.OpenMU.Network.Packets.CharacterClassNumber Class, ushort Level, byte[] Appearance)> list)
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
                if (_optionsPanel != null)
                {
                    _optionsPanel.Visible = false;
                }
                _panel.Visible = true;

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
                if (_optionsPanel != null)
                {
                    _optionsPanel.Visible = false;
                }
                _panel.Visible = true;

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

        private void ApplyBackgroundMusicSetting(bool enabled)
        {
            if (!enabled)
            {
                SoundController.Instance.StopBackgroundMusic();
                return;
            }

            var scene = MuGame.Instance?.ActiveScene as BaseScene;
            var music = scene?.World?.BackgroundMusicPath;
            if (!string.IsNullOrEmpty(music))
            {
                SoundController.Instance.PlayBackgroundMusic(music);
                SoundController.Instance.ApplyBackgroundMusicVolume();
            }
        }

        private void ApplyGraphicsSettings()
        {
            MuGame.ScheduleOnMainThread(() => MuGame.Instance?.ApplyGraphicsOptions());
        }

        private void ApplyQualityPreset(GraphicsQualityPreset preset, Action onComplete = null)
        {
            // Radio-button refreshes can invoke the selected option again. Reapplying the
            // same preset needlessly resets the graphics device and can present a black frame.
            if (GraphicsQualityManager.UserPreset == preset)
            {
                onComplete?.Invoke();
                return;
            }

            MuGame.ScheduleOnMainThread(() =>
            {
                var adapter = GraphicsManager.Instance?.GraphicsDevice?.Adapter ?? GraphicsAdapter.DefaultAdapter;
                GraphicsQualityManager.ApplyPreset(preset, adapter, _logger);
                MuGame.Instance?.ApplyGraphicsOptions();
                GraphicsManager.Instance?.UpdateRenderScale();
                onComplete?.Invoke();
            });

            if (MuGame.AppSettings?.Graphics != null)
            {
                MuGame.AppSettings.Graphics.QualityPreset = preset.ToString();
            }
            MuGame.PersistGraphicsPreset(preset);
        }

        private void SetVSync(bool enabled)
        {
            Constants.DISABLE_VSYNC = !enabled;
            if (enabled)
                Constants.UNLIMITED_FPS = false;
            ApplyGraphicsSettings();
        }

        private void SetUnlimitedFps(bool enabled)
        {
            Constants.UNLIMITED_FPS = enabled;
            if (enabled)
                Constants.DISABLE_VSYNC = true;
            ApplyGraphicsSettings();
        }

        private void ApplyBackgroundMusicVolume()
        {
            if (!Constants.BACKGROUND_MUSIC)
            {
                return;
            }
            SoundController.Instance.ApplyBackgroundMusicVolume();
        }

        private void ApplySoundEffectsVolume()
        {
            SoundController.Instance.ApplySoundEffectsVolume();
        }

        private void ApplyDebugPanelSetting()
        {
            if (MuGame.Instance?.ActiveScene is BaseScene scene && scene.DebugPanel != null)
            {
                scene.DebugPanel.Visible = Constants.SHOW_DEBUG_PANEL;
                if (Constants.SHOW_DEBUG_PANEL)
                {
                    scene.DebugPanel.BringToFront();
                }
            }
        }

        public override void Update(GameTime time)
        {
            base.Update(time);

            if (!Visible)
            {
                return;
            }

            if (_optionsPanel == null || !_optionsPanel.Visible)
            {
                if (_panel != null)
                {
                    _panel.Visible = true;
                }
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

        private sealed class OptionsPanelControl : PausePanelControl
        {
            private readonly PauseMenuControl _owner;
            private readonly List<IOptionRow> _options = new();
            private readonly List<GameControl> _dynamicControls = new();
            private const int ContentStartY = 228;
            private const int ContentPaddingX = 30;
            private const int OptionRowHeight = 30;
            private readonly ButtonControl _closeButton;
            private readonly int _panelWidth;
            private MenuTabButtonControl _activeCategoryButton;

            public OptionsPanelControl(PauseMenuControl owner)
            {
                _owner = owner;
                AutoViewSize = false;
                ControlSize = new Point(560, 700);
                ViewSize = ControlSize;
                Align = Models.ControlAlign.HorizontalCenter | Models.ControlAlign.VerticalCenter;
                Interactive = true;
                HeaderHeight = 220;
                ContentTop = 220;
                DrawContentSurface = true;
                _panelWidth = ControlSize.X;

                var title = new LabelControl
                {
                    Text = "SETTINGS",
                    FontSize = 22f,
                    TextColor = ModernHudTheme.TextGold,
                    IsBold = true,
                    Align = Models.ControlAlign.HorizontalCenter,
                    X = 0,
                    Y = 18
                };
                Controls.Add(title);

                var subtitle = new LabelControl
                {
                    Text = "Tune the client without leaving the game",
                    FontSize = 10f,
                    TextColor = ModernHudTheme.TextGray,
                    HasShadow = false,
                    Align = Models.ControlAlign.HorizontalCenter,
                    X = 0,
                    Y = 50
                };
                Controls.Add(subtitle);

                int categoryStartY = 78;
                int categoryX = 20;
                int categoryWidth = 166;
                int categoryHeight = 28;
                int categorySpacing = 7;
                int categoriesPerRow = 3;
                int categoryIndex = 0;

                AddCategoryButton("Audio", () => BuildAudioCategory(), categoryStartY,
                    ref categoryX, categoryWidth, categoryHeight, categorySpacing, categoriesPerRow, ref categoryIndex);
                AddCategoryButton("Display", () => BuildDisplayCategory(), categoryStartY,
                    ref categoryX, categoryWidth, categoryHeight, categorySpacing, categoriesPerRow, ref categoryIndex);
                AddCategoryButton("Quality Preset", () => BuildQualityPresetCategory(), categoryStartY,
                    ref categoryX, categoryWidth, categoryHeight, categorySpacing, categoriesPerRow, ref categoryIndex);
                AddCategoryButton("World & Visibility", () => BuildWorldCategory(), categoryStartY,
                    ref categoryX, categoryWidth, categoryHeight, categorySpacing, categoriesPerRow, ref categoryIndex);
                AddCategoryButton("Render Scale", () => BuildRenderScaleCategory(), categoryStartY,
                    ref categoryX, categoryWidth, categoryHeight, categorySpacing, categoriesPerRow, ref categoryIndex);
                AddCategoryButton("Graphics", () => BuildGraphicsCategory(), categoryStartY,
                    ref categoryX, categoryWidth, categoryHeight, categorySpacing, categoriesPerRow, ref categoryIndex);
                AddCategoryButton("Lighting", () => BuildLightingCategory(), categoryStartY,
                    ref categoryX, categoryWidth, categoryHeight, categorySpacing, categoriesPerRow, ref categoryIndex);
                AddCategoryButton("Shadow Quality", () => BuildShadowQualityCategory(), categoryStartY,
                    ref categoryX, categoryWidth, categoryHeight, categorySpacing, categoriesPerRow, ref categoryIndex);
                AddCategoryButton("Performance", () => BuildPerformanceCategory(), categoryStartY,
                    ref categoryX, categoryWidth, categoryHeight, categorySpacing, categoriesPerRow, ref categoryIndex);
                AddCategoryButton("Interface", () => BuildInterfaceCategory(), categoryStartY,
                    ref categoryX, categoryWidth, categoryHeight, categorySpacing, categoriesPerRow, ref categoryIndex);

                _closeButton = new PauseMenuButtonControl
                {
                    Text = "Back to Pause Menu",
                    Subtitle = string.Empty,
                    Compact = true,
                    AccentColor = ModernHudTheme.Accent,
                    ControlSize = new Point(190, 38),
                    ViewSize = new Point(190, 38),
                    X = (ControlSize.X - 190) / 2,
                    Y = ContentStartY,
                    AutoViewSize = false,
                    FontSize = 12f,
                    TextColor = ModernHudTheme.TextWhite
                };
                _closeButton.Click += (s, e) => _owner.ToggleOptionsPanel();
                Controls.Add(_closeButton);

                BuildAudioCategory(); // default category
            }

            protected override void OnThemeChanged(UiThemeChangedEventArgs e)
            {
                base.OnThemeChanged(e);
                ApplyThemeLayout();
            }

            public void ApplyThemeLayout()
            {
                HeaderHeight = 220;
                ContentTop = 220;
                _closeButton.X = (ControlSize.X - _closeButton.ViewSize.X) / 2;
                _closeButton.Y = ContentTop;
                _closeButton.BringToFront();
                MarkLayoutDirty();
            }

            private void BuildInterfaceCategory()
            {
                BuildCategory("Interface", (ref int currentY) =>
                {
                    AddOption(
                        "Classic interface",
                        () => UiThemeManager.CurrentId == UiThemeId.Classic,
                        value => UiThemeManager.SetTheme(value ? UiThemeId.Classic : UiThemeId.Modern),
                        ref currentY,
                        OptionRowHeight);

                    AddHeading("Modern is the original interface. Classic uses the alternate HUD, windows and assets.", ref currentY);
                });
            }

            private delegate void CategoryBuilder(ref int currentY);

            private void ClearDynamicControls()
            {
                foreach (var ctrl in _dynamicControls)
                {
                    Controls.Remove(ctrl);
                }
                _dynamicControls.Clear();
                _options.Clear();
            }

            private void BuildCategory(string categoryName, CategoryBuilder builder)
            {
                ClearDynamicControls();

                int currentY = ContentStartY;
                AddHeading(categoryName, ref currentY);
                builder(ref currentY);

                _closeButton.Y = currentY + 10;
                _closeButton.BringToFront();
            }

            private void BuildAudioCategory()
            {
                BuildCategory("Audio", (ref int currentY) =>
                {
                    AddOption("Background Music", () => Constants.BACKGROUND_MUSIC, value =>
                    {
                        Constants.BACKGROUND_MUSIC = value;
                        _owner.ApplyBackgroundMusicSetting(value);
                    }, ref currentY, OptionRowHeight);

                    AddOption("Sound Effects", () => Constants.SOUND_EFFECTS, value =>
                    {
                        Constants.SOUND_EFFECTS = value;
                        _owner.ApplySoundEffectsVolume();
                    }, ref currentY, OptionRowHeight);
                    AddVolumeControl("Music Volume", () => Constants.BACKGROUND_MUSIC_VOLUME, value =>
                    {
                        Constants.BACKGROUND_MUSIC_VOLUME = value;
                        _owner.ApplyBackgroundMusicVolume();
                    }, ref currentY, OptionRowHeight);
                    AddVolumeControl("Effects Volume", () => Constants.SOUND_EFFECTS_VOLUME, value =>
                    {
                        Constants.SOUND_EFFECTS_VOLUME = value;
                        _owner.ApplySoundEffectsVolume();
                    }, ref currentY, OptionRowHeight);
                });
            }

            private void BuildWorldCategory()
            {
                BuildCategory("World & Visibility", (ref int currentY) =>
                {
                    AddOption("Draw Bounding Boxes", () => Constants.DRAW_BOUNDING_BOXES, value => Constants.DRAW_BOUNDING_BOXES = value, ref currentY, OptionRowHeight);
                    AddOption("Draw Bounding Boxes (Interactives)", () => Constants.DRAW_BOUNDING_BOXES_INTERACTIVES, value => Constants.DRAW_BOUNDING_BOXES_INTERACTIVES = value, ref currentY, OptionRowHeight);
                    AddOption("Draw Grass", () => Constants.DRAW_GRASS, value =>
                    {
                        Constants.DRAW_GRASS = value;
                        if (value)
                        {
                            // When enabling grass, ensure textures are loaded
                            var scene = MuGame.Instance?.ActiveScene as BaseScene;
                            scene?.World?.Terrain?.ReloadGrassIfNeeded();
                        }
                    }, ref currentY, OptionRowHeight);
                    AddOption("Low Quality Switch", () => Constants.ENABLE_LOW_QUALITY_SWITCH, value => Constants.ENABLE_LOW_QUALITY_SWITCH = value, ref currentY, OptionRowHeight);
                    AddOption("Low Quality in Login", () => Constants.ENABLE_LOW_QUALITY_IN_LOGIN_SCENE, value => Constants.ENABLE_LOW_QUALITY_IN_LOGIN_SCENE = value, ref currentY, OptionRowHeight);
                });
            }

            private void BuildQualityPresetCategory()
            {
                BuildCategory("Quality Preset", (ref int currentY) =>
                {
                    AddOption("Auto (Detect)", () => GraphicsQualityManager.UserPreset == GraphicsQualityPreset.Auto, value =>
                    {
                        if (value) _owner.ApplyQualityPreset(GraphicsQualityPreset.Auto, RefreshOptions);
                    }, ref currentY, OptionRowHeight);
                    AddOption("Low (0.75x)", () => GraphicsQualityManager.UserPreset == GraphicsQualityPreset.Low, value =>
                    {
                        if (value) _owner.ApplyQualityPreset(GraphicsQualityPreset.Low, RefreshOptions);
                    }, ref currentY, OptionRowHeight);
                    AddOption("Medium (1.0x)", () => GraphicsQualityManager.UserPreset == GraphicsQualityPreset.Medium, value =>
                    {
                        if (value) _owner.ApplyQualityPreset(GraphicsQualityPreset.Medium, RefreshOptions);
                    }, ref currentY, OptionRowHeight);
                    AddOption("High (2.0x)", () => GraphicsQualityManager.UserPreset == GraphicsQualityPreset.High, value =>
                    {
                        if (value) _owner.ApplyQualityPreset(GraphicsQualityPreset.High, RefreshOptions);
                    }, ref currentY, OptionRowHeight);
                });
            }

            private void BuildRenderScaleCategory()
            {
                BuildCategory("Render Scale", (ref int currentY) =>
                {
                    AddOption("Render Scale: 300%", () => Math.Abs(Constants.RENDER_SCALE - 3.0f) < 0.01f, value =>
                    {
                        if (value) { SetRenderScale(3.0f); }
                    }, ref currentY, OptionRowHeight);
                    AddOption("Render Scale: 200%", () => Math.Abs(Constants.RENDER_SCALE - 2.0f) < 0.01f, value =>
                    {
                        if (value) { SetRenderScale(2.0f); }
                    }, ref currentY, OptionRowHeight);
                    AddOption("Render Scale: 150%", () => Math.Abs(Constants.RENDER_SCALE - 1.5f) < 0.01f, value =>
                    {
                        if (value) { SetRenderScale(1.5f); }
                    }, ref currentY, OptionRowHeight);
                    AddOption("Render Scale: 125%", () => Math.Abs(Constants.RENDER_SCALE - 1.25f) < 0.01f, value =>
                    {
                        if (value) { SetRenderScale(1.25f); }
                    }, ref currentY, OptionRowHeight);
                    AddOption("Render Scale: 100%", () => Math.Abs(Constants.RENDER_SCALE - 1.0f) < 0.01f, value =>
                    {
                        if (value) { SetRenderScale(1.0f); }
                    }, ref currentY, OptionRowHeight);
                    AddOption("Render Scale: 75%", () => Math.Abs(Constants.RENDER_SCALE - 0.75f) < 0.01f, value =>
                    {
                        if (value) { SetRenderScale(0.75f); }
                    }, ref currentY, OptionRowHeight);
                    AddOption("Render Scale: 60%", () => Math.Abs(Constants.RENDER_SCALE - 0.6f) < 0.01f, value =>
                    {
                        if (value) { SetRenderScale(0.6f); }
                    }, ref currentY, OptionRowHeight);
                    AddOption("Render Scale: 50%", () => Math.Abs(Constants.RENDER_SCALE - 0.5f) < 0.01f, value =>
                    {
                        if (value) { SetRenderScale(0.5f); }
                    }, ref currentY, OptionRowHeight);
                    AddOption("Render Scale: 37.5%", () => Math.Abs(Constants.RENDER_SCALE - 0.375f) < 0.01f, value =>
                    {
                        if (value) { SetRenderScale(0.375f); }
                    }, ref currentY, OptionRowHeight);
                });
            }

            private void BuildGraphicsCategory()
            {
                BuildCategory("Graphics", (ref int currentY) =>
                {
                    AddOption("High Quality Textures", () => Constants.HIGH_QUALITY_TEXTURES, value => Constants.HIGH_QUALITY_TEXTURES = value, ref currentY, OptionRowHeight);
                    AddOption("V-Sync", () => !Constants.DISABLE_VSYNC, value =>
                    {
                        _owner.SetVSync(value);
                    }, ref currentY, OptionRowHeight, RefreshOptions);
                });
            }

            private void BuildLightingCategory()
            {
                BuildCategory("Lighting & Materials", (ref int currentY) =>
                {
                    AddOption("Sun Light", () => Constants.SUN_ENABLED, value => Constants.SUN_ENABLED = value, ref currentY, OptionRowHeight);
                    AddOption("Day-Night Cycle (Real Time)", () => Constants.ENABLE_DAY_NIGHT_CYCLE, value =>
                    {
                        Constants.ENABLE_DAY_NIGHT_CYCLE = value;
                        if (!value)
                            SunCycleManager.ResetToDefault();
                    }, ref currentY, OptionRowHeight);
                    AddOption("Sun From +X", () => Constants.SUN_DIRECTION.X >= 0f, value =>
                    {
                        var dir = Constants.SUN_DIRECTION;
                        if (dir.LengthSquared() < 0.0001f)
                            dir = new Vector3(1f, 0f, -0.6f);
                        dir.X = Math.Abs(dir.X) * (value ? 1f : -1f);
                        Constants.SUN_DIRECTION = dir;
                    }, ref currentY, OptionRowHeight);
                    AddVolumeControl("Sun Strength (%)", () => Constants.SUN_STRENGTH * 100f, value =>
                    {
                        Constants.SUN_STRENGTH = MathHelper.Clamp(value, 0f, 200f) / 100f;
                    }, ref currentY, OptionRowHeight, 0f, 200f, 5f);
                    AddVolumeControl("Sun Shadow (%)", () => Constants.SUN_SHADOW_STRENGTH * 100f, value =>
                    {
                        Constants.SUN_SHADOW_STRENGTH = MathHelper.Clamp(value, 0f, 100f) / 100f;
                    }, ref currentY, OptionRowHeight, 0f, 100f, 5f);
                    AddOption("Terrain GPU Lighting", () => Constants.ENABLE_TERRAIN_GPU_LIGHTING, value => Constants.ENABLE_TERRAIN_GPU_LIGHTING = value, ref currentY, OptionRowHeight);
                    AddOption("Dynamic Lights", () => Constants.ENABLE_DYNAMIC_LIGHTS, value =>
                    {
                        Constants.ENABLE_DYNAMIC_LIGHTS = value;
                    }, ref currentY, OptionRowHeight, RefreshOptions);
                    AddOption("Dynamic Lighting Shader (GPU)", () => Constants.ENABLE_DYNAMIC_LIGHTING_SHADER, value =>
                    {
                        Constants.ENABLE_DYNAMIC_LIGHTING_SHADER = value;
                        if (!value)
                            Constants.ENABLE_TERRAIN_GPU_LIGHTING = false;
                    }, ref currentY, OptionRowHeight, RefreshOptions);
                    AddOption("Optimize for Integrated GPU", () => Constants.OPTIMIZE_FOR_INTEGRATED_GPU, value => Constants.OPTIMIZE_FOR_INTEGRATED_GPU = value, ref currentY, OptionRowHeight);
                    AddOption("Debug Lighting Areas", () => Constants.DEBUG_LIGHTING_AREAS, value => Constants.DEBUG_LIGHTING_AREAS = value, ref currentY, OptionRowHeight);
                    AddOption("Item Material Shader", () => Constants.ENABLE_ITEM_MATERIAL_SHADER, value => Constants.ENABLE_ITEM_MATERIAL_SHADER = value, ref currentY, OptionRowHeight);
                    AddOption("Monster Material Shader", () => Constants.ENABLE_MONSTER_MATERIAL_SHADER, value => Constants.ENABLE_MONSTER_MATERIAL_SHADER = value, ref currentY, OptionRowHeight);
                });
            }

            private void BuildShadowQualityCategory()
            {
                BuildCategory("Shadow Quality", (ref int currentY) =>
                {
                    AddOption("Shadow Mapping", () => Constants.ENABLE_SHADOW_MAPPING, value =>
                    {
                        Constants.ENABLE_SHADOW_MAPPING = value;
                        if (value && Constants.GetCurrentShadowQuality() == Constants.ShadowQuality.Off)
                        {
                            Constants.ApplyShadowQualityPreset(Constants.ShadowQuality.Medium);
                        }
                        OnShadowSettingChanged();
                    }, ref currentY, OptionRowHeight);

                    AddOption("Force Monster Mesh Shadows", () => MuGame.AppSettings?.Graphics?.ForceMonsterMeshShadows == true, value =>
                    {
                        var graphicsSettings = MuGame.AppSettings?.Graphics;
                        if (graphicsSettings == null)
                            return;

                        graphicsSettings.ForceMonsterMeshShadows = value;
                        MuGame.PersistMonsterShadowMode(value);
                        OnShadowSettingChanged();
                    }, ref currentY, OptionRowHeight);

                    currentY += 8;
                    AddHeading("Quality Presets", ref currentY);

                    AddOption("Off (Disabled)", () => Constants.GetCurrentShadowQuality() == Constants.ShadowQuality.Off, value =>
                    {
                        if (value) { Constants.ApplyShadowQualityPreset(Constants.ShadowQuality.Off); OnShadowSettingChanged(); }
                    }, ref currentY, OptionRowHeight);

                    AddOption("Low (512px, 800 units)", () => Constants.GetCurrentShadowQuality() == Constants.ShadowQuality.Low, value =>
                    {
                        if (value) { Constants.ApplyShadowQualityPreset(Constants.ShadowQuality.Low); OnShadowSettingChanged(); }
                    }, ref currentY, OptionRowHeight);

                    AddOption("Medium (1024px, 1200 units)", () => Constants.GetCurrentShadowQuality() == Constants.ShadowQuality.Medium, value =>
                    {
                        if (value) { Constants.ApplyShadowQualityPreset(Constants.ShadowQuality.Medium); OnShadowSettingChanged(); }
                    }, ref currentY, OptionRowHeight);

                    AddOption("High (1024px, 1500 units)", () => Constants.GetCurrentShadowQuality() == Constants.ShadowQuality.High, value =>
                    {
                        if (value) { Constants.ApplyShadowQualityPreset(Constants.ShadowQuality.High); OnShadowSettingChanged(); }
                    }, ref currentY, OptionRowHeight);

                    AddOption("Ultra (2048px, 2000 units)", () => Constants.GetCurrentShadowQuality() == Constants.ShadowQuality.Ultra, value =>
                    {
                        if (value) { Constants.ApplyShadowQualityPreset(Constants.ShadowQuality.Ultra); OnShadowSettingChanged(); }
                    }, ref currentY, OptionRowHeight);
                });
            }

            private void OnShadowSettingChanged()
            {
                // Force shadow map renderer to recreate render targets with new settings
                var shadowRenderer = GraphicsManager.Instance?.ShadowMapRenderer;
                if (shadowRenderer != null)
                {
                    shadowRenderer.EnsureRenderTarget();
                }
                RefreshOptions();
            }

            private void BuildPerformanceCategory()
            {
                BuildCategory("Performance & Debug", (ref int currentY) =>
                {
                    AddVolumeControl("Dynamic Light Update FPS", () => Constants.DYNAMIC_LIGHT_UPDATE_FPS, value =>
                    {
                        int fps = Constants.ClampPerformanceFps((int)value);
                        Constants.DYNAMIC_LIGHT_UPDATE_FPS = fps;

                        var graphicsSettings = MuGame.AppSettings?.Graphics;
                        if (graphicsSettings != null)
                        {
                            graphicsSettings.DynamicLightUpdateFps = fps;
                            MuGame.PersistGraphicsPerformanceCaps(graphicsSettings.DynamicLightUpdateFps, graphicsSettings.AnimationUpdateFps);
                        }
                    }, ref currentY, OptionRowHeight,
                    Constants.MIN_PERFORMANCE_FPS_CAP, Constants.MAX_PERFORMANCE_FPS_CAP, 1f, " FPS");

                    AddVolumeControl("Animation Update FPS", () => Constants.ANIMATION_UPDATE_FPS, value =>
                    {
                        int fps = Constants.ClampPerformanceFps((int)value);
                        Constants.ANIMATION_UPDATE_FPS = fps;

                        var graphicsSettings = MuGame.AppSettings?.Graphics;
                        if (graphicsSettings != null)
                        {
                            graphicsSettings.AnimationUpdateFps = fps;
                            MuGame.PersistGraphicsPerformanceCaps(graphicsSettings.DynamicLightUpdateFps, graphicsSettings.AnimationUpdateFps);
                        }
                    }, ref currentY, OptionRowHeight,
                    Constants.MIN_PERFORMANCE_FPS_CAP, Constants.MAX_PERFORMANCE_FPS_CAP, 1f, " FPS");

                    AddOption("Unlimited FPS", () => Constants.UNLIMITED_FPS, value => _owner.SetUnlimitedFps(value), ref currentY, OptionRowHeight, RefreshOptions);
                    AddOption("Dynamic Buffer Pool", () => Constants.ENABLE_DYNAMIC_BUFFER_POOL, value =>
                    {
                        DynamicBufferPool.SetEnabled(value);
                    }, ref currentY, OptionRowHeight);
                    AddOption("Item Material Animation", () => Constants.ENABLE_ITEM_MATERIAL_ANIMATION, value => Constants.ENABLE_ITEM_MATERIAL_ANIMATION = value, ref currentY, OptionRowHeight);
                    AddOption("Debug Panel", () => Constants.SHOW_DEBUG_PANEL, value =>
                    {
                        Constants.SHOW_DEBUG_PANEL = value;
                        _owner.ApplyDebugPanelSetting();
                    }, ref currentY, OptionRowHeight);
                });
            }

            private void BuildDisplayCategory()
            {
                BuildCategory("Display", (ref int currentY) =>
                {
                    var settings = MuGame.AppSettings?.Graphics;
                    if (settings == null) return;

                    // Get supported display modes from adapter
                    var adapter = GraphicsManager.Instance?.GraphicsDevice?.Adapter ?? GraphicsAdapter.DefaultAdapter;
                    var maxDisplayMode = adapter.CurrentDisplayMode;
                    int maxWidth = maxDisplayMode.Width;
                    int maxHeight = maxDisplayMode.Height;

                    // Helper to check if resolution is supported by adapter for fullscreen
                    bool IsResolutionSupported(int w, int h)
                    {
                        // Always allow resolutions up to max for windowed mode
                        if (!settings.IsFullScreen) return w <= maxWidth && h <= maxHeight;

                        // For fullscreen, check if adapter supports this mode
                        foreach (var mode in adapter.SupportedDisplayModes)
                        {
                            if (mode.Width == w && mode.Height == h)
                                return true;
                        }
                        return false;
                    }

                    AddHeading("Resolution", ref currentY);

                    // Standard 16:9 resolutions only - to maintain UI aspect ratio
                    if (IsResolutionSupported(1280, 720))
                    {
                        AddOption("1280x720", () => settings.Width == 1280 && settings.Height == 720, value =>
                        {
                            if (value) SetResolution(1280, 720);
                        }, ref currentY, OptionRowHeight);
                    }

                    if (IsResolutionSupported(1920, 1080))
                    {
                        AddOption("1920x1080", () => settings.Width == 1920 && settings.Height == 1080, value =>
                        {
                            if (value) SetResolution(1920, 1080);
                        }, ref currentY, OptionRowHeight);
                    }

                    if (IsResolutionSupported(2560, 1440))
                    {
                        AddOption("2560x1440", () => settings.Width == 2560 && settings.Height == 1440, value =>
                        {
                            if (value) SetResolution(2560, 1440);
                        }, ref currentY, OptionRowHeight);
                    }

                    if (IsResolutionSupported(3840, 2160))
                    {
                        AddOption("3840x2160", () => settings.Width == 3840 && settings.Height == 2160, value =>
                        {
                            if (value) SetResolution(3840, 2160);
                        }, ref currentY, OptionRowHeight);
                    }

                    currentY += 8;
                    AddHeading("Window Mode", ref currentY);

                    AddOption("Fullscreen", () => settings.IsFullScreen, value =>
                    {
                        SetFullscreen(value);
                    }, ref currentY, OptionRowHeight);
                });
            }

            private void SetResolution(int width, int height)
            {
                var settings = MuGame.AppSettings?.Graphics;
                if (settings == null) return;

                settings.Width = width;
                settings.Height = height;

                MuGame.ScheduleOnMainThread(() =>
                {
                    MuGame.Instance.ApplyGraphicsConfiguration(settings);
                    GraphicsManager.Instance.UpdateRenderScale();
                });

                MuGame.PersistDisplaySettings(width, height, settings.IsFullScreen);
                RefreshOptions();
            }

            private void SetFullscreen(bool enabled)
            {
                var settings = MuGame.AppSettings?.Graphics;
                if (settings == null) return;

                settings.IsFullScreen = enabled;

                MuGame.ScheduleOnMainThread(() =>
                {
                    MuGame.Instance.ApplyGraphicsConfiguration(settings);
                    GraphicsManager.Instance.UpdateRenderScale();
                });

                MuGame.PersistDisplaySettings(settings.Width, settings.Height, enabled);
                RefreshOptions();
            }

            private void AddCategoryButton(string label, Action onClick, int startY,
                ref int currentX, int width, int height, int spacing, int perRow, ref int index)
            {
                int row = index / perRow;
                int col = index % perRow;
                int x = 20 + col * (width + spacing);
                int y = startY + row * (height + spacing);

                var button = new MenuTabButtonControl
                {
                    Text = label,
                    X = x,
                    Y = y,
                    ControlSize = new Point(width, height),
                    ViewSize = new Point(width, height),
                    AutoViewSize = false,
                    FontSize = 10.5f,
                    TextColor = ModernHudTheme.TextGray
                };
                button.Click += (s, e) =>
                {
                    if (_activeCategoryButton != null)
                        _activeCategoryButton.Active = false;
                    _activeCategoryButton = button;
                    _activeCategoryButton.Active = true;
                    onClick();
                };
                Controls.Add(button);
                if (_activeCategoryButton == null)
                {
                    _activeCategoryButton = button;
                    _activeCategoryButton.Active = true;
                }

                currentX += width + spacing;
                index++;
            }

            private void SetRenderScale(float scale)
            {
                float clampedScale = MathHelper.Clamp(scale, 0.3f, 3.0f);

                if (Math.Abs(Constants.RENDER_SCALE - clampedScale) < 0.0001f)
                {
                    RefreshOptions();
                    return;
                }

                Constants.RENDER_SCALE = clampedScale;
                GraphicsManager.Instance.UpdateRenderScale();
                RefreshOptions();
            }

            private void RefreshOptions()
            {
                foreach (var option in _options)
                {
                    option.Refresh();
                }
            }

            private void AddOption(string label, Func<bool> getter, Action<bool> setter, ref int currentY, int rowHeight, Action onChanged = null)
            {
                var option = new OptionToggle(label, getter, value =>
                {
                    setter(value);
                    onChanged?.Invoke();
                }, currentY, _panelWidth);
                option.AddTo(Controls);
                option.CollectControls(_dynamicControls);
                _options.Add(option);
                currentY += rowHeight;
            }

            private void AddHeading(string label, ref int currentY)
            {
                var heading = new LabelControl
                {
                    Text = label,
                    X = ContentPaddingX,
                    Y = currentY,
                    FontSize = 13f,
                    TextColor = ModernHudTheme.TextGold,
                    IsBold = true,
                    HasShadow = false
                };
                Controls.Add(heading);
                _dynamicControls.Add(heading);
                currentY += 18;
            }

            public void Refresh()
            {
                foreach (var option in _options)
                {
                    option.Refresh();
                }
            }

            private void AddVolumeControl(string label, Func<float> getter, Action<float> setter, ref int currentY, int rowHeight, float minValue = 0f, float maxValue = 100f, float step = 5f, string valueSuffix = "%")
            {
                var option = new OptionVolume(label, getter, setter, currentY, _panelWidth, minValue, maxValue, step, valueSuffix);
                option.AddTo(Controls);
                option.CollectControls(_dynamicControls);
                _options.Add(option);
                currentY += rowHeight;
            }

            private interface IOptionRow
            {
                void AddTo(ChildrenCollection<GameControl> controls);
                void Refresh();
                void CollectControls(List<GameControl> controls);
            }

            private sealed class OptionToggle : IOptionRow
            {
                private readonly LabelControl _label;
                private readonly ButtonControl _button;
                private readonly Func<bool> _getter;
                private readonly Action<bool> _setter;

                public OptionToggle(string label, Func<bool> getter, Action<bool> setter, int y, int panelWidth)
                {
                    _getter = getter;
                    _setter = setter;

                    _label = new LabelControl
                    {
                        Text = label,
                        X = ContentPaddingX,
                        Y = y,
                        FontSize = 11.5f,
                        TextColor = ModernHudTheme.TextWhite,
                        HasShadow = false
                    };

                    _button = new ButtonControl
                    {
                        ControlSize = new Point(110, 26),
                        ViewSize = new Point(110, 26),
                        AutoViewSize = false,
                        X = panelWidth - 150,
                        Y = y - 4,
                        BackgroundColor = new Color(28, 35, 46, 230),
                        HoverBackgroundColor = new Color(48, 58, 73, 240),
                        PressedBackgroundColor = new Color(18, 23, 31, 245),
                        FontSize = 11f,
                        TextColor = ModernHudTheme.TextWhite,
                        HoverTextColor = ModernHudTheme.TextGold
                    };
                    _button.Click += (s, e) =>
                    {
                        bool newValue = !_getter();
                        _setter(newValue);
                        Refresh();
                    };

                    Refresh();
                }

                public void AddTo(ChildrenCollection<GameControl> controls)
                {
                    controls.Add(_label);
                    controls.Add(_button);
                }

                public void Refresh()
                {
                    bool value = _getter();
                    _button.Text = value ? "ENABLED" : "DISABLED";
                    _button.BackgroundColor = value ? new Color(34, 74, 55, 225) : new Color(55, 37, 43, 220);
                    _button.HoverBackgroundColor = value ? new Color(45, 96, 70, 240) : new Color(78, 47, 55, 238);
                    _button.TextColor = value ? new Color(150, 235, 180) : new Color(210, 145, 150);
                    _button.HoverTextColor = Color.White;
                }

                public void CollectControls(List<GameControl> controls)
                {
                    controls.Add(_label);
                    controls.Add(_button);
                }
            }

            private sealed class OptionVolume : IOptionRow
            {
                private readonly LabelControl _label;
                private readonly LabelControl _valueLabel;
                private readonly ButtonControl _minusButton;
                private readonly ButtonControl _plusButton;
                private readonly Func<float> _getter;
                private readonly Action<float> _setter;
                private readonly float _minValue;
                private readonly float _maxValue;
                private readonly float _step;
                private readonly string _valueSuffix;

                public OptionVolume(string label, Func<float> getter, Action<float> setter, int y, int panelWidth, float minValue = 0f, float maxValue = 100f, float step = 5f, string valueSuffix = "%")
                {
                    _getter = getter;
                    _setter = setter;
                    _minValue = minValue;
                    _maxValue = maxValue;
                    _step = step;
                    _valueSuffix = string.IsNullOrWhiteSpace(valueSuffix) ? string.Empty : valueSuffix;

                    _label = new LabelControl
                    {
                        Text = label,
                        X = ContentPaddingX,
                        Y = y,
                        FontSize = 11.5f,
                        TextColor = ModernHudTheme.TextWhite,
                        HasShadow = false
                    };

                    _valueLabel = new LabelControl
                    {
                        X = panelWidth - 210,
                        Y = y,
                        FontSize = 11f,
                        TextColor = ModernHudTheme.TextGold,
                        BackgroundColor = new Color(8, 12, 18, 180),
                        UseControlSizeBackground = true,
                        Padding = new Margin { Left = 6, Right = 6, Top = 2, Bottom = 2 },
                        HasShadow = false,
                        ControlSize = new Point(70, 24),
                        ViewSize = new Point(70, 24)
                    };

                    _minusButton = new ButtonControl
                    {
                        Text = "-",
                        ControlSize = new Point(28, 24),
                        ViewSize = new Point(28, 24),
                        AutoViewSize = false,
                        X = panelWidth - 130,
                        Y = y - 2,
                        BackgroundColor = new Color(28, 35, 46, 230),
                        HoverBackgroundColor = new Color(48, 58, 73, 240),
                        PressedBackgroundColor = new Color(18, 23, 31, 245),
                        FontSize = 11f,
                        TextColor = ModernHudTheme.TextWhite,
                        HoverTextColor = ModernHudTheme.TextGold
                    };

                    _plusButton = new ButtonControl
                    {
                        Text = "+",
                        ControlSize = new Point(28, 24),
                        ViewSize = new Point(28, 24),
                        AutoViewSize = false,
                        X = panelWidth - 96,
                        Y = y - 2,
                        BackgroundColor = new Color(28, 35, 46, 230),
                        HoverBackgroundColor = new Color(48, 58, 73, 240),
                        PressedBackgroundColor = new Color(18, 23, 31, 245),
                        FontSize = 11f,
                        TextColor = ModernHudTheme.TextWhite,
                        HoverTextColor = ModernHudTheme.TextGold
                    };

                    _minusButton.Click += (s, e) => AdjustVolume(-_step);
                    _plusButton.Click += (s, e) => AdjustVolume(_step);

                    Refresh();
                }

                private void AdjustVolume(float delta)
                {
                    float value = MathHelper.Clamp(_getter() + delta, _minValue, _maxValue);
                    value = (float)Math.Round(value);
                    _setter(value);
                    Refresh();
                }

                public void AddTo(ChildrenCollection<GameControl> controls)
                {
                    controls.Add(_label);
                    controls.Add(_valueLabel);
                    controls.Add(_minusButton);
                    controls.Add(_plusButton);
                }

                public void Refresh()
                {
                    float value = MathHelper.Clamp(_getter(), _minValue, _maxValue);
                    _valueLabel.Text = $"{Math.Round(value)}{_valueSuffix}";
                    _minusButton.Enabled = value > _minValue;
                    _plusButton.Enabled = value < _maxValue;
                }

                public void CollectControls(List<GameControl> controls)
                {
                    controls.Add(_label);
                    controls.Add(_valueLabel);
                    controls.Add(_minusButton);
                    controls.Add(_plusButton);
                }
            }
        }

    }
}
