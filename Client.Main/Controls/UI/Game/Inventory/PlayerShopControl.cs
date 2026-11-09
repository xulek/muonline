#nullable enable
using System;
using System.Collections.Generic;
using Client.Main.Controllers;
using Client.Main.Controls.UI.Common;
using Client.Main.Controls.UI.Game.Common;
using Client.Main.Core.Utilities;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Client.Main.Controls.UI.Game.Inventory
{
    /// <summary>
    /// Window showing another player's personal shop: items in their real shop layout,
    /// price tags, item tooltips and click-to-buy with confirmation.
    /// </summary>
    public class PlayerShopControl : UIControl
    {
        private const int Pad = 14;
        private const int HeaderHeight = 48;
        private const int WindowWidth = PlayerShopUi.GridWidth + Pad * 2;
        private static readonly Rectangle GridLocal = new(Pad, HeaderHeight + 10, PlayerShopUi.GridWidth, PlayerShopUi.GridHeight);
        private static readonly int InfoY = GridLocal.Bottom + 10;
        private static readonly int WindowHeight = InfoY + 50;
        private static readonly Rectangle CloseLocal = new(WindowWidth - 40, 10, 28, 28);

        private string _storeName = string.Empty;
        private ushort _ownerId;
        private string _ownerName = string.Empty;
        private readonly List<ShopItemEntry> _items = new();
        private readonly ItemTextureCache _textures = new();
        private ushort? _requestedOwner;
        private bool _purchasePending;
        private byte? _pendingPurchaseSlot;
        private ShopItemEntry? _hovered;
        private bool _closeHovered;
        private bool _isDragging;
        private Point _dragOffset;
        private GameTime? _gameTime;
        private string? _status;
        private Color _statusColor;
        private double _statusUntil;
        private string? _reopenStatus;
        private bool _keepPosition;

        public event Action<ushort, string, byte>? BuyRequested;
        public event Action<ushort, string>? ShopClosed;
        /// <summary>Raised after a successful purchase so the caller can fetch a fresh offer from the seller.</summary>
        public event Action<ushort, string>? RefreshRequested;

        public PlayerShopControl()
        {
            AutoViewSize = false;
            Interactive = true;
            ControlSize = new Point(WindowWidth, WindowHeight);
            ViewSize = ControlSize;
            Align = ControlAlign.HorizontalCenter | ControlAlign.VerticalCenter;
            Visible = false;
        }

        private Rectangle Grid => Translate(GridLocal);

        private Rectangle Translate(Rectangle local)
            => new(DisplayRectangle.X + local.X, DisplayRectangle.Y + local.Y, local.Width, local.Height);

        public void ExpectShop(ushort ownerId)
        {
            CloseShop();
            _keepPosition = false;
            _reopenStatus = null;
            _requestedOwner = ownerId;
        }

        public bool AcceptList(ushort ownerId, string ownerName, string storeName,
            IReadOnlyList<(byte Slot, byte[] ItemData, uint Price)> items, bool isUpdate)
        {
            if (isUpdate ? !Visible || _ownerId != ownerId : _requestedOwner != ownerId)
                return false;
            _requestedOwner = null;
            OpenShop(ownerId, ownerName, storeName, items);
            return true;
        }

        public void PurchaseFinished()
        {
            _purchasePending = false;
            _pendingPurchaseSlot = null;
        }

        public void PurchaseFinished(ushort ownerId, bool success)
        {
            if (!Visible || _ownerId != ownerId) return;
            if (!success)
            {
                PurchaseFinished();
                return;
            }

            // The offer is stale after a purchase: close it and ask for a fresh list from the same seller.
            var bought = _pendingPurchaseSlot is byte slot ? _items.Find(entry => entry.Slot == slot) : null;
            string ownerName = _ownerName;
            CloseShop();
            _requestedOwner = ownerId;
            _keepPosition = true;
            _reopenStatus = bought != null ? $"Purchased {PlayerShopUi.GetItemDisplayName(bought.Item)}" : "Purchase completed";
            RefreshRequested?.Invoke(ownerId, ownerName);
        }

        public void OpenShop(ushort ownerId, string ownerName, string storeName, IReadOnlyList<(byte Slot, byte[] ItemData, uint Price)> items)
        {
            bool refresh = Visible && _ownerId == ownerId;
            _ownerId = ownerId;
            _ownerName = ownerName;
            _storeName = storeName;
            _items.Clear();
            _textures.Clear();
            _hovered = null;

            var occupied = new bool[PlayerShopUi.Columns, PlayerShopUi.Rows];
            foreach (var (slot, data, price) in items)
            {
                var item = PlayerShopUi.CreateItem(data, Point.Zero);
                bool known = item != null;
                item ??= new InventoryItem(new ItemDefinition(0, ItemDatabase.GetItemName(data) ?? "Unknown item", 1, 1), Point.Zero, data);
                var cell = PlayerShopUi.SlotToCell(slot);
                if (!Fits(occupied, cell, item) && !TryFindFree(occupied, item, out cell)) continue;
                item.GridPosition = cell;
                Mark(occupied, cell, item);
                _items.Add(new ShopItemEntry(slot, price, item, known));
            }

            if (!refresh)
            {
                if (!_keepPosition) Align = ControlAlign.HorizontalCenter | ControlAlign.VerticalCenter;
                _status = null;
                if (_reopenStatus != null) SetStatus(_reopenStatus, ModernHudTheme.Success);
                else SoundController.Instance.PlayBuffer("Sound/iCreateWindow.wav");
            }
            _keepPosition = false;
            _reopenStatus = null;
            Visible = true;
        }

        public void CloseShop()
        {
            if (Visible) ShopClosed?.Invoke(_ownerId, _ownerName);
            Visible = false;
            _requestedOwner = null;
            PurchaseFinished();
            _items.Clear();
            _textures.Clear();
            _hovered = null;
            _isDragging = false;
        }

        public void CloseIfOwner(ushort ownerId)
        {
            if (Visible && _ownerId == ownerId)
            {
                CloseShop();
                RequestDialog.ShowInfo($"{_ownerName} closed the shop.");
            }
        }

        private void SetStatus(string text, Color color)
        {
            _status = text;
            _statusColor = color;
            _statusUntil = (_gameTime?.TotalGameTime.TotalSeconds ?? 0) + 4;
        }

        private static uint CurrentZen => MuGame.Network?.GetCharacterState()?.InventoryZen ?? 0;

        // ─────────────────────────────── Update ───────────────────────────────

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
            _gameTime = gameTime;
            if (!Visible) return;

            if (MuGame.Instance.Keyboard.IsKeyDown(Keys.Escape) && MuGame.Instance.PrevKeyboard.IsKeyUp(Keys.Escape))
            {
                Scene?.ConsumeKeyboardEscape();
                CloseShop();
                return;
            }

            var mouse = MuGame.Instance.UiMouseState;
            var previous = MuGame.Instance.PrevUiMouseState;
            Point position = mouse.Position;
            Point hit = PointerForHitTest(position); // hovers and clicks only when no window covers this one
            bool leftDown = mouse.LeftButton == ButtonState.Pressed;
            bool leftPressed = leftDown && previous.LeftButton == ButtonState.Released;

            _closeHovered = Translate(CloseLocal).Contains(hit);
            _hovered = null;
            if (!_isDragging && Grid.Contains(hit))
            {
                foreach (var entry in _items)
                {
                    if (PlayerShopUi.CellRect(Grid, entry.Item.GridPosition, entry.Item.Definition.Width, entry.Item.Definition.Height).Contains(hit))
                    {
                        _hovered = entry;
                        break;
                    }
                }
            }

            if (_isDragging)
            {
                if (leftDown) { X = position.X - _dragOffset.X; Y = position.Y - _dragOffset.Y; }
                else _isDragging = false;
                return;
            }

            if (!leftPressed || !DisplayRectangle.Contains(hit)) return;
            Scene?.SetMouseInputConsumed();

            if (_closeHovered)
            {
                CloseShop();
                return;
            }

            if (Translate(new Rectangle(0, 0, WindowWidth, HeaderHeight)).Contains(hit))
            {
                _isDragging = true;
                _dragOffset = new Point(position.X - X, position.Y - Y);
                Align = ControlAlign.None;
                return;
            }

            if (_hovered != null && !_purchasePending)
                RequestPurchase(_hovered);
        }

        private void RequestPurchase(ShopItemEntry entry)
        {
            string itemName = PlayerShopUi.GetItemDisplayName(entry.Item);
            if (!entry.Known || entry.Price == 0)
            {
                RequestDialog.ShowInfo("This item or its price cannot be identified. Purchase is unavailable.");
                return;
            }
            if (entry.Price > CurrentZen)
            {
                RequestDialog.ShowInfo($"Not enough Zen. {itemName} costs {PlayerShopUi.FormatZen(entry.Price)} Zen, you have {PlayerShopUi.FormatZen(CurrentZen)}.");
                return;
            }

            ushort ownerId = _ownerId;
            string ownerName = _ownerName;
            RequestDialog.Show($"Buy {itemName} for {PlayerShopUi.FormatZen(entry.Price)} Zen?",
                onAccept: () =>
                {
                    if (Visible && !_purchasePending && _ownerId == ownerId && _ownerName == ownerName && _items.Contains(entry))
                    {
                        _purchasePending = true;
                        _pendingPurchaseSlot = entry.Slot;
                        BuyRequested?.Invoke(ownerId, ownerName, entry.Slot);
                    }
                },
                acceptText: "Buy", rejectText: "Cancel");
        }

        // ─────────────────────────────── Draw ───────────────────────────────

        public override void Draw(GameTime gameTime)
        {
            if (!Visible || Status != GameControlStatus.Ready) return;
            var sb = GraphicsManager.Instance.Sprite;
            var pixel = GraphicsManager.Instance.Pixel;
            if (sb == null || pixel == null) return;

            var scope = PlayerShopUi.BeginUiBatch();
            try
            {
                var rect = DisplayRectangle;
                PlayerShopUi.DrawWindow(sb, rect, Alpha);
                string title = string.IsNullOrWhiteSpace(_storeName) ? "Personal Shop" : _storeName;
                PlayerShopUi.DrawHeader(sb, Translate(new Rectangle(0, 0, WindowWidth, HeaderHeight)), title, $"Seller: {_ownerName}", Alpha);
                PlayerShopUi.DrawCloseButton(sb, Translate(CloseLocal), _closeHovered, Alpha);

                var grid = Grid;
                PlayerShopUi.DrawGrid(sb, grid, Alpha);
                uint zen = CurrentZen;
                foreach (var entry in _items)
                {
                    var item = entry.Item;
                    var itemRect = PlayerShopUi.CellRect(grid, item.GridPosition, item.Definition.Width, item.Definition.Height);
                    bool affordable = entry.Price <= zen;
                    PlayerShopUi.DrawItem(sb, _textures, item, itemRect, entry == _hovered, !affordable, gameTime, Alpha);
                    PlayerShopUi.DrawPriceTag(sb, itemRect, entry.Price, affordable ? null : ModernHudTheme.Danger, Alpha);
                }

                if (_items.Count == 0)
                    PlayerShopUi.DrawTextCentered(sb, "This shop has no items left.", grid, ModernHudTheme.TextGray, 12f, Alpha);

                if (_purchasePending)
                {
                    sb.Draw(pixel, grid, Color.Black * 0.55f * Alpha);
                    PlayerShopUi.DrawTextCentered(sb, "Purchasing...", grid, ModernHudTheme.TextGold, 14f, Alpha);
                }

                DrawInfo(sb);
            }
            finally
            {
                scope?.Dispose();
            }
        }

        private void DrawInfo(Microsoft.Xna.Framework.Graphics.SpriteBatch sb)
        {
            var info = Translate(new Rectangle(Pad - 4, InfoY, WindowWidth - (Pad - 4) * 2, 22));
            PlayerShopUi.DrawPanel(sb, info, ModernHudTheme.BgMid, Alpha);
            PlayerShopUi.DrawText(sb, "Your Zen:", new Vector2(info.X + 8, info.Y + 4), ModernHudTheme.TextGray, 11f, Alpha);
            string zen = PlayerShopUi.FormatZen(CurrentZen);
            var zenSize = PlayerShopUi.Measure(zen, 12f);
            PlayerShopUi.DrawText(sb, zen, new Vector2(info.Right - zenSize.X - 8, info.Y + 3), ModernHudTheme.TextGold, 12f, Alpha);

            string hint;
            Color hintColor;
            if (_status != null && _gameTime != null && _gameTime.TotalGameTime.TotalSeconds < _statusUntil)
            {
                hint = _status;
                hintColor = _statusColor;
            }
            else
            {
                hint = _items.Count == 1 ? "1 item  |  Click an item to buy" : $"{_items.Count} items  |  Click an item to buy";
                hintColor = ModernHudTheme.TextGray;
            }
            var line = Translate(new Rectangle(Pad, InfoY + 26, WindowWidth - Pad * 2, 18));
            PlayerShopUi.DrawTextCentered(sb, PlayerShopUi.Ellipsize(hint, 11f, line.Width), line, hintColor, 11f, Alpha);
        }

        public override void DrawAfter(GameTime gameTime)
        {
            base.DrawAfter(gameTime);
            if (!Visible || _hovered == null || _isDragging) return;
            var sb = GraphicsManager.Instance.Sprite;
            if (sb == null) return;

            var lines = ItemUiHelper.BuildTooltipLines(_hovered.Item);
            uint zen = CurrentZen;
            lines.Add((string.Empty, Color.White));
            lines.Add(($"Price: {PlayerShopUi.FormatZen(_hovered.Price)} Zen", PlayerShopUi.GetPriceColor(_hovered.Price)));
            if (!_hovered.Known)
                lines.Add(("Unknown item - cannot be bought", ModernHudTheme.Danger));
            else if (_hovered.Price > zen)
                lines.Add(($"Not enough Zen (missing {PlayerShopUi.FormatZen(_hovered.Price - zen)})", ModernHudTheme.Danger));
            else
                lines.Add(("Click to buy", ModernHudTheme.Success));

            var item = _hovered.Item;
            var avoid = PlayerShopUi.CellRect(Grid, item.GridPosition, item.Definition.Width, item.Definition.Height);
            var scope = PlayerShopUi.BeginUiBatch();
            try
            {
                PlayerShopUi.DrawTooltip(sb, lines, item, avoid);
            }
            finally
            {
                scope?.Dispose();
            }
        }

        private static bool Fits(bool[,] occupied, Point cell, InventoryItem item)
        {
            int width = item.Definition.Width, height = item.Definition.Height;
            if (!PlayerShopUi.FitsInGrid(cell, width, height)) return false;
            for (int y = cell.Y; y < cell.Y + height; y++)
            for (int x = cell.X; x < cell.X + width; x++)
                if (occupied[x, y]) return false;
            return true;
        }

        private static bool TryFindFree(bool[,] occupied, InventoryItem item, out Point cell)
        {
            for (int y = 0; y < PlayerShopUi.Rows; y++)
            for (int x = 0; x < PlayerShopUi.Columns; x++)
            {
                cell = new Point(x, y);
                if (Fits(occupied, cell, item)) return true;
            }
            cell = Point.Zero;
            return false;
        }

        private static void Mark(bool[,] occupied, Point cell, InventoryItem item)
        {
            for (int y = cell.Y; y < cell.Y + item.Definition.Height; y++)
            for (int x = cell.X; x < cell.X + item.Definition.Width; x++)
                occupied[x, y] = true;
        }

        private sealed record ShopItemEntry(byte Slot, uint Price, InventoryItem Item, bool Known);
    }
}
