#nullable enable
using System;
using System.Collections.Generic;
using Client.Main.Controls.UI.Game.Common;
using Client.Main.Controls.UI.Game.Inventory;
using Client.Main.Core.Client;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Controls.UI.Game.Friends
{
    /// <summary>
    /// Letter reader, modelled on the original CUIReadLetterWindow (UIWindows.cpp): sender, subject, date and text,
    /// with Reply and Delete. The text is requested with LetterReadRequest (0xC7) when it is not cached yet.
    /// </summary>
    public sealed class LetterReadWindow : MessengerWindowBase
    {
        private const int Width = 340;
        private const int InfoY = HeaderHeight + 8;
        private const int BodyY = InfoY + 58;
        private const int BodyHeight = 240;
        private const int ButtonY = BodyY + BodyHeight + 10;
        private const int Height = ButtonY + 32 + Pad;
        private const float TextSize = 12f;
        private const int LineHeight = 17;

        private static readonly Rectangle BodyLocal = new(Pad, BodyY, Width - Pad * 2, BodyHeight);

        private readonly FriendManager _friends;
        private LetterEntry? _letter;
        private List<string> _lines = new();
        private int _scroll;
        private int _hoveredButton = -1;

        public LetterReadWindow(FriendManager friends) : base(Width, Height)
        {
            _friends = friends;
            Align = ControlAlign.HorizontalCenter | ControlAlign.VerticalCenter;
            _friends.LetterOpened += OnLetterOpened;
            _friends.LettersChanged += OnLettersChanged;
        }

        public override void Dispose()
        {
            _friends.LetterOpened -= OnLetterOpened;
            _friends.LettersChanged -= OnLettersChanged;
            base.Dispose();
        }

        protected override string Title => "LETTER";
        protected override string? Subtitle => _letter?.Subject;

        public void Open(LetterEntry letter)
        {
            _letter = letter;
            _scroll = 0;
            _lines = letter.Message != null ? WrapText(letter.Message, TextSize, BodyLocal.Width - 20) : new List<string>();
            // The server marks the letter as read and sends the text; ask again so the state is synced.
            if (MuGame.Network?.GetCharacterService() is { } service)
                _ = service.SendLetterReadRequestAsync(letter.Index);
            Show();
        }

        private void OnLetterOpened(LetterEntry letter)
        {
            MuGame.ScheduleOnMainThread(() =>
            {
                if (_letter == null || letter.Index != _letter.Index) return;
                _letter = letter;
                _lines = WrapText(letter.Message ?? string.Empty, TextSize, BodyLocal.Width - 20);
            });
        }

        private void OnLettersChanged()
        {
            MuGame.ScheduleOnMainThread(() =>
            {
                if (_letter == null || !Visible) return;
                foreach (var letter in _friends.GetLetters())
                    if (letter.Index == _letter.Index) return;
                Hide(); // deleted
            });
        }

        private static Rectangle ButtonLocal(int index)
        {
            int width = (Width - Pad * 2 - 12) / 3;
            return new Rectangle(Pad + index * (width + 6), ButtonY, width, 32);
        }

        private void Reply()
        {
            if (_letter == null) return;
            string subject = _letter.Subject.StartsWith("Re:", StringComparison.OrdinalIgnoreCase) ? _letter.Subject : "Re: " + _letter.Subject;
            (Scene as Scenes.GameScene)?.LetterCompose?.ShowFor(_letter.Sender, subject);
        }

        private void Delete()
        {
            if (_letter is not { } letter) return;
            RequestDialog.Show($"Delete the letter \"{letter.Subject}\"?", onAccept: () =>
            {
                if (MuGame.Network?.GetCharacterService() is { } service) _ = service.SendLetterDeleteRequestAsync(letter.Index);
            });
        }

        protected override void UpdateContent(GameTime gameTime, bool mouseInside, int wheel)
        {
            int visible = BodyHeight / LineHeight - 1;
            if (wheel != 0) _scroll -= Math.Sign(wheel) * 2;
            _scroll = Math.Clamp(_scroll, 0, Math.Max(0, _lines.Count - visible));
            _hoveredButton = -1;
            for (int i = 0; i < 3; i++)
                if (Translate(ButtonLocal(i)).Contains(MousePosition)) _hoveredButton = i;
        }

        protected override bool HandleClick()
        {
            switch (_hoveredButton)
            {
                case 0: Reply(); return true;
                case 1: Delete(); return true;
                case 2: Hide(); return true;
                default: return false;
            }
        }

        protected override void DrawContent(SpriteBatch sb, GameTime gameTime)
        {
            if (_letter == null) return;
            var info = Translate(new Rectangle(Pad, InfoY, Width - Pad * 2, 50));
            PlayerShopUi.DrawPanel(sb, info, ModernHudTheme.BgDarkest, Alpha);
            PlayerShopUi.DrawText(sb, "From", new Vector2(info.X + 10, info.Y + 6), ModernHudTheme.TextGray, 11f, Alpha);
            PlayerShopUi.DrawText(sb, _letter.Sender, new Vector2(info.X + 70, info.Y + 5), ModernHudTheme.TextGold, 12f, Alpha);
            PlayerShopUi.DrawText(sb, "Date", new Vector2(info.X + 10, info.Y + 27), ModernHudTheme.TextGray, 11f, Alpha);
            PlayerShopUi.DrawText(sb, PlayerShopUi.Ellipsize(_letter.Timestamp, 11f, info.Width - 80), new Vector2(info.X + 70, info.Y + 27), ModernHudTheme.TextWhite, 11f, Alpha);

            var body = Translate(BodyLocal);
            PlayerShopUi.DrawPanel(sb, body, ModernHudTheme.BgDarkest, Alpha);
            if (_letter.Message == null)
            {
                PlayerShopUi.DrawTextCentered(sb, "Loading letter...", body, ModernHudTheme.TextGray, 12f, Alpha);
            }
            else
            {
                int visible = BodyHeight / LineHeight - 1;
                for (int i = 0; i < visible && _scroll + i < _lines.Count; i++)
                    PlayerShopUi.DrawText(sb, _lines[_scroll + i], new Vector2(body.X + 10, body.Y + 8 + i * LineHeight), ModernHudTheme.TextWhite, TextSize, Alpha);
                DrawScrollBar(sb, new Rectangle(body.X, body.Y + 4, body.Width - 2, body.Height - 8), _scroll, visible, _lines.Count);
            }

            string[] captions = { "REPLY", "DELETE", "CLOSE" };
            Color[] accents = { ModernHudTheme.Success, ModernHudTheme.Danger, ModernHudTheme.Accent };
            for (int i = 0; i < 3; i++)
                PlayerShopUi.DrawButton(sb, Translate(ButtonLocal(i)), captions[i], _hoveredButton == i, true, accents[i], Alpha, 13f);
        }
    }
}
