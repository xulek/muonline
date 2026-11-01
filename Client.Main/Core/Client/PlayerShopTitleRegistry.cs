using System;
using System.Collections.Concurrent;

namespace Client.Main.Core.Client
{
    /// <summary>
    /// Titles of the personal shops that are currently open in the scope, keyed by masked player id.
    /// Filled from 0x3F/00 (and from the own shop open response) and shown above the players' heads.
    /// </summary>
    public sealed class PlayerShopTitleRegistry
    {
        private readonly ConcurrentDictionary<ushort, string> _titles = new();

        public bool HasAny => !_titles.IsEmpty;

        public void Set(ushort playerId, string title)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                _titles.TryRemove(playerId, out _);
                return;
            }

            _titles[playerId] = title.Trim();
        }

        public bool Remove(ushort playerId) => _titles.TryRemove(playerId, out _);

        public void Clear(ushort? keepPlayerId = null)
        {
            foreach (ushort id in _titles.Keys)
            {
                if (keepPlayerId.HasValue && id == keepPlayerId.Value)
                    continue;

                _titles.TryRemove(id, out _);
            }
        }

        public bool TryGet(ushort playerId, out string title)
        {
            if (_titles.IsEmpty)
            {
                title = string.Empty;
                return false;
            }

            return _titles.TryGetValue(playerId, out title!);
        }
    }
}
