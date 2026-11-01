#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace Client.Main.Core.Client
{
    /// <summary>
    /// Friend list entry (0xC0 / 0xC1 / 0xC4).
    /// </summary>
    public sealed class FriendEntry
    {
        public const byte OfflineServerId = 0xFF;

        public string Name { get; init; } = string.Empty;

        /// <summary>Game server the friend is connected to, or <see cref="OfflineServerId"/>.</summary>
        public byte ServerId { get; set; } = OfflineServerId;

        public bool IsOnline => ServerId != OfflineServerId;
    }

    /// <summary>
    /// Letter header of the mailbox (0xC6 AddLetter); the text is filled by 0xC7 OpenLetter.
    /// </summary>
    public sealed class LetterEntry
    {
        /// <summary>Position in the server's letter list; shifts down when an earlier letter is deleted.</summary>
        public ushort Index { get; set; }
        public string Sender { get; init; } = string.Empty;
        public string Timestamp { get; init; } = string.Empty;
        public string Subject { get; init; } = string.Empty;
        public bool IsRead { get; set; }
        public string? Message { get; set; }
    }

    /// <summary>
    /// ChatRoomConnectionInfo (0xCA): where to connect to join a chat room.
    /// </summary>
    public sealed record ChatRoomInvite(string Host, ushort RoomId, uint Token, string FriendName);

    /// <summary>
    /// Messenger state of the local character: friend list, mailbox and pending friend requests.
    /// Equivalent to the original CUIFriendListWindow / CUIMailList backends (UIWindows.cpp) and WSclient ReceiveFriend*/ReceiveLetter*.
    /// </summary>
    public sealed class FriendManager
    {
        public const int MaxNameLength = 10;
        public const int MaxSubjectLength = 32;
        public const int MaxLetterLength = 1000;

        private readonly ILogger<FriendManager> _logger;
        private readonly object _lock = new();
        private readonly List<FriendEntry> _friends = new();
        private readonly List<LetterEntry> _letters = new();

        public FriendManager(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<FriendManager>();
        }

        /// <summary>Raised whenever the friend list or a friend's online state changed.</summary>
        public event Action? FriendListChanged;

        /// <summary>Raised whenever the mailbox changed (letter added, read or removed).</summary>
        public event Action? LettersChanged;

        /// <summary>Raised when the text of a letter arrived (0xC7).</summary>
        public event Action<LetterEntry>? LetterOpened;

        /// <summary>Raised when a new letter arrived while playing (AddLetter with state New).</summary>
        public event Action<LetterEntry>? NewLetterReceived;

        /// <summary>Raised with the result of the own letter send request (0xC5).</summary>
        public event Action<byte>? LetterSendResult;

        /// <summary>Another player wants to add us as friend (0xC2); answer with FriendAddResponse.</summary>
        public event Action<string>? FriendRequestReceived;

        /// <summary>The server opened a chat room for us (0xCA).</summary>
        public event Action<ChatRoomInvite>? ChatRoomOpened;

        /// <summary>A friend logged in (offline → online), original SOUND_FRIEND_LOGIN_ALERT.</summary>
        public event Action<string>? FriendCameOnline;

        /// <summary>Result of the own chat room invitation (0xCB): (success, request id).</summary>
        public event Action<bool, uint>? ChatInvitationResult;

        public bool IsInitialized { get; private set; }
        public int MaxLetters { get; private set; } = 50;

        /// <summary>Own messenger visibility (0xC4 SetFriendOnlineState). The server starts every session online.</summary>
        public bool IsOnline { get; private set; } = true;

        public IReadOnlyList<FriendEntry> GetFriends()
        {
            lock (_lock) return _friends.ToList();
        }

        public IReadOnlyList<LetterEntry> GetLetters()
        {
            lock (_lock) return _letters.ToList();
        }

        public int UnreadLetterCount
        {
            get { lock (_lock) return _letters.Count(l => !l.IsRead); }
        }

        public bool IsFriend(string name)
        {
            lock (_lock) return _friends.Exists(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        // ─────────────────────────── Friends ───────────────────────────

        /// <summary>MessengerInitialization (0xC0): the full friend list after entering the game.</summary>
        public void Initialize(IEnumerable<FriendEntry> friends, int maxLetters)
        {
            lock (_lock)
            {
                _friends.Clear();
                _friends.AddRange(friends);
                SortFriends();
                _letters.Clear();
                MaxLetters = maxLetters > 0 ? maxLetters : MaxLetters;
                IsInitialized = true;
                IsOnline = true;
            }

            _logger.LogDebug("Messenger initialized: {Count} friends, {Max} letters max.", _friends.Count, MaxLetters);
            FriendListChanged?.Invoke();
            LettersChanged?.Invoke();
        }

        public void AddOrUpdateFriend(string name, byte serverId)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            lock (_lock)
            {
                var friend = _friends.Find(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
                if (friend == null) _friends.Add(new FriendEntry { Name = name, ServerId = serverId });
                else friend.ServerId = serverId;
                SortFriends();
            }

            FriendListChanged?.Invoke();
        }

        public void UpdateFriendServer(string name, byte serverId)
        {
            bool changed = false, cameOnline = false;
            lock (_lock)
            {
                var friend = _friends.Find(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
                if (friend != null && friend.ServerId != serverId)
                {
                    cameOnline = !friend.IsOnline && serverId != FriendEntry.OfflineServerId;
                    friend.ServerId = serverId;
                    SortFriends();
                    changed = true;
                }
            }

            if (changed) FriendListChanged?.Invoke();
            if (cameOnline) FriendCameOnline?.Invoke(name);
        }

        public void RemoveFriend(string name)
        {
            int removed;
            lock (_lock) removed = _friends.RemoveAll(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
            if (removed > 0) FriendListChanged?.Invoke();
        }

        public void SetOwnOnlineState(bool online)
        {
            IsOnline = online;
            FriendListChanged?.Invoke();
        }

        private void SortFriends()
            => _friends.Sort((a, b) => a.IsOnline != b.IsOnline ? (a.IsOnline ? -1 : 1) : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

        // ─────────────────────────── Letters ───────────────────────────

        public void AddLetter(LetterEntry letter, bool isNew)
        {
            lock (_lock)
            {
                _letters.RemoveAll(l => l.Index == letter.Index);
                _letters.Add(letter);
                _letters.Sort((a, b) => b.Index.CompareTo(a.Index));
            }

            LettersChanged?.Invoke();
            if (isNew) NewLetterReceived?.Invoke(letter);
        }

        public void SetLetterText(ushort index, string message)
        {
            LetterEntry? letter;
            lock (_lock)
            {
                letter = _letters.Find(l => l.Index == index);
                if (letter != null)
                {
                    letter.Message = message;
                    letter.IsRead = true;
                }
            }

            if (letter == null)
            {
                _logger.LogDebug("Letter text for unknown index {Index}.", index);
                return;
            }

            LettersChanged?.Invoke();
            LetterOpened?.Invoke(letter);
        }

        /// <summary>
        /// RemoveLetter (0xC8). OpenMU addresses letters by their position in the character's letter list,
        /// so the letters after the removed one move down by one.
        /// </summary>
        public void RemoveLetter(ushort index)
        {
            int removed;
            lock (_lock)
            {
                removed = _letters.RemoveAll(l => l.Index == index);
                if (removed > 0)
                    foreach (var letter in _letters)
                        if (letter.Index > index) letter.Index--;
            }
            if (removed > 0) LettersChanged?.Invoke();
        }

        public void RaiseLetterSendResult(byte result) => LetterSendResult?.Invoke(result);
        public void RaiseFriendRequest(string requester) => FriendRequestReceived?.Invoke(requester);
        public void RaiseChatRoomOpened(ChatRoomInvite invite) => ChatRoomOpened?.Invoke(invite);
        public void RaiseChatInvitationResult(bool success, uint requestId) => ChatInvitationResult?.Invoke(success, requestId);

        /// <summary>Drops everything tied to the current session (disconnect / character change).</summary>
        public void Reset()
        {
            lock (_lock)
            {
                _friends.Clear();
                _letters.Clear();
                IsInitialized = false;
                IsOnline = true;
            }

            FriendListChanged?.Invoke();
            LettersChanged?.Invoke();
        }
    }
}
