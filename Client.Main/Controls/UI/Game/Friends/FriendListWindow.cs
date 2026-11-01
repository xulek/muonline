#nullable enable
using System;
using System.Collections.Generic;
using Client.Main.Controllers;
using Client.Main.Controls.UI.Game.Common;
using Client.Main.Controls.UI.Game.Inventory;
using Client.Main.Core.Client;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Controls.UI.Game.Friends
{
    /// <summary>
    /// Friend window (hotkey F), modelled on the original CUIFriendListWindow / CUIMailList (UIWindows.cpp):
    /// friends with online state (add, delete, chat, write letter) and the letter box (read, write, delete).
    /// </summary>
    public sealed class FriendListWindow : MessengerWindowBase
    {
        private enum Tab { Friends, Mail }

        private const int Width = 300;
        private const int TabY = HeaderHeight + 8;
        private const int TabHeight = 24;
        private const int ListY = TabY + TabHeight + 8;
        private const int RowHeight = 20;
        private const int VisibleRows = 12;
        private const int ListHeight = RowHeight * VisibleRows + 20;
        private const int InputY = ListY + ListHeight + 8;
        private const int ButtonY = InputY + 32;
        private const int Height = ButtonY + 32 + Pad;

        private static readonly string[] TabNames = { "FRIENDS", "LETTERS" };
        private static readonly Rectangle ListLocal = new(Pad, ListY, Width - Pad * 2, ListHeight);
        private static readonly Rectangle OnlineLocal = new(16, 17, 56, 16);
        private static readonly Rectangle AddLocal = new(Width - Pad - 70, InputY, 70, 24);

        private readonly FriendManager _friends;
        private readonly TextFieldControl _nameField;
        private Tab _tab = Tab.Friends;
        private int _scroll;
        private string? _selectedFriend;
        private int? _selectedLetter;
        private int _hoveredTab = -1, _hoveredRow = -1, _hoveredButton = -1;
        private bool _addHovered, _onlineHovered;

        public FriendListWindow(FriendManager friends) : base(Width, Height)
        {
            _friends = friends;
            Align = ControlAlign.Right | ControlAlign.VerticalCenter;
            Margin = new Margin { Right = 320 };
            _nameField = AddField(Pad, InputY, Width - Pad * 2 - 78, FriendManager.MaxNameLength);
            _nameField.EnterKeyPressed += (_, _) => MuGame.ScheduleOnMainThread(AddFriend);
        }

        protected override string Title => "FRIENDS";
        protected override string? Subtitle => _tab == Tab.Mail
            ? $"Letters {_friends.GetLetters().Count} / {_friends.MaxLetters}"
            : $"{CountOnline()} / {_friends.GetFriends().Count} online";

        private int CountOnline()
        {
            int online = 0;
            foreach (var friend in _friends.GetFriends()) if (friend.IsOnline) online++;
            return online;
        }

        public void ShowLetters()
        {
            _tab = Tab.Mail;
            _scroll = 0;
            Show();
        }

        private static Rectangle TabLocal(int index)
        {
            int width = (Width - Pad * 2 - 4) / 2;
            return new Rectangle(Pad + index * (width + 4), TabY, width, TabHeight);
        }

        private static Rectangle ButtonLocal(int index, int count)
        {
            int width = (Width - Pad * 2 - (count - 1) * 6) / count;
            return new Rectangle(Pad + index * (width + 6), ButtonY, width, 32);
        }

        private static Rectangle RowLocal(int visibleIndex)
            => new(Pad + 1, ListY + 20 + visibleIndex * RowHeight, Width - Pad * 2 - 2, RowHeight);

        private int RowCount => _tab == Tab.Friends ? _friends.GetFriends().Count : _friends.GetLetters().Count;

        // ─────────────────────────────── Actions ───────────────────────────────

        private readonly record struct ButtonSpec(string Text, bool Enabled, Color Accent, Action OnClick);

        private List<ButtonSpec> GetButtons()
        {
            if (_tab == Tab.Friends)
            {
                var friend = SelectedFriend;
                return new List<ButtonSpec>
                {
                    new("CHAT", friend?.IsOnline == true, ModernHudTheme.Accent, () => OpenChat(friend!)),
                    new("LETTER", friend != null, ModernHudTheme.Accent, () => ComposeTo(friend!.Name)),
                    new("DELETE", friend != null, ModernHudTheme.Danger, () => ConfirmDelete(friend!)),
                };
            }

            var letter = SelectedLetter;
            return new List<ButtonSpec>
            {
                new("READ", letter != null, ModernHudTheme.Accent, () => OpenLetter(letter!)),
                new("WRITE", true, ModernHudTheme.Success, () => ComposeTo(string.Empty)),
                new("DELETE", letter != null, ModernHudTheme.Danger, () => ConfirmDeleteLetter(letter!)),
            };
        }

        private FriendEntry? SelectedFriend
        {
            get
            {
                if (_selectedFriend == null) return null;
                foreach (var friend in _friends.GetFriends())
                    if (friend.Name == _selectedFriend) return friend;
                return null;
            }
        }

        private LetterEntry? SelectedLetter
        {
            get
            {
                if (_selectedLetter == null) return null;
                foreach (var letter in _friends.GetLetters())
                    if (letter.Index == _selectedLetter) return letter;
                return null;
            }
        }

        private static Networking.Services.CharacterService? Service => MuGame.Network?.GetCharacterService();

        private void AddFriend()
        {
            string name = _nameField.Value.Trim();
            if (name.Length == 0) return;
            if (_friends.IsFriend(name))
            {
                RequestDialog.ShowInfo($"{name} is already on your friend list.");
                return;
            }
            if (Service is { } service)
            {
                _ = service.SendFriendAddRequestAsync(name);
                _nameField.Value = string.Empty;
            }
        }

        private void ToggleOnline()
        {
            bool online = !_friends.IsOnline;
            if (Service is { } service)
            {
                _ = service.SendSetFriendOnlineStateAsync(online);
                _friends.SetOwnOnlineState(online);
            }
        }

        private static void OpenChat(FriendEntry friend)
        {
            if (Service is { } service) _ = service.SendChatRoomCreateRequestAsync(friend.Name);
        }

        private void ComposeTo(string receiver)
            => (Scene as Scenes.GameScene)?.LetterCompose?.ShowFor(receiver, string.Empty);

        private void OpenLetter(LetterEntry letter)
            => (Scene as Scenes.GameScene)?.LetterRead?.Open(letter);

        private void ConfirmDelete(FriendEntry friend)
            => RequestDialog.Show($"Remove {friend.Name} from your friend list?",
                onAccept: () => { if (Service is { } service) _ = service.SendFriendDeleteAsync(friend.Name); });

        private static void ConfirmDeleteLetter(LetterEntry letter)
            => RequestDialog.Show($"Delete the letter \"{letter.Subject}\" from {letter.Sender}?",
                onAccept: () => { if (Service is { } service) _ = service.SendLetterDeleteRequestAsync(letter.Index); });

        // ─────────────────────────────── Update ───────────────────────────────

        protected override void UpdateContent(GameTime gameTime, bool mouseInside, int wheel)
        {
            _nameField.Visible = _tab == Tab.Friends;
            int total = RowCount;
            if (wheel != 0 && Translate(ListLocal).Contains(MousePosition)) _scroll -= Math.Sign(wheel) * 3;
            _scroll = Math.Clamp(_scroll, 0, Math.Max(0, total - VisibleRows));

            _hoveredTab = -1;
            for (int i = 0; i < TabNames.Length; i++)
                if (Translate(TabLocal(i)).Contains(MousePosition)) _hoveredTab = i;
            _hoveredRow = -1;
            for (int i = 0; i < VisibleRows && _scroll + i < total; i++)
                if (Translate(RowLocal(i)).Contains(MousePosition)) _hoveredRow = _scroll + i;
            var buttons = GetButtons();
            _hoveredButton = -1;
            for (int i = 0; i < buttons.Count; i++)
                if (Translate(ButtonLocal(i, buttons.Count)).Contains(MousePosition)) _hoveredButton = i;
            _addHovered = _tab == Tab.Friends && Translate(AddLocal).Contains(MousePosition);
            _onlineHovered = Translate(OnlineLocal).Contains(MousePosition);
        }

        protected override bool HandleClick()
        {
            if (_onlineHovered) { ToggleOnline(); return true; }
            if (_hoveredTab >= 0)
            {
                _tab = (Tab)_hoveredTab;
                _scroll = 0;
                return true;
            }
            if (_addHovered) { AddFriend(); return true; }
            if (_hoveredRow >= 0)
            {
                if (_tab == Tab.Friends)
                {
                    var friend = _friends.GetFriends()[_hoveredRow];
                    _selectedFriend = friend.Name;
                }
                else
                {
                    var letter = _friends.GetLetters()[_hoveredRow];
                    // Double-click like behaviour: clicking the selected letter opens it.
                    if (_selectedLetter == letter.Index) OpenLetter(letter);
                    _selectedLetter = letter.Index;
                }
                return true;
            }
            var buttons = GetButtons();
            if (_hoveredButton >= 0 && buttons[_hoveredButton].Enabled)
            {
                buttons[_hoveredButton].OnClick();
                return true;
            }
            return false;
        }

        // ─────────────────────────────── Draw ───────────────────────────────

        protected override void DrawContent(SpriteBatch sb, GameTime gameTime)
        {
            var pixel = GraphicsManager.Instance.Pixel;
            PlayerShopUi.DrawPill(sb, Translate(OnlineLocal), _friends.IsOnline ? "ONLINE" : "OFFLINE",
                _friends.IsOnline ? ModernHudTheme.Success : ModernHudTheme.TextGray, Alpha);

            for (int i = 0; i < TabNames.Length; i++)
            {
                bool active = (int)_tab == i;
                string caption = i == 1 && _friends.UnreadLetterCount > 0 ? $"{TabNames[i]} ({_friends.UnreadLetterCount})" : TabNames[i];
                PlayerShopUi.DrawButton(sb, Translate(TabLocal(i)), caption, _hoveredTab == i || active, true,
                    active ? ModernHudTheme.AccentBright : ModernHudTheme.Accent, Alpha, 12f);
            }

            var list = Translate(ListLocal);
            PlayerShopUi.DrawPanel(sb, list, ModernHudTheme.BgDarkest, Alpha);
            var header = new Rectangle(list.X + 1, list.Y + 1, list.Width - 2, 19);
            sb.Draw(pixel, header, ModernHudTheme.BgMid * Alpha);

            if (_tab == Tab.Friends) DrawFriends(sb, list, header);
            else DrawLetters(sb, list, header);

            DrawScrollBar(sb, new Rectangle(list.X, list.Y + 20, list.Width, RowHeight * VisibleRows), _scroll, VisibleRows, RowCount);

            if (_tab == Tab.Friends)
                PlayerShopUi.DrawButton(sb, Translate(AddLocal), "ADD", _addHovered, _nameField.Value.Trim().Length > 0, ModernHudTheme.Success, Alpha, 12f);

            var buttons = GetButtons();
            for (int i = 0; i < buttons.Count; i++)
                PlayerShopUi.DrawButton(sb, Translate(ButtonLocal(i, buttons.Count)), buttons[i].Text, _hoveredButton == i, buttons[i].Enabled, buttons[i].Accent, Alpha, 13f);
        }

        private void DrawRowBackground(SpriteBatch sb, Rectangle row, int index, bool selected)
        {
            var pixel = GraphicsManager.Instance.Pixel;
            if (selected) sb.Draw(pixel, row, ModernHudTheme.Accent * 0.35f * Alpha);
            else if (_hoveredRow == index) sb.Draw(pixel, row, Color.White * 0.06f * Alpha);
            else if (index % 2 == 1) sb.Draw(pixel, row, Color.White * 0.025f * Alpha);
        }

        private void DrawFriends(SpriteBatch sb, Rectangle list, Rectangle header)
        {
            int nameX = list.X + 10, stateX = list.Right - 90;
            PlayerShopUi.DrawText(sb, "Name", new Vector2(nameX, header.Y + 3), ModernHudTheme.TextGold, 11f, Alpha);
            PlayerShopUi.DrawText(sb, "Server", new Vector2(stateX, header.Y + 3), ModernHudTheme.TextGold, 11f, Alpha);

            var friends = _friends.GetFriends();
            if (friends.Count == 0)
            {
                PlayerShopUi.DrawTextCentered(sb, _friends.IsInitialized ? "Your friend list is empty." : "Loading...",
                    new Rectangle(list.X, list.Y + list.Height / 2 - 8, list.Width, 16), ModernHudTheme.TextGray, 12f, Alpha);
                return;
            }

            for (int i = 0; i < VisibleRows && _scroll + i < friends.Count; i++)
            {
                var friend = friends[_scroll + i];
                var row = Translate(RowLocal(i));
                DrawRowBackground(sb, row, _scroll + i, friend.Name == _selectedFriend);
                var dot = new Rectangle(row.X + 4, row.Y + RowHeight / 2 - 3, 6, 6);
                sb.Draw(GraphicsManager.Instance.Pixel, dot, (friend.IsOnline ? ModernHudTheme.Success : ModernHudTheme.TextGray * 0.5f) * Alpha);
                PlayerShopUi.DrawText(sb, friend.Name, new Vector2(nameX + 6, row.Y + 3),
                    friend.IsOnline ? ModernHudTheme.TextWhite : ModernHudTheme.TextGray * 0.8f, 12f, Alpha);
                PlayerShopUi.DrawText(sb, friend.IsOnline ? $"Server {friend.ServerId + 1}" : "Offline", new Vector2(stateX, row.Y + 3),
                    friend.IsOnline ? ModernHudTheme.Success : ModernHudTheme.TextGray * 0.7f, 11f, Alpha);
            }
        }

        private void DrawLetters(SpriteBatch sb, Rectangle list, Rectangle header)
        {
            int fromX = list.X + 10, subjectX = list.X + 100;
            PlayerShopUi.DrawText(sb, "From", new Vector2(fromX, header.Y + 3), ModernHudTheme.TextGold, 11f, Alpha);
            PlayerShopUi.DrawText(sb, "Subject", new Vector2(subjectX, header.Y + 3), ModernHudTheme.TextGold, 11f, Alpha);

            var letters = _friends.GetLetters();
            if (letters.Count == 0)
            {
                PlayerShopUi.DrawTextCentered(sb, "Your letter box is empty.", new Rectangle(list.X, list.Y + list.Height / 2 - 8, list.Width, 16),
                    ModernHudTheme.TextGray, 12f, Alpha);
                return;
            }

            for (int i = 0; i < VisibleRows && _scroll + i < letters.Count; i++)
            {
                var letter = letters[_scroll + i];
                var row = Translate(RowLocal(i));
                DrawRowBackground(sb, row, _scroll + i, letter.Index == _selectedLetter);
                Color color = letter.IsRead ? ModernHudTheme.TextGray : ModernHudTheme.TextWhite;
                if (!letter.IsRead)
                    sb.Draw(GraphicsManager.Instance.Pixel, new Rectangle(row.X + 4, row.Y + RowHeight / 2 - 3, 6, 6), ModernHudTheme.TextGold * Alpha);
                PlayerShopUi.DrawText(sb, PlayerShopUi.Ellipsize(letter.Sender, 12f, subjectX - fromX - 12), new Vector2(fromX + 6, row.Y + 3), color, 12f, Alpha);
                PlayerShopUi.DrawText(sb, PlayerShopUi.Ellipsize(letter.Subject, 12f, row.Right - subjectX - 8), new Vector2(subjectX, row.Y + 3), color, 12f, Alpha);
            }
        }

        protected override void DrawOverlay(SpriteBatch sb, GameTime gameTime)
            => DrawPlaceholder(sb, _nameField, "Character name to add");
    }
}
