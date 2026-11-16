using System;
using System.Collections.Generic;
using Client.Main.Core.Utilities;
using Client.Main.Models;
using Client.Main.Controls.UI.Game.Inventory;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Client.Main.Content;
using Client.Main;
using Client.Main.Core.Utilities.ItemTooltip;
using Client.Main.Controllers;

namespace Client.Main.Controls.UI.Game.Common
{
    public readonly struct ItemGlowPalette
    {
        public ItemGlowPalette(Color normal, Color magic, Color excellent, Color ancient, Color legendary)
        {
            Normal = normal;
            Magic = magic;
            Excellent = excellent;
            Ancient = ancient;
            Legendary = legendary;
        }

        public Color Normal { get; }
        public Color Magic { get; }
        public Color Excellent { get; }
        public Color Ancient { get; }
        public Color Legendary { get; }
    }

    /// <summary>Style of one tooltip row as RenderTipTextList draws it: bold font and optional background (DARKRED, DARKBLUE, DARKYELLOW, GREEN_BLUE).</summary>
    public readonly record struct ItemTooltipRowStyle(bool Bold, Color? Background);

    /// <summary>
    /// Tooltip rows (text, color) plus the original per-row style. Rows appended by callers have no style.
    /// </summary>
    public sealed class ItemTooltipLines : List<(string text, Color color)>
    {
        private readonly List<ItemTooltipRowStyle> _styles = new();

        internal void Add(string text, Color color, ItemTooltipRowStyle style)
        {
            Add((text, color));
            while (_styles.Count < Count - 1) _styles.Add(default);
            _styles.Add(style);
        }

        public ItemTooltipRowStyle GetStyle(int index) => index >= 0 && index < _styles.Count ? _styles[index] : default;

        public static ItemTooltipRowStyle StyleOf(IReadOnlyList<(string Text, Color Color)> lines, int index)
            => lines is ItemTooltipLines l ? l.GetStyle(index) : default;
    }

    public static class ItemUiHelper
    {
        /// <summary>
        /// Max durability: SourceMain5.2 CalcMaxDurability (ZzzInventory.cpp:1553) applied to the item's ITEM data.
        /// </summary>
        public static int CalculateMaxDurability(ItemDefinition def, ItemDatabase.ItemDetails details, bool isStaff)
        {
            if (def == null) return 0;
            var ip = new SourceItem
            {
                Type = def.Group * SourceConstants.MAX_ITEM_INDEX + def.Id,
                Level = details.Level,
                ExcellentFlags = details.IsExcellent ? details.ExcellentFlags & 0x3F : 0,
                AncientDiscriminator = details.IsAncient ? 1 : 0,
            };
            return SourceItemAttributes.CalcMaxDurability(ip, SourceItemAttribute.FromDefinition(def), details.Level);
        }

        /// <summary>Max durability of an inventory item (uses the full ITEM data, incl. extended packets).</summary>
        public static int CalculateMaxDurability(InventoryItem item)
        {
            var ip = SourceItemTooltip.CreateSourceItem(item);
            return ip == null ? 0 : SourceItemAttributes.CalcMaxDurability(ip, ip.Attribute, ip.Level);
        }

        public static Color GetItemGlowColor(InventoryItem item, ItemGlowPalette palette)
        {
            if (item?.Details is null)
                return palette.Normal;

            if (item.Details.IsExcellent) return palette.Excellent;
            if (item.Details.IsAncient) return palette.Ancient;
            if (item.Details.Level >= 9) return palette.Legendary;
            if (item.Details.Level >= 5) return palette.Magic;
            return palette.Normal;
        }

        public static void DrawItemGlow(SpriteBatch spriteBatch, Texture2D pixel, Rectangle rect, Color color, int glowSize = 4)
        {
            if (spriteBatch == null || pixel == null) return;

            for (int i = glowSize; i > 0; i--)
            {
                float alpha = (float)(glowSize - i + 1) / glowSize * 0.6f;
                Color layerColor = color * alpha;

                var glowRect = new Rectangle(rect.X - i, rect.Y - i, rect.Width + i * 2, rect.Height + i * 2);

                spriteBatch.Draw(pixel, new Rectangle(glowRect.X, glowRect.Y, glowRect.Width, 1), layerColor);
                spriteBatch.Draw(pixel, new Rectangle(glowRect.X, glowRect.Bottom - 1, glowRect.Width, 1), layerColor);
                spriteBatch.Draw(pixel, new Rectangle(glowRect.X, glowRect.Y, 1, glowRect.Height), layerColor);
                spriteBatch.Draw(pixel, new Rectangle(glowRect.Right - 1, glowRect.Y, 1, glowRect.Height), layerColor);
            }
        }

        /// <summary>RGB of a TEXT_COLOR_* value as RenderTipTextList sets it (ZzzInventory.cpp:385-443).</summary>
        public static Color GetSourceTextColor(int textColor) => textColor switch
        {
            SourceConstants.TEXT_COLOR_BLUE => new Color(0.5f, 0.7f, 1f),
            SourceConstants.TEXT_COLOR_GRAY => new Color(0.4f, 0.4f, 0.4f),
            SourceConstants.TEXT_COLOR_GREEN_BLUE => new Color(0, 255, 0),
            SourceConstants.TEXT_COLOR_RED => new Color(1f, 0.2f, 0.1f),
            SourceConstants.TEXT_COLOR_YELLOW => new Color(1f, 0.8f, 0.1f),
            SourceConstants.TEXT_COLOR_GREEN => new Color(0.1f, 1f, 0.5f),
            SourceConstants.TEXT_COLOR_PURPLE => new Color(1f, 0.1f, 1f),
            SourceConstants.TEXT_COLOR_REDPURPLE => new Color(0.8f, 0.5f, 0.8f),
            SourceConstants.TEXT_COLOR_VIOLET => new Color(0.7f, 0.4f, 1f),
            SourceConstants.TEXT_COLOR_ORANGE => new Color(0.9f, 0.42f, 0.04f),
            _ => Color.White, // WHITE, DARKRED, DARKBLUE, DARKYELLOW
        };

        /// <summary>Background of a TEXT_COLOR_* row (DARKRED/DARKBLUE/DARKYELLOW/GREEN_BLUE), or null.</summary>
        public static Color? GetSourceTextBackground(int textColor) => textColor switch
        {
            SourceConstants.TEXT_COLOR_DARKRED => new Color(160, 0, 0),
            SourceConstants.TEXT_COLOR_DARKBLUE => new Color(0, 0, 160),
            SourceConstants.TEXT_COLOR_DARKYELLOW => new Color(160, 102, 0),
            SourceConstants.TEXT_COLOR_GREEN_BLUE => new Color(60, 60, 200),
            _ => null,
        };

        /// <summary>
        /// Draws one tooltip row like RenderTipTextList: optional background box (SetBgColor) and bold text.
        /// SpriteFont has no bold face, so bold rows are drawn twice with a one pixel offset.
        /// </summary>
        public static void DrawTooltipRow(SpriteBatch sb, SpriteFont font, IReadOnlyList<(string Text, Color Color)> lines, int index,
            string text, Vector2 position, Color color, float scale, Rectangle rowBounds)
        {
            if (sb == null || font == null || string.IsNullOrEmpty(text)) return;
            var style = ItemTooltipLines.StyleOf(lines, index);
            if (style.Background is Color bg && GraphicsManager.Instance?.Pixel is Texture2D pixel)
                sb.Draw(pixel, rowBounds, bg);
            sb.DrawString(font, text, position, color, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
            if (style.Bold)
                sb.DrawString(font, text, position + new Vector2(1f, 0f), color, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        }

        /// <summary>Background of a row (for renderers that draw text themselves).</summary>
        public static void DrawTooltipRowBackground(SpriteBatch sb, IReadOnlyList<(string Text, Color Color)> lines, int index, Rectangle rowBounds)
        {
            var style = ItemTooltipLines.StyleOf(lines, index);
            if (style.Background is Color bg && GraphicsManager.Instance?.Pixel is Texture2D pixel)
                sb.Draw(pixel, rowBounds, bg);
        }

        [ThreadStatic] private static SourceItemTooltip t_tooltipBuilder;
        [ThreadStatic] private static List<SourceTooltipLine> t_lastLines;
        [ThreadStatic] private static (InventoryItem Item, int Durability, ItemTooltipType Type, bool Npc, long? Price, bool Equipped, int Tax, int StateHash) t_lastKey;

        /// <summary>
        /// Original item tooltip rows (RenderItemInfo, ZzzInventory.cpp:2096) with TEXT_COLOR value and bold flag.
        /// The leading "\n" row of the original is dropped; other blank rows are empty strings.
        /// The result for the same item/context is cached, so per-frame callers do not rebuild it.
        /// </summary>
        public static IReadOnlyList<SourceTooltipLine> BuildSourceTooltip(InventoryItem item, ItemTooltipContext context = null)
        {
            if (item?.Definition == null)
                return Array.Empty<SourceTooltipLine>();

            context ??= new ItemTooltipContext { NpcShopOpen = NpcShopControl.IsOpen };
            var state = context.Character ?? MuGame.Network?.GetCharacterState();
            int heroHash = state == null ? 0 : HashCode.Combine(state.Level, (int)state.Class, state.TotalStrength, state.TotalAgility, state.TotalEnergy, state.TotalVitality, state.TotalLeadership, state.InventoryZen);
            int petHash = context.PetInfo == null ? -1 : HashCode.Combine(context.PetInfo.m_wLevel, context.PetInfo.m_dwExp1, context.PetInfo.m_wLife);
            var key = (item, item.Durability, context.Type, context.NpcShopOpen, context.PersonalShopPrice, context.IsEquipped, context.NpcTaxRate, HashCode.Combine(heroHash, petHash));
            if (t_lastLines != null && t_lastKey.Equals(key))
                return t_lastLines;

            t_tooltipBuilder ??= new SourceItemTooltip();
            var lines = t_tooltipBuilder.Build(item, context);
            if (lines.Count > 0 && lines[0].Text.Length == 0)
                lines.RemoveAt(0);
            t_lastLines = lines;
            t_lastKey = key;
            return lines;
        }

        /// <summary>
        /// Tooltip rows as (text, color) for the existing renderers. Blank rows are empty strings.
        /// <paramref name="context"/> carries the NPC shop / personal shop state (prices, GlobalText 62/63/1620).
        /// </summary>
        public static ItemTooltipLines BuildTooltipLines(InventoryItem item, ItemTooltipContext context = null)
        {
            var lines = new ItemTooltipLines();
            if (item?.Definition == null || item?.Details is null)
                return lines;

            // TOOLTIP_TYPE_REPAIR: with the NPC repair window open the original renders RenderRepairInfo instead.
            var npcShop = NpcShopControl.IsOpen ? NpcShopControl.Instance : null;
            if (npcShop != null && npcShop.IsRepairMode && (context == null || context.Type == ItemTooltipType.Inventory))
                return BuildRepairTooltipLines(item, npcRepairShop: true, context);

            AppendRows(lines, BuildSourceTooltip(item, context));
            return lines;
        }

        /// <summary>RenderRepairInfo (ZzzInventory.cpp:5642): NPC repair (npcRepairShop) or inventory self-repair price.</summary>
        public static ItemTooltipLines BuildRepairTooltipLines(InventoryItem item, bool npcRepairShop, ItemTooltipContext context = null)
        {
            var lines = new ItemTooltipLines();
            if (item?.Definition == null) return lines;
            var ctx = new ItemTooltipContext
            {
                Type = ItemTooltipType.Inventory,
                NpcRepairShop = npcRepairShop,
                PetInfo = context?.PetInfo,
                Character = context?.Character,
            };
            t_tooltipBuilder ??= new SourceItemTooltip();
            var rows = t_tooltipBuilder.BuildRepair(item, ctx);
            if (rows.Count > 0 && rows[0].Text.Length == 0) rows.RemoveAt(0);
            AppendRows(lines, rows);
            t_lastLines = null; // the shared builder was used for another layout
            return lines;
        }

        private static void AppendRows(ItemTooltipLines lines, IReadOnlyList<SourceTooltipLine> rows)
        {
            foreach (var line in rows)
                lines.Add(line.Text, GetSourceTextColor(line.TextColor), new ItemTooltipRowStyle(line.Bold, line.Text.Length == 0 ? null : GetSourceTextBackground(line.TextColor)));
        }
    }
}
