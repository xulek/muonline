#nullable enable
using System;
using System.Collections.Generic;
using Client.Main.Controls.UI.Game.Common;
using Client.Main.Controllers;
using Client.Main.Controls.UI.Game.Inventory;
using Client.Main.Core.Client;
using Client.Main.Models;
using Client.Main.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Controls.UI.Game.Friends
{
    /// <summary>
    /// Private chat window for one chat room, modelled on the original CUIChatWindow (UIWindows.cpp).
    /// Closing the window leaves the room (LeaveChatRoom) and closes the chat server connection.
    /// </summary>
    public sealed class ChatRoomWindow : MessengerWindowBase
    {
        private const int Width = 320;
        private const int LogY = HeaderHeight + 8;
        private const int LogHeight = 230;
        private const int InputY = LogY + LogHeight + 8;
        private const int Height = InputY + 24 + Pad;
        private const float TextSize = 12f;
        private const int LineHeight = 17;
        private const int MaxHistory = 200;

        private static readonly Rectangle LogLocal = new(Pad, LogY, Width - Pad * 2, LogHeight);
        private static readonly Rectangle SendLocal = new(Width - Pad - 64, InputY, 64, 24);
        private static readonly Rectangle InviteLocal = new(Width - 34 - 66, 14, 60, 18);
        private const int PickerRowHeight = 20;

        private readonly ChatRoomSession _session;
        private readonly string _ownName;
        private readonly TextFieldControl _input;
        private readonly List<(string Sender, string Text, Color Color)> _messages = new();
        private List<(string Text, Color Color)> _lines = new();
        private int _scrollFromBottom;
        private bool _sendHovered, _inviteHovered;
        private bool _pickerOpen;
        private int _hoveredPick = -1;
        private readonly List<string> _pickable = new();
        private readonly Dictionary<uint, string> _pendingInvites = new();
        private static readonly Random InviteIds = new();
        private FriendManager? Friends => MuGame.Network?.GetFriendManager();
        private bool _closing;

        public ChatRoomWindow(ChatRoomSession session, string ownName) : base(Width, Height)
        {
            _session = session;
            _ownName = ownName;
            Align = ControlAlign.HorizontalCenter | ControlAlign.VerticalCenter;
            _input = AddField(Pad, InputY, Width - Pad * 2 - 70, 60);
            _input.EnterKeyPressed += (_, _) => MuGame.ScheduleOnMainThread(SendInput);
            _session.MessageReceived += OnMessage;
            _session.ClientChanged += OnClientChanged;
            _session.Disconnected += OnDisconnected;
            if (Friends is { } friends) friends.ChatInvitationResult += OnInvitationResult;
            AddLine(string.Empty, $"Connecting to the chat with {session.FriendName}...", ModernHudTheme.TextGray);
        }

        public ushort RoomId => _session.RoomId;

        protected override string Title => "CHAT";
        protected override string? Subtitle
        {
            get
            {
                var names = _session.GetClientNames();
                var others = new List<string>();
                foreach (var name in names) if (name != _ownName) others.Add(name);
                return others.Count > 0 ? string.Join(", ", others) : _session.FriendName;
            }
        }

        public override void Show()
        {
            base.Show();
            FocusField(_input);
        }

        /// <summary>Closing the window leaves the chat room.</summary>
        public override void Hide()
        {
            base.Hide();
            if (_closing) return;
            _closing = true;
            _session.MessageReceived -= OnMessage;
            _session.ClientChanged -= OnClientChanged;
            _session.Disconnected -= OnDisconnected;
            if (Friends is { } friends) friends.ChatInvitationResult -= OnInvitationResult;
            _ = _session.LeaveAsync();
            MuGame.ScheduleOnMainThread(() =>
            {
                Parent?.Controls.Remove(this);
                Dispose();
            });
        }

        private void SendInput()
        {
            string text = _input.Value.Trim();
            if (text.Length == 0) return;
            _input.Value = string.Empty;
            _ = SendAsync(text);
        }

        private async System.Threading.Tasks.Task SendAsync(string text)
        {
            if (!await _session.SendMessageAsync(text))
                MuGame.ScheduleOnMainThread(() => AddLine(string.Empty, "The message could not be sent.", ModernHudTheme.Danger));
        }

        private void OnMessage(string sender, string text)
            => MuGame.ScheduleOnMainThread(() =>
            {
                bool own = sender == _ownName;
                AddLine(sender, text, own ? new Color(200, 255, 0) : ModernHudTheme.TextWhite);
                // Original SOUND_FRIEND_CHAT_ALERT when a message arrives while the window is not in use.
                if (!own && !IsFocused(_input))
                    SoundController.Instance.PlayBuffer("Sound/iFMSGAlert.wav");
            });

        private void OnInvitationResult(bool success, uint requestId)
        {
            if (!_pendingInvites.Remove(requestId, out var name)) return;
            AddLine(string.Empty, success ? $"{name} has been invited." : $"{name} could not be invited.",
                success ? ModernHudTheme.TextGray : ModernHudTheme.Danger);
        }

        private void RefreshPickable()
        {
            _pickable.Clear();
            var inRoom = new HashSet<string>(_session.GetClientNames(), StringComparer.OrdinalIgnoreCase);
            if (Friends is not { } friends) return;
            foreach (var friend in friends.GetFriends())
                if (friend.IsOnline && !inRoom.Contains(friend.Name)) _pickable.Add(friend.Name);
        }

        private void Invite(string name)
        {
            _pickerOpen = false;
            if (MuGame.Network?.GetCharacterService() is not { } service) return;
            uint requestId = (uint)InviteIds.Next(1, int.MaxValue);
            _pendingInvites[requestId] = name;
            _ = service.SendChatRoomInvitationAsync(name, _session.RoomId, requestId);
            AddLine(string.Empty, $"Inviting {name}...", ModernHudTheme.TextGray);
        }

        private Rectangle PickerRowLocal(int index)
            => new(LogLocal.Right - 150, LogLocal.Y + 4 + index * PickerRowHeight, 146, PickerRowHeight);

        private void OnClientChanged(string name, bool joined)
        {
            if (string.IsNullOrEmpty(name) || name == _ownName) return;
            MuGame.ScheduleOnMainThread(() => AddLine(string.Empty, joined ? $"{name} joined the chat." : $"{name} left the chat.", ModernHudTheme.TextGray));
        }

        private void OnDisconnected()
            => MuGame.ScheduleOnMainThread(() => AddLine(string.Empty, "The chat connection was closed.", ModernHudTheme.Danger));

        private void AddLine(string sender, string text, Color color)
        {
            _messages.Add((sender, text, color));
            if (_messages.Count > MaxHistory) _messages.RemoveAt(0);
            RebuildLines();
            _scrollFromBottom = 0;
        }

        private void RebuildLines()
        {
            _lines = new List<(string, Color)>();
            foreach (var (sender, text, color) in _messages)
            {
                string full = sender.Length > 0 ? $"{sender}: {text}" : text;
                foreach (var line in WrapText(full, TextSize, LogLocal.Width - 20))
                    _lines.Add((line, color));
            }
        }

        protected override void UpdateContent(GameTime gameTime, bool mouseInside, int wheel)
        {
            int visible = LogHeight / LineHeight - 1;
            if (wheel != 0) _scrollFromBottom += Math.Sign(wheel) * 2;
            _scrollFromBottom = Math.Clamp(_scrollFromBottom, 0, Math.Max(0, _lines.Count - visible));
            _sendHovered = Translate(SendLocal).Contains(MousePosition);
            _inviteHovered = Translate(InviteLocal).Contains(MousePosition);
            _hoveredPick = -1;
            if (_pickerOpen)
            {
                RefreshPickable();
                for (int i = 0; i < _pickable.Count; i++)
                    if (Translate(PickerRowLocal(i)).Contains(MousePosition)) _hoveredPick = i;
            }
        }

        protected override bool HandleClick()
        {
            if (_pickerOpen && _hoveredPick >= 0) { Invite(_pickable[_hoveredPick]); return true; }
            if (_inviteHovered)
            {
                _pickerOpen = !_pickerOpen && _session.IsConnected;
                RefreshPickable();
                return true;
            }
            _pickerOpen = false;
            if (_sendHovered) { SendInput(); return true; }
            return false;
        }

        protected override void DrawContent(SpriteBatch sb, GameTime gameTime)
        {
            var log = Translate(LogLocal);
            PlayerShopUi.DrawPanel(sb, log, ModernHudTheme.BgDarkest, Alpha);
            int visible = LogHeight / LineHeight - 1;
            int end = _lines.Count - _scrollFromBottom;
            int start = Math.Max(0, end - visible);
            for (int i = start; i < end; i++)
                PlayerShopUi.DrawText(sb, _lines[i].Text, new Vector2(log.X + 10, log.Y + 8 + (i - start) * LineHeight), _lines[i].Color, TextSize, Alpha);
            DrawScrollBar(sb, new Rectangle(log.X, log.Y + 4, log.Width - 2, log.Height - 8), Math.Max(0, _lines.Count - visible - _scrollFromBottom), visible, _lines.Count);

            PlayerShopUi.DrawButton(sb, Translate(SendLocal), "SEND", _sendHovered, _session.IsConnected && _input.Value.Trim().Length > 0,
                ModernHudTheme.Success, Alpha, 12f);
            PlayerShopUi.DrawButton(sb, Translate(InviteLocal), "INVITE", _inviteHovered || _pickerOpen, _session.IsConnected,
                ModernHudTheme.Accent, Alpha, 10f);

            if (!_pickerOpen) return;
            var pixel = GraphicsManager.Instance.Pixel;
            int rows = Math.Max(1, _pickable.Count);
            var first = Translate(PickerRowLocal(0));
            var panel = new Rectangle(first.X - 2, first.Y - 2, first.Width + 4, rows * PickerRowHeight + 4);
            PlayerShopUi.DrawPanel(sb, panel, ModernHudTheme.BgMid, Alpha);
            if (_pickable.Count == 0)
            {
                PlayerShopUi.DrawTextCentered(sb, "No other friend online", first, ModernHudTheme.TextGray, 11f, Alpha);
                return;
            }
            for (int i = 0; i < _pickable.Count; i++)
            {
                var row = Translate(PickerRowLocal(i));
                if (_hoveredPick == i) sb.Draw(pixel, row, ModernHudTheme.Accent * 0.4f * Alpha);
                PlayerShopUi.DrawText(sb, _pickable[i], new Vector2(row.X + 8, row.Y + 3), ModernHudTheme.TextWhite, 12f, Alpha);
            }
        }

        protected override void DrawOverlay(SpriteBatch sb, GameTime gameTime)
            => DrawPlaceholder(sb, _input, "Type a message and press Enter");
    }
}
