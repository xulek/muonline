#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Client.Main.Controllers;
using Client.Main.Controls.UI.Common;
using Client.Main.Controls.UI.Game.Common;
using Client.Main.Core.Client;
using Client.Main.Core.Utilities;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Client.Main.Controls.UI.Game.Inventory
{
    /// <summary>
    /// Editor for the local player's personal shop (Season 6 slots 204-235).
    /// Items are added by dragging or right-clicking them in the inventory, priced in an inline
    /// editor, re-priced with a click and taken back with a right-click.
    /// </summary>
    public sealed class OwnPlayerShopControl : UIControl
    {
        private const int FirstSlot = PlayerShopUi.FirstShopSlot;
        private const int MaxTitleBytes = 36;
        private const int Pad = 14;
        private const int HeaderHeight = 48;
        private const int WindowWidth = PlayerShopUi.GridWidth + Pad * 2;
        private const int TitleLabelY = HeaderHeight + 8;
        private const int TitleFieldY = TitleLabelY + 16;
        private static readonly Rectangle GridLocal = new(Pad, TitleFieldY + 36, PlayerShopUi.GridWidth, PlayerShopUi.GridHeight);
        private static readonly int SummaryY = GridLocal.Bottom + 10;
        private static readonly int EditorY = SummaryY + 30;
        private static readonly int ActionY = EditorY + 62;
        private static readonly int WindowHeight = ActionY + 44;
        private static readonly Rectangle CloseLocal = new(WindowWidth - 34, 12, 22, 22);
        private static readonly Rectangle ActionLocal = new(Pad, ActionY, PlayerShopUi.GridWidth, 32);
        private static readonly Rectangle ConfirmLocal = new(Pad + 150, EditorY + 26, 54, 24);
        private static readonly Rectangle CancelLocal = new(Pad + 210, EditorY + 26, 62, 24);

        private readonly CharacterState _state;
        private readonly TextFieldControl _title;
        private readonly TextFieldControl _price;
        private readonly Dictionary<byte, uint> _prices = new();
        private readonly Dictionary<byte, InventoryItem> _shopItems = new();
        private readonly ItemTextureCache _textures = new();
        private byte[]? _pendingItem;
        private uint _movingPrice;
        private PricingRequest? _pricing;
        private string _lastPrice = string.Empty;
        private TextFieldControl? _focusedField;

        private byte? _hoveredSlot;
        private bool _closeHovered, _actionHovered, _confirmHovered, _cancelHovered;
        private bool _isDragging;
        private Point _dragOffset;
        private GameTime? _gameTime;
        private string? _status;
        private Color _statusColor;
        private double _statusUntil;

        public OwnPlayerShopControl(CharacterState state)
        {
            _state = state;
            AutoViewSize = false;
            Interactive = true;
            ControlSize = ViewSize = new Point(WindowWidth, WindowHeight);
            Align = ControlAlign.Left | ControlAlign.VerticalCenter;
            Visible = false;

            _title = CreateField(Pad, TitleFieldY, PlayerShopUi.GridWidth);
            _title.ValueChanged += (_, _) => LimitTitle();
            _price = CreateField(Pad, EditorY + 26, 130);
            _price.Visible = false;
            _price.ValueChanged += (_, _) => SanitizePrice();
            // Deferred one frame: the focused field keeps Enter from also opening the chat this frame.
            _price.EnterKeyPressed += (_, _) => MuGame.ScheduleOnMainThread(ConfirmPricing);
            Controls.Add(_title);
            Controls.Add(_price);

            _state.PlayerShopPriceResult += OnPriceResult;
            _state.PlayerShopMoveResult += OnMoveResult;
        }

        private static TextFieldControl CreateField(int x, int y, int width)
        {
            var field = TextFieldControl.Create();
            field.X = x;
            field.Y = y;
            field.ViewSize = new Point(width, 24);
            field.FontSize = 12f;
            field.TextColor = ModernHudTheme.TextWhite;
            field.BackgroundColor = ModernHudTheme.SlotBg;
            field.BorderColor = ModernHudTheme.BorderInner;
            field.BorderThickness = 1;
            return field;
        }

        public override void Dispose()
        {
            _state.PlayerShopPriceResult -= OnPriceResult;
            _state.PlayerShopMoveResult -= OnMoveResult;
            base.Dispose();
        }

        // ─────────────────────────────── Visibility ───────────────────────────────

        public void Show()
        {
            var inventory = FindInventory();
            if (inventory is { Visible: true })
            {
                Align = ControlAlign.None;
                X = Math.Max(10, inventory.X - WindowWidth - 8);
                Y = Math.Max(10, inventory.Y);
            }
            Visible = true;
            BringToFront();
        }

        private InventoryControl? _inventory;

        /// <summary>Finds the scene's inventory window without creating the lazy singleton.</summary>
        private InventoryControl? FindInventory()
        {
            if (_inventory != null || Parent == null) return _inventory;
            foreach (var control in Parent.Controls.GetSnapshotArray())
                if (control is InventoryControl inventory) return _inventory = inventory;
            return null;
        }

        private InventoryItem? CarriedInventoryItem => FindInventory()?._pickedItemRenderer?.Item;

        public void Hide()
        {
            CancelPricing();
            BlurField(_title);
            _isDragging = false;
            Visible = false;
        }

        public void Toggle()
        {
            if (Visible) Hide();
            else Show();
        }

        /// <summary>True when an inventory item released at <paramref name="position"/> should be handed to this window.</summary>
        public bool AcceptsDrop(Point position) => Visible && DisplayRectangle.Contains(position);

        private Rectangle Grid => Translate(GridLocal);

        private Rectangle Translate(Rectangle local)
            => new(DisplayRectangle.X + local.X, DisplayRectangle.Y + local.Y, local.Width, local.Height);
        private bool IsBusy => _state.IsPlayerShopMovePending || _state.IsPlayerShopPricePending || _pendingItem != null || _movingPrice != 0;

        // ─────────────────────────────── Adding items ───────────────────────────────

        /// <summary>Handles an inventory item dropped onto the window.</summary>
        public void StageItem(InventoryItem item, byte fromSlot, Point position)
        {
            Point cell = new(-1, -1);
            if (Grid.Contains(position))
                cell = PlayerShopUi.CellUnderCursor(Grid, position, item.Definition.Width, item.Definition.Height);
            BeginPlacement(item, fromSlot, cell);
        }

        /// <summary>Adds an inventory item to the first free shop position (inventory right-click).</summary>
        public void QuickAdd(InventoryItem item, byte fromSlot) => BeginPlacement(item, fromSlot, new Point(-1, -1));

        private void BeginPlacement(InventoryItem item, byte fromSlot, Point cell)
        {
            if (!CanEditItems(out string? reason))
            {
                RequestDialog.ShowInfo(reason!);
                return;
            }
            if (fromSlot < 12)
            {
                RequestDialog.ShowInfo("Unequip the item before putting it in your shop.");
                return;
            }
            if (fromSlot >= FirstSlot)
            {
                RequestDialog.ShowInfo("This item cannot be placed in the shop.");
                return;
            }

            int width = item.Definition.Width, height = item.Definition.Height;
            if (!CanPlace(cell.X, cell.Y, width, height) && !TryFindFreeCell(width, height, out cell))
            {
                RequestDialog.ShowInfo("There is no room for this item in your shop.");
                return;
            }

            BeginPricing(new PricingRequest(item, item.RawData.ToArray(), fromSlot, PlayerShopUi.CellToSlot(cell)), _lastPrice);
        }

        private bool CanEditItems(out string? reason)
        {
            reason = _state.IsPlayerShopOpen ? "Close your shop before changing its items or prices."
                   : IsBusy ? "Please wait until the previous item operation is confirmed."
                   : null;
            return reason == null;
        }

        private bool TryFindFreeCell(int width, int height, out Point cell)
        {
            for (int y = 0; y < PlayerShopUi.Rows; y++)
            for (int x = 0; x < PlayerShopUi.Columns; x++)
            {
                if (!CanPlace(x, y, width, height)) continue;
                cell = new Point(x, y);
                return true;
            }
            cell = new Point(-1, -1);
            return false;
        }

        private bool CanPlace(int x, int y, int width, int height)
        {
            if (!PlayerShopUi.FitsInGrid(new Point(x, y), width, height)) return false;
            var candidate = new Rectangle(x, y, width, height);
            foreach (var entry in _state.GetInventoryItems())
            {
                if (!PlayerShopUi.IsShopSlot(entry.Key)) continue;
                var definition = ItemDatabase.GetItemDefinition(entry.Value);
                if (definition == null) return false;
                var cell = PlayerShopUi.SlotToCell(entry.Key);
                if (candidate.Intersects(new Rectangle(cell.X, cell.Y, definition.Width, definition.Height))) return false;
            }
            return true;
        }

        // ─────────────────────────────── Pricing editor ───────────────────────────────

        private void BeginPricing(PricingRequest request, string initialPrice)
        {
            _pricing = request;
            _price.Value = initialPrice;
            _price.Visible = true;
            FocusField(_price);
        }

        private void CancelPricing()
        {
            _pricing = null;
            BlurField(_price);
            _price.Visible = false;
        }

        /// <summary>Removes keyboard focus so game hotkeys work again.</summary>
        private void BlurField(TextFieldControl field)
        {
            if (_focusedField == field) _focusedField = null;
            field.Blur();
            if (Scene?.FocusControl == field) Scene.FocusControl = null;
        }

        /// <summary>
        /// Gives keyboard focus to a field. The window keeps re-asserting it every frame because
        /// other controls updated in the same click frame may take over the scene focus.
        /// </summary>
        private void FocusField(TextFieldControl field)
        {
            if (_focusedField != null && _focusedField != field) BlurField(_focusedField);
            _focusedField = field;
            EnsureFieldFocus();
        }

        private void EnsureFieldFocus()
        {
            if (_focusedField is not { } field) return;
            if (!Visible || !field.Visible || !field.Interactive)
            {
                BlurField(field);
                return;
            }
            if (!field.IsFocused) field.Focus();
            if (Scene != null && Scene.FocusControl != field) Scene.FocusControl = field;
        }

        private void UpdateFieldFocus(Point position, bool leftPressed)
        {
            if (leftPressed)
            {
                if (_title.Interactive && _title.DisplayRectangle.Contains(position)) FocusField(_title);
                else if (_price.Visible && _price.DisplayRectangle.Contains(position)) FocusField(_price);
                else if (_focusedField != null && !(_pricing != null && _focusedField == _price && DisplayRectangle.Contains(position)))
                    BlurField(_focusedField);
            }
            EnsureFieldFocus();

            _title.BorderColor = _focusedField == _title ? ModernHudTheme.AccentBright : ModernHudTheme.BorderInner;
            _price.BorderColor = _focusedField == _price ? ModernHudTheme.AccentBright : ModernHudTheme.BorderInner;
        }

        private void ConfirmPricing()
        {
            if (_pricing is not { } request) return;
            if (!PlayerShopUi.TryParseZen(_price.Value, out uint price))
            {
                SetStatus("Enter a price between 1 and 2,147,483,647 Zen (e.g. 500k, 1.5kk).", ModernHudTheme.Danger);
                return;
            }
            if (!CanEditItems(out string? reason))
            {
                CancelPricing();
                RequestDialog.ShowInfo(reason!);
                return;
            }

            var cell = PlayerShopUi.SlotToCell(request.ToSlot);
            if (request.FromSlot != request.ToSlot &&
                !CanPlace(cell.X, cell.Y, request.Item.Definition.Width, request.Item.Definition.Height))
            {
                CancelPricing();
                RequestDialog.ShowInfo("That shop position is no longer free.");
                return;
            }

            _lastPrice = _price.Value.Trim();
            CancelPricing();
            _ = SetPrice(request.RawData, request.FromSlot, request.ToSlot, price);
        }

        private void SanitizePrice()
        {
            string value = _price.Value;
            var filtered = new StringBuilder(value.Length);
            foreach (char c in value.ToLowerInvariant())
                if (char.IsDigit(c) || c is 'k' or 'm' or 'b' or '.' or ',' or ' ')
                    filtered.Append(c);
            if (filtered.Length > 16) filtered.Length = 16;
            if (filtered.ToString() != value) _price.Value = filtered.ToString();
        }

        private void LimitTitle()
        {
            string value = _title.Value;
            if (Encoding.UTF8.GetByteCount(value) <= MaxTitleBytes) return;
            while (value.Length > 0 && Encoding.UTF8.GetByteCount(value) > MaxTitleBytes)
                value = value[..^1];
            _title.Value = value;
        }

        // ─────────────────────────────── Server round-trips ───────────────────────────────

        private async System.Threading.Tasks.Task SetPrice(byte[] rawData, byte fromSlot, byte toSlot, uint price)
        {
            if (!_state.TryBeginPlayerShopPrice(fromSlot, toSlot, price))
            {
                RequestDialog.ShowInfo("Another item operation is pending or this item is unavailable.");
                return;
            }
            _pendingItem = rawData;
            var service = MuGame.Network?.GetCharacterService();
            if (service == null || !await service.SendPlayerShopSetItemPriceAsync(fromSlot, price))
                _state.CompletePlayerShopPrice(fromSlot, false);
        }

        private void OnPriceResult(byte from, byte to, uint price, bool success)
        {
            MuGame.ScheduleOnMainThread(async () =>
            {
                var expected = _pendingItem;
                _pendingItem = null;
                if (!success || expected == null || _state.IsPlayerShopOpen ||
                    !_state.GetInventoryItems().TryGetValue(from, out var current) || !current.SequenceEqual(expected))
                {
                    RequestDialog.ShowInfo("The item price was not confirmed or the item changed. Nothing was moved.");
                    return;
                }
                if (from == to)
                {
                    _prices[to] = price;
                    SetStatus($"Price set to {PlayerShopUi.FormatZen(price)} Zen.", ModernHudTheme.Success);
                    return;
                }
                if (!_state.TryBeginPlayerShopMove(from, to))
                {
                    RequestDialog.ShowInfo("Another item operation is pending. Nothing was moved.");
                    return;
                }
                _movingPrice = price;
                var service = MuGame.Network?.GetCharacterService();
                if (service == null || !await service.SendPlayerShopMoveAsync(from, to))
                {
                    _state.TryFinishPlayerShopMove(to, out _);
                    _movingPrice = 0;
                    RequestDialog.ShowInfo("Could not send the item move.");
                }
            });
        }

        private void OnMoveResult(byte from, byte to, bool success)
        {
            MuGame.ScheduleOnMainThread(() =>
            {
                if (success && PlayerShopUi.IsShopSlot(to) && _movingPrice > 0)
                {
                    _prices[to] = _movingPrice;
                    SetStatus($"Item added for {PlayerShopUi.FormatZen(_movingPrice)} Zen.", ModernHudTheme.Success);
                }
                if (success && PlayerShopUi.IsShopSlot(from))
                {
                    _prices.Remove(from);
                    if (!PlayerShopUi.IsShopSlot(to)) SetStatus("Item returned to your inventory.", ModernHudTheme.TextWhite);
                }
                _movingPrice = 0;
            });
        }

        private async void OpenShop()
        {
            if (_state.IsPlayerShopOpen || IsBusy) return;
            if (GetOpenBlocker() is { } blocker)
            {
                RequestDialog.ShowInfo(blocker);
                return;
            }
            var service = MuGame.Network?.GetCharacterService();
            if (service != null)
            {
                SetStatus("Opening shop...", ModernHudTheme.TextGold);
                _state.PendingPlayerShopTitle = _title.Value.Trim();
                await service.SendPlayerShopOpenAsync(_state.PendingPlayerShopTitle);
            }
        }

        private async void CloseShop()
        {
            if (!_state.IsPlayerShopOpen) return;
            var service = MuGame.Network?.GetCharacterService();
            if (service != null)
            {
                SetStatus("Closing shop...", ModernHudTheme.TextGold);
                await service.SendPlayerShopCloseAsync();
            }
        }

        private string? GetOpenBlocker()
        {
            string title = _title.Value.Trim();
            if (string.IsNullOrWhiteSpace(title)) return "Enter a shop name.";
            if (Encoding.UTF8.GetByteCount(title) > MaxTitleBytes) return "The shop name is too long.";
            if (_shopItems.Count == 0) return "Add at least one item.";
            int unpriced = _shopItems.Keys.Count(slot => !_prices.ContainsKey(slot));
            if (unpriced > 0) return unpriced == 1 ? "1 item has no price." : $"{unpriced} items have no price.";
            return null;
        }

        private async void ReturnToInventory(byte fromSlot, ItemDefinition item)
        {
            int columns = InventoryControl.Columns, rows = InventoryControl.Rows;
            var occupied = new bool[columns, rows];
            foreach (var entry in _state.GetInventoryItems())
            {
                int index = entry.Key - 12;
                if (index < 0 || index >= columns * rows) continue;
                var existing = ItemDatabase.GetItemDefinition(entry.Value);
                if (existing == null) { RequestDialog.ShowInfo("Cannot determine free inventory space."); return; }
                int startX = index % columns, startY = index / columns;
                for (int y = startY; y < Math.Min(rows, startY + existing.Height); y++)
                    for (int x = startX; x < Math.Min(columns, startX + existing.Width); x++) occupied[x, y] = true;
            }
            for (int y = 0; y + item.Height <= rows; y++)
            for (int x = 0; x + item.Width <= columns; x++)
            {
                bool free = true;
                for (int dy = 0; dy < item.Height; dy++)
                    for (int dx = 0; dx < item.Width; dx++) free &= !occupied[x + dx, y + dy];
                if (!free) continue;
                byte toSlot = (byte)(12 + y * columns + x);
                if (!_state.TryBeginPlayerShopMove(fromSlot, toSlot)) return;
                var service = MuGame.Network?.GetCharacterService();
                if (service == null || !await service.SendPlayerShopMoveAsync(fromSlot, toSlot))
                {
                    _state.TryFinishPlayerShopMove(toSlot, out _);
                    RequestDialog.ShowInfo("Could not send the item move.");
                }
                return;
            }
            RequestDialog.ShowInfo("There is not enough room in your inventory.");
        }

        // ─────────────────────────────── Update ───────────────────────────────

        public override void Update(GameTime gameTime)
        {
            // Re-assert before the fields update so their keyboard polling sees the focus this frame.
            if (Visible) EnsureFieldFocus();
            base.Update(gameTime);
            _gameTime = gameTime;
            if (!Visible) return;

            RefreshShopItems();
            bool shopOpen = _state.IsPlayerShopOpen;
            _title.Interactive = !shopOpen;
            if (shopOpen && _title.IsFocused) BlurField(_title);
            if (_pricing != null && (shopOpen || !_state.GetInventoryItems().ContainsKey(_pricing.FromSlot))) CancelPricing();

            if (MuGame.Instance.Keyboard.IsKeyDown(Keys.Escape) && MuGame.Instance.PrevKeyboard.IsKeyUp(Keys.Escape))
            {
                Scene?.ConsumeKeyboardEscape();
                if (_pricing != null) CancelPricing();
                else if (_focusedField != null) BlurField(_focusedField);
                else Hide();
                return;
            }

            var mouse = MuGame.Instance.UiMouseState;
            var previous = MuGame.Instance.PrevUiMouseState;
            Point position = mouse.Position;
            bool leftDown = mouse.LeftButton == ButtonState.Pressed;
            bool leftPressed = leftDown && previous.LeftButton == ButtonState.Released;
            bool rightPressed = mouse.RightButton == ButtonState.Pressed && previous.RightButton == ButtonState.Released;

            UpdateFieldFocus(position, leftPressed && !_isDragging);

            if (_isDragging)
            {
                if (leftDown) { X = position.X - _dragOffset.X; Y = position.Y - _dragOffset.Y; }
                else _isDragging = false;
                return;
            }

            _closeHovered = Translate(CloseLocal).Contains(position);
            _actionHovered = Translate(ActionLocal).Contains(position);
            _confirmHovered = _pricing != null && Translate(ConfirmLocal).Contains(position);
            _cancelHovered = _pricing != null && Translate(CancelLocal).Contains(position);
            _hoveredSlot = FindShopSlotAt(position);

            if (!DisplayRectangle.Contains(position) || (!leftPressed && !rightPressed)) return;
            // An item carried from the inventory is dropped by InventoryControl on release.
            if (CarriedInventoryItem != null) return;
            Scene?.SetMouseInputConsumed();

            if (leftPressed)
            {
                if (_closeHovered) { Hide(); return; }
                if (_confirmHovered) { ConfirmPricing(); return; }
                if (_cancelHovered) { CancelPricing(); return; }
                if (_actionHovered)
                {
                    if (shopOpen) CloseShop();
                    else OpenShop();
                    return;
                }
                if (Translate(new Rectangle(0, 0, WindowWidth, HeaderHeight)).Contains(position))
                {
                    _isDragging = true;
                    _dragOffset = new Point(position.X - X, position.Y - Y);
                    Align = ControlAlign.None;
                    return;
                }
            }

            if (_hoveredSlot is not byte slot || !_shopItems.TryGetValue(slot, out var item)) return;
            if (!CanEditItems(out string? reason))
            {
                SetStatus(reason!, ModernHudTheme.Warning);
                return;
            }

            if (leftPressed)
            {
                string initial = _prices.TryGetValue(slot, out uint current) ? current.ToString() : _lastPrice;
                BeginPricing(new PricingRequest(item, item.RawData.ToArray(), slot, slot), initial);
            }
            else
            {
                CancelPricing();
                ReturnToInventory(slot, item.Definition);
            }
        }

        private void RefreshShopItems()
        {
            var inventory = _state.GetInventoryItems();
            foreach (var slot in _shopItems.Keys.ToList())
                if (!inventory.TryGetValue(slot, out var data) || !ReferenceEquals(data, _shopItems[slot].RawData) && !data.AsSpan().SequenceEqual(_shopItems[slot].RawData))
                    _shopItems.Remove(slot);

            foreach (var entry in inventory)
            {
                if (!PlayerShopUi.IsShopSlot(entry.Key) || _shopItems.ContainsKey(entry.Key)) continue;
                var cell = PlayerShopUi.SlotToCell(entry.Key);
                var item = PlayerShopUi.CreateItem(entry.Value, cell)
                           ?? new InventoryItem(new ItemDefinition(0, ItemDatabase.GetItemName(entry.Value) ?? "Unknown item", 1, 1), cell, entry.Value);
                _shopItems[entry.Key] = item;
            }

            foreach (var slot in _prices.Keys.ToList())
                if (!_shopItems.ContainsKey(slot)) _prices.Remove(slot);
        }

        private byte? FindShopSlotAt(Point position)
        {
            var grid = Grid;
            if (!grid.Contains(position)) return null;
            foreach (var (slot, item) in _shopItems)
                if (PlayerShopUi.CellRect(grid, item.GridPosition, item.Definition.Width, item.Definition.Height).Contains(position))
                    return slot;
            return null;
        }

        private void SetStatus(string text, Color color)
        {
            _status = text;
            _statusColor = color;
            _statusUntil = (_gameTime?.TotalGameTime.TotalSeconds ?? 0) + 4;
        }

        // ─────────────────────────────── Draw ───────────────────────────────

        public override void Draw(GameTime gameTime)
        {
            if (!Visible || Status != GameControlStatus.Ready) return;
            var sb = GraphicsManager.Instance.Sprite;
            var pixel = GraphicsManager.Instance.Pixel;
            if (sb == null || pixel == null) return;

            bool shopOpen = _state.IsPlayerShopOpen;
            var scope = PlayerShopUi.BeginUiBatch();
            try
            {
                PlayerShopUi.DrawWindow(sb, DisplayRectangle, Alpha);
                var header = Translate(new Rectangle(0, 0, WindowWidth, HeaderHeight));
                PlayerShopUi.DrawHeader(sb, header, "MY PERSONAL SHOP", null, Alpha);
                PlayerShopUi.DrawCloseButton(sb, Translate(CloseLocal), _closeHovered, Alpha);
                PlayerShopUi.DrawPill(sb, Translate(new Rectangle(16, 17, 52, 16)), shopOpen ? "OPEN" : "CLOSED",
                    shopOpen ? ModernHudTheme.Success : ModernHudTheme.TextGray, Alpha);

                PlayerShopUi.DrawText(sb, "Shop name", Translate(new Rectangle(Pad, TitleLabelY, 0, 0)).Location.ToVector2(), ModernHudTheme.TextGray, 11f, Alpha);
                string counter = $"{Encoding.UTF8.GetByteCount(_title.Value)}/{MaxTitleBytes}";
                var counterSize = PlayerShopUi.Measure(counter, 10f);
                PlayerShopUi.DrawText(sb, counter, new Vector2(Translate(GridLocal).Right - counterSize.X, Translate(new Rectangle(0, TitleLabelY, 0, 0)).Y + 1),
                    ModernHudTheme.TextGray * 0.8f, 10f, Alpha);

                DrawGrid(sb, pixel, gameTime, shopOpen);
                DrawSummary(sb, shopOpen);
                DrawEditor(sb, shopOpen);

                bool canAct = shopOpen || (!IsBusy && GetOpenBlocker() == null);
                PlayerShopUi.DrawButton(sb, Translate(ActionLocal), shopOpen ? "CLOSE SHOP" : "OPEN SHOP", _actionHovered, canAct,
                    shopOpen ? ModernHudTheme.Danger : ModernHudTheme.Success, Alpha, 14f);
            }
            finally
            {
                scope?.Dispose();
            }

            base.Draw(gameTime);

            // Placeholders are drawn after the fields, which render their own background.
            var placeholderScope = PlayerShopUi.BeginUiBatch();
            try
            {
                if (_title.Value.Length == 0 && !_title.IsFocused)
                    PlayerShopUi.DrawText(sb, "Enter a name visible to other players", new Vector2(_title.DisplayRectangle.X + 6, _title.DisplayRectangle.Y + 5),
                        ModernHudTheme.TextGray * 0.7f, 11f, Alpha, shadow: false);
                if (_price.Visible && _price.Value.Length == 0)
                    PlayerShopUi.DrawText(sb, "e.g. 500k, 1.5kk", new Vector2(_price.DisplayRectangle.X + 14, _price.DisplayRectangle.Y + 5),
                        ModernHudTheme.TextGray * 0.7f, 11f, Alpha, shadow: false);
            }
            finally
            {
                placeholderScope?.Dispose();
            }
        }

        private void DrawGrid(SpriteBatch sb, Texture2D pixel, GameTime gameTime, bool shopOpen)
        {
            var grid = Grid;
            PlayerShopUi.DrawGrid(sb, grid, Alpha);

            foreach (var (slot, item) in _shopItems)
            {
                var rect = PlayerShopUi.CellRect(grid, item.GridPosition, item.Definition.Width, item.Definition.Height);
                bool editing = _pricing != null && _pricing.FromSlot == slot;
                PlayerShopUi.DrawItem(sb, _textures, item, rect, _hoveredSlot == slot || editing, false, gameTime, Alpha);
                PlayerShopUi.DrawPriceTag(sb, rect, _prices.TryGetValue(slot, out uint price) ? price : null, null, Alpha);
                if (editing) UiDrawHelper.DrawBorder(sb, rect, ModernHudTheme.AccentBright * Alpha, 2);
            }

            if (_shopItems.Count == 0 && _pricing == null)
            {
                PlayerShopUi.DrawTextCentered(sb, "Drag items here from your inventory", new Rectangle(grid.X, grid.Y + grid.Height / 2 - 16, grid.Width, 16),
                    ModernHudTheme.TextGray, 12f, Alpha);
                PlayerShopUi.DrawTextCentered(sb, "or right-click them", new Rectangle(grid.X, grid.Y + grid.Height / 2 + 2, grid.Width, 16),
                    ModernHudTheme.TextGray * 0.8f, 11f, Alpha);
            }

            // Target position of an item that is being priced before it is moved.
            if (_pricing is { } request && request.FromSlot != request.ToSlot)
            {
                var cell = PlayerShopUi.SlotToCell(request.ToSlot);
                var rect = PlayerShopUi.CellRect(grid, cell, request.Item.Definition.Width, request.Item.Definition.Height);
                PlayerShopUi.DrawItem(sb, _textures, request.Item, rect, true, true, gameTime, Alpha);
                UiDrawHelper.DrawBorder(sb, rect, ModernHudTheme.AccentBright * Alpha, 2);
            }

            // Drop preview while carrying an item from the inventory.
            var carried = CarriedInventoryItem;
            Point mouse = MuGame.Instance.UiMouseState.Position;
            if (carried != null && !shopOpen && grid.Contains(mouse))
            {
                int w = carried.Definition.Width, h = carried.Definition.Height;
                var cell = PlayerShopUi.CellUnderCursor(grid, mouse, w, h);
                PlayerShopUi.DrawFootprint(sb, grid, cell, w, h, CanPlace(cell.X, cell.Y, w, h), Alpha);
            }

            if (shopOpen)
            {
                sb.Draw(pixel, grid, ModernHudTheme.Success * 0.08f * Alpha);
                UiDrawHelper.DrawBorder(sb, grid, ModernHudTheme.Success * 0.6f * Alpha);
            }
            else if (IsBusy)
            {
                sb.Draw(pixel, grid, Color.Black * 0.5f * Alpha);
                PlayerShopUi.DrawTextCentered(sb, "Waiting for server...", grid, ModernHudTheme.TextGold, 13f, Alpha);
            }
        }

        private void DrawSummary(SpriteBatch sb, bool shopOpen)
        {
            var panel = Translate(new Rectangle(Pad - 4, SummaryY, WindowWidth - (Pad - 4) * 2, 22));
            PlayerShopUi.DrawPanel(sb, panel, ModernHudTheme.BgMid, Alpha);

            ulong total = 0;
            foreach (var slot in _shopItems.Keys)
                if (_prices.TryGetValue(slot, out uint price)) total += price;
            string left = _shopItems.Count == 1 ? "1 item" : $"{_shopItems.Count} items";
            PlayerShopUi.DrawText(sb, left, new Vector2(panel.X + 8, panel.Y + 4), ModernHudTheme.TextWhite, 11f, Alpha);
            if (total > 0)
            {
                string totalText = $"Total: {PlayerShopUi.FormatZen((uint)Math.Min(total, uint.MaxValue))} Zen";
                var leftSize = PlayerShopUi.Measure(left, 11f);
                PlayerShopUi.DrawText(sb, totalText, new Vector2(panel.X + 16 + leftSize.X, panel.Y + 4), ModernHudTheme.TextGold, 11f, Alpha);
            }

            string? right = shopOpen ? "Selling" : GetOpenBlocker();
            Color rightColor = shopOpen ? ModernHudTheme.Success : ModernHudTheme.Warning;
            if (right == null) { right = "Ready to open"; rightColor = ModernHudTheme.Success; }
            var rightSize = PlayerShopUi.Measure(right, 11f);
            PlayerShopUi.DrawText(sb, right, new Vector2(panel.Right - rightSize.X - 8, panel.Y + 4), rightColor, 11f, Alpha);
        }

        private void DrawEditor(SpriteBatch sb, bool shopOpen)
        {
            var area = Translate(new Rectangle(Pad - 4, EditorY, WindowWidth - (Pad - 4) * 2, 56));
            PlayerShopUi.DrawPanel(sb, area, ModernHudTheme.BgDark, Alpha);

            bool statusActive = _status != null && _gameTime != null && _gameTime.TotalGameTime.TotalSeconds < _statusUntil;

            if (_pricing is { } request)
            {
                string label = PlayerShopUi.Ellipsize("Price: " + PlayerShopUi.GetItemDisplayName(request.Item), 11f, 150);
                PlayerShopUi.DrawText(sb, label, new Vector2(area.X + 8, area.Y + 6), ModernHudTheme.TextWhite, 11f, Alpha);

                string preview;
                Color previewColor;
                if (statusActive) { preview = string.Empty; previewColor = Color.Transparent; }
                else if (PlayerShopUi.TryParseZen(_price.Value, out uint parsed))
                {
                    preview = $"= {PlayerShopUi.FormatZen(parsed)} Zen";
                    previewColor = PlayerShopUi.GetPriceColor(parsed);
                }
                else
                {
                    preview = _price.Value.Length == 0 ? "Enter a price" : "Invalid price";
                    previewColor = _price.Value.Length == 0 ? ModernHudTheme.TextGray : ModernHudTheme.Danger;
                }
                if (statusActive)
                {
                    string status = PlayerShopUi.Ellipsize(_status!, 10f, 110);
                    var size = PlayerShopUi.Measure(status, 10f);
                    PlayerShopUi.DrawText(sb, status, new Vector2(area.Right - size.X - 8, area.Y + 7), _statusColor, 10f, Alpha);
                }
                else
                {
                    var size = PlayerShopUi.Measure(preview, 11f);
                    PlayerShopUi.DrawText(sb, preview, new Vector2(area.Right - size.X - 8, area.Y + 6), previewColor, 11f, Alpha);
                }

                bool valid = PlayerShopUi.TryParseZen(_price.Value, out _);
                PlayerShopUi.DrawButton(sb, Translate(ConfirmLocal), "OK", _confirmHovered, valid, ModernHudTheme.Success, Alpha, 12f);
                PlayerShopUi.DrawButton(sb, Translate(CancelLocal), "Cancel", _cancelHovered, true, ModernHudTheme.Danger, Alpha, 12f);
                return;
            }

            string line1, line2;
            Color color1 = ModernHudTheme.TextGray;
            if (statusActive)
            {
                line1 = _status!;
                color1 = _statusColor;
                line2 = shopOpen ? "Other players can now buy your items." : "Click an item to change its price.";
            }
            else if (shopOpen)
            {
                line1 = "Your shop is open.";
                color1 = ModernHudTheme.Success;
                line2 = "Close it to change items or prices.";
            }
            else
            {
                line1 = "Drag or right-click inventory items to add them.";
                line2 = "Click: change price   |   Right-click: take back";
            }
            PlayerShopUi.DrawTextCentered(sb, PlayerShopUi.Ellipsize(line1, 11f, area.Width - 16), new Rectangle(area.X, area.Y + 8, area.Width, 18), color1, 11f, Alpha);
            PlayerShopUi.DrawTextCentered(sb, PlayerShopUi.Ellipsize(line2, 11f, area.Width - 16), new Rectangle(area.X, area.Y + 30, area.Width, 18), ModernHudTheme.TextGray * 0.85f, 11f, Alpha);
        }

        public override void DrawAfter(GameTime gameTime)
        {
            base.DrawAfter(gameTime);
            if (!Visible || _isDragging || _hoveredSlot is not byte slot || !_shopItems.TryGetValue(slot, out var item)) return;
            if (CarriedInventoryItem != null) return;
            var sb = GraphicsManager.Instance.Sprite;
            if (sb == null) return;

            var lines = ItemUiHelper.BuildTooltipLines(item);
            lines.Add((string.Empty, Color.White));
            if (_prices.TryGetValue(slot, out uint price))
                lines.Add(($"Price: {PlayerShopUi.FormatZen(price)} Zen", PlayerShopUi.GetPriceColor(price)));
            else
                lines.Add(("No price set - click to set it", ModernHudTheme.Danger));
            if (!_state.IsPlayerShopOpen)
                lines.Add(("Click: change price  |  Right-click: take back", ModernHudTheme.TextGray));

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

        private sealed record PricingRequest(InventoryItem Item, byte[] RawData, byte FromSlot, byte ToSlot);
    }
}
