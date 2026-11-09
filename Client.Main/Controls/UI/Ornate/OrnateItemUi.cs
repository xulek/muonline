using System;
using System.Collections.Generic;
using System.Globalization;
using Client.Main.Controllers;
using Client.Main.Controls.UI.Common;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Controls.UI.Ornate
{
    /// <summary>
    /// Ornate widgets of the item windows (inventory, personal shops): item grids, equipment slot frames with empty-slot
    /// glyphs, square icon buttons, the Zen field, status gems, hint labels and item tooltips. Same conventions as
    /// <see cref="OrnateWidgets"/>: colours are multiplied by alpha and every widget has a flat fallback.
    /// </summary>
    public static class OrnateItemUi
    {
        private static Texture2D Pixel => GraphicsManager.Instance.Pixel;

        public static readonly string[] PreloadPaths =
        {
            "Controls/slot", "Controls/equip_frame", "Frame/paperdoll", "Glyphs/shop", "Glyphs/repair",
            "Glyphs/helm", "Glyphs/armor", "Glyphs/pants", "Glyphs/gloves", "Glyphs/boots", "Glyphs/weapon",
            "Glyphs/shield", "Glyphs/wings", "Glyphs/pendant", "Glyphs/ring", "Glyphs/pet",
            "Glyphs/zen", "Glyphs/price_tag", "Glyphs/gem_green", "Glyphs/gem_red",
        };

        /// <summary>Frame line in the colour used by the window border.</summary>
        public static readonly Color Rim = new(96, 72, 38);

        // --- Grids and slots ----------------------------------------------------------------------------

        /// <summary>Recessed well around an item grid plus one slot texture per cell.</summary>
        public static void Grid(SpriteBatch sb, Rectangle grid, int columns, int rows, int cell, float alpha)
        {
            Rectangle well = grid;
            well.Inflate(5, 5);
            OrnateDraw.Inset(sb, well, alpha, 0.55f);
            UiDrawHelper.DrawBorder(sb, new Rectangle(well.X - 1, well.Y - 1, well.Width + 2, well.Height + 2), Rim * (0.9f * alpha));

            Texture2D slot = OrnateAssets.Get("Controls/slot");
            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < columns; x++)
                {
                    var rect = new Rectangle(grid.X + x * cell, grid.Y + y * cell, cell, cell);
                    if (slot != null)
                    {
                        sb.Draw(slot, rect, Color.White * alpha);
                    }
                    else
                    {
                        sb.Draw(Pixel, rect, new Color(12, 12, 14) * alpha);
                        UiDrawHelper.DrawBorder(sb, rect, new Color(52, 42, 30) * alpha);
                    }
                }
            }
        }

        /// <summary>
        /// Equipment slot: ornate frame stretched as nine-slice (corners keep their size), an engraved glyph of what
        /// belongs there while it is empty, gold rim while hovered and a red rim when the slot rejects the carried item.
        /// </summary>
        public static void EquipSlot(SpriteBatch sb, Rectangle rect, string glyph, bool empty, bool hover, bool invalid, float alpha)
        {
            Texture2D frame = OrnateAssets.Get("Controls/equip_frame");
            UiDrawHelper.DrawVerticalGradient(sb, Inner(rect, 3), new Color(40, 33, 26) * (0.92f * alpha), new Color(16, 13, 11) * (0.95f * alpha), 16);

            if (empty && !string.IsNullOrEmpty(glyph))
            {
                int side = Math.Min(rect.Width, rect.Height);
                int size = Math.Min(side < 50 ? side - 8 : side - 16, 56);
                var glyphRect = new Rectangle(rect.Center.X - size / 2, rect.Center.Y - size / 2, size, size);
                Glyph(sb, glyph, glyphRect, Color.White * (hover ? 0.55f : 0.36f), alpha);
            }

            Color tint = invalid ? new Color(255, 120, 110) : hover ? new Color(255, 236, 190) : new Color(205, 195, 180);
            if (frame != null)
                NineSlice(sb, frame, rect, OrnateStyle.EquipFrameMargin, Math.Min(rect.Width, rect.Height) < 50 ? 10 : 14, tint * alpha);
            else
                UiDrawHelper.DrawBorder(sb, rect, (hover ? OrnateStyle.Gold : OrnateStyle.Bronze) * alpha, 2);

            if (hover || invalid)
            {
                Color glow = invalid ? OrnateStyle.Danger : OrnateStyle.Gold;
                UiDrawHelper.DrawBorder(sb, Inner(rect, 2), glow * (0.55f * alpha));
            }
        }

        /// <summary>Hover/drop highlight of one item rectangle inside a grid.</summary>
        public static void CellHighlight(SpriteBatch sb, Rectangle rect, Color color, float alpha)
        {
            sb.Draw(Pixel, Inner(rect, 1), color * (0.18f * alpha));
            UiDrawHelper.DrawBorder(sb, Inner(rect, 1), color * (0.75f * alpha));
        }

        /// <summary>
        /// Plate under an item in a grid: darker than the empty cells and outlined, so an item spanning several cells
        /// reads as one piece. <paramref name="rarity"/> tints the rim (null = plain bronze).
        /// </summary>
        public static void ItemBacking(SpriteBatch sb, Rectangle rect, Color? rarity, float alpha)
        {
            Rectangle inner = Inner(rect, 1);
            UiDrawHelper.DrawVerticalGradient(sb, inner, new Color(14, 12, 10) * (0.82f * alpha), new Color(6, 5, 4) * (0.88f * alpha), 12);
            Color rim = rarity ?? new Color(120, 92, 52);
            UiDrawHelper.DrawBorder(sb, inner, rim * ((rarity.HasValue ? 0.85f : 0.7f) * alpha));
            if (rarity is Color glow)
                UiDrawHelper.DrawVerticalGradient(sb, Inner(inner, 1), Color.Transparent, glow * (0.16f * alpha), 12);
        }

        /// <summary>Glyph from <c>Ornate/Glyphs</c>: flat silhouette or small ornament drawn without a medallion.</summary>
        public static void Glyph(SpriteBatch sb, string name, Rectangle rect, Color tint, float alpha)
        {
            Texture2D texture = string.IsNullOrEmpty(name) ? null : OrnateAssets.Get("Glyphs/" + name);
            if (texture != null)
                sb.Draw(texture, rect, tint * alpha);
        }

        // --- Buttons ------------------------------------------------------------------------------------

        /// <summary>
        /// Button with a glyph and a label on the standard plate. <see cref="WidgetState.Selected"/> lights the plate
        /// red-gold: a mode that is on (repair mode, an open shop).
        /// </summary>
        public static void ActionButton(SpriteBatch sb, Rectangle rect, string glyph, string label, WidgetState state, float alpha)
        {
            if (state.Pressed)
                rect.Y += 1;

            float a = (state.Disabled ? 0.5f : 1f) * alpha;
            bool hover = state.Hover && !state.Disabled;
            // A mode that is on keeps the same plate (same room for the label) in a red-gold tint.
            Texture2D texture = hover || state.Selected ? OrnateAssets.ButtonHover : OrnateAssets.Button;
            PlateLayout layout = hover || state.Selected ? OrnateStyle.ButtonHover : OrnateStyle.Button;
            Color tint = state.Selected ? (hover ? new Color(255, 150, 130) : new Color(240, 110, 95)) : Color.White;
            OrnateDraw.Plate(sb, texture, layout, rect, hover || state.Selected, tint * a, overhang: 0.6f);

            // The icon sits on the left cap (over its gem), the label fills the rest up to the right cap.
            Rectangle inner = OrnateDraw.PlateInner(texture, layout, rect);
            int icon = rect.Height + 2;
            var iconRect = new Rectangle(rect.X + 3, rect.Center.Y - icon / 2 - 1, icon, icon);
            var textArea = new Rectangle(iconRect.Right + 1, rect.Y, Math.Max(0, inner.Right - iconRect.Right - 1), rect.Height);
            float size = OrnateDraw.FitSize(label, 14f, textArea.Width, 10f);

            Glyph(sb, glyph, iconRect, Color.White, a);
            Color text = state.Disabled ? OrnateStyle.TextDisabled : hover || state.Selected ? Color.White : OrnateStyle.Gold;
            OrnateDraw.TextCentered(sb, label, textArea, size, text, a, bold: true);
        }

        /// <summary>Small dark label with a bronze rim centred above <paramref name="anchor"/> (button hints).</summary>
        public static void Hint(SpriteBatch sb, Rectangle anchor, string text, float alpha, Color? color = null)
        {
            const float size = 12f;
            Vector2 measured = OrnateDraw.Measure(text, size);
            var box = new Rectangle((int)(anchor.Center.X - measured.X / 2f) - 8, anchor.Y - (int)measured.Y - 14,
                (int)measured.X + 16, (int)measured.Y + 8);
            Point screen = UiScaler.VirtualSize;
            box.X = Math.Clamp(box.X, 4, Math.Max(4, screen.X - box.Width - 4));
            Panel(sb, box, alpha);
            OrnateDraw.TextCentered(sb, text, box, size, color ?? OrnateStyle.TextLight, alpha);
        }

        // --- Panels and values --------------------------------------------------------------------------

        /// <summary>Dark translucent panel with a double bronze rim (tooltips, hints, summaries).</summary>
        public static void Panel(SpriteBatch sb, Rectangle rect, float alpha, Color? accent = null)
        {
            OrnateDraw.SoftShadow(sb, rect, alpha, 10, 4);
            UiDrawHelper.DrawVerticalGradient(sb, rect, new Color(22, 19, 16) * (0.97f * alpha), new Color(10, 9, 8) * (0.98f * alpha), 16);
            UiDrawHelper.DrawBorder(sb, rect, Color.Black * alpha);
            UiDrawHelper.DrawBorder(sb, Inner(rect, 1), Rim * alpha);
            sb.Draw(Pixel, new Rectangle(rect.X + 2, rect.Y + 1, rect.Width - 4, 1), new Color(214, 176, 104) * (0.45f * alpha));
            if (accent is Color line)
                UiDrawHelper.DrawBorder(sb, Inner(rect, 3), line * (0.55f * alpha));
        }

        /// <summary>Recessed Zen field: coin glyph on the left, the amount right-aligned in gold with thousands separators.</summary>
        public static void ZenField(SpriteBatch sb, Rectangle rect, long amount, float alpha, string caption = null)
        {
            OrnateWidgets.InputFrame(sb, rect, false, false, alpha);
            int coin = rect.Height - 6;
            Glyph(sb, "zen", new Rectangle(rect.X + 4, rect.Center.Y - coin / 2, coin, coin), Color.White, alpha);
            if (OrnateAssets.Get("Glyphs/zen") == null)
                OrnateDraw.TextLeft(sb, "Z", new Rectangle(rect.X + 9, rect.Y, coin, rect.Height), 13f, OrnateStyle.Gold, alpha, bold: true);

            var textArea = new Rectangle(rect.X + coin + 10, rect.Y, rect.Width - coin - 20, rect.Height);
            float captionWidth = string.IsNullOrEmpty(caption) ? 0f : OrnateDraw.Measure(caption, 12f).X + 8f;
            if (captionWidth > 0f)
                OrnateDraw.TextLeft(sb, caption, textArea, 12f, OrnateStyle.TextMuted, alpha);

            // Full amount while it fits at a readable size, otherwise the MU short form (1.23kkk).
            float room = textArea.Width - captionWidth;
            string value = FormatZen(amount);
            float size = OrnateDraw.FitSize(value, 14f, room, 11f);
            if (OrnateDraw.Measure(value, size).X > room)
            {
                value = FormatZenShort(amount);
                size = OrnateDraw.FitSize(value, 14f, room, 10f);
            }
            OrnateDraw.TextRight(sb, value, textArea, size, OrnateStyle.Gold, alpha, bold: true);
        }

        public static string FormatZen(long amount) => amount.ToString("N0", CultureInfo.InvariantCulture);

        /// <summary>MU short form: 950, 25.5k, 1.5kk, 1.23kkk.</summary>
        public static string FormatZenShort(long amount) =>
            amount >= 1_000_000_000 ? (amount / 1e9).ToString("0.##", CultureInfo.InvariantCulture) + "kkk" :
            amount >= 1_000_000 ? (amount / 1e6).ToString("0.##", CultureInfo.InvariantCulture) + "kk" :
            amount >= 10_000 ? (amount / 1e3).ToString("0.#", CultureInfo.InvariantCulture) + "k" :
            amount.ToString(CultureInfo.InvariantCulture);

        /// <summary>Gem and caption showing whether something is on (green) or off (dull red), e.g. a shop's state.</summary>
        public static void StatusGem(SpriteBatch sb, Rectangle area, bool on, string label, float alpha)
        {
            int gem = Math.Min(area.Height, 18);
            var gemRect = new Rectangle(area.X, area.Center.Y - gem / 2, gem, gem);
            Texture2D texture = OrnateAssets.Get(on ? "Glyphs/gem_green" : "Glyphs/gem_red");
            if (texture != null)
                sb.Draw(texture, gemRect, Color.White * alpha);
            else
                sb.Draw(Pixel, Inner(gemRect, 4), (on ? new Color(90, 220, 110) : new Color(150, 50, 45)) * alpha);

            OrnateDraw.TextLeft(sb, label, new Rectangle(gemRect.Right + 5, area.Y, area.Width - gem - 5, area.Height), 12f,
                on ? new Color(150, 235, 150) : OrnateStyle.TextMuted, alpha, bold: true);
        }

        // --- Tooltips -----------------------------------------------------------------------------------

        /// <summary>
        /// Item tooltip: name in the rarity colour, a rule under it, then the remaining lines (empty text = rule).
        /// Placed beside <paramref name="avoid"/> (the item) and kept on screen.
        /// </summary>
        public static void Tooltip(SpriteBatch sb, IReadOnlyList<(string Text, Color Color)> lines, Color rarity, Rectangle avoid, Point? near = null)
        {
            if (lines == null || lines.Count == 0)
                return;

            const float size = 13f;
            const int spacing = 3, padX = 14, padY = 10;
            int width = 0, height = 0;
            foreach (var (text, _) in lines)
            {
                Vector2 measured = OrnateDraw.Measure(text.Length == 0 ? " " : text, size);
                width = Math.Max(width, (int)MathF.Ceiling(measured.X));
                height += (int)MathF.Ceiling(measured.Y) + spacing;
            }
            height += 6;

            var rect = new Rectangle(avoid.Right + 10, avoid.Y, width + padX * 2, height + padY * 2);
            if (near is Point mouse)
                rect.Location = new Point(mouse.X + 18, mouse.Y + 18);

            Point screen = UiScaler.VirtualSize;
            if (rect.Intersects(avoid) || rect.Right > screen.X - 8)
                rect.X = avoid.X - rect.Width - 10;
            if (rect.X < 8)
                rect.X = Math.Min(avoid.Right + 10, screen.X - rect.Width - 8);
            rect.X = Math.Clamp(rect.X, 8, Math.Max(8, screen.X - rect.Width - 8));
            rect.Y = Math.Clamp(rect.Y, 8, Math.Max(8, screen.Y - rect.Height - 8));

            rarity.A = 255;
            Panel(sb, rect, 1f);
            sb.Draw(Pixel, new Rectangle(rect.X + 3, rect.Y + 3, rect.Width - 6, 2), rarity * 0.7f);

            int y = rect.Y + padY;
            for (int i = 0; i < lines.Count; i++)
            {
                var (text, color) = lines[i];
                Vector2 measured = OrnateDraw.Measure(text.Length == 0 ? " " : text, size);
                if (text.Length == 0)
                    Rule(sb, rect.X + 10, rect.Right - 10, y + (int)measured.Y / 2, rarity * 0.35f);
                else
                    OrnateDraw.Text(sb, text, new Vector2(rect.X + (rect.Width - measured.X) / 2f, y), size, i == 0 ? rarity : color, 1f, bold: i == 0);

                y += (int)MathF.Ceiling(measured.Y) + spacing;
                if (i == 0)
                {
                    y += 2;
                    Rule(sb, rect.X + 10, rect.Right - 10, y, rarity * 0.45f);
                    y += 4;
                }
            }
        }

        private static void Rule(SpriteBatch sb, int left, int right, int y, Color color)
        {
            int half = (right - left) / 2;
            UiDrawHelper.DrawHorizontalGradient(sb, new Rectangle(left, y, half, 1), Color.Transparent, color, 16);
            UiDrawHelper.DrawHorizontalGradient(sb, new Rectangle(left + half, y, right - left - half, 1), color, Color.Transparent, 16);
        }

        // --- Helpers ------------------------------------------------------------------------------------

        /// <summary>
        /// Nine-slice: corners of <paramref name="srcMargin"/> (fraction of the texture size) drawn at
        /// <paramref name="destMargin"/> pixels, edges and centre stretched.
        /// </summary>
        public static void NineSlice(SpriteBatch sb, Texture2D tex, Rectangle dest, float srcMargin, int destMargin, Color color,
            bool drawCenter = true)
        {
            int sm = (int)MathF.Round(tex.Width * srcMargin);
            int dm = Math.Min(destMargin, Math.Min(dest.Width, dest.Height) / 2);
            int sw = tex.Width, sh = tex.Height;

            int[] sx = { 0, sm, sw - sm, sw };
            int[] sy = { 0, sm, sh - sm, sh };
            int[] dx = { dest.X, dest.X + dm, dest.Right - dm, dest.Right };
            int[] dy = { dest.Y, dest.Y + dm, dest.Bottom - dm, dest.Bottom };

            for (int row = 0; row < 3; row++)
            {
                for (int col = 0; col < 3; col++)
                {
                    var d = new Rectangle(dx[col], dy[row], dx[col + 1] - dx[col], dy[row + 1] - dy[row]);
                    if (d.Width <= 0 || d.Height <= 0 || (!drawCenter && row == 1 && col == 1))
                        continue;
                    sb.Draw(tex, d, new Rectangle(sx[col], sy[row], sx[col + 1] - sx[col], sy[row + 1] - sy[row]), color);
                }
            }
        }

        private static Rectangle Inner(Rectangle rect, int by) => new(rect.X + by, rect.Y + by, rect.Width - by * 2, rect.Height - by * 2);
    }
}
