#nullable enable
using System;
using System.Text;
using Client.Main.Controls.UI.Game.Common;
using Client.Main.Controls.UI.Game.Inventory;
using Client.Main.Core.Client;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

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
            _body.EnterKeyPressed += (_, _) => MuGame.ScheduleOnMainThread(() =>
            {
                if (Encoding.UTF8.GetByteCount(_body.Value) < FriendManager.MaxLetterLength) _body.Value += "\n";
            });
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

            var lines = WrapText(_body.Value, TextSize, body.Width - 20);
            int visible = BodyHeight / LineHeight - 1;
            int first = Math.Max(0, lines.Count - visible); // keep the caret line visible
            for (int i = first; i < lines.Count; i++)
                PlayerShopUi.DrawText(sb, lines[i], new Vector2(body.X + 10, body.Y + 8 + (i - first) * LineHeight), ModernHudTheme.TextWhite, TextSize, Alpha);

            if (IsFocused(_body) && (int)(gameTime.TotalGameTime.TotalSeconds * 2) % 2 == 0)
            {
                string last = lines.Count > 0 ? lines[^1] : string.Empty;
                float x = body.X + 10 + PlayerShopUi.Measure(last, TextSize).X + 1;
                int y = body.Y + 8 + (lines.Count - 1 - first) * LineHeight;
                PlayerShopUi.DrawText(sb, "|", new Vector2(x, y), ModernHudTheme.TextWhite, TextSize, Alpha, shadow: false);
            }

            if (_body.Value.Length == 0 && !IsFocused(_body))
                PlayerShopUi.DrawText(sb, "Write your message here (Enter = new line)", new Vector2(body.X + 10, body.Y + 8),
                    ModernHudTheme.TextGray * 0.7f, 11f, Alpha, shadow: false);
        }
    }
}
