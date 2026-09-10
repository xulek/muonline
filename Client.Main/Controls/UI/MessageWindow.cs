using Client.Main.Models;
using Client.Main.Controllers;
using Client.Main.Helpers;
using Client.Main.Controls.UI.Common;
using Client.Main.Controls.UI.Game.Common;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;

namespace Client.Main.Controls.UI
{
    public class MessageWindow : DialogControl
    {
        private readonly TextureControl _background;
        private readonly LabelControl _label;
        private readonly OkButton _okButton;
        private static readonly ILogger _logger = MuGame.AppLoggerFactory?.CreateLogger<MessageWindow>();

        public string Text
        {
            get => _label.Text;
            set
            {
                _label.Text = value ?? string.Empty;
                AdjustSizeAndLayout();
            }
        }

        private MessageWindow()
        {
            Align = ControlAlign.HorizontalCenter | ControlAlign.VerticalCenter;
            AutoViewSize = false;
            BorderColor = Color.Gray * 0.7f;
            BorderThickness = 1;
            BackgroundColor = Color.Black * 0.8f;

            _background = new TextureControl
            {
                TexturePath = "Interface/message_back.tga",
                AutoViewSize = false,
                ViewSize = new Point(352, 113),
                BlendState = BlendState.AlphaBlend
            };
            Controls.Add(_background);

            _label = new LabelControl
            {
                FontSize = 14f,
                TextColor = Color.White,
                TextAlign = HorizontalAlign.Center,
            };
            Controls.Add(_label);

            _okButton = new OkButton
            {
                // Align = ControlAlign.HorizontalCenter // This would also work if X were not manually set
            };
            _okButton.Click += (s, e) => Close();
            Controls.Add(_okButton);

            AdjustSizeAndLayout();
            ApplyThemeLayout();
        }

        private void AdjustSizeAndLayout()
        {
            if (_label == null || _okButton == null)
                return;

            int minWidth = 200;
            int minHeight = 120;

            int textWidth = _label.ControlSize.X;
            int textHeight = _label.ControlSize.Y;
            int buttonWidth = _okButton.ViewSize.X;
            int buttonHeight = _okButton.ViewSize.Y;

            int requiredWidth = Math.Max(textWidth, buttonWidth) + 40;
            int finalWidth = Math.Max(minWidth, requiredWidth);

            int requiredHeight = textHeight + buttonHeight + 50;
            int finalHeight = Math.Max(minHeight, requiredHeight);

            ControlSize = new Point(finalWidth, finalHeight);
            ViewSize = ControlSize;

            _label.X = (finalWidth - textWidth) / 2;
            _label.Y = 25;

            _okButton.X = (finalWidth - buttonWidth) / 2;
            _okButton.Y = finalHeight - buttonHeight - 20;
        }

        protected override void OnThemeChanged(UiThemeChangedEventArgs e)
        {
            base.OnThemeChanged(e);
            ApplyThemeLayout();
        }

        private void ApplyThemeLayout()
        {
            bool season6 = UiThemeManager.CurrentId == UiThemeId.Season6;
            _background.Visible = !season6;
            _label.TextColor = season6 ? ModernHudTheme.TextWhite : Color.White;
            _okButton.ViewSize = season6 ? new Point(100, 34) : new Point(54, 30);
            _okButton.ControlSize = _okButton.ViewSize;
            AdjustSizeAndLayout();
        }

        public static MessageWindow Show(string text)
        {
            var scene = MuGame.Instance?.ActiveScene;
            if (scene == null)
            {
                _logger?.LogDebug("[MessageWindow.Show] Error: ActiveScene is null.");
                return null;
            }

            foreach (var existingWindow in scene.Controls.OfType<MessageWindow>().ToList())
            {
                existingWindow.Close();
            }

            var window = new MessageWindow { Text = text };
            window.ShowDialog();
            window.BringToFront();
            return window;
        }

        public override void Draw(GameTime gameTime)
        {
            if (Status != GameControlStatus.Ready || !Visible)
                return;

            if (UiThemeManager.CurrentId == UiThemeId.Season6)
            {
                using (new SpriteBatchScope(
                    GraphicsManager.Instance.Sprite,
                    SpriteSortMode.Deferred,
                    BlendState.AlphaBlend,
                    transform: UiScaler.SpriteTransform))
                {
                    var sprite = GraphicsManager.Instance.Sprite;
                    var rect = DisplayRectangle;
                    UiDrawHelper.DrawPanel(sprite, rect, ModernHudTheme.BgDark * 0.98f,
                        ModernHudTheme.BorderInner, ModernHudTheme.BorderOuter,
                        ModernHudTheme.BorderHighlight, withGlow: true,
                        glowColor: ModernHudTheme.AccentGlow * 0.4f);
                    sprite.Draw(GraphicsManager.Instance.Pixel,
                        new Rectangle(rect.X + 18, rect.Y + 16, rect.Width - 36, 2),
                        ModernHudTheme.Accent * 0.75f);

                    _label?.Draw(gameTime);

                    Rectangle button = _okButton.DisplayRectangle;
                    Color buttonColor = _okButton.IsMouseOver
                        ? ModernHudTheme.Accent
                        : ModernHudTheme.BgLight;
                    UiDrawHelper.DrawPanel(sprite, button, buttonColor,
                        ModernHudTheme.BorderInner, ModernHudTheme.BorderOuter,
                        ModernHudTheme.BorderHighlight);
                    SpriteFont font = GraphicsManager.GetUiFont(12f, out float scale) ?? GraphicsManager.Instance.Font;
                    if (font != null)
                    {
                        const string text = "OK";
                        Vector2 size = font.MeasureString(text) * scale;
                        Vector2 position = new(button.X + (button.Width - size.X) * 0.5f,
                            button.Y + (button.Height - size.Y) * 0.5f);
                        sprite.DrawString(font, text, position + Vector2.One,
                            Color.Black * 0.7f, 0f, Vector2.Zero, scale,
                            SpriteEffects.None, 0f);
                        sprite.DrawString(font, text, position,
                            ModernHudTheme.TextWhite, 0f, Vector2.Zero, scale,
                            SpriteEffects.None, 0f);
                    }
                }

                return;
            }

            using (new SpriteBatchScope(
                GraphicsManager.Instance.Sprite,
                SpriteSortMode.Deferred,
                BlendState.AlphaBlend,
                transform: UiScaler.SpriteTransform))
            {
                DrawBackground();
                DrawBorder();

                _label?.Draw(gameTime);
                _okButton?.Draw(gameTime);
            }
        }
    }
}
