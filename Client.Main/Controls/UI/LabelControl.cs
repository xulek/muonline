using Client.Main.Controllers;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Text;

namespace Client.Main.Controls.UI
{
    public static class MatrixExtensions
    {
        // Methods
        public static Matrix CreateSkew(float skewX, float skewY)
        {
            // Creates a 2D skew matrix.
            return new Matrix(
                1, skewY, 0, 0,
                skewX, 1, 0, 0,
                0, 0, 1, 0,
                0, 0, 0, 1
            );
        }
    }

    public class LabelControl : UIControl
    {
        // Fields
        private string _text;
        private object[] _textArgs = Array.Empty<object>();
        private string _renderedText;
        private float _fontSize = 12f;
        private float _scaleFactor;
        private Vector2 _baseTextSize;
        private SpriteFont _renderFont;

        // Properties

        // New properties for text formatting
        public bool IsBold { get; set; } = false;
        public bool IsItalic { get; set; } = false;
        public bool HasShadow { get; set; } = true;
        public bool HasUnderline { get; set; } = false;
        public float ShadowOpacity { get; set; } = 0.5f;
        public Vector2 ShadowOffset { get; set; } = new Vector2(1, 1);
        public Color ShadowColor { get; set; } = Color.Black;

        // Improved bold settings
        public float BoldStrength { get; set; } = 0.5f; // Subpixel offset for smoother bold effect

        public bool UseManualPosition { get; set; } = false;

        // When true, background size uses ControlSize instead of text dimensions
        public bool UseControlSizeBackground { get; set; } = false;

        /// <summary>
        /// Text to display. Changing this recalculates the rendered text.
        /// </summary>
        public string Text
        {
            get => _text;
            set
            {
                if (_text != value)
                {
                    _text = value;
                    OnChangeText();
                }
            }
        }

        /// <summary>
        /// Format arguments.
        /// </summary>
        public object[] TextArgs
        {
            get => _textArgs;
            set
            {
                if (_textArgs != value)
                {
                    _textArgs = value;
                    OnChangeText();
                }
            }
        }

        /// <summary>
        /// Desired font size on screen.
        /// </summary>
        public float FontSize
        {
            get => _fontSize;
            set
            {
                if (_fontSize != value)
                {
                    _fontSize = value;
                    OnChangeText();
                }
            }
        }

        public new float Alpha { get; set; } = 1f;

        public Color TextColor { get; set; } = Color.WhiteSmoke;
        public HorizontalAlign TextAlign { get; set; } = HorizontalAlign.Left;

        protected override void OnThemeChanged(UiThemeChangedEventArgs e)
        {
            base.OnThemeChanged(e);
            TextColor = RemapThemeColor(TextColor, e.Previous.Palette, e.Current.Palette);
            BackgroundColor = RemapThemeColor(BackgroundColor, e.Previous.Palette, e.Current.Palette);
            ShadowColor = RemapThemeColor(ShadowColor, e.Previous.Palette, e.Current.Palette);
            OnChangeText();
        }

        private static Color RemapThemeColor(Color color, UiThemePalette previous, UiThemePalette current)
        {
            if (color == previous.TextWhite) return current.TextWhite;
            if (color == previous.TextGold) return current.TextGold;
            if (color == previous.TextGray) return current.TextGray;
            if (color == previous.TextDark) return current.TextDark;
            if (color == previous.SecondaryBright) return current.SecondaryBright;
            if (color == previous.BgDarkest) return current.BgDarkest;
            if (color == previous.BgDark) return current.BgDark;
            if (color == previous.BgMid) return current.BgMid;
            if (color == previous.BgLight) return current.BgLight;
            return color;
        }

        // Methods
        public override void Draw(GameTime gameTime)
        {
            if (!Visible || string.IsNullOrEmpty(_renderedText) || _renderFont == null)
                return;

            var sb = GraphicsManager.Instance.Sprite;
            var font = _renderFont;

            Vector2 textPosition = DisplayRectangle.Location.ToVector2();

            var totalTextWidth = (int)Math.Ceiling(_baseTextSize.X);
            var totalTextHeight = (int)Math.Ceiling(_baseTextSize.Y);

            if (HasShadow)
            {
                totalTextWidth += (int)Math.Ceiling(Math.Abs(ShadowOffset.X));
                totalTextHeight += (int)Math.Ceiling(Math.Abs(ShadowOffset.Y));
            }

            if (IsBold)
            {
                totalTextWidth += (int)Math.Ceiling(BoldStrength * 2);
                totalTextHeight += (int)Math.Ceiling(BoldStrength * 2);
            }

            if (BackgroundColor.A > 0)
            {
                int bgWidth = totalTextWidth + Padding.Left + Padding.Right;
                int bgHeight = totalTextHeight + Padding.Top + Padding.Bottom;

                if (UseControlSizeBackground)
                {
                    bgWidth = ControlSize.X + Padding.Left + Padding.Right;
                    bgHeight = ControlSize.Y + Padding.Top + Padding.Bottom;
                }

                var backgroundRect = new Rectangle(
                    (int)(textPosition.X - Padding.Left),
                    (int)(textPosition.Y - Padding.Top),
                    bgWidth,
                    bgHeight
                );
                sb.Draw(GraphicsManager.Instance.Pixel, backgroundRect, BackgroundColor * Alpha);
            }

            Matrix combinedTransform = UiScaler.SpriteTransform;
            if (IsItalic)
            {
                const float skew = 0.2f;
                var italicTransform = Matrix.CreateTranslation(-textPosition.X, -textPosition.Y, 0f) *
                                      MatrixExtensions.CreateSkew(-skew, 0f) *
                                      Matrix.CreateTranslation(textPosition.X, textPosition.Y, 0f);
                combinedTransform = italicTransform * UiScaler.SpriteTransform;
            }

            sb.End();
            sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp, null, null, null, combinedTransform);

            if (HasShadow)
            {
                var shadowColor = new Color(ShadowColor, ShadowOpacity * Alpha);
                var shadowPosition = textPosition + ShadowOffset;

                if (IsBold)
                {
                    sb.DrawString(font, _renderedText, shadowPosition, shadowColor, 0f, Vector2.Zero, _scaleFactor, SpriteEffects.None, 0f);
                    sb.DrawString(font, _renderedText, shadowPosition + new Vector2(BoldStrength, 0), shadowColor, 0f, Vector2.Zero, _scaleFactor, SpriteEffects.None, 0f);
                }
                else
                {
                    sb.DrawString(font, _renderedText, shadowPosition, shadowColor, 0f, Vector2.Zero, _scaleFactor, SpriteEffects.None, 0f);
                }
            }

            var textColor = new Color(TextColor, Alpha);

            if (IsBold)
            {
                sb.DrawString(font, _renderedText, textPosition, textColor, 0f, Vector2.Zero, _scaleFactor, SpriteEffects.None, 0f);
                sb.DrawString(font, _renderedText, textPosition + new Vector2(BoldStrength, 0), textColor, 0f, Vector2.Zero, _scaleFactor, SpriteEffects.None, 0f);
            }
            else
            {
                sb.DrawString(font, _renderedText, textPosition, textColor, 0f, Vector2.Zero, _scaleFactor, SpriteEffects.None, 0f);
            }

            sb.End();

            sb.Begin(
                SpriteSortMode.Deferred,
                BlendState.AlphaBlend,
                SamplerState.LinearClamp,
                null,
                null,
                null,
                UiScaler.SpriteTransform);
        }

        private void OnChangeText()
        {
            if (string.IsNullOrEmpty(Text) || GraphicsManager.Instance?.Font == null)
            {
                _renderedText = string.Empty;
                _baseTextSize = Vector2.Zero;
                ControlSize = Point.Zero;
                return;
            }

            _renderedText = SafeFormat(Text, TextArgs).Normalize(NormalizationForm.FormC);
            _renderFont = GraphicsManager.GetUiFont(FontSize, out _scaleFactor);

            _baseTextSize = _renderFont.MeasureString(_renderedText) * _scaleFactor;
            var totalWidth = _baseTextSize.X;
            var totalHeight = _baseTextSize.Y;

            if (HasShadow)
            {
                totalWidth += Math.Abs(ShadowOffset.X);
                totalHeight += Math.Abs(ShadowOffset.Y);
            }

            if (IsBold)
            {
                totalWidth += BoldStrength;
                totalHeight += BoldStrength * 0.2f;
            }

            ControlSize = new Point(
                (int)Math.Ceiling(totalWidth),
                (int)Math.Ceiling(totalHeight)
            );
        }

        private string SafeFormat(string format, params object[] args)
        {
            if (args == null || args.Length == 0)
                return format;

            try
            {
                return string.Format(format, args);
            }
            catch (FormatException)
            {
                return format;
            }
        }
    }
}
