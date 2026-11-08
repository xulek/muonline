using System;
using Client.Main.Controllers;
using Client.Main.Controls.UI.Common;
using Client.Main.Controls.UI.Game.PauseMenu.Settings;
using Client.Main.Controls.UI.Ornate;
using Client.Main.Models;
using Microsoft.Xna.Framework;

namespace Client.Main.Controls.UI.Game.PauseMenu
{
    /// <summary>
    /// ESC overlay: dims the world and hosts the game menu (<see cref="OrnateMenuWindow"/>) and the
    /// <see cref="SettingsWindow"/>. Session flows (logout, exit) live in PauseMenuControl.Session.cs.
    /// </summary>
    public partial class PauseMenuControl : UIControl
    {
        private const float FadeSeconds = 0.18f;

        private readonly OrnateMenuWindow _menu;
        private readonly SettingsWindow _settings;
        private bool _wasVisible;
        private float _openSeconds;

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

            _menu = new OrnateMenuWindow("GAME MENU", new[]
            {
                new OrnateMenuEntry("Resume Game", "resume", ResumeGame),
                new OrnateMenuEntry("Settings", "settings", OpenSettings),
                new OrnateMenuEntry("Character Select", "character", () => _ = ReturnToCharacterSelectAsync()),
                new OrnateMenuEntry("Server Select", "server", () =>
                {
                    ServerSelectClicked?.Invoke(this, EventArgs.Empty);
                    _ = HandleReturnToServerSelectAsync();
                }),
                new OrnateMenuEntry("Exit Game", "exit", () => _ = ExitAsync(), Danger: true),
            })
            {
                InfoLine = CharacterInfoLine,
                Footer = "ESC  -  Resume Game"
            };
            Controls.Add(_menu);

            _settings = new SettingsWindow { Visible = false };
            _settings.BackRequested += (_, _) => CloseSettings();
            _settings.ResumeRequested += (_, _) => ResumeGame();
            _settings.CloseRequested += (_, _) => ResumeGame();
            Controls.Add(_settings);
        }

        public bool IsSettingsOpen => _settings.Visible;

        internal int SettingsCategoryCount => _settings.CategoryCount;

        private float Opacity => MathHelper.Clamp(_openSeconds / FadeSeconds, 0f, 1f);

        /// <summary>ESC inside the menu: leave the settings window first, then close the menu.</summary>
        public void HandleEscape()
        {
            if (_settings.Visible)
                CloseSettings();
            else
                ResumeGame();
        }

        public void ResumeGame()
        {
            ResumeClicked?.Invoke(this, EventArgs.Empty);
            CloseSettings();
            Visible = false;
        }

        public void OpenSettings()
        {
            OptionsClicked?.Invoke(this, EventArgs.Empty);
            _settings.Reopen();
            _settings.Visible = true;
            _menu.Visible = false;
        }

        public void CloseSettings()
        {
            _settings.Visible = false;
            _menu.Visible = true;
        }

        /// <summary>Diagnostics: show the settings window on the given category.</summary>
        internal void ShowSettingsCategory(int index)
        {
            if (!_settings.Visible)
                OpenSettings();
            _settings.SelectCategory(index);
        }

        private static string CharacterInfoLine()
        {
            var state = MuGame.Network?.GetCharacterState();
            return state != null && !string.IsNullOrEmpty(state.Name) && state.Name != "???"
                ? $"{state.Name}   -   Level {state.Level}"
                : null;
        }

        public override void Update(GameTime gameTime)
        {
            if (Visible && !_wasVisible)
            {
                // Freshly opened: always start on the game menu and fade in.
                _openSeconds = 0f;
                CloseSettings();
            }
            _wasVisible = Visible;

            if (Visible)
                _openSeconds += (float)gameTime.ElapsedGameTime.TotalSeconds;

            _menu.Opacity = _settings.Opacity = Opacity;
            _menu.Busy = IsBusy;

            base.Update(gameTime);
        }

        public override void Draw(GameTime gameTime)
        {
            if (Status != GameControlStatus.Ready || !Visible)
                return;

            float a = Opacity;
            UiDrawHelper.DrawVerticalGradient(GraphicsManager.Instance.Sprite, DisplayRectangle,
                new Color(4, 5, 8) * (0.66f * a), Color.Black * (0.88f * a), 20);

            base.Draw(gameTime);
        }
    }
}
