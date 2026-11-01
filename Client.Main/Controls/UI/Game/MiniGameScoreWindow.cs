#nullable enable
using System;
using System.Collections.Generic;
using Client.Main.Controllers;
using Client.Main.Controls.UI.Game.Common;
using Client.Main.Controls.UI.Game.Friends;
using Client.Main.Controls.UI.Game.Inventory;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Controls.UI.Game
{
    /// <summary>
    /// Score table of a mini game (MiniGameScoreTable 0x93, Devil Square / Chaos Castle), like the original
    /// result window: rank, name, score, bonus experience and Zen, the own entry highlighted.
    /// </summary>
    public sealed class MiniGameScoreWindow : MessengerWindowBase
    {
        public readonly record struct Entry(string Name, uint Score, uint Experience, uint Money);

        private const int Width = 380;
        private const int ListY = HeaderHeight + 10;
        private const int RowHeight = 20;
        private const int MaxRows = 10;
        private const int ButtonY = ListY + 22 + RowHeight * MaxRows + 10;
        private const int Height = ButtonY + 32 + Pad;
        private static readonly Rectangle CloseButtonLocal = new(Width / 2 - 60, ButtonY, 120, 32);

        private readonly List<Entry> _entries = new();
        private int _ownRank;
        private string _title = "EVENT RESULT";
        private bool _closeHovered;

        public MiniGameScoreWindow() : base(Width, Height)
        {
            Align = ControlAlign.HorizontalCenter | ControlAlign.VerticalCenter;
        }

        protected override string Title => _title;
        protected override string? Subtitle => _ownRank > 0 ? $"Your rank: {_ownRank}" : null;

        public void ShowResults(string title, int ownRank, IEnumerable<Entry> entries)
        {
            _title = title;
            _ownRank = ownRank;
            _entries.Clear();
            _entries.AddRange(entries);
            Show();
        }

        protected override void UpdateContent(GameTime gameTime, bool mouseInside, int wheel)
            => _closeHovered = Translate(CloseButtonLocal).Contains(MousePosition);

        protected override bool HandleClick()
        {
            if (!_closeHovered) return false;
            Hide();
            return true;
        }

        protected override void DrawContent(SpriteBatch sb, GameTime gameTime)
        {
            var pixel = GraphicsManager.Instance.Pixel;
            var list = Translate(new Rectangle(Pad, ListY, Width - Pad * 2, 22 + RowHeight * MaxRows));
            PlayerShopUi.DrawPanel(sb, list, ModernHudTheme.BgDarkest, Alpha);
            var header = new Rectangle(list.X + 1, list.Y + 1, list.Width - 2, 20);
            sb.Draw(pixel, header, ModernHudTheme.BgMid * Alpha);
            int rankX = list.X + 8, nameX = list.X + 34, scoreX = list.X + 140, expX = list.X + 210, zenX = list.X + 290;
            PlayerShopUi.DrawText(sb, "#", new Vector2(rankX, header.Y + 3), ModernHudTheme.TextGold, 11f, Alpha);
            PlayerShopUi.DrawText(sb, "Name", new Vector2(nameX, header.Y + 3), ModernHudTheme.TextGold, 11f, Alpha);
            PlayerShopUi.DrawText(sb, "Score", new Vector2(scoreX, header.Y + 3), ModernHudTheme.TextGold, 11f, Alpha);
            PlayerShopUi.DrawText(sb, "Exp", new Vector2(expX, header.Y + 3), ModernHudTheme.TextGold, 11f, Alpha);
            PlayerShopUi.DrawText(sb, "Zen", new Vector2(zenX, header.Y + 3), ModernHudTheme.TextGold, 11f, Alpha);

            if (_entries.Count == 0)
                PlayerShopUi.DrawTextCentered(sb, "No results.", new Rectangle(list.X, list.Y + list.Height / 2 - 8, list.Width, 16), ModernHudTheme.TextGray, 12f, Alpha);

            for (int i = 0; i < _entries.Count && i < MaxRows; i++)
            {
                var entry = _entries[i];
                var row = new Rectangle(list.X + 1, list.Y + 22 + i * RowHeight, list.Width - 2, RowHeight);
                bool own = i + 1 == _ownRank;
                if (own) sb.Draw(pixel, row, ModernHudTheme.Accent * 0.35f * Alpha);
                else if (i % 2 == 1) sb.Draw(pixel, row, Color.White * 0.025f * Alpha);
                Color color = own ? new Color(200, 255, 0) : ModernHudTheme.TextWhite;
                PlayerShopUi.DrawText(sb, (i + 1).ToString(), new Vector2(rankX, row.Y + 3), color, 12f, Alpha);
                PlayerShopUi.DrawText(sb, PlayerShopUi.Ellipsize(entry.Name, 12f, scoreX - nameX - 6), new Vector2(nameX, row.Y + 3), color, 12f, Alpha);
                PlayerShopUi.DrawText(sb, entry.Score.ToString("N0"), new Vector2(scoreX, row.Y + 3), color, 12f, Alpha);
                PlayerShopUi.DrawText(sb, entry.Experience.ToString("N0"), new Vector2(expX, row.Y + 3), ModernHudTheme.Success, 12f, Alpha);
                PlayerShopUi.DrawText(sb, PlayerShopUi.FormatZenShort(entry.Money), new Vector2(zenX, row.Y + 3), ModernHudTheme.TextGold, 12f, Alpha);
            }

            PlayerShopUi.DrawButton(sb, Translate(CloseButtonLocal), "CLOSE", _closeHovered, true, ModernHudTheme.Accent, Alpha, 13f);
        }
    }
}
