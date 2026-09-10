using Client.Main.Controls.UI.Common;
using Client.Main.Controls.UI;
using Client.Main.Controls.UI.Game.Common;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Client.Main.Helpers;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace Client.Main.Controls.UI.SelectCharacter
{
    /// <summary>
    /// Dialog for confirming character deletion with security code input.
    /// </summary>
    public class CharacterDeletionDialog : UIControl
    {
        // ═══════════════════════════════════════════════════════════════
        // MODERN DARK THEME - Matching SelectCharacterScene
        // ═══════════════════════════════════════════════════════════════
        private static class Theme
        {
            // Background layers
            public static Color BgDarkest => ModernHudTheme.BgDarkest;
            public static Color BgDark => ModernHudTheme.BgDark;
            public static Color BgMid => ModernHudTheme.BgMid;
            public static Color BgLight => ModernHudTheme.BgLight;
            public static Color BgLighter => ModernHudTheme.BgLighter;

            // Accent - Warm Gold
            public static Color Accent => ModernHudTheme.Accent;
            public static Color AccentBright => ModernHudTheme.AccentBright;
            public static Color AccentDim => ModernHudTheme.AccentDim;
            public static Color AccentGlow => ModernHudTheme.AccentGlow;

            // Secondary accent - Cool Blue
            public static Color Secondary => ModernHudTheme.Secondary;
            public static Color SecondaryBright => ModernHudTheme.SecondaryBright;
            public static Color SecondaryDim => ModernHudTheme.SecondaryDim;

            // Borders
            public static Color BorderOuter => ModernHudTheme.BorderOuter;
            public static Color BorderInner => ModernHudTheme.BorderInner;
            public static Color BorderHighlight => ModernHudTheme.BorderHighlight;

            // Text
            public static Color TextWhite => ModernHudTheme.TextWhite;
            public static Color TextGold => ModernHudTheme.TextGold;
            public static Color TextGray => ModernHudTheme.TextGray;
            public static Color TextDark => ModernHudTheme.TextDark;

            // Status colors
            public static Color Success => ModernHudTheme.Success;
            public static Color Warning => ModernHudTheme.Warning;
            public static Color Danger => ModernHudTheme.Danger;
        }

        private RenderTarget2D _backgroundSurface;
        private bool _surfaceNeedsRedraw = true;
        private double _bringToFrontTimer = 0;
        private const double BRING_TO_FRONT_INTERVAL = 0.1; // Throttle to every 100ms
        private readonly string _characterName;
        private LabelControl _titleLabel;
        private LabelControl _messageLabel;
        private LabelControl _securityCodeLabel;
        private TextBoxControl _securityCodeInput;
        private ButtonControl _confirmButton;
        private ButtonControl _cancelButton;

        public event EventHandler<string> DeleteConfirmed;
        public event EventHandler CancelRequested;

        public CharacterDeletionDialog(string characterName)
        {
            _characterName = characterName;
            
            AutoViewSize = false;
            ViewSize = new Point(550, 400);
            Align = ControlAlign.HorizontalCenter | ControlAlign.VerticalCenter;
            Interactive = true;

            InitializeControls();
            ApplyThemeLayout();
            
            // Ensure dialog appears on top of character cards
            BringToFront();
        }

        protected override void OnThemeChanged(UiThemeChangedEventArgs e)
        {
            base.OnThemeChanged(e);
            ApplyThemeLayout();
            _surfaceNeedsRedraw = true;
        }

        private void ApplyThemeLayout()
        {
            bool season6 = UiThemeManager.CurrentId == UiThemeId.Season6;
            if (season6)
            {
                AutoViewSize = false;
                Align = ControlAlign.HorizontalCenter | ControlAlign.VerticalCenter;
                ViewSize = new Point(460, 320);
                Place(_titleLabel, 0, 16, 460, 28, HorizontalAlign.Center, 16f);
                Place(_messageLabel, 28, 58, 404, 82, HorizontalAlign.Center, 11f);
                Place(_securityCodeLabel, 28, 154, 130, 22, HorizontalAlign.Left, 11f);
                Place(_securityCodeInput, 28, 180, 404, 34);
                Place(_confirmButton, 28, 258, 190, 36);
                Place(_cancelButton, 242, 258, 190, 36);
                _titleLabel.TextColor = ModernHudTheme.Danger;
                _messageLabel.TextColor = ModernHudTheme.TextWhite;
                _securityCodeLabel.TextColor = ModernHudTheme.TextGray;
                _securityCodeInput.TextColor = ModernHudTheme.TextWhite;
                _securityCodeInput.BackgroundColor = ModernHudTheme.BgDarkest;
                _securityCodeInput.BorderColor = ModernHudTheme.BorderInner;
                _confirmButton.BackgroundColor = ModernHudTheme.Danger;
                _cancelButton.BackgroundColor = ModernHudTheme.BgLight;
            }
            else
            {
                ViewSize = new Point(550, 400);
                _titleLabel.Align = ControlAlign.Top | ControlAlign.HorizontalCenter;
                _messageLabel.Align = ControlAlign.Top | ControlAlign.HorizontalCenter;
                _securityCodeLabel.Align = ControlAlign.None;
                _securityCodeInput.Align = ControlAlign.None;
                _confirmButton.Align = ControlAlign.None;
                _cancelButton.Align = ControlAlign.None;
                _securityCodeLabel.X = 50;
                _securityCodeLabel.Y = 200;
                _securityCodeInput.X = 50;
                _securityCodeInput.Y = 230;
                _securityCodeInput.ViewSize = new Point(450, 36);
                _confirmButton.X = 50;
                _confirmButton.Y = 300;
                _confirmButton.ViewSize = new Point(220, 40);
                _cancelButton.X = 280;
                _cancelButton.Y = 300;
                _cancelButton.ViewSize = new Point(220, 40);
            }
        }

        private static void Place(LabelControl control, int x, int y, int width, int height,
            HorizontalAlign align, float fontSize)
        {
            control.Align = ControlAlign.None;
            control.X = x;
            control.Y = y;
            control.AutoViewSize = false;
            control.ViewSize = new Point(width, height);
            control.TextAlign = align;
            control.FontSize = fontSize;
        }

        private static void Place(GameControl control, int x, int y, int width, int height)
        {
            control.Align = ControlAlign.None;
            control.X = x;
            control.Y = y;
            control.AutoViewSize = false;
            control.ViewSize = new Point(width, height);
        }

        private void InitializeControls()
        {
            // Title
            _titleLabel = new LabelControl
            {
                Text = "DELETE CHARACTER",
                FontSize = 20f,
                TextColor = Theme.Danger,
                Align = ControlAlign.Top | ControlAlign.HorizontalCenter,
                Margin = new Margin { Top = 10 }
            };
            Controls.Add(_titleLabel);

            // Warning message
            _messageLabel = new LabelControl
            {
                Text = $"Are you sure you want to delete '{_characterName}'?\n\nThis action cannot be undone!\n\nEnter your security code to confirm:",
                FontSize = 13f,
                TextColor = Theme.TextWhite,
                Align = ControlAlign.Top | ControlAlign.HorizontalCenter,
                Margin = new Margin { Top = 70 },
                ViewSize = new Point(450, 120),
                X = 50
            };
            Controls.Add(_messageLabel);

            // Security code label
            _securityCodeLabel = new LabelControl
            {
                Text = "Security Code:",
                FontSize = 13f,
                TextColor = Theme.TextWhite,
                X = 50,
                Y = 200
            };
            Controls.Add(_securityCodeLabel);

            // Security code input
            _securityCodeInput = new TextBoxControl
            {
                X = 50,
                Y = 230,
                ViewSize = new Point(450, 36),
                MaxLength = 20,
                PlaceholderText = "Enter security code...",
                FontSize = 8f,
                BackgroundColor = Theme.BgDark,
                TextColor = Theme.TextWhite,
                BorderColor = Theme.BorderInner,
                BorderThickness = 1
            };
            Controls.Add(_securityCodeInput);
            _securityCodeInput.Focus();

            // Delete button
            _confirmButton = CreateModernButton("DELETE CHARACTER", Theme.Danger);
            _confirmButton.X = 50;
            _confirmButton.Y = 300;
            _confirmButton.ViewSize = new Point(220, 40);
            _confirmButton.Click += OnConfirmClick;
            Controls.Add(_confirmButton);

            // Cancel button
            _cancelButton = CreateModernButton("CANCEL", Theme.BgLight);
            _cancelButton.X = 280;
            _cancelButton.Y = 300;
            _cancelButton.ViewSize = new Point(220, 40);
            _cancelButton.Click += OnCancelClick;
            Controls.Add(_cancelButton);
        }

        private ButtonControl CreateModernButton(string text, Color baseColor)
        {
            return new ButtonControl
            {
                Text = text,
                FontSize = 13f,
                AutoViewSize = false,
                BackgroundColor = baseColor,
                HoverBackgroundColor = Color.Lerp(baseColor, Color.White, 0.2f),
                PressedBackgroundColor = Color.Lerp(baseColor, Color.Black, 0.2f),
                TextColor = Theme.TextWhite,
                HoverTextColor = Theme.TextWhite,
                DisabledTextColor = Theme.TextDark,
                Interactive = true,
                BorderThickness = 1,
                BorderColor = Theme.BorderInner
            };
        }

        private void OnConfirmClick(object sender, EventArgs e)
        {
            string securityCode = _securityCodeInput.Text.Trim();
            DeleteConfirmed?.Invoke(this, securityCode);
        }

        private void OnCancelClick(object sender, EventArgs e)
        {
            CancelRequested?.Invoke(this, EventArgs.Empty);
        }

        private void RegenerateBackgroundSurface()
        {
            Client.Main.Graphics.UiRenderTargetPool.Return(_backgroundSurface);

            int width = ViewSize.X;
            int height = ViewSize.Y;

            _backgroundSurface = Client.Main.Graphics.UiRenderTargetPool.Rent(GraphicsDevice, width, height);

            // Render background to surface
            var oldTargets = GraphicsDevice.GetRenderTargets();
            GraphicsDevice.SetRenderTarget(_backgroundSurface);
            GraphicsDevice.Clear(Color.Transparent);

            // Create a new SpriteBatch instance to avoid conflicts with shared instance
            using var batch = new SpriteBatch(GraphicsDevice);
            batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend);

            var pixel = UiDrawHelper.GetPixelTexture(GraphicsDevice);
            var dialogRect = new Rectangle(0, 0, width, height);

            // Main panel background with gradient
            UiDrawHelper.DrawVerticalGradient(batch, dialogRect, Theme.BgMid, Theme.BgDark);

            // Outer border
            batch.Draw(pixel, new Rectangle(0, 0, dialogRect.Width, 1), Theme.BorderOuter);
            batch.Draw(pixel, new Rectangle(0, dialogRect.Height - 1, dialogRect.Width, 1), Theme.BorderOuter);
            batch.Draw(pixel, new Rectangle(0, 0, 1, dialogRect.Height), Theme.BorderOuter);
            batch.Draw(pixel, new Rectangle(dialogRect.Width - 1, 0, 1, dialogRect.Height), Theme.BorderOuter);

            // Header section with danger accent - aligned with dialog (no offset)
            var headerRect = new Rectangle(0, 0, dialogRect.Width, 50);
            UiDrawHelper.DrawHorizontalGradient(batch, headerRect, Theme.BgLighter, Theme.BgMid);
            
            // Danger accent on header (top stripe)
            batch.Draw(pixel, new Rectangle(0, 0, dialogRect.Width, 3), Theme.Danger * 0.6f);
            
            // Corner accents aligned with header
            UiDrawHelper.DrawCornerAccents(batch, headerRect, Theme.Danger, 12, 2);

            // Header separator (aligned with header width)
            batch.Draw(pixel, new Rectangle(0, headerRect.Bottom - 1, headerRect.Width, 1), Theme.BorderInner);
            batch.Draw(pixel, new Rectangle(0, headerRect.Bottom - 2, headerRect.Width, 1), Theme.Danger * 0.3f);

            batch.End();

            GraphicsDevice.SetRenderTargets(oldTargets);
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
            
            // Ensure dialog stays on top of character cards (throttled)
            _bringToFrontTimer += gameTime.ElapsedGameTime.TotalSeconds;
            if (_bringToFrontTimer >= BRING_TO_FRONT_INTERVAL && Parent != null)
            {
                _bringToFrontTimer = 0;
                BringToFront();
            }
        }

        public override void Draw(GameTime gameTime)
        {
            if (UiThemeManager.CurrentId == UiThemeId.Season6)
            {
                DrawSeason6SurfaceAndContent();
                return;
            }

            if (Status == GameControlStatus.Ready)
            {
                if (_backgroundSurface == null || _surfaceNeedsRedraw ||
                    _backgroundSurface.Width != ViewSize.X || _backgroundSurface.Height != ViewSize.Y)
                {
                    RegenerateBackgroundSurface();
                    _surfaceNeedsRedraw = false;
                }

                if (_backgroundSurface != null)
                {
                    var sb = GraphicsManager.Instance.Sprite;
                    using (new SpriteBatchScope(sb, SpriteSortMode.Deferred, BlendState.AlphaBlend,
                        SamplerState.LinearClamp, DepthStencilState.None, RasterizerState.CullNone, null, UiScaler.SpriteTransform))
                    {
                        var destRect = new Rectangle(
                            DisplayPosition.X,
                            DisplayPosition.Y,
                            ViewSize.X,
                            ViewSize.Y);

                        sb.Draw(_backgroundSurface, destRect, Color.White);
                    }
                }
            }

            base.Draw(gameTime);
        }

        private void DrawSeason6SurfaceAndContent()
        {
            if (Status != GameControlStatus.Ready)
                return;

            if (_backgroundSurface == null || _surfaceNeedsRedraw ||
                _backgroundSurface.Width != ViewSize.X || _backgroundSurface.Height != ViewSize.Y)
            {
                RegenerateBackgroundSurface();
                _surfaceNeedsRedraw = false;
            }

            SpriteBatch sprite = GraphicsManager.Instance.Sprite;
            SpriteFont font = GraphicsManager.Instance.Font;
            Texture2D pixel = GraphicsManager.Instance.Pixel;
            if (sprite == null || font == null || pixel == null)
                return;

            using var scope = new SpriteBatchScope(sprite, SpriteSortMode.Deferred,
                BlendState.AlphaBlend, SamplerState.LinearClamp, DepthStencilState.None,
                RasterizerState.CullNone, null, UiScaler.SpriteTransform);

            if (_backgroundSurface != null)
                sprite.Draw(_backgroundSurface,
                    new Rectangle(DisplayPosition.X, DisplayPosition.Y, ViewSize.X, ViewSize.Y), Color.White);

            Rectangle origin = new(DisplayPosition.X, DisplayPosition.Y, ViewSize.X, ViewSize.Y);
            DrawCentered(sprite, font, _titleLabel.Text,
                new Rectangle(origin.X, origin.Y + 16, origin.Width, 28), ModernHudTheme.Danger, 0.54f);
            DrawCentered(sprite, font, _messageLabel.Text,
                new Rectangle(origin.X + 28, origin.Y + 58, 404, 82), ModernHudTheme.TextWhite, 0.38f);
            DrawString(sprite, font, _securityCodeLabel.Text,
                new Rectangle(origin.X + 28, origin.Y + 154, 130, 22), ModernHudTheme.TextGray, 0.42f);
            DrawString(sprite, font, _securityCodeInput.Text,
                new Rectangle(origin.X + 38, origin.Y + 186, 380, 22), ModernHudTheme.TextWhite, 0.44f);
            DrawSeason6Button(sprite, pixel, font, _confirmButton, ModernHudTheme.Danger);
            DrawSeason6Button(sprite, pixel, font, _cancelButton, ModernHudTheme.BgLight);
        }

        private static void DrawSeason6Button(SpriteBatch sprite, Texture2D pixel, SpriteFont font,
            ButtonControl button, Color fill)
        {
            Rectangle rect = button.DisplayRectangle;
            sprite.Draw(pixel, rect, (button.IsMouseOver ? ModernHudTheme.AccentDim : fill) * 0.92f);
            sprite.Draw(pixel, new Rectangle(rect.X, rect.Y, rect.Width, 1), ModernHudTheme.BorderInner);
            sprite.Draw(pixel, new Rectangle(rect.X, rect.Bottom - 1, rect.Width, 1), ModernHudTheme.BorderOuter);
            DrawCentered(sprite, font, button.Text, rect, ModernHudTheme.TextWhite, 0.42f);
        }

        private static void DrawCentered(SpriteBatch sprite, SpriteFont font, string text,
            Rectangle rect, Color color, float scale)
        {
            if (string.IsNullOrEmpty(text))
                return;
            Vector2 size = font.MeasureString(text) * scale;
            Vector2 position = new(rect.X + (rect.Width - size.X) / 2f,
                rect.Y + (rect.Height - size.Y) / 2f);
            sprite.DrawString(font, text, position, color, 0f, Vector2.Zero, scale,
                SpriteEffects.None, 0f);
        }

        private static void DrawString(SpriteBatch sprite, SpriteFont font, string text,
            Rectangle rect, Color color, float scale)
        {
            if (string.IsNullOrEmpty(text))
                return;
            sprite.DrawString(font, text, new Vector2(rect.X, rect.Y + 2), color,
                0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        }

        public override void Dispose()
        {
            Client.Main.Graphics.UiRenderTargetPool.Return(_backgroundSurface);
            _backgroundSurface = null;

            if (_confirmButton != null)
            {
                _confirmButton.Click -= OnConfirmClick;
            }

            if (_cancelButton != null)
            {
                _cancelButton.Click -= OnCancelClick;
            }

            base.Dispose();
        }

        protected override void OnScreenSizeChanged()
        {
            base.OnScreenSizeChanged();
            _surfaceNeedsRedraw = true;
        }
    }
}
