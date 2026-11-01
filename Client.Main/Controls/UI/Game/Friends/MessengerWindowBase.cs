#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using Client.Main.Controllers;
using Client.Main.Controls.UI.Common;
using Client.Main.Controls.UI.Game.Common;
using Client.Main.Controls.UI.Game.Inventory;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Client.Main.Controls.UI.Game.Friends
{
    /// <summary>
    /// Shared chrome and input plumbing of the messenger windows (friends, letters, chat):
    /// personal-shop window style, draggable header, close button, Esc to close and text field focus.
    /// </summary>
    public abstract class MessengerWindowBase : UIControl
    {
        protected const int Pad = 14;
        protected const int HeaderHeight = 48;

        private readonly List<TextFieldControl> _fields = new();
        private TextFieldControl? _focusedField;
        private bool _isDragging;
        private Point _dragOffset;
        private bool _closeHovered;

        protected MessengerWindowBase(int width, int height)
        {
            AutoViewSize = false;
            Interactive = true;
            ControlSize = ViewSize = new Point(width, height);
            Visible = false;
        }

        protected int WindowWidth => ViewSize.X;
        protected int WindowHeight => ViewSize.Y;
        protected Point MousePosition { get; private set; }
        protected bool LeftPressed { get; private set; }
        protected bool RightPressed { get; private set; }

        protected abstract string Title { get; }
        protected virtual string? Subtitle => null;

        private Rectangle CloseLocal => new(WindowWidth - 34, 12, 22, 22);

        public virtual void Show()
        {
            Visible = true;
            BringToFront();
        }

        public virtual void Hide()
        {
            BlurField();
            _isDragging = false;
            Visible = false;
        }

        public void Toggle()
        {
            if (Visible) Hide();
            else Show();
        }

        protected Rectangle Translate(Rectangle local)
            => new(DisplayRectangle.X + local.X, DisplayRectangle.Y + local.Y, local.Width, local.Height);

        // ─────────────────────────────── Fields ───────────────────────────────

        protected TextFieldControl AddField(int x, int y, int width, int maxLength)
        {
            var field = TextFieldControl.Create();
            field.X = x;
            field.Y = y;
            field.ViewSize = new Point(width, 24);
            field.FontSize = 12f;
            field.TextColor = ModernHudTheme.TextWhite;
            field.BackgroundColor = ModernHudTheme.SlotBg;
            field.BorderColor = ModernHudTheme.BorderInner;
            field.BorderThickness = 1;
            field.ValueChanged += (_, _) => LimitLength(field, maxLength);
            _fields.Add(field);
            Controls.Add(field);
            return field;
        }

        private static void LimitLength(TextFieldControl field, int maxBytes)
        {
            string value = field.Value;
            if (Encoding.UTF8.GetByteCount(value) <= maxBytes) return;
            while (value.Length > 0 && Encoding.UTF8.GetByteCount(value) > maxBytes)
                value = value[..^1];
            field.Value = value;
        }

        protected bool IsFocused(TextFieldControl field) => _focusedField == field;

        protected void FocusField(TextFieldControl field)
        {
            if (_focusedField != null && _focusedField != field) BlurField();
            _focusedField = field;
            EnsureFieldFocus();
        }

        protected void BlurField()
        {
            if (_focusedField == null) return;
            var field = _focusedField;
            _focusedField = null;
            field.Blur();
            if (Scene?.FocusControl == field) Scene.FocusControl = null;
        }

        private void EnsureFieldFocus()
        {
            if (_focusedField is not { } field) return;
            if (!Visible || !field.Visible)
            {
                BlurField();
                return;
            }
            if (!field.IsFocused) field.Focus();
            if (Scene != null && Scene.FocusControl != field) Scene.FocusControl = field;
        }

        // ─────────────────────────────── Update ───────────────────────────────

        public override void Update(GameTime gameTime)
        {
            // Re-assert before the fields update so their keyboard polling sees the focus this frame.
            if (Visible) EnsureFieldFocus();
            base.Update(gameTime);
            if (!Visible) return;

            if (MuGame.Instance.Keyboard.IsKeyDown(Keys.Escape) && MuGame.Instance.PrevKeyboard.IsKeyUp(Keys.Escape))
            {
                Scene?.ConsumeKeyboardEscape();
                if (_focusedField != null) BlurField();
                else Hide();
                return;
            }

            var mouse = MuGame.Instance.UiMouseState;
            var previous = MuGame.Instance.PrevUiMouseState;
            MousePosition = mouse.Position;
            bool leftDown = mouse.LeftButton == ButtonState.Pressed;
            LeftPressed = leftDown && previous.LeftButton == ButtonState.Released;
            RightPressed = mouse.RightButton == ButtonState.Pressed && previous.RightButton == ButtonState.Released;

            foreach (var field in _fields)
                field.BorderColor = _focusedField == field ? ModernHudTheme.AccentBright : ModernHudTheme.BorderInner;

            if (_isDragging)
            {
                if (leftDown) { X = MousePosition.X - _dragOffset.X; Y = MousePosition.Y - _dragOffset.Y; }
                else _isDragging = false;
                return;
            }

            _closeHovered = Translate(CloseLocal).Contains(MousePosition);
            bool inside = DisplayRectangle.Contains(MousePosition);
            int wheel = inside ? mouse.ScrollWheelValue - previous.ScrollWheelValue : 0;

            UpdateContent(gameTime, inside, wheel);

            if (!inside || (!LeftPressed && !RightPressed)) return;
            Scene?.SetMouseInputConsumed();
            if (!LeftPressed) return;

            foreach (var field in _fields)
            {
                if (field.Visible && field.DisplayRectangle.Contains(MousePosition))
                {
                    FocusField(field);
                    return;
                }
            }

            if (_closeHovered) { Hide(); return; }
            if (HandleClick()) return;
            BlurField();

            if (Translate(new Rectangle(0, 0, WindowWidth, HeaderHeight)).Contains(MousePosition))
            {
                _isDragging = true;
                _dragOffset = new Point(MousePosition.X - X, MousePosition.Y - Y);
                Align = ControlAlign.None;
            }
        }

        /// <summary>Per-frame hover state; <paramref name="wheel"/> is the mouse wheel delta inside the window.</summary>
        protected virtual void UpdateContent(GameTime gameTime, bool mouseInside, int wheel) { }

        /// <summary>Left click inside the window (not on a field or the close button). Return true when handled.</summary>
        protected virtual bool HandleClick() => false;

        // ─────────────────────────────── Draw ───────────────────────────────

        public override void Draw(GameTime gameTime)
        {
            if (!Visible || Status != GameControlStatus.Ready) return;
            var sb = GraphicsManager.Instance.Sprite;
            if (sb == null || GraphicsManager.Instance.Pixel == null) return;

            var scope = PlayerShopUi.BeginUiBatch();
            try
            {
                PlayerShopUi.DrawWindow(sb, DisplayRectangle, Alpha);
                PlayerShopUi.DrawHeader(sb, Translate(new Rectangle(0, 0, WindowWidth, HeaderHeight)), Title, Subtitle, Alpha);
                PlayerShopUi.DrawCloseButton(sb, Translate(CloseLocal), _closeHovered, Alpha);
                DrawContent(sb, gameTime);
            }
            finally
            {
                scope?.Dispose();
            }

            base.Draw(gameTime);

            var overlay = PlayerShopUi.BeginUiBatch();
            try
            {
                DrawOverlay(sb, gameTime);
            }
            finally
            {
                overlay?.Dispose();
            }
        }

        /// <summary>Window body, drawn below the text fields.</summary>
        protected abstract void DrawContent(SpriteBatch sb, GameTime gameTime);

        /// <summary>Drawn above the text fields (placeholders, multi-line editors).</summary>
        protected virtual void DrawOverlay(SpriteBatch sb, GameTime gameTime) { }

        protected void DrawPlaceholder(SpriteBatch sb, TextFieldControl field, string text)
        {
            if (field.Value.Length == 0 && !field.IsFocused && field.Visible)
                PlayerShopUi.DrawText(sb, text, new Vector2(field.DisplayRectangle.X + 6, field.DisplayRectangle.Y + 5),
                    ModernHudTheme.TextGray * 0.7f, 11f, Alpha, shadow: false);
        }

        /// <summary>Word-wraps text (honouring '\n') to the given pixel width.</summary>
        protected static List<string> WrapText(string text, float size, float maxWidth)
        {
            var lines = new List<string>();
            foreach (string paragraph in (text ?? string.Empty).Replace("\r", string.Empty).Split('\n'))
            {
                var line = new StringBuilder();
                foreach (string word in paragraph.Split(' '))
                {
                    string candidate = line.Length == 0 ? word : line + " " + word;
                    if (PlayerShopUi.Measure(candidate, size).X <= maxWidth)
                    {
                        line.Clear().Append(candidate);
                        continue;
                    }

                    if (line.Length > 0) lines.Add(line.ToString());
                    line.Clear();
                    // Break words longer than a line.
                    string rest = word;
                    while (rest.Length > 0 && PlayerShopUi.Measure(rest, size).X > maxWidth)
                    {
                        int cut = rest.Length - 1;
                        while (cut > 1 && PlayerShopUi.Measure(rest[..cut], size).X > maxWidth) cut--;
                        lines.Add(rest[..cut]);
                        rest = rest[cut..];
                    }
                    line.Append(rest);
                }
                lines.Add(line.ToString());
            }
            return lines;
        }

        /// <summary>Draws a scrollable list of rows and returns nothing; rows are laid out by the caller.</summary>
        protected void DrawScrollBar(SpriteBatch sb, Rectangle track, int scroll, int visible, int total)
        {
            if (total <= visible) return;
            var pixel = GraphicsManager.Instance.Pixel;
            int thumbHeight = Math.Max(16, track.Height * visible / total);
            int maxScroll = total - visible;
            int thumbY = track.Y + (track.Height - thumbHeight) * scroll / Math.Max(1, maxScroll);
            sb.Draw(pixel, new Rectangle(track.Right - 3, track.Y, 2, track.Height), ModernHudTheme.BorderInner * 0.4f * Alpha);
            sb.Draw(pixel, new Rectangle(track.Right - 4, thumbY, 4, thumbHeight), ModernHudTheme.AccentBright * Alpha);
        }
    }
}
