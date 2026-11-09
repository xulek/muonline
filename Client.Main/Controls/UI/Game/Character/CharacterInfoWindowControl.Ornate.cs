using System;
using System.Collections.Generic;
using Client.Main.Controllers;
using Client.Main.Controls.UI.Common;
using Client.Main.Controls.UI.Ornate;
using Client.Main.Controls.UI.SelectCharacter;
using Client.Main.Core.Utilities;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Client.Main.Controls.UI.Game.Character
{
    // Modern theme: the window is drawn with the ornate kit from the same snapshot as the Classic one. Header with the
    // class medallion, level and experience; a banner while points are free; one card per stat with its "+" button and
    // what the stat gives; a combat section with PvM rates and fruits.
    public partial class CharacterInfoWindowControl
    {
        private const int OrnateWidth = 470;
        private const int OrnateMinTop = 70; // keeps the title plaque clear of the map name box
        private const int OrnatePad = 14;
        private const int HeaderTop = 42, HeaderHeight = 66;
        private const int BannerHeight = 24;
        private const int CardHeaderHeight = 24, DetailLineHeight = 16, CardGap = 6;

        private static readonly string[] StatNames = { "Strength", "Agility", "Vitality", "Energy", "Command" };
        private static readonly Color AddedColor = new(130, 220, 130);

        private sealed class OrnateStatCard
        {
            public int Index;
            public string Value = string.Empty;
            public string Added = string.Empty;
            public readonly List<(string Label, string Value)> Details = new();
            public Rectangle Local;
            public Rectangle PlusLocal;
            public bool CanAdd;
        }

        private readonly List<OrnateStatCard> _ornateCards = new();
        private readonly List<(string Label, string Value)> _ornateCombat = new();
        private string _ornateName = string.Empty, _ornateClass = string.Empty, _ornateLevel = string.Empty, _ornateExp = string.Empty;
        private string _ornateEmblem = "class_knight";
        private string _ornatePoints = string.Empty;
        private float _ornateExpPct;
        private Rectangle _ornateCombatLocal;
        private int _ornateHoveredPlus = -1;
        private int _ornatePressedPlus = -1;
        private bool _ornateDragging, _ornateMoved;
        private Point _ornateDragOffset;

        /// <summary>Rebuilds the Modern layout and its texts; called when the snapshot changes.</summary>
        private void BuildOrnate(CharacterInfoSnapshot s)
        {
            _ornateName = string.IsNullOrEmpty(s.Name) ? "Character" : s.Name;
            _ornateClass = CharacterClassDatabase.GetClassName(s.Class);
            _ornateEmblem = CharacterClasses.EmblemFor(s.Class);
            _ornateLevel = s.MasterLevel > 0 ? $"Level {s.Level}   ·   Master {s.MasterLevel}" : $"Level {s.Level}";

            ulong previous = s.Level > 1 ? (ulong)((s.Level - 1 + 9) * (s.Level - 1) * (s.Level - 1) * 10) : 0;
            ulong inLevel = s.Experience >= previous ? s.Experience - previous : 0;
            ulong needed = s.ExperienceForNextLevel > previous ? s.ExperienceForNextLevel - previous : 0;
            _ornateExpPct = needed > 0 ? MathHelper.Clamp((float)(inLevel / (double)needed), 0f, 1f) : 0f;
            _ornateExp = "EXP " + (_ornateExpPct * 100f).ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + "%";

            ushort points = s.MasterLevel > 0 && s.LevelUpPoints == 0 ? s.MasterLevelUpPoints : s.LevelUpPoints;
            _ornatePoints = s.LevelUpPoints > 0
                ? (s.LevelUpPoints == 1 ? "1 point to distribute" : $"{s.LevelUpPoints} points to distribute")
                : points > 0 ? $"{points} master points" : string.Empty;

            _ornateCards.Clear();
            int statCount = s.IsDarkLordFamily ? 5 : 4;
            int top = HeaderTop + HeaderHeight + 8 + BannerHeight + 8;
            int columnWidth = (OrnateWidth - OrnatePad * 2 - CardGap) / 2;
            int[] columnY = { top, top };
            for (int i = 0; i < statCount; i++)
            {
                var card = new OrnateStatCard { Index = i, CanAdd = s.LevelUpPoints > 0 };
                ushort baseValue = s.GetBaseStat(i), added = s.GetAddedStat(i);
                card.Value = baseValue.ToString();
                card.Added = added > 0 ? "+" + added : string.Empty;

                switch (i)
                {
                    case 0:
                        if (s.HasPhysicalDamage)
                        {
                            card.Details.Add(("Attack damage", $"{s.PhysicalMin} ~ {s.PhysicalMax}"));
                            card.Details.Add(("Attack speed", "+" + s.AttackSpeed));
                        }
                        break;
                    case 1:
                        card.Details.Add(("Defense", "+" + s.Defense));
                        card.Details.Add(("Attack rate", s.PvPAttackRate + "%"));
                        card.Details.Add(("Defense rate", s.PvPDefenseRate + "%"));
                        break;
                    case 2:
                        card.Details.Add(("Health", $"{s.CurrentHealth} / {s.MaxHealth}"));
                        card.Details.Add(("Shield", $"{s.CurrentShield} / {s.MaxShield}"));
                        break;
                    case 3:
                        if (s.HasMagicalDamage)
                            card.Details.Add(("Wizardry damage", $"{s.MagicalMin} ~ {s.MagicalMax}"));
                        card.Details.Add(("Mana", $"{s.CurrentMana} / {s.MaxMana}"));
                        card.Details.Add(("Ability (AG)", $"{s.CurrentAbility} / {s.MaxAbility}"));
                        break;
                }

                // Offence (Strength, Agility) on the left; body and mind (Vitality, Energy, Command) on the right.
                int column = i < 2 ? 0 : 1;
                int height = CardHeaderHeight + card.Details.Count * DetailLineHeight + (card.Details.Count > 0 ? 6 : 0);
                card.Local = new Rectangle(OrnatePad + column * (columnWidth + CardGap), columnY[column], columnWidth, height);
                card.PlusLocal = new Rectangle(card.Local.Right - 26, card.Local.Y + 3, 19, 19);
                _ornateCards.Add(card);
                columnY[column] += height + CardGap;
            }

            _ornateCombat.Clear();
            _ornateCombat.Add(("PvM attack success", s.PvMAttackRate + "%"));
            _ornateCombat.Add(("PvM defense success", s.PvMDefenseRate + "%"));
            if (s.Level > 9)
            {
                _ornateCombat.Add(("Fruits used",
                    $"+{s.UsedFruitPoints}/{s.MaxFruitPoints}   -{s.UsedNegativeFruitPoints}/{s.MaxNegativeFruitPoints}"));
                _ornateCombat.Add(("Fruit success",
                    $"+{GetFruitProbability(s.UsedFruitPoints, s.MaxFruitPoints)}%   -{GetFruitProbability(s.UsedNegativeFruitPoints, s.MaxNegativeFruitPoints)}%"));
            }

            int y = Math.Max(columnY[0], columnY[1]) + 2;
            int combatRows = (_ornateCombat.Count + 1) / 2;
            int combatHeight = 24 + combatRows * DetailLineHeight + 6;
            _ornateCombatLocal = new Rectangle(OrnatePad, y, OrnateWidth - OrnatePad * 2, combatHeight);

            var size = new Point(OrnateWidth, _ornateCombatLocal.Bottom + OrnatePad);
            if (ControlSize != size)
            {
                ControlSize = size;
                ViewSize = size;
            }
        }

        private Rectangle OrnateAt(Rectangle local) => new(DisplayRectangle.X + local.X, DisplayRectangle.Y + local.Y, local.Width, local.Height);

        private Rectangle OrnateCloseRect => OrnateAt(new Rectangle(OrnateWidth - 42, 11, 26, 26));

        private void HandleOrnateInput()
        {
            var mouse = MuGame.Instance.UiMouseState;
            var previous = MuGame.Instance.PrevUiMouseState;
            bool pressed = mouse.LeftButton == ButtonState.Pressed && previous.LeftButton == ButtonState.Released;
            bool released = mouse.LeftButton == ButtonState.Released && previous.LeftButton == ButtonState.Pressed;

            Point hit = PointerForHitTest(mouse.Position); // only when no window lies on top of this one
            _closeHovered = OrnateCloseRect.Contains(hit);
            if (released && _closeHovered)
            {
                HideWindow();
                SoundController.Instance.PlayBuffer("Sound/iButtonClick.wav");
                return;
            }

            // Drag by the title area (above the header panel).
            Rectangle dragArea = OrnateAt(new Rectangle(0, 0, OrnateWidth, HeaderTop - 4));
            if (pressed && dragArea.Contains(hit) && !_closeHovered)
            {
                _ornateDragging = true;
                _ornateMoved = true;
                _ornateDragOffset = new Point(mouse.X - X, mouse.Y - Y);
                Align = ControlAlign.None;
            }
            if (_ornateDragging)
            {
                if (mouse.LeftButton == ButtonState.Released)
                {
                    _ornateDragging = false;
                }
                else
                {
                    Point screen = UiScaler.VirtualSize;
                    X = Math.Clamp(mouse.X - _ornateDragOffset.X, 0, Math.Max(0, screen.X - ViewSize.X));
                    Y = Math.Clamp(mouse.Y - _ornateDragOffset.Y, 36, Math.Max(36, screen.Y - ViewSize.Y));
                    return;
                }
            }

            _ornateHoveredPlus = -1;
            foreach (var card in _ornateCards)
            {
                if (card.CanAdd && OrnateAt(card.PlusLocal).Contains(hit))
                    _ornateHoveredPlus = card.Index;
            }

            if (pressed)
                _ornatePressedPlus = _ornateHoveredPlus;
            if (released)
            {
                if (_ornatePressedPlus >= 0 && _ornatePressedPlus == _ornateHoveredPlus)
                    OnStatButtonClicked(_ornatePressedPlus);
                _ornatePressedPlus = -1;
            }
        }

        private void DrawOrnate(SpriteBatch sb)
        {
            float alpha = Alpha;
            Rectangle frame = DisplayRectangle;
            OrnateDraw.Window(sb, frame, alpha);

            DrawOrnateHeader(sb, OrnateAt(new Rectangle(OrnatePad, HeaderTop, OrnateWidth - OrnatePad * 2, HeaderHeight)), alpha);

            var banner = OrnateAt(new Rectangle(OrnatePad + 40, HeaderTop + HeaderHeight + 8, OrnateWidth - OrnatePad * 2 - 80, BannerHeight));
            if (_ornatePoints.Length > 0)
            {
                float glow = 0.85f + 0.15f * MathF.Sin((float)(MuGame.Instance.GameTime?.TotalGameTime.TotalSeconds ?? 0) * 3f);
                OrnateDraw.Plate(sb, OrnateAssets.ButtonPrimaryHover, OrnateStyle.ButtonPrimaryHover, banner, true, Color.White * (alpha * glow), overhang: 1f);
                OrnateDraw.TextCentered(sb, _ornatePoints, banner, 13f, Color.White, alpha, bold: true);
            }
            else
            {
                OrnateDraw.Divider(sb, new Rectangle(banner.X, banner.Center.Y - 6, banner.Width, 12), alpha);
            }

            foreach (var card in _ornateCards)
                DrawOrnateCard(sb, card, alpha);

            Rectangle combat = OrnateAt(_ornateCombatLocal);
            Panel(sb, combat, alpha);
            OrnateWidgets.SectionHeader(sb, new Rectangle(combat.X + 4, combat.Y, combat.Width - 8, 24), "Combat", alpha);
            // Two columns, filled row by row.
            int columnWidth = (combat.Width - 20 - 24) / 2;
            for (int i = 0; i < _ornateCombat.Count; i++)
            {
                var (label, value) = _ornateCombat[i];
                var row = new Rectangle(combat.X + 10 + i % 2 * (columnWidth + 24), combat.Y + 24 + i / 2 * DetailLineHeight, columnWidth, DetailLineHeight);
                DetailRow(sb, row, label, value, alpha);
            }

            OrnateWidgets.CloseButton(sb, OrnateCloseRect, _closeHovered, alpha);
            OrnateDraw.TitlePlaque(sb, _ornateName, frame.Center.X, frame.Y + 2, 30, alpha);
        }

        private void DrawOrnateHeader(SpriteBatch sb, Rectangle area, float alpha)
        {
            Panel(sb, area, alpha);

            var medallion = new Rectangle(area.X + 6, area.Center.Y - 28, 56, 56);
            OrnateDraw.Icon(sb, _ornateEmblem, medallion, alpha);

            int textX = medallion.Right + 10;
            int textWidth = area.Right - textX - 12;
            OrnateDraw.Text(sb, _ornateClass, new Vector2(textX, area.Y + 7), 14f, OrnateStyle.Gold, alpha, bold: true);
            OrnateDraw.Text(sb, _ornateLevel, new Vector2(textX, area.Y + 26), 12f, OrnateStyle.TextLight, alpha);

            // Experience to the next level.
            var bar = new Rectangle(textX, area.Bottom - 16, textWidth, 8);
            Texture2D pixel = GraphicsManager.Instance.Pixel;
            sb.Draw(pixel, new Rectangle(bar.X - 1, bar.Y - 1, bar.Width + 2, bar.Height + 2), OrnateItemUi.Rim * alpha);
            sb.Draw(pixel, bar, new Color(10, 9, 8) * alpha);
            int fill = (int)(bar.Width * _ornateExpPct);
            if (fill > 0)
            {
                var filled = new Rectangle(bar.X, bar.Y, fill, bar.Height);
                UiDrawHelper.DrawVerticalGradient(sb, filled, new Color(250, 214, 120) * alpha, new Color(132, 92, 30) * alpha, 8);
            }
            OrnateDraw.TextRight(sb, _ornateExp, new Rectangle(bar.X, bar.Y - 17, bar.Width, 14), 10f, OrnateStyle.TextMuted, alpha);
        }

        private void DrawOrnateCard(SpriteBatch sb, OrnateStatCard card, float alpha)
        {
            Rectangle rect = OrnateAt(card.Local);
            Panel(sb, rect, alpha);

            var header = new Rectangle(rect.X + 10, rect.Y, rect.Width - 20, CardHeaderHeight);
            OrnateDraw.TextLeft(sb, StatNames[card.Index], header, 13f, OrnateStyle.Gold, alpha, bold: true);

            // Value (and the bonus from items) right-aligned, left of the "+" button.
            int valueRight = card.CanAdd ? OrnateAt(card.PlusLocal).X - 8 : header.Right;
            var valueArea = new Rectangle(header.X, header.Y, valueRight - header.X, header.Height);
            float addedWidth = card.Added.Length > 0 ? OrnateDraw.Measure(card.Added, 11f).X + 3 : 0f;
            if (card.Added.Length > 0)
                OrnateDraw.TextRight(sb, card.Added, valueArea, 11f, AddedColor, alpha, bold: true);
            OrnateDraw.TextRight(sb, card.Value, new Rectangle(valueArea.X, valueArea.Y, valueArea.Width - (int)addedWidth, valueArea.Height),
                13f, Color.White, alpha, bold: true);

            if (card.CanAdd)
                DrawPlus(sb, OrnateAt(card.PlusLocal), _ornateHoveredPlus == card.Index, _ornatePressedPlus == card.Index, alpha);

            if (card.Details.Count == 0)
                return;

            UiDrawHelper.DrawHorizontalGradient(sb, new Rectangle(header.X, header.Bottom, header.Width, 1),
                OrnateStyle.Bronze * (0.7f * alpha), Color.Transparent, 16);
            int y = header.Bottom + 3;
            foreach (var (label, value) in card.Details)
            {
                DetailRow(sb, new Rectangle(header.X, y, header.Width, DetailLineHeight), label, value, alpha);
                y += DetailLineHeight;
            }
        }

        private static void DrawPlus(SpriteBatch sb, Rectangle rect, bool hover, bool pressed, float alpha)
        {
            if (pressed)
                rect.Y += 1;
            UiDrawHelper.DrawVerticalGradient(sb, new Rectangle(rect.X + 2, rect.Y + 2, rect.Width - 4, rect.Height - 4),
                (hover ? new Color(120, 80, 30) : new Color(60, 44, 24)) * alpha, new Color(20, 14, 8) * alpha, 8);
            Texture2D frame = OrnateAssets.Get(hover ? "Hud/slot_active" : "Hud/slot");
            if (frame != null)
                sb.Draw(frame, new Rectangle(rect.X - 2, rect.Y - 2, rect.Width + 4, rect.Height + 4), Color.White * alpha);
            else
                UiDrawHelper.DrawBorder(sb, rect, (hover ? OrnateStyle.Gold : OrnateStyle.Bronze) * alpha);
            OrnateDraw.TextCentered(sb, "+", new Rectangle(rect.X, rect.Y - 1, rect.Width, rect.Height), 14f, hover ? Color.White : OrnateStyle.Gold, alpha, bold: true);
        }

        private static void DetailRow(SpriteBatch sb, Rectangle row, string label, string value, float alpha)
        {
            OrnateDraw.TextLeft(sb, label, row, 11f, OrnateStyle.TextMuted, alpha);
            OrnateDraw.TextRight(sb, value, row, 11f, OrnateStyle.TextLight, alpha);
        }

        private static void Panel(SpriteBatch sb, Rectangle rect, float alpha)
        {
            OrnateDraw.Inset(sb, rect, alpha, 0.42f);
            UiDrawHelper.DrawBorder(sb, new Rectangle(rect.X - 1, rect.Y - 1, rect.Width + 2, rect.Height + 2), OrnateItemUi.Rim * (0.85f * alpha));
        }
    }
}
