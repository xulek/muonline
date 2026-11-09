#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Controls.UI.Common;
using Client.Main.Controls.UI.Game.Common;
using Client.Main.Controls.UI.Ornate;
using Client.Main.Core.Utilities;
using Client.Main.Helpers;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Controls.UI.Game.Inventory
{
    /// <summary>
    /// Shared layout constants, formatting and drawing primitives for the personal shop windows.
    /// </summary>
    internal static class PlayerShopUi
    {
        public const int FirstShopSlot = 204;
        public const int Columns = 8;
        public const int Rows = 4;
        public const int Cell = 34;
        public const int SlotCount = Columns * Rows;
        public const int GridWidth = Columns * Cell;
        public const int GridHeight = Rows * Cell;
        public const uint MaxPrice = int.MaxValue;

        public static bool IsShopSlot(byte slot) => slot >= FirstShopSlot && slot < FirstShopSlot + SlotCount;

        public static Point SlotToCell(byte slot)
        {
            int index = slot >= FirstShopSlot ? slot - FirstShopSlot : slot;
            return new Point(index % Columns, index / Columns);
        }

        public static byte CellToSlot(Point cell) => (byte)(FirstShopSlot + cell.Y * Columns + cell.X);

        /// <summary>
        /// Converts a cursor position to the top-left cell of an item centered under the cursor,
        /// matching how <see cref="PickedItemRenderer"/> draws a dragged item.
        /// </summary>
        public static Point CellUnderCursor(Rectangle grid, Point mouse, int width, int height)
        {
            float x = (mouse.X - grid.X) / (float)Cell - (width - 1) / 2f;
            float y = (mouse.Y - grid.Y) / (float)Cell - (height - 1) / 2f;
            return new Point((int)MathF.Floor(x), (int)MathF.Floor(y));
        }

        public static bool FitsInGrid(Point cell, int width, int height)
            => cell.X >= 0 && cell.Y >= 0 && width > 0 && height > 0 &&
               cell.X + width <= Columns && cell.Y + height <= Rows;

        public static Rectangle CellRect(Rectangle grid, Point cell, int width, int height)
            => new(grid.X + cell.X * Cell, grid.Y + cell.Y * Cell, width * Cell, height * Cell);

        // ─────────────────────────────── Formatting ───────────────────────────────

        public static string FormatZen(uint value) => value.ToString("N0", CultureInfo.InvariantCulture);

        /// <summary>Compact MU-style price: 950, 25k, 1.5kk, 2kkk.</summary>
        public static string FormatZenShort(uint value)
        {
            static string Trim(double v) => v >= 100 ? v.ToString("0", CultureInfo.InvariantCulture)
                                                     : v.ToString("0.#", CultureInfo.InvariantCulture);
            if (value >= 1_000_000_000) return Trim(Math.Floor(value / 100_000_000d) / 10d) + "kkk";
            if (value >= 1_000_000) return Trim(Math.Floor(value / 100_000d) / 10d) + "kk";
            if (value >= 10_000) return Trim(Math.Floor(value / 100d) / 10d) + "k";
            return value.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Parses a Zen amount. Accepts digits with optional separators and MU-style suffixes
        /// (k = thousand, kk/m = million, kkk/b = billion), e.g. "1.5kk", "250k", "2 000 000".
        /// </summary>
        public static bool TryParseZen(string? text, out uint value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;

            string s = text.Trim().ToLowerInvariant().Replace(" ", string.Empty).Replace("_", string.Empty);
            double multiplier = 1;
            if (s.EndsWith("kkk") || s.EndsWith("b")) { multiplier = 1_000_000_000; s = s[..^(s.EndsWith("b") ? 1 : 3)]; }
            else if (s.EndsWith("kk") || s.EndsWith("m")) { multiplier = 1_000_000; s = s[..^(s.EndsWith("m") ? 1 : 2)]; }
            else if (s.EndsWith("k")) { multiplier = 1_000; s = s[..^1]; }

            if (s.Length == 0) return false;

            if (multiplier == 1)
            {
                // Plain amounts: separators are only grouping characters.
                s = s.Replace(",", string.Empty).Replace(".", string.Empty);
                if (!ulong.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out ulong plain)) return false;
                if (plain == 0 || plain > MaxPrice) return false;
                value = (uint)plain;
                return true;
            }

            s = s.Replace(',', '.');
            if (!double.TryParse(s, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double number)) return false;
            double result = Math.Round(number * multiplier);
            if (result < 1 || result > MaxPrice) return false;
            value = (uint)result;
            return true;
        }

        public static Color GetPriceColor(uint price)
        {
            if (price >= 1_000_000_000) return new Color(255, 110, 110);
            if (price >= 100_000_000) return new Color(255, 160, 70);
            if (price >= 10_000_000) return ModernHudTheme.TextGold;
            if (price >= 1_000_000) return new Color(140, 230, 140);
            return ModernHudTheme.TextWhite;
        }

        public static string GetItemDisplayName(InventoryItem item)
        {
            string name = item.Definition?.Name ?? "Unknown item";
            if (item.Details.IsExcellent) name = "Excellent " + name;
            else if (item.Details.IsAncient) name = "Ancient " + name;
            if (item.Details.Level > 0) name += $" +{item.Details.Level}";
            return name;
        }

        public static InventoryItem? CreateItem(byte[] data, Point cell)
        {
            var definition = ItemDatabase.GetItemDefinition(data);
            if (definition == null) return null;
            return new InventoryItem(definition, cell, data) { Durability = ItemDatabase.GetItemDurability(data) };
        }

        // ─────────────────────────────── Text ───────────────────────────────

        public static Vector2 Measure(string text, float size)
        {
            var font = GraphicsManager.GetUiFont(size, out float scale);
            return font == null || string.IsNullOrEmpty(text) ? Vector2.Zero : font.MeasureString(text) * scale;
        }

        public static void DrawText(SpriteBatch sb, string text, Vector2 position, Color color, float size, float alpha = 1f, bool shadow = true)
        {
            var font = GraphicsManager.GetUiFont(size, out float scale);
            if (font == null || string.IsNullOrEmpty(text)) return;
            position = new Vector2(MathF.Round(position.X), MathF.Round(position.Y));
            if (shadow)
                sb.DrawString(font, text, position + Vector2.One, Color.Black * 0.75f * alpha, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
            sb.DrawString(font, text, position, color * alpha, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        }

        public static void DrawTextCentered(SpriteBatch sb, string text, Rectangle area, Color color, float size, float alpha = 1f)
        {
            var measured = Measure(text, size);
            DrawText(sb, text, new Vector2(area.X + (area.Width - measured.X) / 2f, area.Y + (area.Height - measured.Y) / 2f), color, size, alpha);
        }

        /// <summary>Shortens text with an ellipsis so it fits in <paramref name="maxWidth"/> pixels.</summary>
        public static string Ellipsize(string text, float size, float maxWidth)
        {
            if (Measure(text, size).X <= maxWidth) return text;
            for (int length = text.Length - 1; length > 0; length--)
            {
                string candidate = text[..length].TrimEnd() + "...";
                if (Measure(candidate, size).X <= maxWidth) return candidate;
            }
            return "...";
        }

        // ─────────────────────────────── Chrome ───────────────────────────────

        public static void DrawWindow(SpriteBatch sb, Rectangle rect, float alpha) => OrnateDraw.Window(sb, rect, alpha);

        /// <summary>Gold title (and muted subtitle) centred in the header, with an ornamental divider under it.</summary>
        public static void DrawHeader(SpriteBatch sb, Rectangle header, string title, string? subtitle, float alpha)
        {
            const float titleSize = 15f, subtitleSize = 12f;
            float maxWidth = header.Width - 84;
            title = Ellipsize(title, titleSize, maxWidth);
            var titleMeasure = OrnateDraw.Measure(title, titleSize);
            float totalHeight = titleMeasure.Y + (subtitle != null ? OrnateDraw.Measure(subtitle, subtitleSize).Y : 0);
            float y = header.Y + 4 + (header.Height - 14 - totalHeight) / 2f;

            OrnateDraw.Text(sb, title, new Vector2(header.Center.X - titleMeasure.X / 2f, y), titleSize, OrnateStyle.Gold, alpha, bold: true);
            if (subtitle != null)
            {
                subtitle = Ellipsize(subtitle, subtitleSize, maxWidth);
                var subMeasure = OrnateDraw.Measure(subtitle, subtitleSize);
                OrnateDraw.Text(sb, subtitle, new Vector2(header.Center.X - subMeasure.X / 2f, y + titleMeasure.Y), subtitleSize, OrnateStyle.TextMuted, alpha);
            }

            OrnateDraw.Divider(sb, new Rectangle(header.X + 24, header.Bottom - 12, header.Width - 48, 12), alpha);
        }

        public static void DrawCloseButton(SpriteBatch sb, Rectangle rect, bool hovered, float alpha)
            => OrnateWidgets.CloseButton(sb, rect, hovered, alpha);

        /// <summary>Recessed panel (summaries, editor area); the colour argument is kept for callers and ignored.</summary>
        public static void DrawPanel(SpriteBatch sb, Rectangle rect, Color background, float alpha)
        {
            OrnateDraw.Inset(sb, rect, alpha, 0.4f);
            UiDrawHelper.DrawBorder(sb, new Rectangle(rect.X - 1, rect.Y - 1, rect.Width + 2, rect.Height + 2), OrnateItemUi.Rim * (0.8f * alpha));
        }

        /// <summary>Ornate button: the crimson plate for positive actions (open, confirm), the bronze one for the rest.</summary>
        public static void DrawButton(SpriteBatch sb, Rectangle rect, string text, bool hovered, bool enabled, Color accent, float alpha, float textSize = 13f)
        {
            var state = new WidgetState(Hover: hovered, Disabled: !enabled);
            if (accent == ModernHudTheme.Success)
                OrnateWidgets.PrimaryButton(sb, rect, text, state, alpha);
            else
                OrnateWidgets.Button(sb, rect, text, state, alpha, overhang: 0.6f);
        }

        /// <summary>Open/closed state with a green or dull red gem.</summary>
        public static void DrawPill(SpriteBatch sb, Rectangle rect, string text, Color color, float alpha)
            => OrnateItemUi.StatusGem(sb, rect, color == ModernHudTheme.Success, text, alpha);

        public static void DrawGrid(SpriteBatch sb, Rectangle grid, float alpha)
            => OrnateItemUi.Grid(sb, grid, Columns, Rows, Cell, alpha);

        public static void DrawFootprint(SpriteBatch sb, Rectangle grid, Point cell, int width, int height, bool valid, float alpha)
        {
            var pixel = GraphicsManager.Instance.Pixel;
            if (pixel == null) return;
            Color color = valid ? ModernHudTheme.Success : ModernHudTheme.Danger;
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                var target = new Point(cell.X + x, cell.Y + y);
                if (target.X < 0 || target.Y < 0 || target.X >= Columns || target.Y >= Rows) continue;
                sb.Draw(pixel, CellRect(grid, target, 1, 1), color * 0.35f * alpha);
            }
            var outline = Rectangle.Intersect(CellRect(grid, cell, width, height), grid);
            if (!outline.IsEmpty) UiDrawHelper.DrawBorder(sb, outline, color * 0.9f * alpha);
        }

        // ─────────────────────────────── Items ───────────────────────────────

        /// <summary>Rim colour of an item's backing plate by rarity; null for plain items.</summary>
        public static Color? RarityColor(InventoryItem item) =>
            item.Details.IsExcellent ? new Color(110, 230, 110) :
            item.Details.IsAncient ? new Color(90, 190, 255) :
            item.Details.Level >= 7 ? OrnateStyle.Gold :
            null;

        public static void DrawItem(SpriteBatch sb, ItemTextureCache textures, InventoryItem item, Rectangle rect,
            bool hovered, bool dimmed, GameTime? gameTime, float alpha)
        {
            var pixel = GraphicsManager.Instance.Pixel;
            if (pixel == null) return;

            OrnateItemUi.ItemBacking(sb, rect, hovered ? OrnateStyle.Gold : RarityColor(item), alpha);

            var texture = textures.Resolve(item, rect.Width, rect.Height, hovered, gameTime);
            Color tint = Color.White * (dimmed ? 0.45f : 1f) * alpha;
            if (texture != null)
                sb.Draw(texture, rect, tint);
            else
                ItemGridRenderHelper.DrawItemPlaceholder(sb, pixel, GraphicsManager.Instance.Font, rect, item,
                    ModernHudTheme.BgLight * alpha, ModernHudTheme.TextGray * 0.8f * alpha);

            if (item.Definition.BaseDurability == 0 && item.Definition.MagicDurability == 0 && item.Durability > 1)
                ItemGridRenderHelper.DrawItemStackCount(sb, GraphicsManager.Instance.Font, rect, item.Durability, ModernHudTheme.TextGold, alpha);

            if (item.Details.Level > 0)
                ItemGridRenderHelper.DrawItemLevelBadge(sb, pixel, GraphicsManager.Instance.Font, rect, item.Details.Level,
                    level => level >= 9 ? ModernHudTheme.Danger : level >= 7 ? ModernHudTheme.Accent :
                             level >= 4 ? ModernHudTheme.AccentDim : ModernHudTheme.TextGray,
                    new Color(0, 0, 0, 180) * alpha);
        }

        /// <summary>Draws a price strip along the top edge of an item. <c>null</c> price renders a "no price" warning.</summary>
        public static void DrawPriceTag(SpriteBatch sb, Rectangle itemRect, uint? price, Color? overrideColor, float alpha)
        {
            var pixel = GraphicsManager.Instance.Pixel;
            if (pixel == null) return;

            string text = price.HasValue ? FormatZenShort(price.Value) : "NO PRICE";
            Color color = overrideColor ?? (price.HasValue ? GetPriceColor(price.Value) : ModernHudTheme.Danger);
            const float size = 9f;
            var measured = Measure(text, size);
            if (!price.HasValue && measured.X > itemRect.Width - 4)
            {
                text = "?"; // one cell is too narrow for the words; the red rim and tooltip still explain it
                measured = Measure(text, size);
            }
            int height = (int)MathF.Ceiling(measured.Y) + 1;
            var strip = new Rectangle(itemRect.X + 1, itemRect.Y + 1, itemRect.Width - 2, height);
            sb.Draw(pixel, strip, Color.Black * 0.72f * alpha);
            sb.Draw(pixel, new Rectangle(strip.X, strip.Bottom, strip.Width, 1), color * 0.6f * alpha);
            DrawText(sb, text, new Vector2(strip.X + (strip.Width - measured.X) / 2f, strip.Y), color, size, alpha);

            if (!price.HasValue)
                UiDrawHelper.DrawBorder(sb, itemRect, ModernHudTheme.Danger * 0.8f * alpha);
        }

        public static void DrawTooltip(SpriteBatch sb, IReadOnlyList<(string Text, Color Color)> lines, InventoryItem item, Rectangle avoid)
            => OrnateItemUi.Tooltip(sb, lines, RarityColor(item) ?? OrnateStyle.TextLight, avoid);

        public static SpriteBatchScope? BeginUiBatch()
        {
            var sb = GraphicsManager.Instance?.Sprite;
            if (sb == null || SpriteBatchScope.BatchIsBegun) return null;
            return new SpriteBatchScope(sb, SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp, transform: UiScaler.SpriteTransform);
        }
    }

    /// <summary>Caches item icon textures and BMD previews for one window.</summary>
    internal sealed class ItemTextureCache
    {
        private readonly Dictionary<string, Texture2D> _textures = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<(InventoryItem Item, int Width, int Height), Texture2D> _previews = new();

        public void Clear() => _previews.Clear();

        public Texture2D? Resolve(InventoryItem item, int width, int height, bool animated, GameTime? gameTime)
        {
            string? path = item.Definition?.TexturePath;
            if (string.IsNullOrEmpty(path)) return null;

            if (!path.EndsWith(".bmd", StringComparison.OrdinalIgnoreCase))
            {
                if (_textures.TryGetValue(path, out var cached) && cached != null) return cached;
                var texture = TextureLoader.Instance.GetTexture2D(path);
                if (texture != null) _textures[path] = texture;
                return texture;
            }

            try
            {
                if (animated && gameTime != null)
                {
                    var animatedPreview = BmdPreviewRenderer.GetAnimatedPreview(item, width, height, gameTime);
                    if (animatedPreview != null) return animatedPreview;
                }
                else if (Constants.ENABLE_ITEM_MATERIAL_ANIMATION && gameTime != null)
                {
                    var material = BmdPreviewRenderer.GetMaterialAnimatedPreview(item, width, height, gameTime);
                    if (material != null) return material;
                }

                var key = (item, width, height);
                if (_previews.TryGetValue(key, out var preview) && preview != null) return preview;
                preview = BmdPreviewRenderer.GetPreview(item, width, height);
                if (preview != null) _previews[key] = preview;
                return preview;
            }
            catch
            {
                return null;
            }
        }
    }
}
