using System;
using Client.Main.Controllers;
using Client.Main.Controls.UI.Common;
using Client.Main.Controls.UI.Game.Common;
using Client.Main.Controls.UI.Ornate;
using Client.Main.Core.Utilities;
using Client.Main.Controls.UI.Game.Inventory;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Controls.UI.Game
{
    // Modern theme: the shop drawn with the ornate kit - title plaque, slot grid with rarity-rimmed items, the player's
    // Zen, Repair / Repair All buttons for repairing NPCs and ornate tooltips with the price.
    public partial class NpcShopControl
    {
        private const int OrnateSidePad = 22;
        private const int OrnateGridTop = 54;
        private const int OrnateButtonRow = 44;
        private const int OrnateFooterHeight = 34;

        private Rectangle _ornateZenRect;

        private void BuildOrnateLayout()
        {
            _headerRect = new Rectangle(0, 0, WINDOW_WIDTH, OrnateGridTop - 10);
            _gridRect = new Rectangle(OrnateSidePad, OrnateGridTop, GRID_WIDTH, GRID_HEIGHT);
            _gridFrameRect = _gridRect;
            _closeButtonRect = new Rectangle(WINDOW_WIDTH - 44, 12, 28, 28);

            int y = _gridRect.Bottom + 14;
            if (_isRepairShop)
            {
                int width = (GRID_WIDTH - 8) / 2;
                _repairButtonRect = new Rectangle(OrnateSidePad, y + 4, width, 30);
                _repairAllButtonRect = new Rectangle(OrnateSidePad + width + 8, y + 4, width, 30);
                _buttonAreaRect = new Rectangle(OrnateSidePad, y, GRID_WIDTH, OrnateButtonRow);
                y += OrnateButtonRow;
            }
            else
            {
                _buttonAreaRect = Rectangle.Empty;
                _repairButtonRect = Rectangle.Empty;
                _repairAllButtonRect = Rectangle.Empty;
            }

            _footerRect = new Rectangle(OrnateSidePad, y, GRID_WIDTH, OrnateFooterHeight);
            _ornateZenRect = new Rectangle(OrnateSidePad, y + 2, GRID_WIDTH, 28);
        }

        private void DrawOrnateShop(SpriteBatch sb)
        {
            float alpha = Alpha;
            Rectangle frame = DisplayRectangle;
            OrnateDraw.Window(sb, frame, alpha);

            Rectangle grid = Translate(_gridRect);
            OrnateItemUi.Grid(sb, grid, SHOP_COLUMNS, SHOP_ROWS, SHOP_SQUARE_WIDTH, alpha);
            bool repairing = _shopMode == ShopMode.Repair;
            if (repairing)
            {
                Rectangle well = grid;
                well.Inflate(6, 6);
                UiDrawHelper.DrawBorder(sb, well, OrnateStyle.Danger * (0.8f * alpha), 2);
            }

            DrawOrnateItems(sb, grid, alpha);

            if (_isRepairShop)
            {
                OrnateItemUi.ActionButton(sb, Translate(_repairButtonRect), "repair", "Repair",
                    new WidgetState(Hover: _repairButtonHovered, Selected: repairing), alpha);
                OrnateItemUi.ActionButton(sb, Translate(_repairAllButtonRect), "repair", "Repair All",
                    new WidgetState(Hover: _repairAllButtonHovered), alpha);
            }

            OrnateItemUi.ZenField(sb, Translate(_ornateZenRect), _characterState?.InventoryZen ?? 0, alpha, "Your Zen");

            OrnateWidgets.CloseButton(sb, Translate(_closeButtonRect), _closeHovered, alpha);
            OrnateDraw.TitlePlaque(sb, repairing ? "Repair" : "Shop", frame.Center.X, frame.Y + 2, 32, alpha);
        }

        private void DrawOrnateItems(SpriteBatch sb, Rectangle grid, float alpha)
        {
            var font = _font ?? GraphicsManager.Instance.Font;
            var pixel = GraphicsManager.Instance.Pixel;
            _jewelEntries.Clear();

            foreach (var item in _items)
            {
                if (item.GridPosition.Y < 0 || item.GridPosition.Y >= SHOP_ROWS)
                    continue;

                var rect = new Rectangle(
                    grid.X + item.GridPosition.X * SHOP_SQUARE_WIDTH,
                    grid.Y + item.GridPosition.Y * SHOP_SQUARE_HEIGHT,
                    item.Definition.Width * SHOP_SQUARE_WIDTH,
                    item.Definition.Height * SHOP_SQUARE_HEIGHT);

                bool hovered = item == _hoveredItem;
                OrnateItemUi.ItemBacking(sb, rect, hovered ? OrnateStyle.Gold : RarityColor(item), alpha);
                if (hovered)
                    sb.Draw(pixel, new Rectangle(rect.X + 2, rect.Y + 2, rect.Width - 4, rect.Height - 4), new Color(255, 214, 128) * (0.08f * alpha));

                Texture2D texture = ResolveItemTexture(item, rect.Width, rect.Height, hovered);
                if (texture != null)
                {
                    sb.Draw(texture, rect, Color.White * alpha);
                    if (JewelShineOverlay.ShouldShine(item))
                        _jewelEntries.Add((item, rect));
                }
                else if (pixel != null)
                {
                    ItemGridRenderHelper.DrawItemPlaceholder(sb, pixel, font, rect, item, Theme.BgLight, Theme.TextGray * 0.8f);
                }

                if (font != null && item.Definition.BaseDurability == 0 && item.Definition.MagicDurability == 0 && item.Durability > 1)
                    ItemGridRenderHelper.DrawItemStackCount(sb, font, rect, item.Durability, Theme.TextGold, alpha);

                ItemGridRenderHelper.DrawItemLevelBadge(sb, pixel, font, rect, item.Details.Level,
                    lvl => lvl >= 9 ? Theme.AccentBright : lvl >= 7 ? Theme.Accent : lvl >= 4 ? Theme.AccentDim : Theme.TextGray,
                    new Color(0, 0, 0, 180));
            }

            if (_jewelEntries.Count > 0)
                JewelShineOverlay.DrawBatch(sb, _jewelEntries, _currentGameTime, alpha, UiScaler.SpriteTransform);
        }

        private static Color? RarityColor(InventoryItem item) =>
            item.Details.IsExcellent ? new Color(110, 230, 110) :
            item.Details.IsAncient ? new Color(90, 190, 255) :
            item.Details.Level >= 7 ? OrnateStyle.Gold :
            null;

        private void DrawOrnateTooltip(SpriteBatch sb)
        {
            if (_hoveredItem == null)
                return;

            var lines = ItemUiHelper.BuildTooltipLines(_hoveredItem);
            int buyPrice = ItemPriceCalculator.CalculateBuyPrice(_hoveredItem);
            if (buyPrice > 0)
            {
                lines.Add((string.Empty, Color.White));
                long zen = _characterState?.InventoryZen ?? 0;
                lines.Add(($"Price: {OrnateItemUi.FormatZen(buyPrice)} Zen", buyPrice <= zen ? OrnateStyle.Gold : OrnateStyle.Danger));
                if (buyPrice > zen)
                    lines.Add(($"Not enough Zen (missing {OrnateItemUi.FormatZen(buyPrice - zen)})", OrnateStyle.Danger));
            }

            var itemRect = new Rectangle(
                DisplayRectangle.X + _gridRect.X + _hoveredItem.GridPosition.X * SHOP_SQUARE_WIDTH,
                DisplayRectangle.Y + _gridRect.Y + _hoveredItem.GridPosition.Y * SHOP_SQUARE_HEIGHT,
                _hoveredItem.Definition.Width * SHOP_SQUARE_WIDTH,
                _hoveredItem.Definition.Height * SHOP_SQUARE_HEIGHT);
            Color rarity = RarityColor(_hoveredItem) ?? OrnateStyle.TextLight;
            OrnateItemUi.Tooltip(sb, lines, rarity, itemRect, MuGame.Instance.UiMouseState.Position);
        }
    }
}
