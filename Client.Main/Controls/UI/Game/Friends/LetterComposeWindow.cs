#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using Client.Main.Controls.UI.Game.Common;
using Client.Main.Controls.UI.Game.Inventory;
using Client.Main.Core.Client;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Client.Main.Controls.UI.Game.Friends
{
    /// <summary>
    /// Letter editor, modelled on the original CUIWriteLetterWindow (UIWindows.cpp): receiver, subject and
    /// a multi-line text (Enter starts a new line). Sent with LetterSendRequest (0xC5); the server charges Zen.
    /// </summary>
    public sealed class LetterComposeWindow : MessengerWindowBase
    {
        private const int Width = 340;
        private const int ToY = HeaderHeight + 24;
        private const int SubjectY = ToY + 46;
        private const int BodyY = SubjectY + 46;
        private const int BodyHeight = 200;
        private const int StatusY = BodyY + BodyHeight + 6;
        private const int ButtonY = StatusY + 22;
        private const int Height = ButtonY + 32 + Pad;
        private const float TextSize = 12f;
        private const int LineHeight = 17;

        private static readonly Rectangle BodyLocal = new(Pad, BodyY, Width - Pad * 2, BodyHeight);
        private static readonly Rectangle SendLocal = new(Pad, ButtonY, (Width - Pad * 2 - 6) / 2, 32);
        private static readonly Rectangle CancelLocal = new(Pad + (Width - Pad * 2 - 6) / 2 + 6, ButtonY, (Width - Pad * 2 - 6) / 2, 32);

        private readonly FriendManager _friends;
        private readonly TextFieldControl _to;
        private readonly TextFieldControl _subject;
        private readonly TextFieldControl _body;
        private bool _sendHovered, _cancelHovered;
        private int _firstLine;
        private bool _waiting;
        private string? _error;

        public LetterComposeWindow(FriendManager friends) : base(Width, Height)
        {
            _friends = friends;
            Align = ControlAlign.HorizontalCenter | ControlAlign.VerticalCenter;
            _to = AddField(Pad, ToY, Width - Pad * 2, FriendManager.MaxNameLength);
            _subject = AddField(Pad, SubjectY, Width - Pad * 2, FriendManager.MaxSubjectLength);
            // The body field only collects keyboard input; the window draws the wrapped text over it.
            _body = AddField(Pad, BodyY, Width - Pad * 2, FriendManager.MaxLetterLength);
            _body.ViewSize = new Point(BodyLocal.Width, BodyLocal.Height);
            _body.EnableCaretNavigation = true;
            _body.EnterKeyPressed += (_, _) =>
            {
                if (Encoding.UTF8.GetByteCount(_body.Value) < FriendManager.MaxLetterLength) _body.InsertAtCaret("\n");
            };
            _to.EnterKeyPressed += (_, _) => MuGame.ScheduleOnMainThread(() => FocusField(_subject));
            _subject.EnterKeyPressed += (_, _) => MuGame.ScheduleOnMainThread(() => FocusField(_body));
            _friends.LetterSendResult += OnSendResult;
        }

        public override void Dispose()
        {
            _friends.LetterSendResult -= OnSendResult;
            base.Dispose();
        }

        protected override string Title => "WRITE LETTER";

        public void ShowFor(string receiver, string subject)
        {
            _to.Value = receiver ?? string.Empty;
            _subject.Value = subject ?? string.Empty;
            _body.Value = string.Empty;
            _firstLine = 0;
            _waiting = false;
            _error = null;
            Show();
            FocusField(string.IsNullOrEmpty(receiver) ? _to : string.IsNullOrEmpty(subject) ? _subject : _body);
        }

        private void OnSendResult(byte result)
        {
            if (!_waiting) return;
            _waiting = false;
            if (result == 1) Hide(); // Success
            else _error = "The letter was not sent.";
        }

        private string? GetBlocker()
        {
            if (_to.Value.Trim().Length == 0) return "Enter the receiver.";
            if (_subject.Value.Trim().Length == 0) return "Enter a subject.";
            if (_body.Value.Trim().Length == 0) return "Write a message.";
            return null;
        }

        private void Send()
        {
            if (_waiting) return;
            if (GetBlocker() is { } blocker)
            {
                _error = blocker;
                return;
            }
            if (MuGame.Network?.GetCharacterService() is not { } service) return;
            _waiting = true;
            _error = null;
            _ = service.SendLetterAsync(_to.Value.Trim(), _subject.Value.Trim(), _body.Value.TrimEnd());
        }

        protected override void UpdateContent(GameTime gameTime, bool mouseInside, int wheel)
        {
            _sendHovered = Translate(SendLocal).Contains(MousePosition);
            _cancelHovered = Translate(CancelLocal).Contains(MousePosition);

            if (!IsFocused(_body)) return;
            var keyboard = MuGame.Instance.Keyboard;
            var previous = MuGame.Instance.PrevKeyboard;
            int direction = keyboard.IsKeyDown(Keys.Up) && previous.IsKeyUp(Keys.Up) ? -1
                          : keyboard.IsKeyDown(Keys.Down) && previous.IsKeyUp(Keys.Down) ? 1 : 0;
            if (direction == 0) return;

            // Up/Down keep the caret column on the previous/next wrapped line.
            string text = _body.Value;
            var segments = WrapSegments(text, BodyLocal.Width - 20);
            int line = FindLine(segments, _body.CaretIndex);
            int target = line + direction;
            if (target < 0 || target >= segments.Count) return;
            float x = PlayerShopUi.Measure(text.Substring(segments[line].Start, _body.CaretIndex - segments[line].Start), TextSize).X;
            var (start, length) = segments[target];
            int column = 0;
            while (column < length && text[start + column] != '\n' &&
                   PlayerShopUi.Measure(text.Substring(start, column + 1), TextSize).X <= x + 2)
                column++;
            _body.CaretIndex = start + column;
        }

        /// <summary>
        /// Word-wraps the text into (start, length) segments of the original string so the caret index maps to a line.
        /// A segment ends after its break character (space or '\n').
        /// </summary>
        private static List<(int Start, int Length)> WrapSegments(string text, float maxWidth)
        {
            var segments = new List<(int, int)>();
            int lineStart = 0, lastSpace = -1;
            for (int i = 0; i <= text.Length; i++)
            {
                if (i == text.Length)
                {
                    segments.Add((lineStart, i - lineStart));
                    break;
                }
                char c = text[i];
                if (c == '\n')
                {
                    segments.Add((lineStart, i - lineStart + 1));
                    lineStart = i + 1;
                    lastSpace = -1;
                    continue;
                }
                if (c == ' ') lastSpace = i;
                if (PlayerShopUi.Measure(text.Substring(lineStart, i - lineStart + 1), TextSize).X > maxWidth && i > lineStart)
                {
                    int breakAt = lastSpace >= lineStart ? lastSpace + 1 : i;
                    segments.Add((lineStart, breakAt - lineStart));
                    lineStart = breakAt;
                    lastSpace = -1;
                    i = breakAt - 1;
                }
            }
            return segments;
        }

        private static int FindLine(List<(int Start, int Length)> segments, int caret)
        {
            for (int i = 0; i < segments.Count; i++)
            {
                var (start, length) = segments[i];
                bool isLast = i == segments.Count - 1;
                if (caret >= start && (caret < start + length || isLast)) return i;
            }
            return Math.Max(0, segments.Count - 1);
        }

        protected override bool HandleClick()
        {
            if (_sendHovered) { Send(); return true; }
            if (_cancelHovered) { Hide(); return true; }
            return false;
        }

        protected override void DrawContent(SpriteBatch sb, GameTime gameTime)
        {
            PlayerShopUi.DrawText(sb, "To", Translate(new Rectangle(Pad, ToY - 16, 0, 0)).Location.ToVector2(), ModernHudTheme.TextGray, 11f, Alpha);
            PlayerShopUi.DrawText(sb, "Subject", Translate(new Rectangle(Pad, SubjectY - 16, 0, 0)).Location.ToVector2(), ModernHudTheme.TextGray, 11f, Alpha);

            string status = _error ?? (_waiting ? "Sending..." : $"{Encoding.UTF8.GetByteCount(_body.Value)} / {FriendManager.MaxLetterLength}");
            Color color = _error != null ? ModernHudTheme.Danger : _waiting ? ModernHudTheme.TextGold : ModernHudTheme.TextGray;
            PlayerShopUi.DrawTextCentered(sb, status, Translate(new Rectangle(Pad, StatusY, Width - Pad * 2, 16)), color, 11f, Alpha);

            PlayerShopUi.DrawButton(sb, Translate(SendLocal), "SEND", _sendHovered, !_waiting && GetBlocker() == null, ModernHudTheme.Success, Alpha, 14f);
            PlayerShopUi.DrawButton(sb, Translate(CancelLocal), "CANCEL", _cancelHovered, true, ModernHudTheme.Danger, Alpha, 14f);
        }

        protected override void DrawOverlay(SpriteBatch sb, GameTime gameTime)
        {
            DrawPlaceholder(sb, _to, "Character name");
            DrawPlaceholder(sb, _subject, "Subject");

            // Multi-line body drawn over the input field.
            var body = Translate(BodyLocal);
            PlayerShopUi.DrawPanel(sb, body, ModernHudTheme.BgDarkest, Alpha);
            if (IsFocused(_body))
                Client.Main.Controls.UI.Common.UiDrawHelper.DrawBorder(sb, body, ModernHudTheme.AccentBright * Alpha);

            string text = _body.Value;
            var segments = WrapSegments(text, body.Width - 20);
            int caret = _body.CaretIndex;
            int caretLine = FindLine(segments, caret);
            int visible = BodyHeight / LineHeight - 1;
            // Keep the caret line visible.
            _firstLine = Math.Clamp(_firstLine, Math.Max(0, caretLine - visible + 1), caretLine);
            for (int i = _firstLine; i < segments.Count && i < _firstLine + visible; i++)
            {
                var (start, length) = segments[i];
                string line = text.Substring(start, length).TrimEnd('\n');
                PlayerShopUi.DrawText(sb, line, new Vector2(body.X + 10, body.Y + 8 + (i - _firstLine) * LineHeight), ModernHudTheme.TextWhite, TextSize, Alpha);
            }

            if (IsFocused(_body) && (int)(gameTime.TotalGameTime.TotalSeconds * 2) % 2 == 0)
            {
                var (start, _) = segments[caretLine];
                string before = text.Substring(start, Math.Max(0, caret - start)).TrimEnd('\n');
                float x = body.X + 10 + PlayerShopUi.Measure(before, TextSize).X;
                int y = body.Y + 8 + (caretLine - _firstLine) * LineHeight;
                PlayerShopUi.DrawText(sb, "|", new Vector2(x - 2, y), ModernHudTheme.TextWhite, TextSize, Alpha, shadow: false);
            }

            if (_body.Value.Length == 0 && !IsFocused(_body))
                PlayerShopUi.DrawText(sb, "Write your message here (Enter = new line, arrows move the cursor)", new Vector2(body.X + 10, body.Y + 8),
                    ModernHudTheme.TextGray * 0.7f, 11f, Alpha, shadow: false);
        }
    }
}
