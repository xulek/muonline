using Client.Main.Models;
using Client.Main.Helpers;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using Client.Main.Controls.UI.Common;
using Client.Main.Controllers;
using Client.Main.Networking;

namespace Client.Main.Controls.UI
{
    /// <summary>
    /// Reconnection dialog with progress bar that appears during connection recovery attempts.
    /// Similar to the original MU Online reconnection dialog.
    /// </summary>
    public class ReconnectionDialog : DialogControl
    {
        private const int DIALOG_WIDTH = 400;
        private const int DIALOG_HEIGHT = 150;
        private const int PROGRESS_BAR_WIDTH = 320;
        private const int PROGRESS_BAR_HEIGHT = 20;

        private readonly TextureControl _background;
        private readonly LabelControl _titleLabel;
        private readonly LabelControl _statusLabel;
        private readonly ColorBarControl _progressBar;
        private readonly ButtonControl _cancelButton;

        private static readonly ILogger _logger = MuGame.AppLoggerFactory?.CreateLogger<ReconnectionDialog>();

        private int _currentAttempt = 0;
        private int _maxAttempts = 0;
        private bool _isReconnecting = false;

        public event EventHandler CancelRequested;

        public ReconnectionDialog()
        {
            Interactive = true;
            Align = ControlAlign.HorizontalCenter | ControlAlign.VerticalCenter;
            AutoViewSize = false;
            ControlSize = new Point(DIALOG_WIDTH, DIALOG_HEIGHT);
            ViewSize = ControlSize;
            BorderColor = Color.Gold;
            BorderThickness = 2;
            BackgroundColor = Color.Black * 0.9f;

            // Background texture
            _background = new TextureControl
            {
                TexturePath = "Interface/message_back.tga",
                AutoViewSize = false,
                ControlSize = new Point(DIALOG_WIDTH, DIALOG_HEIGHT),
                ViewSize = new Point(DIALOG_WIDTH, DIALOG_HEIGHT),
                BlendState = Blendings.Alpha,
                Interactive = false
            };
            Controls.Add(_background);

            // Title label
            _titleLabel = new LabelControl
            {
                Text = "Reconnecting...",
                FontSize = 16f,
                TextColor = Color.Gold,
                TextAlign = HorizontalAlign.Center,
                X = 0,
                Y = 20,
                ControlSize = new Point(DIALOG_WIDTH, 20)
            };
            Controls.Add(_titleLabel);

            // Status label
            _statusLabel = new LabelControl
            {
                Text = "Attempting to restore connection...",
                FontSize = 12f,
                TextColor = Color.White,
                TextAlign = HorizontalAlign.Center,
                X = 0,
                Y = 50,
                ControlSize = new Point(DIALOG_WIDTH, 15)
            };
            Controls.Add(_statusLabel);

            // Progress bar background
            var progressBg = new ColorBarControl
            {
                X = (DIALOG_WIDTH - PROGRESS_BAR_WIDTH) / 2,
                Y = 75,
                ControlSize = new Point(PROGRESS_BAR_WIDTH, PROGRESS_BAR_HEIGHT),
                ViewSize = new Point(PROGRESS_BAR_WIDTH, PROGRESS_BAR_HEIGHT),
                BackgroundColor = new Color(0f, 0f, 0f, 0.45f),
                BorderColor = Color.DarkSlateGray,
                BorderThickness = 1,
                Percentage = 1f,
                FillColor = Color.Transparent,
                Interactive = false
            };
            Controls.Add(progressBg);

            // Progress bar
            _progressBar = new ColorBarControl
            {
                X = progressBg.X + 2,
                Y = progressBg.Y + 2,
                ControlSize = new Point(PROGRESS_BAR_WIDTH - 4, PROGRESS_BAR_HEIGHT - 4),
                ViewSize = new Point(PROGRESS_BAR_WIDTH - 4, PROGRESS_BAR_HEIGHT - 4),
                BackgroundColor = Color.Transparent,
                FillColor = Color.Orange,
                Percentage = 0f,
                Interactive = false
            };
            Controls.Add(_progressBar);

            // Cancel button
            _cancelButton = new ButtonControl
            {
                Text = "Cancel",
                FontSize = 12f,
                ViewSize = new Point(80, 25),
                ControlSize = new Point(80, 25),
                X = (DIALOG_WIDTH - 80) / 2,
                Y = DIALOG_HEIGHT - 35,
                BackgroundColor = new Color(0.30f, 0.10f, 0.10f, 0.80f),
                HoverBackgroundColor = new Color(0.40f, 0.15f, 0.15f, 0.90f),
                PressedBackgroundColor = new Color(0.20f, 0.05f, 0.05f, 0.90f),
                BorderColor = Color.DarkRed,
                BorderThickness = 1
            };
            _cancelButton.Click += (s, e) =>
            {
                _logger?.LogInformation("Reconnection cancelled by user.");
                CancelRequested?.Invoke(this, EventArgs.Empty);
                Close();
            };
            Controls.Add(_cancelButton);
        }

        /// <summary>
        /// Updates the reconnection progress.
        /// </summary>
        /// <param name="attempt">Current attempt number (1-based)</param>
        /// <param name="maxAttempts">Maximum number of attempts</param>
        /// <param name="status">Status message to display</param>
        public void UpdateProgress(int attempt, int maxAttempts, string status = null)
        {
            _currentAttempt = attempt;
            _maxAttempts = maxAttempts;

            // Update progress bar
            float progress = maxAttempts > 0 ? (float)attempt / maxAttempts : 0f;
            _progressBar.Percentage = Math.Clamp(progress, 0f, 1f);
            if (_progressBar.FillColor != Color.Orange && _isReconnecting)
            {
                _progressBar.FillColor = Color.Orange;
            }

            // Update status text
            if (!string.IsNullOrEmpty(status))
            {
                _statusLabel.Text = status;
            }
            else
            {
                _statusLabel.Text = $"Attempt {attempt} of {maxAttempts}...";
            }

            _logger?.LogDebug("Reconnection progress updated: {Attempt}/{MaxAttempts} - {Status}",
                attempt, maxAttempts, _statusLabel.Text);
        }

        /// <summary>
        /// Sets the dialog to show connection success.
        /// </summary>
        public void SetConnectionRestored()
        {
            _titleLabel.Text = "Connection Restored!";
            _titleLabel.TextColor = Color.LimeGreen;
            _statusLabel.Text = "Successfully reconnected to server.";
            _progressBar.Percentage = 1f;
            _progressBar.FillColor = Color.LimeGreen;
            _cancelButton.Text = "Close";
            _isReconnecting = false;

            _logger?.LogInformation("Reconnection dialog showing success.");

            // Auto-close after 2 seconds using a simple task delay
            Task.Delay(2000).ContinueWith(_ => MuGame.ScheduleOnMainThread(() => Close()));
        }

        /// <summary>
        /// Sets the dialog to show connection failure.
        /// </summary>
        /// <param name="message">Failure message to display</param>
        public void SetConnectionFailed(string message = null)
        {
            _titleLabel.Text = "Reconnection Failed";
            _titleLabel.TextColor = Color.Red;
            _statusLabel.Text = message ?? "Unable to restore connection. Please try again manually.";
            _progressBar.FillColor = Color.Red;
            _cancelButton.Text = "Close";
            _isReconnecting = false;

            _logger?.LogWarning("Reconnection dialog showing failure: {Message}", _statusLabel.Text);
        }

        /// <summary>
        /// Shows the reconnection dialog.
        /// </summary>
        /// <param name="maxAttempts">Maximum number of reconnection attempts</param>
        /// <returns>The dialog instance</returns>
        public static ReconnectionDialog Show(int maxAttempts = 5)
        {
            var scene = MuGame.Instance?.ActiveScene;
            if (scene == null)
            {
                _logger?.LogWarning("Cannot show reconnection dialog - no active scene.");
                return null;
            }

            // Close any existing reconnection dialogs
            foreach (var existing in scene.Controls.OfType<ReconnectionDialog>().ToList())
            {
                existing.Close();
            }

            var dialog = new ReconnectionDialog();
            dialog._maxAttempts = maxAttempts;
            dialog._isReconnecting = true;
            dialog.UpdateProgress(0, maxAttempts, "Preparing to reconnect...");

            dialog.ShowDialog();
            dialog.BringToFront();

            _logger?.LogInformation("Reconnection dialog shown with max attempts: {MaxAttempts}", maxAttempts);
            return dialog;
        }

        /// <summary>
        /// Closes any existing reconnection dialogs.
        /// </summary>
        public static void CloseAll()
        {
            var scene = MuGame.Instance?.ActiveScene;
            if (scene == null) return;

            foreach (var dialog in scene.Controls.OfType<ReconnectionDialog>().ToList())
            {
                dialog.Close();
            }
        }

        public override void Draw(GameTime gameTime)
        {
            if (Status != GameControlStatus.Ready || !Visible) return;

            using (new SpriteBatchScope(
                GraphicsManager.Instance.Sprite,
                SpriteSortMode.Deferred,
                BlendState.AlphaBlend,
                transform: UiScaler.SpriteTransform))
            {
                DrawBackground();
                DrawBorder();
            }

            base.Draw(gameTime);
        }

        public override bool OnClick()
        {
            return true; // Consume clicks to prevent interaction with background
        }

        public override void Dispose()
        {
            if (_isReconnecting)
            {
                _logger?.LogDebug("Reconnection dialog disposed while reconnecting.");
            }
            base.Dispose();
        }
    }
}
