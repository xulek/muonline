#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Controls.UI.Common;
using Client.Main.Controls.UI.Game.Common;
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

        private static readonly ItemGlowPalette GlowPalette = new(
            new Color(150, 150, 150, 25),
            new Color(100, 150, 255, 50),
            new Color(120, 255, 120, 60),
            new Color(80, 200, 255, 70),
            new Color(255, 180, 80, 70));

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

        public static void DrawWindow(SpriteBatch sb, Rectangle rect, float alpha)
        {
            var pixel = GraphicsManager.Instance.Pixel;
            if (pixel == null) return;

            sb.Draw(pixel, new Rectangle(rect.X + 5, rect.Y + 5, rect.Width, rect.Height), Color.Black * 0.35f * alpha);
            sb.Draw(pixel, rect, ModernHudTheme.BorderOuter * alpha);
            var inner = new Rectangle(rect.X + 2, rect.Y + 2, rect.Width - 4, rect.Height - 4);
            UiDrawHelper.DrawVerticalGradient(sb, inner, ModernHudTheme.BgDark * alpha, ModernHudTheme.BgDarkest * alpha);
            sb.Draw(pixel, new Rectangle(inner.X, inner.Y, inner.Width, 1), ModernHudTheme.BorderInner * 0.5f * alpha);
            sb.Draw(pixel, new Rectangle(inner.X, inner.Y, 1, inner.Height), ModernHudTheme.BorderInner * 0.3f * alpha);
            UiDrawHelper.DrawCornerAccents(sb, rect, ModernHudTheme.Secondary * 0.5f * alpha);
        }

        public static void DrawHeader(SpriteBatch sb, Rectangle header, string title, string? subtitle, float alpha)
        {
            var pixel = GraphicsManager.Instance.Pixel;
            if (pixel == null) return;

            var panel = new Rectangle(header.X + 8, header.Y + 6, header.Width - 16, header.Height - 8);
            UiDrawHelper.DrawPanel(sb, panel, ModernHudTheme.BgMid * alpha, ModernHudTheme.BorderInner * 0.8f * alpha,
                ModernHudTheme.BorderOuter * alpha, ModernHudTheme.BorderInner * 0.6f * alpha);
            sb.Draw(pixel, new Rectangle(header.X + 20, header.Y + 8, header.Width - 40, 2), ModernHudTheme.Accent * 0.8f * alpha);

            const float titleSize = 15f, subtitleSize = 11f;
            float maxWidth = header.Width - 90;
            title = Ellipsize(title, titleSize, maxWidth);
            var titleMeasure = Measure(title, titleSize);
            float totalHeight = titleMeasure.Y + (subtitle != null ? Measure(subtitle, subtitleSize).Y : 0);
            float y = header.Y + 6 + (header.Height - 6 - totalHeight) / 2f;

            DrawText(sb, title, new Vector2(header.X + (header.Width - titleMeasure.X) / 2f, y), ModernHudTheme.TextGold, titleSize, alpha);
            if (subtitle != null)
            {
                subtitle = Ellipsize(subtitle, subtitleSize, maxWidth);
                var subMeasure = Measure(subtitle, subtitleSize);
                DrawText(sb, subtitle, new Vector2(header.X + (header.Width - subMeasure.X) / 2f, y + titleMeasure.Y), ModernHudTheme.TextGray, subtitleSize, alpha);
            }

            int sepY = header.Bottom - 1;
            UiDrawHelper.DrawHorizontalGradient(sb, new Rectangle(header.X + 20, sepY, (header.Width - 40) / 2, 1), Color.Transparent, ModernHudTheme.BorderInner * alpha);
            UiDrawHelper.DrawHorizontalGradient(sb, new Rectangle(header.X + header.Width / 2, sepY, (header.Width - 40) / 2, 1), ModernHudTheme.BorderInner * alpha, Color.Transparent);
        }

        public static void DrawCloseButton(SpriteBatch sb, Rectangle rect, bool hovered, float alpha)
        {
            var pixel = GraphicsManager.Instance.Pixel;
            if (pixel == null) return;
            if (hovered) sb.Draw(pixel, rect, ModernHudTheme.Danger * 0.25f * alpha);
            Color color = (hovered ? ModernHudTheme.Danger : ModernHudTheme.TextGray) * alpha;
            int cx = rect.Center.X, cy = rect.Center.Y;
            for (int i = -5; i <= 5; i++)
            {
                sb.Draw(pixel, new Rectangle(cx + i - 1, cy + i - 1, 2, 2), color);
                sb.Draw(pixel, new Rectangle(cx + i - 1, cy - i - 1, 2, 2), color);
            }
        }

        public static void DrawPanel(SpriteBatch sb, Rectangle rect, Color background, float alpha)
            => UiDrawHelper.DrawPanel(sb, rect, background * alpha, ModernHudTheme.BorderInner * 0.8f * alpha,
                ModernHudTheme.BorderOuter * alpha, ModernHudTheme.BorderInner * 0.6f * alpha);

        public static void DrawButton(SpriteBatch sb, Rectangle rect, string text, bool hovered, bool enabled, Color accent, float alpha, float textSize = 13f)
        {
            var pixel = GraphicsManager.Instance.Pixel;
            if (pixel == null) return;

            Color top = enabled ? Color.Lerp(ModernHudTheme.BgLight, accent, hovered ? 0.55f : 0.35f) : ModernHudTheme.BgMid;
            Color bottom = enabled ? Color.Lerp(ModernHudTheme.BgDark, accent, hovered ? 0.35f : 0.18f) : ModernHudTheme.BgDark;
            if (enabled && hovered)
                sb.Draw(pixel, new Rectangle(rect.X - 2, rect.Y - 2, rect.Width + 4, rect.Height + 4), accent * 0.25f * alpha);
            UiDrawHelper.DrawVerticalGradient(sb, rect, top * alpha, bottom * alpha);
            Color border = enabled ? (hovered ? accent : Color.Lerp(ModernHudTheme.BorderInner, accent, 0.5f)) : ModernHudTheme.BorderOuter;
            UiDrawHelper.DrawBorder(sb, rect, border * alpha);
            if (enabled)
                sb.Draw(pixel, new Rectangle(rect.X + 1, rect.Y + 1, rect.Width - 2, 1), Color.White * (hovered ? 0.25f : 0.12f) * alpha);
            DrawTextCentered(sb, text, rect, enabled ? ModernHudTheme.TextWhite : ModernHudTheme.TextGray * 0.7f, textSize, alpha);
        }

        public static void DrawPill(SpriteBatch sb, Rectangle rect, string text, Color color, float alpha)
        {
            var pixel = GraphicsManager.Instance.Pixel;
            if (pixel == null) return;
            sb.Draw(pixel, rect, color * 0.25f * alpha);
            UiDrawHelper.DrawBorder(sb, rect, color * 0.9f * alpha);
            DrawTextCentered(sb, text, rect, color, 10f, alpha);
        }

        public static void DrawGrid(SpriteBatch sb, Rectangle grid, float alpha)
        {
            var pixel = GraphicsManager.Instance.Pixel;
            if (pixel == null) return;

            DrawPanel(sb, new Rectangle(grid.X - 4, grid.Y - 4, grid.Width + 8, grid.Height + 8), ModernHudTheme.BgMid, alpha);
            sb.Draw(pixel, grid, ModernHudTheme.SlotBg * alpha);
            sb.Draw(pixel, new Rectangle(grid.X, grid.Y, grid.Width, 2), Color.Black * 0.4f * alpha);
            sb.Draw(pixel, new Rectangle(grid.X, grid.Y, 2, grid.Height), Color.Black * 0.3f * alpha);

            Color line = new Color(40, 48, 60, 100) * alpha;
            Color major = new Color(55, 65, 80, 120) * alpha;
            for (int x = 1; x < Columns; x++)
                sb.Draw(pixel, new Rectangle(grid.X + x * Cell, grid.Y, 1, grid.Height), x == Columns / 2 ? major : line);
            for (int y = 1; y < Rows; y++)
                sb.Draw(pixel, new Rectangle(grid.X, grid.Y + y * Cell, grid.Width, 1), line);

            sb.Draw(pixel, new Rectangle(grid.X, grid.Bottom - 1, grid.Width, 1), ModernHudTheme.BorderHighlight * 0.2f * alpha);
            sb.Draw(pixel, new Rectangle(grid.Right - 1, grid.Y, 1, grid.Height), ModernHudTheme.BorderHighlight * 0.15f * alpha);
        }

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

        public static void DrawItem(SpriteBatch sb, ItemTextureCache textures, InventoryItem item, Rectangle rect,
            bool hovered, bool dimmed, GameTime? gameTime, float alpha)
        {
            var pixel = GraphicsManager.Instance.Pixel;
            if (pixel == null) return;

            Color glow = ItemUiHelper.GetItemGlowColor(item, GlowPalette);
            if (glow.A > 0 || hovered)
            {
                Color finalGlow = hovered ? Color.Lerp(glow, ModernHudTheme.Accent, 0.4f) : glow;
                finalGlow.A = (byte)Math.Min(255, finalGlow.A + (hovered ? 40 : 0));
                ItemUiHelper.DrawItemGlow(sb, pixel, rect, finalGlow * alpha);
            }

            sb.Draw(pixel, new Rectangle(rect.X + 1, rect.Y + 1, rect.Width - 2, rect.Height - 2),
                (hovered ? ModernHudTheme.SlotHover : ModernHudTheme.SlotBg) * alpha);

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
            int height = (int)MathF.Ceiling(measured.Y) + 1;
            var strip = new Rectangle(itemRect.X + 1, itemRect.Y + 1, itemRect.Width - 2, height);
            sb.Draw(pixel, strip, Color.Black * 0.72f * alpha);
            sb.Draw(pixel, new Rectangle(strip.X, strip.Bottom, strip.Width, 1), color * 0.6f * alpha);
            DrawText(sb, text, new Vector2(strip.X + (strip.Width - measured.X) / 2f, strip.Y), color, size, alpha);

            if (!price.HasValue)
                UiDrawHelper.DrawBorder(sb, itemRect, ModernHudTheme.Danger * 0.8f * alpha);
        }

        public static void DrawTooltip(SpriteBatch sb, IReadOnlyList<(string Text, Color Color)> lines, InventoryItem item, Rectangle avoid)
        {
            var pixel = GraphicsManager.Instance.Pixel;
            if (pixel == null || lines.Count == 0) return;

            const float size = 12f;
            const int lineSpacing = 3, paddingX = 12, paddingY = 10;
            int width = 0, height = 0;
            foreach (var (text, _) in lines)
            {
                var measured = Measure(text.Length == 0 ? " " : text, size);
                width = Math.Max(width, (int)MathF.Ceiling(measured.X));
                height += (int)MathF.Ceiling(measured.Y) + lineSpacing;
            }
            height += 6;

            var rect = new Rectangle(avoid.Right + 10, avoid.Y, width + paddingX * 2, height + paddingY * 2);
            var screen = new Rectangle(0, 0, UiScaler.VirtualSize.X, UiScaler.VirtualSize.Y);
            if (rect.Right > screen.Right - 10) rect.X = avoid.X - rect.Width - 10;
            rect.X = Math.Clamp(rect.X, 10, Math.Max(10, screen.Right - rect.Width - 10));
            rect.Y = Math.Clamp(rect.Y, 10, Math.Max(10, screen.Bottom - rect.Height - 10));

            sb.Draw(pixel, new Rectangle(rect.X + 4, rect.Y + 4, rect.Width, rect.Height), Color.Black * 0.5f);
            UiDrawHelper.DrawVerticalGradient(sb, rect, new Color(20, 24, 32, 252), new Color(12, 14, 18, 254));
            Color border = item.Details.IsExcellent ? GlowPalette.Excellent :
                           item.Details.IsAncient ? GlowPalette.Ancient :
                           item.Details.Level >= 7 ? ModernHudTheme.Accent : ModernHudTheme.TextWhite;
            border.A = 255;
            UiDrawHelper.DrawBorder(sb, rect, border, 2);

            int y = rect.Y + paddingY;
            for (int i = 0; i < lines.Count; i++)
            {
                var (text, color) = lines[i];
                var measured = Measure(text.Length == 0 ? " " : text, size);
                if (text.Length == 0)
                {
                    sb.Draw(pixel, new Rectangle(rect.X + 8, y + (int)measured.Y / 2, rect.Width - 16, 1), border * 0.3f);
                }
                else
                {
                    DrawText(sb, text, new Vector2(rect.X + (rect.Width - measured.X) / 2f, y), i == 0 ? border : color, size);
                }
                y += (int)MathF.Ceiling(measured.Y) + lineSpacing;
                if (i == 0)
                {
                    y += 2;
                    sb.Draw(pixel, new Rectangle(rect.X + 8, y, rect.Width - 16, 1), border * 0.3f);
                    y += 4;
                }
            }
        }

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
