#nullable enable
using System;
using System.Collections.Generic;
using Client.Main.Controls.UI.Game.Common;
using Client.Main.Controls.UI.Game.Inventory;
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

        private readonly ChatRoomSession _session;
        private readonly string _ownName;
        private readonly TextFieldControl _input;
        private readonly List<(string Sender, string Text, Color Color)> _messages = new();
        private List<(string Text, Color Color)> _lines = new();
        private int _scrollFromBottom;
        private bool _sendHovered;
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
            => MuGame.ScheduleOnMainThread(() => AddLine(sender, text, sender == _ownName ? new Color(200, 255, 0) : ModernHudTheme.TextWhite));

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
        }

        protected override bool HandleClick()
        {
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
        }

        protected override void DrawOverlay(SpriteBatch sb, GameTime gameTime)
            => DrawPlaceholder(sb, _input, "Type a message and press Enter");
    }
}
