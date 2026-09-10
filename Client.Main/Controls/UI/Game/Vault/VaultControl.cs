using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Client.Main;
using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Core.Client;
using Client.Main.Core.Utilities;
using Client.Main.Controls.UI;
using Client.Main.Controls.UI.Common;
using Client.Main.Controls.UI.Game.Common;
using Client.Main.Controls.UI.Game.Hud;
using Client.Main.Controls.UI.Game.Inventory;
using Client.Main.Networking;
using Client.Main.Models;
using Client.Main.Helpers;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MUnique.OpenMU.Network.Packets;
using MUnique.OpenMU.Network.Packets.ClientToServer;

namespace Client.Main.Controls.UI.Game
{
    public class VaultControl : UIControl
    {
        // ═══════════════════════════════════════════════════════════════
        // WINDOW DIMENSIONS
        // ═══════════════════════════════════════════════════════════════
        public const int Columns = 8;
        public const int Rows = 15;
        private static bool IsSeason6 => UiThemeManager.CurrentId == UiThemeId.Season6;
        private static int VisibleRows => IsSeason6 ? VaultLayout.GridRows : Rows;
        private static int Season6PageCount => (Rows + VaultLayout.GridRows - 1) / VaultLayout.GridRows;
        private const int VAULT_SQUARE_WIDTH = 32;
        private const int VAULT_SQUARE_HEIGHT = 32;

        private static int HEADER_HEIGHT => IsSeason6 ? VaultLayout.DragBarH : 46;
        private const int SECTION_HEADER_HEIGHT = 22;
        private const int GRID_PADDING = 10;
        private const int FOOTER_HEIGHT = 50;
        private const int WINDOW_MARGIN = 12;

        private static int GRID_WIDTH => Columns * VAULT_SQUARE_WIDTH;
        private static int GRID_HEIGHT => VisibleRows * VAULT_SQUARE_HEIGHT;
        private static int WINDOW_WIDTH => IsSeason6
            ? VaultLayout.PanelW
            : GRID_WIDTH + GRID_PADDING * 2 + WINDOW_MARGIN * 2;
        private static int WINDOW_HEIGHT => IsSeason6
            ? VaultLayout.PanelH
            : HEADER_HEIGHT + SECTION_HEADER_HEIGHT + GRID_PADDING * 2 + GRID_HEIGHT + FOOTER_HEIGHT + WINDOW_MARGIN;

        // ═══════════════════════════════════════════════════════════════
        // MODERN DARK THEME
        // ═══════════════════════════════════════════════════════════════
        private static class Theme
        {
            public static Color BgDarkest => ModernHudTheme.BgDarkest;
            public static Color BgDark => ModernHudTheme.BgDark;
            public static Color BgMid => ModernHudTheme.BgMid;
            public static Color BgLight => ModernHudTheme.BgLight;

            public static Color Accent => ModernHudTheme.Accent;
            public static Color AccentBright => ModernHudTheme.AccentBright;
            public static Color AccentDim => ModernHudTheme.AccentDim;
            public static Color AccentGlow => ModernHudTheme.AccentGlow;
            public static Color Secondary => ModernHudTheme.Secondary;
            public static Color BorderOuter => ModernHudTheme.BorderOuter;
            public static Color BorderInner => ModernHudTheme.BorderInner;
            public static Color BorderHighlight => ModernHudTheme.BorderHighlight;
            public static Color SlotBg => ModernHudTheme.SlotBg;
            public static Color SlotBorder => ModernHudTheme.SlotBorder;
            public static Color SlotHover => ModernHudTheme.SlotHover;
            public static Color SlotSelected => ModernHudTheme.SlotSelected;

            public static readonly Color GlowNormal = new(150, 150, 150, 25);
            public static readonly Color GlowMagic = new(100, 150, 255, 50);
            public static readonly Color GlowExcellent = new(120, 255, 120, 60);
            public static readonly Color GlowAncient = new(80, 200, 255, 70);
            public static readonly Color GlowLegendary = new(255, 180, 80, 70);

            public static Color TextWhite => ModernHudTheme.TextWhite;
            public static Color TextGold => ModernHudTheme.TextGold;
            public static Color TextGray => ModernHudTheme.TextGray;
            public static Color TextDark => ModernHudTheme.TextDark;
            public static Color Success => ModernHudTheme.Success;
            public static Color Danger => ModernHudTheme.Danger;
        }

        private static readonly ItemGlowPalette GlowPalette = new(
            Theme.GlowNormal,
            Theme.GlowMagic,
            Theme.GlowExcellent,
            Theme.GlowAncient,
            Theme.GlowLegendary);

        private static VaultControl _instance;

        private readonly List<InventoryItem> _items = new();
        private readonly List<(InventoryItem Item, Rectangle Rect)> _jewelEntries = new();
        private InventoryItem[,] _itemGrid = new InventoryItem[Columns, Rows];
        private readonly Dictionary<InventoryItem, byte> _serverSlots = new();
        private int _season6Page;
        private bool _pagePreviousHovered;
        private bool _pageNextHovered;

        private readonly Dictionary<string, Texture2D> _itemTextureCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<(InventoryItem item, int width, int height, bool animated), Texture2D> _bmdPreviewCache = new();

        private Rectangle _headerRect;
        private Rectangle _gridRect;
        private Rectangle _gridFrameRect;
        private Rectangle _footerRect;
        private Rectangle _zenFieldRect;
        private Rectangle _depositButtonRect;
        private Rectangle _withdrawButtonRect;
        private Rectangle _closeButtonRect;
        private Rectangle _pagePreviousRect;
        private Rectangle _pageNextRect;

        private RenderTarget2D _staticSurface;
        private bool _staticSurfaceDirty = true;

        private Texture2D _s6Panel;
        private Texture2D _s6Rect;
        private Texture2D _s6DarkCard;
        private Texture2D _s6GridRow;
        private Texture2D _s6Field;
        private Texture2D _s6ButtonDark;
        private Texture2D _s6ButtonGold;
        private Texture2D _s6Close;
        private Task _season6AssetsTask;

        private SpriteFont _font;

        private CharacterState _characterState;
        private readonly NetworkManager _networkManager;
        private readonly ILogger<VaultControl> _logger;

        private InventoryItem _hoveredItem;
        private Point _hoveredSlot = new(-1, -1);
        private Point _pendingDropSlot = new(-1, -1);

        private InventoryItem _draggedItem;
        private Point _draggedOriginalSlot = new(-1, -1);

        private GameTime _currentGameTime;
        private bool _wasVisible;
        private bool _closeRequestSent;
        private bool _closeHovered;
        private bool _pendingShow;
        private bool _warmupComplete;

        // Zen transfer
        private const uint MaxZen = 2_000_000_000;
        private const int ZenInputMaxDigits = 10;
        private const double CursorBlinkIntervalMs = 500;
        private bool _zenFieldHovered;
        private bool _depositHovered;
        private bool _withdrawHovered;
        private bool _isZenInputActive;
        private string _zenInputText = string.Empty;
        private double _zenInputBlinkTimer;
        private bool _zenInputShowCursor;
        private VaultMoveMoneyRequest.VaultMoneyMoveDirection _zenMoveDirection = VaultMoveMoneyRequest.VaultMoneyMoveDirection.InventoryToVault;
        private long _vaultZen;

        // Window drag support
        private bool _isDragging;
        private Point _dragOffset;
        private DateTime _lastClickTime = DateTime.MinValue;

        private VaultControl(NetworkManager networkManager = null, ILoggerFactory loggerFactory = null)
        {
            _networkManager = networkManager ?? MuGame.Network;
            var factory = loggerFactory ?? MuGame.AppLoggerFactory;
            _logger = factory?.CreateLogger<VaultControl>();

            BuildLayoutMetrics();

            ControlSize = new Point(WINDOW_WIDTH, WINDOW_HEIGHT);
            ViewSize = ControlSize;
            AutoViewSize = false;
            Interactive = true;
            Visible = false;
            Align = ControlAlign.VerticalCenter | ControlAlign.Left;

            EnsureCharacterState();
        }

        public override bool NonDisposable => true;
        public static VaultControl Instance => _instance ??= new VaultControl();

        /// <summary>
        /// Forces immediate position calculation based on Align property.
        /// Call this before showing the control to prevent position flickering.
        /// </summary>
        private void ForceAlignNow()
        {
            if (Parent == null || Align == ControlAlign.None)
                return;

            const int padding = 20;

            if (Align.HasFlag(ControlAlign.Top))
                Y = padding;
            else if (Align.HasFlag(ControlAlign.Bottom))
                Y = Parent.DisplaySize.Y - DisplaySize.Y - padding;
            else if (Align.HasFlag(ControlAlign.VerticalCenter))
                Y = (Parent.DisplaySize.Y / 2) - (DisplaySize.Y / 2);

            if (Align.HasFlag(ControlAlign.Left))
                X = padding;
            else if (Align.HasFlag(ControlAlign.Right))
                X = Parent.DisplaySize.X - DisplaySize.X - padding;
            else if (Align.HasFlag(ControlAlign.HorizontalCenter))
                X = (Parent.DisplaySize.X / 2) - (DisplaySize.X / 2);
        }

        private void BuildLayoutMetrics()
        {
            if (IsSeason6)
            {
                _headerRect = new Rectangle(0, 0, VaultLayout.PanelW, VaultLayout.DragBarH);
                _gridFrameRect = new Rectangle(VaultLayout.CardX, VaultLayout.CardY,
                    VaultLayout.CardW, VaultLayout.CardH);
                _gridRect = new Rectangle(VaultLayout.GridX, VaultLayout.GridY,
                    VaultLayout.GridCols * VAULT_SQUARE_WIDTH,
                    VaultLayout.GridRows * VAULT_SQUARE_HEIGHT);
                _footerRect = new Rectangle(0, VaultLayout.ButtonRowY - 82,
                    VaultLayout.PanelW, VaultLayout.PanelH - (VaultLayout.ButtonRowY - 82));
                _zenFieldRect = new Rectangle(VaultLayout.ZenFieldX,
                    VaultLayout.ZenRowY - VaultLayout.ZenFieldH / 2,
                    VaultLayout.ZenFieldW, VaultLayout.ZenFieldH);
                _depositButtonRect = new Rectangle(VaultLayout.ButtonRowX, VaultLayout.ButtonRowY,
                    VaultLayout.ButtonW, VaultLayout.ButtonH);
                _withdrawButtonRect = new Rectangle(
                    VaultLayout.ButtonRowX + VaultLayout.ButtonW + VaultLayout.ButtonGap,
                    VaultLayout.ButtonRowY, VaultLayout.ButtonW, VaultLayout.ButtonH);
                _closeButtonRect = new Rectangle(VaultLayout.CloseX, VaultLayout.CloseY,
                    VaultLayout.CloseW, VaultLayout.CloseH);
                _pagePreviousRect = new Rectangle(VaultLayout.PagePreviousX, VaultLayout.PageY,
                    VaultLayout.PageButtonW, VaultLayout.PageButtonH);
                _pageNextRect = new Rectangle(VaultLayout.PageNextX, VaultLayout.PageY,
                    VaultLayout.PageButtonW, VaultLayout.PageButtonH);
                return;
            }

            _headerRect = new Rectangle(0, 0, WINDOW_WIDTH, HEADER_HEIGHT);

            int gridFrameX = WINDOW_MARGIN;
            int gridFrameY = HEADER_HEIGHT;
            int gridFrameWidth = GRID_WIDTH + GRID_PADDING * 2;
            int gridFrameHeight = SECTION_HEADER_HEIGHT + GRID_PADDING * 2 + GRID_HEIGHT;
            _gridFrameRect = new Rectangle(gridFrameX, gridFrameY, gridFrameWidth, gridFrameHeight);

            _gridRect = new Rectangle(
                gridFrameX + GRID_PADDING,
                gridFrameY + SECTION_HEADER_HEIGHT + GRID_PADDING,
                GRID_WIDTH,
                GRID_HEIGHT);

            _footerRect = new Rectangle(WINDOW_MARGIN, _gridFrameRect.Bottom + 4, _gridFrameRect.Width, FOOTER_HEIGHT - 8);

            const int buttonWidth = 44;
            const int buttonGap = 6;
            const int fieldHeight = 28;
            int fieldX = _footerRect.X + 36;
            int fieldWidth = _footerRect.Width - (fieldX - _footerRect.X) - (buttonWidth * 2 + buttonGap * 2);
            _zenFieldRect = new Rectangle(fieldX, _footerRect.Y + 8, fieldWidth, fieldHeight);
            _depositButtonRect = new Rectangle(_zenFieldRect.Right + buttonGap, _zenFieldRect.Y, buttonWidth, fieldHeight);
            _withdrawButtonRect = new Rectangle(_depositButtonRect.Right + buttonGap, _zenFieldRect.Y, buttonWidth, fieldHeight);
            _closeButtonRect = new Rectangle(WINDOW_WIDTH - 30, 10, 20, 20);
            _pagePreviousRect = Rectangle.Empty;
            _pageNextRect = Rectangle.Empty;
        }

        public override async Task Load()
        {
            await base.Load();
            _font = GraphicsManager.Instance.Font;
            if (IsSeason6)
                await EnsureSeason6AssetsAsync();
            InvalidateStaticSurface();
        }

        public Task EnsureSeason6AssetsAsync()
        {
            if (!IsSeason6)
                return Task.CompletedTask;
            return _season6AssetsTask ??= LoadSeason6AssetsAsync();
        }

        private async Task LoadSeason6AssetsAsync()
        {
            async Task<Texture2D> Load(string path, string fallback = null)
            {
                try { return await UiThemeManager.LoadThemeTextureAsync(path, fallback); }
                catch { return null; }
            }

            _s6Panel = await Load("Interface/Imprint/imprint_panel.OZP", "Interface/GFx/NpcShop_I3.ozd");
            _s6Rect = await Load("Interface/Inventory/inv_rect.OZP");
            _s6DarkCard = await Load("Interface/Inventory/inv_darkcard.OZP");
            _s6GridRow = await Load("Interface/Inventory/inv_grid_row.OZP");
            _s6Field = await Load("Interface/Inventory/inv_field.OZP");
            _s6ButtonDark = await Load("Interface/Inventory/inv_btn_dark.OZP");
            _s6ButtonGold = await Load("Interface/Inventory/inv_btn_gold.OZP");
            _s6Close = await Load("Interface/Imprint/imprint_close.OZP", "Interface/newui_exit_00.tga");
            InvalidateStaticSurface();
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
            EnsureCharacterState();
            _currentGameTime = gameTime;

            // Handle deferred show - wait one frame after warmup to avoid black screen
            if (_pendingShow && !Visible)
            {
                if (_warmupComplete)
                {
                    // Warmup done in previous frame, now safe to show
                    Visible = true;
                    BringToFront();
                    SoundController.Instance.PlayBuffer("Sound/iCreateWindow.wav");
                    _pendingShow = false;
                    _warmupComplete = false;
                }
                else
                {
                    // Do warmup this frame, show next frame
                    WarmupTexturesSync();
                    InvalidateStaticSurface();
                    EnsureStaticSurface();
                    _warmupComplete = true;
                }
            }

            if (Visible)
            {
                if (!_wasVisible)
                {
                    _closeRequestSent = false;
                }

                if (_isZenInputActive &&
                    MuGame.Instance.Keyboard.IsKeyDown(Keys.Escape) &&
                    MuGame.Instance.PrevKeyboard.IsKeyUp(Keys.Escape))
                {
                    CancelZenInput();
                    Scene?.ConsumeKeyboardEscape();
                    return;
                }

                if (MuGame.Instance.Keyboard.IsKeyDown(Keys.Escape) &&
                    MuGame.Instance.PrevKeyboard.IsKeyUp(Keys.Escape))
                {
                    CloseWindow();
                    return;
                }

                Point mousePos = MuGame.Instance.UiMouseState.Position;
                bool leftPressed = MuGame.Instance.UiMouseState.LeftButton == ButtonState.Pressed;
                bool leftJustPressed = leftPressed && MuGame.Instance.PrevUiMouseState.LeftButton == ButtonState.Released;
                bool leftJustReleased = !leftPressed && MuGame.Instance.PrevUiMouseState.LeftButton == ButtonState.Pressed;

                UpdateChromeHover(mousePos);

                // Handle close button
                if (leftJustPressed && _closeHovered)
                {
                    CloseWindow();
                    return;
                }

                HandleZenInputKeyboard();

                // Handle window dragging
                if (leftJustPressed && IsMouseOverDragArea(mousePos) && !_isDragging && _draggedItem == null)
                {
                    DateTime now = DateTime.Now;
                    if ((now - _lastClickTime).TotalMilliseconds < 500)
                    {
                        Align = ControlAlign.None;
                        _lastClickTime = DateTime.MinValue;
                    }
                    else
                    {
                        _isDragging = true;
                        _dragOffset = new Point(mousePos.X - X, mousePos.Y - Y);
                        Align = ControlAlign.None;
                        _lastClickTime = now;
                    }
                }
                else if (leftJustReleased && _isDragging)
                {
                    _isDragging = false;
                }
                else if (_isDragging && leftPressed)
                {
                    X = mousePos.X - _dragOffset.X;
                    Y = mousePos.Y - _dragOffset.Y;
                }

                if (!_isDragging)
                {
                    if (HandleZenControlsMouseInput(mousePos, leftJustPressed))
                    {
                        Scene?.SetMouseInputConsumed();
                    }
                    else
                    {
                        UpdateHoverState();
                        HandleMouseInput();
                    }
                }

                PrecacheAnimatedPreviews();
            }
            else if (_wasVisible)
            {
                HandleVisibilityLost();
            }

            _wasVisible = Visible;
        }

        private bool IsMouseOverDragArea(Point mousePos)
        {
            Rectangle headerScreen = Translate(_headerRect);
            Rectangle closeScreen = Translate(_closeButtonRect);
            return headerScreen.Contains(mousePos) && !closeScreen.Contains(mousePos);
        }

        private void UpdateChromeHover(Point mousePos)
        {
            var closeRect = Translate(_closeButtonRect);
            _closeHovered = closeRect.Contains(mousePos);

            var zenRect = Translate(_zenFieldRect);
            _zenFieldHovered = zenRect.Contains(mousePos);

            var depRect = Translate(_depositButtonRect);
            _depositHovered = depRect.Contains(mousePos);

            var wdrRect = Translate(_withdrawButtonRect);
            _withdrawHovered = wdrRect.Contains(mousePos);
        }

        public override void Draw(GameTime gameTime)
        {
            if (!Visible) return;

            EnsureStaticSurface();

            var gm = GraphicsManager.Instance;
            var spriteBatch = gm?.Sprite;
            if (spriteBatch == null) return;

            SpriteBatchScope? scope = null;
            if (!SpriteBatchScope.BatchIsBegun)
            {
                scope = new SpriteBatchScope(spriteBatch, SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp, transform: UiScaler.SpriteTransform);
            }

            try
            {
                if (_staticSurface != null && !_staticSurface.IsDisposed)
                {
                    spriteBatch.Draw(_staticSurface, DisplayRectangle, Color.White * Alpha);
                }

                DrawGridOverlays(spriteBatch);
                DrawVaultItems(spriteBatch);
                DrawSeason6PageButtons(spriteBatch);
                DrawCloseButton(spriteBatch);
                DrawZenButtons(spriteBatch);
                DrawZenText(spriteBatch);
            }
            finally
            {
                scope?.Dispose();
            }
        }

        public override void DrawAfter(GameTime gameTime)
        {
            if (!Visible || _hoveredItem == null) return;

            var gm = GraphicsManager.Instance;
            var spriteBatch = gm?.Sprite;
            if (spriteBatch == null) return;

            SpriteBatchScope? scope = null;
            if (!SpriteBatchScope.BatchIsBegun)
            {
                scope = new SpriteBatchScope(spriteBatch, SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp, transform: UiScaler.SpriteTransform);
            }

            try
            {
                DrawTooltip(spriteBatch);
            }
            finally
            {
                scope?.Dispose();
            }
        }

        public override void Dispose()
        {
            base.Dispose();

            if (_characterState != null)
            {
                _characterState.VaultItemsChanged -= RefreshVaultContent;
                _characterState = null;
            }

            Client.Main.Graphics.UiRenderTargetPool.Return(_staticSurface);
            _staticSurface = null;
        }

        protected override void OnScreenSizeChanged()
        {
            base.OnScreenSizeChanged();
            InvalidateStaticSurface();
        }

        public void CloseWindow()
        {
            if (!Visible) return;
            Visible = false;
            HandleVisibilityLost();
            _wasVisible = false;
        }

        public InventoryItem GetDraggedItem() => _draggedItem;

        public void DrawPickedPreview(SpriteBatch spriteBatch, GameTime gameTime)
        {
            if (_draggedItem == null || spriteBatch == null) return;

            int w = _draggedItem.Definition.Width * VAULT_SQUARE_WIDTH;
            int h = _draggedItem.Definition.Height * VAULT_SQUARE_HEIGHT;

            var mouse = MuGame.Instance.UiMouseState.Position;
            var destRect = new Rectangle(mouse.X - w / 2, mouse.Y - h / 2, w, h);

            // Use cached previews only to avoid render-target switches while a sprite batch is active.
            Texture2D texture = ResolveItemTexture(_draggedItem, w, h, animated: false, allowGenerate: false)
                                ?? ResolveItemTexture(
                                    _draggedItem,
                                    _draggedItem.Definition.Width * InventoryControl.INVENTORY_SQUARE_WIDTH,
                                    _draggedItem.Definition.Height * InventoryControl.INVENTORY_SQUARE_HEIGHT,
                                    animated: false,
                                    allowGenerate: false);

            if (texture != null)
            {
                spriteBatch.Draw(texture, destRect, Color.White * 0.9f);
            }
            else if (GraphicsManager.Instance?.Pixel != null)
            {
                spriteBatch.Draw(GraphicsManager.Instance.Pixel, destRect, Theme.Accent * 0.8f);
            }
        }

        public Point GetSlotAtScreenPosition(Point screenPos)
        {
            return ItemGridRenderHelper.GetSlotAtScreenPosition(DisplayRectangle, _gridRect, Columns, VisibleRows, VAULT_SQUARE_WIDTH, VAULT_SQUARE_HEIGHT, screenPos);
        }

        public bool CanPlaceAt(Point gridSlot, InventoryItem item)
        {
            if (item?.Definition == null) return false;

            for (int y = 0; y < item.Definition.Height; y++)
            {
                for (int x = 0; x < item.Definition.Width; x++)
                {
                    int gx = gridSlot.X + x;
                    int gy = gridSlot.Y + y;

                    if (gx < 0 || gx >= Columns || gy < 0 || gy >= Rows ||
                        (IsSeason6 && gy >= VisibleRows))
                        return false;

                    var occupant = _itemGrid[gx, gy];
                    if (occupant != null && occupant != item)
                        return false;
                }
            }

            return true;
        }

        // ═══════════════════════════════════════════════════════════════
        // DRAWING PRIMITIVES
        // ═══════════════════════════════════════════════════════════════

        private void DrawWindowBackground(SpriteBatch spriteBatch, Rectangle rect)
        {
            var pixel = GraphicsManager.Instance.Pixel;
            if (pixel == null) return;

            spriteBatch.Draw(pixel, rect, Theme.BorderOuter);

            var innerRect = new Rectangle(rect.X + 2, rect.Y + 2, rect.Width - 4, rect.Height - 4);
            UiDrawHelper.DrawVerticalGradient(spriteBatch, innerRect, Theme.BgDark, Theme.BgDarkest);

            spriteBatch.Draw(pixel, new Rectangle(innerRect.X, innerRect.Y, innerRect.Width, 1), Theme.BorderInner * 0.5f);
            spriteBatch.Draw(pixel, new Rectangle(innerRect.X, innerRect.Y, 1, innerRect.Height), Theme.BorderInner * 0.3f);

            UiDrawHelper.DrawCornerAccents(spriteBatch, rect, Theme.Secondary * 0.5f);
        }

        private void DrawPanel(SpriteBatch spriteBatch, Rectangle rect, Color bgColor, bool withBorder = true)
        {
            UiDrawHelper.DrawPanel(spriteBatch, rect, bgColor,
                withBorder ? Theme.BorderInner * 0.8f : (Color?)null,
                withBorder ? Theme.BorderOuter : (Color?)null,
                withBorder ? Theme.BorderInner * 0.6f : null);
        }

        private void DrawSectionHeader(SpriteBatch spriteBatch, string title, int x, int y, int width)
        {
            if (_font == null) return;

            float scale = 0.32f;
            Vector2 size = _font.MeasureString(title) * scale;
            float textX = x + (width - size.X) / 2;

            spriteBatch.DrawString(_font, title, new Vector2(textX + 1, y + 1), Color.Black * 0.6f,
                                   0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
            spriteBatch.DrawString(_font, title, new Vector2(textX, y), Theme.TextGold,
                                   0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        }

        private void DrawFilledCircle(SpriteBatch spriteBatch, int centerX, int centerY, int radius, Color color)
        {
            var pixel = GraphicsManager.Instance.Pixel;
            if (pixel == null || radius <= 0) return;

            for (int y = -radius; y <= radius; y++)
            {
                int halfWidth = (int)MathF.Sqrt(radius * radius - y * y);
                if (halfWidth > 0)
                {
                    spriteBatch.Draw(pixel, new Rectangle(centerX - halfWidth, centerY + y, halfWidth * 2, 1), color);
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // STATIC SURFACE RENDERING
        // ═══════════════════════════════════════════════════════════════

        private void EnsureStaticSurface()
        {
            if (!_staticSurfaceDirty && _staticSurface != null && !_staticSurface.IsDisposed)
                return;

            var gd = GraphicsManager.Instance?.GraphicsDevice;
            if (gd == null) return;

            Client.Main.Graphics.UiRenderTargetPool.Return(_staticSurface);
            _staticSurface = Client.Main.Graphics.UiRenderTargetPool.Rent(gd, WINDOW_WIDTH, WINDOW_HEIGHT);

            var previousTargets = gd.GetRenderTargets();
            gd.SetRenderTarget(_staticSurface);
            gd.Clear(Color.Transparent);

            var spriteBatch = GraphicsManager.Instance.Sprite;
            using (new SpriteBatchScope(spriteBatch, SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp))
            {
                DrawStaticElements(spriteBatch);
            }

            gd.SetRenderTargets(previousTargets);
            _staticSurfaceDirty = false;
        }

        private void InvalidateStaticSurface() => _staticSurfaceDirty = true;

        protected override void OnThemeChanged(UiThemeChangedEventArgs e)
        {
            base.OnThemeChanged(e);
            BuildLayoutMetrics();
            _season6Page = 0;
            RebuildVisibleVaultGrid();
            ControlSize = new Point(WINDOW_WIDTH, WINDOW_HEIGHT);
            ViewSize = ControlSize;
            if (Visible)
                ForceAlignNow();
            if (IsSeason6)
            {
                _season6AssetsTask = null;
                _ = EnsureSeason6AssetsAsync();
            }
            else
            {
                _s6Panel = null;
                _s6Rect = null;
                _s6DarkCard = null;
                _s6GridRow = null;
                _s6Field = null;
                _s6ButtonDark = null;
                _s6ButtonGold = null;
                _s6Close = null;
                _season6AssetsTask = null;
            }
            InvalidateStaticSurface();
        }

        private void DrawStaticElements(SpriteBatch spriteBatch)
        {
            var pixel = GraphicsManager.Instance.Pixel;
            if (pixel == null) return;

            if (IsSeason6)
            {
                DrawSeason6StaticElements(spriteBatch);
                return;
            }

            var fullRect = new Rectangle(0, 0, WINDOW_WIDTH, WINDOW_HEIGHT);
            DrawWindowBackground(spriteBatch, fullRect);
            DrawModernHeader(spriteBatch);
            DrawModernGridSection(spriteBatch);
            DrawModernFooter(spriteBatch);
        }

        private void DrawSeason6StaticElements(SpriteBatch spriteBatch)
        {
            var pixel = GraphicsManager.Instance.Pixel;
            var fullRect = new Rectangle(0, 0, WINDOW_WIDTH, WINDOW_HEIGHT);
            if (IsUsableTexture(_s6Panel))
                spriteBatch.Draw(_s6Panel, fullRect, Color.White);
            else
                DrawWindowBackground(spriteBatch, fullRect);

            if (_font != null)
            {
                string title = "Storage";
                float scale = 13f / 25f;
                Vector2 size = _font.MeasureString(title) * scale;
                Vector2 pos = new((WINDOW_WIDTH - size.X) / 2f, 65f - size.Y / 2f);
                spriteBatch.DrawString(_font, title, pos + Vector2.One, Color.Black * 0.6f,
                    0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
                spriteBatch.DrawString(_font, title, pos, Theme.TextWhite,
                    0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
            }

            if (IsUsableTexture(_s6DarkCard))
                spriteBatch.Draw(_s6DarkCard, new Rectangle(VaultLayout.DarkCardX, VaultLayout.DarkCardY,
                    VaultLayout.DarkCardW, VaultLayout.DarkCardH), Color.White);
            if (IsUsableTexture(_s6Rect))
                DrawNineSlice(spriteBatch, _s6Rect, _gridFrameRect);
            else if (!IsUsableTexture(_s6DarkCard))
                DrawPanel(spriteBatch, _gridFrameRect, Theme.BgMid);

            if (IsUsableTexture(_s6GridRow))
            {
                for (int row = 0; row < VisibleRows; row++)
                {
                    var rowRect = new Rectangle(_gridRect.X, _gridRect.Y + row * VAULT_SQUARE_HEIGHT,
                        _gridRect.Width, VAULT_SQUARE_HEIGHT);
                    spriteBatch.Draw(_s6GridRow, rowRect, Color.White);
                }
            }
            else
            {
                spriteBatch.Draw(pixel, _gridRect, Theme.SlotBg);
                for (int x = 1; x < Columns; x++)
                    spriteBatch.Draw(pixel, new Rectangle(_gridRect.X + x * VAULT_SQUARE_WIDTH, _gridRect.Y, 1, _gridRect.Height), Theme.SlotBorder);
                for (int y = 1; y < VisibleRows; y++)
                    spriteBatch.Draw(pixel, new Rectangle(_gridRect.X, _gridRect.Y + y * VAULT_SQUARE_HEIGHT, _gridRect.Width, 1), Theme.SlotBorder);
            }

            if (IsUsableTexture(_s6Field))
            {
                spriteBatch.Draw(_s6Field, _zenFieldRect, Color.White);
            }
            else
            {
                DrawPanel(spriteBatch, _zenFieldRect, Theme.SlotBg);
            }

            DrawSeason6Button(spriteBatch, _depositButtonRect, "Deposit", _s6ButtonGold);
            DrawSeason6Button(spriteBatch, _withdrawButtonRect, "Withdraw", _s6ButtonDark);
        }

        private void DrawSeason6PageButtons(SpriteBatch spriteBatch)
        {
            if (!IsSeason6 || _font == null || Season6PageCount <= 1)
                return;

            DrawPageButton(spriteBatch, _pagePreviousRect, "<", _season6Page > 0, _pagePreviousHovered);
            DrawPageButton(spriteBatch, _pageNextRect, ">", _season6Page < Season6PageCount - 1, _pageNextHovered);

            Rectangle labelRect = new(VaultLayout.PagePreviousX + VaultLayout.PageButtonW,
                VaultLayout.PageY, VaultLayout.PageNextX - VaultLayout.PagePreviousX - VaultLayout.PageButtonW,
                VaultLayout.PageButtonH);
            string label = $"{_season6Page + 1}/{Season6PageCount}";
            float scale = 0.23f;
            Vector2 size = _font.MeasureString(label) * scale;
            Vector2 position = new(DisplayRectangle.X + labelRect.X + (labelRect.Width - size.X) / 2f,
                DisplayRectangle.Y + labelRect.Y + (labelRect.Height - size.Y) / 2f);
            spriteBatch.DrawString(_font, label, position + Vector2.One, Color.Black * 0.6f,
                0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
            spriteBatch.DrawString(_font, label, position, Theme.TextWhite,
                0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        }

        private void DrawPageButton(SpriteBatch spriteBatch, Rectangle localRect, string label,
            bool enabled, bool hovered)
        {
            Rectangle rect = Translate(localRect);
            Color background = !enabled ? Theme.BgDarkest * 0.45f : hovered ? Theme.SlotHover : Theme.SlotBg;
            spriteBatch.Draw(GraphicsManager.Instance.Pixel, rect, background);
            UiDrawHelper.DrawBorder(spriteBatch, rect, enabled ? Theme.BorderInner : Theme.BorderOuter, 1);

            float scale = 0.28f;
            Vector2 size = _font.MeasureString(label) * scale;
            Vector2 position = new(rect.X + (rect.Width - size.X) / 2f,
                rect.Y + (rect.Height - size.Y) / 2f);
            spriteBatch.DrawString(_font, label, position, enabled ? Theme.TextWhite : Theme.TextDark,
                0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        }

        private static bool IsUsableTexture(Texture2D texture)
            => texture != null && !texture.IsDisposed;

        private static void DrawNineSlice(SpriteBatch spriteBatch, Texture2D texture, Rectangle destination, int margin = 8)
        {
            if (!IsUsableTexture(texture) || destination.Width <= margin * 2 || destination.Height <= margin * 2)
            {
                if (IsUsableTexture(texture)) spriteBatch.Draw(texture, destination, Color.White);
                return;
            }

            int sw = texture.Width - margin * 2;
            int sh = texture.Height - margin * 2;
            int dw = destination.Width - margin * 2;
            int dh = destination.Height - margin * 2;
            spriteBatch.Draw(texture, new Rectangle(destination.X, destination.Y, margin, margin), new Rectangle(0, 0, margin, margin), Color.White);
            spriteBatch.Draw(texture, new Rectangle(destination.Right - margin, destination.Y, margin, margin), new Rectangle(texture.Width - margin, 0, margin, margin), Color.White);
            spriteBatch.Draw(texture, new Rectangle(destination.X, destination.Bottom - margin, margin, margin), new Rectangle(0, texture.Height - margin, margin, margin), Color.White);
            spriteBatch.Draw(texture, new Rectangle(destination.Right - margin, destination.Bottom - margin, margin, margin), new Rectangle(texture.Width - margin, texture.Height - margin, margin, margin), Color.White);
            spriteBatch.Draw(texture, new Rectangle(destination.X + margin, destination.Y, dw, margin), new Rectangle(margin, 0, sw, margin), Color.White);
            spriteBatch.Draw(texture, new Rectangle(destination.X + margin, destination.Bottom - margin, dw, margin), new Rectangle(margin, texture.Height - margin, sw, margin), Color.White);
            spriteBatch.Draw(texture, new Rectangle(destination.X, destination.Y + margin, margin, dh), new Rectangle(0, margin, margin, sh), Color.White);
            spriteBatch.Draw(texture, new Rectangle(destination.Right - margin, destination.Y + margin, margin, dh), new Rectangle(texture.Width - margin, margin, margin, sh), Color.White);
            spriteBatch.Draw(texture, new Rectangle(destination.X + margin, destination.Y + margin, dw, dh), new Rectangle(margin, margin, sw, sh), Color.White);
        }

        private void DrawSeason6Button(SpriteBatch spriteBatch, Rectangle localRect, string label, Texture2D texture)
        {
            // This method is called while the static surface is bound. Coordinates must stay
            // local to the render target; screen translation is only valid in live Draw().
            var rect = localRect;
            if (IsUsableTexture(texture))
                spriteBatch.Draw(texture, rect, Color.White);
            else
            {
                spriteBatch.Draw(GraphicsManager.Instance.Pixel, rect, Theme.SlotBg);
                UiDrawHelper.DrawBorder(spriteBatch, rect, Theme.BorderInner, 1);
            }
            if (_font == null) return;
            float scale = 0.25f;
            Vector2 size = _font.MeasureString(label) * scale;
            Vector2 pos = new(rect.X + (rect.Width - size.X) / 2f, rect.Y + (rect.Height - size.Y) / 2f);
            spriteBatch.DrawString(_font, label, pos + Vector2.One, Color.Black * 0.55f,
                0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
            spriteBatch.DrawString(_font, label, pos, Theme.TextWhite,
                0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        }

        private void DrawModernHeader(SpriteBatch spriteBatch)
        {
            var pixel = GraphicsManager.Instance.Pixel;
            if (pixel == null) return;

            var headerBg = new Rectangle(8, 6, WINDOW_WIDTH - 16, HEADER_HEIGHT - 8);
            DrawPanel(spriteBatch, headerBg, Theme.BgMid);

            spriteBatch.Draw(pixel, new Rectangle(20, 8, WINDOW_WIDTH - 40, 2), Theme.Secondary * 0.8f);
            spriteBatch.Draw(pixel, new Rectangle(30, 10, WINDOW_WIDTH - 60, 1), Theme.Secondary * 0.3f);

            if (_font != null)
            {
                string title = "VAULT";
                float scale = 0.50f;
                Vector2 size = _font.MeasureString(title) * scale;
                Vector2 pos = new((WINDOW_WIDTH - size.X) / 2, (HEADER_HEIGHT - size.Y) / 2 + 2);

                spriteBatch.Draw(pixel, new Rectangle((int)pos.X - 20, (int)pos.Y - 4, (int)size.X + 40, (int)size.Y + 8),
                                new Color(90, 140, 200, 30));

                spriteBatch.DrawString(_font, title, pos + new Vector2(2, 2), Color.Black * 0.5f,
                                       0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
                spriteBatch.DrawString(_font, title, pos, Theme.TextWhite,
                                       0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
            }

            int sepY = HEADER_HEIGHT - 2;
            UiDrawHelper.DrawHorizontalGradient(spriteBatch, new Rectangle(20, sepY, (WINDOW_WIDTH - 40) / 2, 1),
                                  Color.Transparent, Theme.BorderInner);
            UiDrawHelper.DrawHorizontalGradient(spriteBatch, new Rectangle(WINDOW_WIDTH / 2, sepY, (WINDOW_WIDTH - 40) / 2, 1),
                                  Theme.BorderInner, Color.Transparent);
        }

        private void DrawModernGridSection(SpriteBatch spriteBatch)
        {
            var pixel = GraphicsManager.Instance.Pixel;
            if (pixel == null) return;

            DrawSectionHeader(spriteBatch, "STORED ITEMS", _gridFrameRect.X, _gridFrameRect.Y + 4, _gridFrameRect.Width);
            DrawPanel(spriteBatch, _gridFrameRect, Theme.BgMid);

            spriteBatch.Draw(pixel, _gridRect, Theme.SlotBg);

            spriteBatch.Draw(pixel, new Rectangle(_gridRect.X, _gridRect.Y, _gridRect.Width, 2), Color.Black * 0.4f);
            spriteBatch.Draw(pixel, new Rectangle(_gridRect.X, _gridRect.Y, 2, _gridRect.Height), Color.Black * 0.3f);

            Color gridLine = new(40, 48, 60, 100);
            Color gridLineMajor = new(55, 65, 80, 120);

            for (int x = 1; x < Columns; x++)
            {
                int lineX = _gridRect.X + x * VAULT_SQUARE_WIDTH;
                bool isMajor = x == Columns / 2;
                spriteBatch.Draw(pixel, new Rectangle(lineX, _gridRect.Y, 1, _gridRect.Height), isMajor ? gridLineMajor : gridLine);
            }

            for (int y = 1; y < VisibleRows; y++)
            {
                int lineY = _gridRect.Y + y * VAULT_SQUARE_HEIGHT;
                bool isMajor = y == VisibleRows / 2;
                spriteBatch.Draw(pixel, new Rectangle(_gridRect.X, lineY, _gridRect.Width, 1), isMajor ? gridLineMajor : gridLine);
            }

            spriteBatch.Draw(pixel, new Rectangle(_gridRect.X, _gridRect.Bottom - 1, _gridRect.Width, 1), Theme.BorderHighlight * 0.2f);
            spriteBatch.Draw(pixel, new Rectangle(_gridRect.Right - 1, _gridRect.Y, 1, _gridRect.Height), Theme.BorderHighlight * 0.15f);
        }

        private void DrawModernFooter(SpriteBatch spriteBatch)
        {
            var pixel = GraphicsManager.Instance.Pixel;
            if (pixel == null) return;

            int sepY = _footerRect.Y - 4;
            UiDrawHelper.DrawHorizontalGradient(spriteBatch, new Rectangle(30, sepY, (WINDOW_WIDTH - 60) / 2, 1),
                                  Color.Transparent, Theme.Secondary * 0.4f);
            UiDrawHelper.DrawHorizontalGradient(spriteBatch, new Rectangle(WINDOW_WIDTH / 2, sepY, (WINDOW_WIDTH - 60) / 2, 1),
                                  Theme.Secondary * 0.4f, Color.Transparent);

            DrawPanel(spriteBatch, _footerRect, Theme.BgMid);

            // Zen coin icon
            int coinX = _footerRect.X + 18;
            int coinY = _footerRect.Y + _footerRect.Height / 2;

            DrawFilledCircle(spriteBatch, coinX, coinY, 10, Theme.AccentDim);
            DrawFilledCircle(spriteBatch, coinX, coinY, 7, Theme.Accent);
            DrawFilledCircle(spriteBatch, coinX - 2, coinY - 2, 3, Theme.AccentBright * 0.6f);

            // Zen field background
            DrawPanel(spriteBatch, _zenFieldRect, Theme.SlotBg);
            var innerField = new Rectangle(_zenFieldRect.X + 2, _zenFieldRect.Y + 2,
                                           _zenFieldRect.Width - 4, _zenFieldRect.Height - 4);
            spriteBatch.Draw(pixel, innerField, Theme.BgDarkest * 0.7f);
        }

        // ═══════════════════════════════════════════════════════════════
        // DYNAMIC DRAWING
        // ═══════════════════════════════════════════════════════════════

        private void DrawCloseButton(SpriteBatch spriteBatch)
        {
            var pixel = GraphicsManager.Instance.Pixel;
            if (pixel == null) return;

            var rect = Translate(_closeButtonRect);
            if (IsSeason6 && IsUsableTexture(_s6Close))
            {
                spriteBatch.Draw(_s6Close, rect, Color.White);
                return;
            }
            Color btnColor = _closeHovered ? Theme.Accent : Theme.TextGray;

            // Draw X symbol
            int cx = rect.X + rect.Width / 2;
            int cy = rect.Y + rect.Height / 2;
            int halfSize = 6;
            int thickness = 2;

            for (int i = -halfSize; i <= halfSize; i++)
            {
                spriteBatch.Draw(pixel, new Rectangle(cx + i - thickness / 2, cy + i - thickness / 2, thickness, thickness), btnColor);
                spriteBatch.Draw(pixel, new Rectangle(cx + i - thickness / 2, cy - i - thickness / 2, thickness, thickness), btnColor);
            }
        }

        private void DrawZenText(SpriteBatch spriteBatch)
        {
            if (_font == null) return;

            var zenRect = Translate(_zenFieldRect);
            var pixel = GraphicsManager.Instance.Pixel;

            if (_isZenInputActive && pixel != null)
            {
                var borderColor = Theme.AccentBright * 0.9f;
                spriteBatch.Draw(pixel, new Rectangle(zenRect.X, zenRect.Y, zenRect.Width, 2), borderColor);
                spriteBatch.Draw(pixel, new Rectangle(zenRect.X, zenRect.Bottom - 2, zenRect.Width, 2), borderColor);
                spriteBatch.Draw(pixel, new Rectangle(zenRect.X, zenRect.Y, 2, zenRect.Height), borderColor);
                spriteBatch.Draw(pixel, new Rectangle(zenRect.Right - 2, zenRect.Y, 2, zenRect.Height), borderColor);
            }

            string zenText = _isZenInputActive
                ? $"{(_zenMoveDirection == VaultMoveMoneyRequest.VaultMoneyMoveDirection.InventoryToVault ? "IN" : "OUT")}: {(_zenInputText.Length == 0 ? "0" : _zenInputText)}{(_zenInputShowCursor ? "|" : string.Empty)}"
                : _vaultZen.ToString();
            float scale = 0.35f;
            Vector2 size = _font.MeasureString(zenText) * scale;
            Vector2 pos = new(zenRect.X + 8, zenRect.Y + (zenRect.Height - size.Y) / 2);

            spriteBatch.DrawString(_font, zenText, pos, Theme.TextGold * Alpha,
                                  0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        }

        private void DrawZenButtons(SpriteBatch spriteBatch)
        {
            var pixel = GraphicsManager.Instance.Pixel;
            if (pixel == null || _font == null) return;

            DrawZenButton(spriteBatch, _depositButtonRect, "IN", _depositHovered, VaultMoveMoneyRequest.VaultMoneyMoveDirection.InventoryToVault);
            DrawZenButton(spriteBatch, _withdrawButtonRect, "OUT", _withdrawHovered, VaultMoveMoneyRequest.VaultMoneyMoveDirection.VaultToInventory);
        }

        private void DrawZenButton(SpriteBatch spriteBatch, Rectangle localRect, string label, bool hovered, VaultMoveMoneyRequest.VaultMoneyMoveDirection direction)
        {
            var pixel = GraphicsManager.Instance.Pixel;
            if (pixel == null || _font == null) return;

            var rect = Translate(localRect);
            bool selected = _isZenInputActive && _zenMoveDirection == direction;

            Color bg = Theme.BgDarkest * 0.7f;
            if (hovered) bg = Color.Lerp(bg, Theme.BgLight, 0.25f);
            if (selected) bg = Color.Lerp(bg, Theme.Secondary, 0.45f);

            Color border = selected ? Theme.Accent : Theme.BorderInner;

            spriteBatch.Draw(pixel, rect, bg);
            spriteBatch.Draw(pixel, new Rectangle(rect.X, rect.Y, rect.Width, 2), border);
            spriteBatch.Draw(pixel, new Rectangle(rect.X, rect.Bottom - 2, rect.Width, 2), border);
            spriteBatch.Draw(pixel, new Rectangle(rect.X, rect.Y, 2, rect.Height), border);
            spriteBatch.Draw(pixel, new Rectangle(rect.Right - 2, rect.Y, 2, rect.Height), border);

            float scale = 0.30f;
            Vector2 size = _font.MeasureString(label) * scale;
            Vector2 pos = new(rect.X + (rect.Width - size.X) / 2, rect.Y + (rect.Height - size.Y) / 2);
            Color textColor = selected ? Theme.TextWhite : Theme.TextGray;

            spriteBatch.DrawString(_font, label, pos + Vector2.One, Color.Black * 0.6f, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
            spriteBatch.DrawString(_font, label, pos, textColor, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        }

        private void DrawGridOverlays(SpriteBatch spriteBatch)
        {
            var pixel = GraphicsManager.Instance.Pixel;
            if (pixel == null) return;

            Point gridOrigin = new(DisplayRectangle.X + _gridRect.X, DisplayRectangle.Y + _gridRect.Y);
            var activeDragged = _draggedItem ?? InventoryControl.Instance?.GetDraggedItem();

            // Drag preview highlight
            if (activeDragged != null && _pendingDropSlot.X >= 0)
            {
                // Match inventory: highlight the entire footprint (green=valid, red=invalid)
                for (int y = 0; y < VisibleRows; y++)
                {
                    for (int x = 0; x < Columns; x++)
                    {
                        var highlight = GetSlotHighlightColor(new Point(x, y), activeDragged);
                        if (!highlight.HasValue)
                            continue;

                        var rect = new Rectangle(
                            gridOrigin.X + x * VAULT_SQUARE_WIDTH,
                            gridOrigin.Y + y * VAULT_SQUARE_HEIGHT,
                            VAULT_SQUARE_WIDTH, VAULT_SQUARE_HEIGHT);
                        spriteBatch.Draw(pixel, rect, highlight.Value);
                    }
                }
            }

            // Hovered slot highlight (when not dragging item)
            if (activeDragged == null)
            {
                ItemGridRenderHelper.DrawGridOverlays(spriteBatch, pixel, DisplayRectangle, _gridRect, _hoveredItem, _hoveredSlot,
                                 VAULT_SQUARE_WIDTH, VAULT_SQUARE_HEIGHT, Theme.SlotHover, Theme.Secondary, Alpha);
            }
        }

        private void DrawVaultItems(SpriteBatch spriteBatch)
        {
            var font = _font ?? GraphicsManager.Instance.Font;
            Point gridOrigin = new(DisplayRectangle.X + _gridRect.X, DisplayRectangle.Y + _gridRect.Y);
            var pixel = GraphicsManager.Instance.Pixel;
            _jewelEntries.Clear();

            foreach (var item in _items)
            {
                if (item == _draggedItem) continue;
                if (!IsItemOnCurrentPage(item)) continue;
                if (item.GridPosition.Y < 0 || item.GridPosition.Y >= VisibleRows ||
                    item.GridPosition.Y + item.Definition.Height > VisibleRows)
                    continue;

                var rect = new Rectangle(
                    gridOrigin.X + item.GridPosition.X * VAULT_SQUARE_WIDTH,
                    gridOrigin.Y + item.GridPosition.Y * VAULT_SQUARE_HEIGHT,
                    item.Definition.Width * VAULT_SQUARE_WIDTH,
                    item.Definition.Height * VAULT_SQUARE_HEIGHT);

                bool isHovered = item == _hoveredItem;
                Texture2D texture = ResolveItemTexture(item, rect.Width, rect.Height, isHovered, allowGenerate: true);

                // Glow similar to inventory
                Color glowColor = ItemUiHelper.GetItemGlowColor(item, GlowPalette);
                if (glowColor.A > 0 || isHovered)
                {
                    Color finalGlow = isHovered ? Color.Lerp(glowColor, Theme.Accent, 0.4f) : glowColor;
                    finalGlow.A = (byte)Math.Min(255, finalGlow.A + (isHovered ? 40 : 0));
                    ItemUiHelper.DrawItemGlow(spriteBatch, pixel, rect, finalGlow);
                }

                // Cell background
                if (pixel != null)
                {
                    var bgRect = new Rectangle(rect.X + 1, rect.Y + 1, rect.Width - 2, rect.Height - 2);
                    spriteBatch.Draw(pixel, bgRect, isHovered ? Theme.SlotHover : Theme.SlotBg);
                }

                if (texture != null)
                {
                    spriteBatch.Draw(texture, rect, Color.White * Alpha);

                    if (JewelShineOverlay.ShouldShine(item))
                    {
                        _jewelEntries.Add((item, rect));
                    }
                }
                else if (pixel != null)
                {
                    ItemGridRenderHelper.DrawItemPlaceholder(spriteBatch, pixel, font, rect, item, Theme.BgLight, Theme.TextGray * 0.8f);
                }

                if (font != null && item.Definition.BaseDurability == 0 && item.Definition.MagicDurability == 0 && item.Durability > 1)
                {
                    ItemGridRenderHelper.DrawItemStackCount(spriteBatch, font, rect, item.Durability, Theme.TextGold, Alpha);
                }

                if (font != null && item.Details.Level > 0)
                {
                    ItemGridRenderHelper.DrawItemLevelBadge(spriteBatch, pixel, font, rect, item.Details.Level,
                                       lvl => lvl >= 9 ? Theme.Danger :
                                              lvl >= 7 ? Theme.Accent :
                                              lvl >= 4 ? Theme.AccentDim :
                                              Theme.TextGray,
                                       new Color(0, 0, 0, 180));
                }
            }

            if (_jewelEntries.Count > 0)
            {
                JewelShineOverlay.DrawBatch(spriteBatch, _jewelEntries, _currentGameTime, Alpha, UiScaler.SpriteTransform);
            }
        }

        private void DrawTooltip(SpriteBatch spriteBatch)
        {
            if (_hoveredItem == null || _font == null) return;

            var lines = ItemUiHelper.BuildTooltipLines(_hoveredItem);
            const float scale = 0.44f;
            const int lineSpacing = 4;
            const int paddingX = 14;
            const int paddingY = 12;

            int maxWidth = 0;
            int totalHeight = 0;
            foreach (var (text, _) in lines)
            {
                Vector2 sz = _font.MeasureString(text) * scale;
                maxWidth = Math.Max(maxWidth, (int)MathF.Ceiling(sz.X));
                totalHeight += (int)MathF.Ceiling(sz.Y) + lineSpacing;
            }
            totalHeight += 6; // separator after first line

            int tooltipWidth = maxWidth + paddingX * 2;
            int tooltipHeight = totalHeight + paddingY * 2;

            Point mouse = MuGame.Instance.UiMouseState.Position;
            var itemRect = new Rectangle(
                DisplayRectangle.X + _gridRect.X + _hoveredItem.GridPosition.X * VAULT_SQUARE_WIDTH,
                DisplayRectangle.Y + _gridRect.Y + _hoveredItem.GridPosition.Y * VAULT_SQUARE_HEIGHT,
                _hoveredItem.Definition.Width * VAULT_SQUARE_WIDTH,
                _hoveredItem.Definition.Height * VAULT_SQUARE_HEIGHT);

            Rectangle tooltipRect = new(mouse.X + 16, mouse.Y + 16, tooltipWidth, tooltipHeight);
            Rectangle screenBounds = new(0, 0, UiScaler.VirtualSize.X, UiScaler.VirtualSize.Y);

            if (tooltipRect.Intersects(itemRect))
            {
                tooltipRect.X = itemRect.X - tooltipWidth - 8;
                tooltipRect.Y = itemRect.Y;

                if (tooltipRect.X < 10 || tooltipRect.Intersects(itemRect))
                {
                    tooltipRect.X = itemRect.X;
                    tooltipRect.Y = itemRect.Y - tooltipHeight - 8;

                    if (tooltipRect.Y < 10)
                    {
                        tooltipRect.X = itemRect.X;
                        tooltipRect.Y = itemRect.Bottom + 8;
                    }
                }
            }

            tooltipRect.X = Math.Clamp(tooltipRect.X, 10, screenBounds.Right - tooltipRect.Width - 10);
            tooltipRect.Y = Math.Clamp(tooltipRect.Y, 10, screenBounds.Bottom - tooltipRect.Height - 10);

            var pixel = GraphicsManager.Instance.Pixel;
            if (pixel == null) return;

            var shadowRect = new Rectangle(tooltipRect.X + 4, tooltipRect.Y + 4, tooltipRect.Width, tooltipRect.Height);
            spriteBatch.Draw(pixel, shadowRect, Color.Black * 0.5f);

            UiDrawHelper.DrawVerticalGradient(spriteBatch, tooltipRect, new Color(20, 24, 32, 252), new Color(12, 14, 18, 254));

            bool isExcellent = _hoveredItem.Details.IsExcellent;
            bool isAncient = _hoveredItem.Details.IsAncient;
            bool isHighLevel = _hoveredItem.Details.Level >= 7;

            Color borderColor = isExcellent ? Theme.GlowExcellent :
                                isAncient ? Theme.GlowAncient :
                                isHighLevel ? Theme.Accent :
                                Theme.TextWhite;

            const int borderThickness = 2;
            spriteBatch.Draw(pixel, new Rectangle(tooltipRect.X, tooltipRect.Y, tooltipRect.Width, borderThickness), borderColor);
            spriteBatch.Draw(pixel, new Rectangle(tooltipRect.X, tooltipRect.Bottom - borderThickness, tooltipRect.Width, borderThickness), borderColor);
            spriteBatch.Draw(pixel, new Rectangle(tooltipRect.X, tooltipRect.Y, borderThickness, tooltipRect.Height), borderColor);
            spriteBatch.Draw(pixel, new Rectangle(tooltipRect.Right - borderThickness, tooltipRect.Y, borderThickness, tooltipRect.Height), borderColor);

            int textY = tooltipRect.Y + paddingY;
            bool firstLine = true;
            foreach (var (text, color) in lines)
            {
                Vector2 textSize = _font.MeasureString(text) * scale;
                int textX = tooltipRect.X + (tooltipRect.Width - (int)textSize.X) / 2;

                spriteBatch.DrawString(_font, text, new Vector2(textX + 1, textY + 1), Color.Black * 0.7f,
                                       0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
                Color lineColor = firstLine ? borderColor : color;
                spriteBatch.DrawString(_font, text, new Vector2(textX, textY), lineColor,
                                       0f, Vector2.Zero, scale, SpriteEffects.None, 0f);

                textY += (int)textSize.Y + lineSpacing;

                if (firstLine)
                {
                    textY += 2;
                    spriteBatch.Draw(pixel, new Rectangle(tooltipRect.X + 8, textY, tooltipRect.Width - 16, 1), borderColor * 0.3f);
                    textY += 4;
                    firstLine = false;
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // INPUT HANDLING
        // ═══════════════════════════════════════════════════════════════

        private void HandleMouseInput()
        {
            var mouse = MuGame.Instance.UiMouseState;
            var prev = MuGame.Instance.PrevUiMouseState;

            bool leftJustPressed = mouse.LeftButton == ButtonState.Pressed &&
                                   prev.LeftButton == ButtonState.Released;

            if (!leftJustPressed) return;

            Point mousePos = mouse.Position;

            if (IsSeason6 && _draggedItem == null)
            {
                if (Translate(_pagePreviousRect).Contains(mousePos))
                {
                    SetSeason6Page(_season6Page - 1);
                    Scene?.SetMouseInputConsumed();
                    return;
                }

                if (Translate(_pageNextRect).Contains(mousePos))
                {
                    SetSeason6Page(_season6Page + 1);
                    Scene?.SetMouseInputConsumed();
                    return;
                }
            }

            if (_draggedItem == null)
            {
                if (_hoveredItem != null)
                {
                    BeginDrag(_hoveredItem);
                    Scene?.SetMouseInputConsumed();
                }
                return;
            }

            AttemptDrop(mousePos);
            Scene?.SetMouseInputConsumed();
        }

        private void UpdateHoverState()
        {
            var mouse = MuGame.Instance.UiMouseState.Position;
            _pagePreviousHovered = IsSeason6 && Translate(_pagePreviousRect).Contains(mouse);
            _pageNextHovered = IsSeason6 && Translate(_pageNextRect).Contains(mouse);
            var externalDragged = InventoryControl.Instance?.GetDraggedItem();

            if (_draggedItem != null)
            {
                var dropSlot = GetSlotAtScreenPosition(mouse);
                _pendingDropSlot = dropSlot.X >= 0 ? dropSlot : new Point(-1, -1);
                _hoveredItem = null;
                _hoveredSlot = dropSlot;
                return;
            }
            else if (externalDragged != null)
            {
                var dropSlot = GetSlotAtScreenPosition(mouse);
                _pendingDropSlot = dropSlot.X >= 0 ? dropSlot : new Point(-1, -1);
                _hoveredItem = null;
                _hoveredSlot = dropSlot;
                return;
            }

            _hoveredSlot = GetSlotAtScreenPosition(mouse);
            _hoveredItem = GetItemAt(mouse);
        }

        private void PrecacheAnimatedPreviews()
        {
            if (_currentGameTime == null)
                return;

            CacheAnimatedPreview(_hoveredItem);
            CacheAnimatedPreview(_draggedItem);
        }

        private void CacheAnimatedPreview(InventoryItem item)
        {
            if (item == null)
                return;

            int w = item.Definition.Width * VAULT_SQUARE_WIDTH;
            int h = item.Definition.Height * VAULT_SQUARE_HEIGHT;
            var key = (item, w, h, true);
            if (_bmdPreviewCache.TryGetValue(key, out var existing) && existing != null)
            {
                return;
            }

            try
            {
                var animated = BmdPreviewRenderer.GetAnimatedPreview(item, w, h, _currentGameTime);
                if (animated != null)
                {
                    _bmdPreviewCache[key] = animated;
                }
            }
            catch
            {
                // ignore and leave cache empty; will fall back to static
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // DRAG & DROP
        // ═══════════════════════════════════════════════════════════════

        private void BeginDrag(InventoryItem item)
        {
            _draggedItem = item;
            _draggedOriginalSlot = item.GridPosition;
            RemoveItemFromGrid(item);
            _hoveredItem = null;
            _pendingDropSlot = new Point(-1, -1);

            // Pre-cache drag preview while we're outside the draw pass to avoid render-target thrashing.
            int w = item.Definition.Width * VAULT_SQUARE_WIDTH;
            int h = item.Definition.Height * VAULT_SQUARE_HEIGHT;
            _ = ResolveItemTexture(item, w, h, animated: false);
        }

        private void AttemptDrop(Point mousePos)
        {
            if (_draggedItem == null) return;

            var dropSlot = GetSlotAtScreenPosition(mousePos);
            var inventory = InventoryControl.Instance;
            bool dropped = false;

            if (dropSlot.X >= 0 && CanPlaceAt(dropSlot, _draggedItem))
            {
                PlaceDraggedItem(dropSlot);
                int w = _draggedItem.Definition.Width * VAULT_SQUARE_WIDTH;
                int h = _draggedItem.Definition.Height * VAULT_SQUARE_HEIGHT;
                _ = ResolveItemTexture(_draggedItem, w, h, animated: Constants.ENABLE_ITEM_MATERIAL_ANIMATION);
                if (dropSlot != _draggedOriginalSlot)
                {
                    SendVaultMove(_draggedOriginalSlot, dropSlot);
                }
                dropped = true;
            }
            else if (inventory != null && inventory.Visible && inventory.DisplayRectangle.Contains(mousePos))
            {
                Point invSlot = inventory.GetSlotAtScreenPositionPublic(mousePos);
                if (invSlot.X >= 0 && inventory.CanPlaceAt(invSlot, _draggedItem))
                {
                    MoveItemToInventory(invSlot, inventory);
                    dropped = true;
                }
            }

            if (!dropped)
            {
                ReturnDraggedItem();
            }

            _draggedItem = null;
            _draggedOriginalSlot = new Point(-1, -1);
            _pendingDropSlot = new Point(-1, -1);
        }

        private void ReturnDraggedItem()
        {
            if (_draggedItem != null && _draggedOriginalSlot.X >= 0)
            {
                PlaceItemOnGrid(_draggedItem, _draggedOriginalSlot);
            }
        }

        private void PlaceDraggedItem(Point newSlot)
        {
            if (_draggedItem == null) return;
            PlaceItemOnGrid(_draggedItem, newSlot);
        }

        private void MoveItemToInventory(Point targetSlot, InventoryControl inventory)
        {
            if (_draggedItem == null) return;

            byte fromSlot = (byte)GetServerSlot(_draggedItem, _draggedOriginalSlot);
            byte toSlot = (byte)(InventoryControl.InventorySlotOffsetConstant +
                                 (targetSlot.Y * InventoryControl.Columns) + targetSlot.X);

            var svc = _networkManager?.GetCharacterService();
            var state = _networkManager?.GetCharacterState();
            var version = _networkManager?.TargetVersion ?? TargetProtocolVersion.Season6;

            if (svc != null && state != null)
            {
                state.StashPendingVaultMove(fromSlot, 0xFF);

                var raw = _draggedItem.RawData ?? Array.Empty<byte>();

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await svc.SendStorageItemMoveAsync(ItemStorageKind.Vault, fromSlot, ItemStorageKind.Inventory, toSlot, version, raw);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Failed to move item from vault to inventory.");
                    }

                    await Task.Delay(1200);

                    if (_networkManager != null && state.IsVaultMovePending(fromSlot, 0xFF))
                    {
                        MuGame.ScheduleOnMainThread(state.RaiseVaultItemsChanged);
                    }

                    if (_networkManager != null && state.IsInventoryMovePending(toSlot, toSlot))
                    {
                        MuGame.ScheduleOnMainThread(state.RaiseInventoryChanged);
                    }
                });
            }

            _items.Remove(_draggedItem);
            _serverSlots.Remove(_draggedItem);
            inventory?.BringToFront();
        }

        private void SendVaultMove(Point fromSlot, Point toSlot)
        {
            byte from = (byte)GetServerSlot(_draggedItem, fromSlot);
            byte to = (byte)GetServerSlotForVisualSlot(toSlot);

            if (_draggedItem != null)
                _serverSlots[_draggedItem] = to;

            if (_networkManager == null) return;

            var svc = _networkManager.GetCharacterService();
            var state = _networkManager.GetCharacterState();

            if (svc == null || state == null) return;

            state.StashPendingVaultMove(from, to);

            var raw = _draggedItem?.RawData ?? Array.Empty<byte>();
            var version = _networkManager.TargetVersion;

            _ = Task.Run(async () =>
            {
                try
                {
                    await svc.SendStorageItemMoveAsync(ItemStorageKind.Vault, from, ItemStorageKind.Vault, to, version, raw);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to move item inside vault.");
                }

                await Task.Delay(1200);
                if (_networkManager != null && state.IsVaultMovePending(from, to))
                {
                    MuGame.ScheduleOnMainThread(state.RaiseVaultItemsChanged);
                }
            });
        }

        // ═══════════════════════════════════════════════════════════════
        // HELPERS
        // ═══════════════════════════════════════════════════════════════

        private Rectangle Translate(Rectangle rect)
            => new(DisplayRectangle.X + rect.X, DisplayRectangle.Y + rect.Y, rect.Width, rect.Height);

        private InventoryItem GetItemAt(Point mousePos)
        {
            if (!DisplayRectangle.Contains(mousePos)) return null;

            Point gridOrigin = new(DisplayRectangle.X + _gridRect.X, DisplayRectangle.Y + _gridRect.Y);

            foreach (var item in _items)
            {
                if (item == _draggedItem) continue;
                if (!IsItemOnCurrentPage(item)) continue;

                var rect = new Rectangle(
                    gridOrigin.X + item.GridPosition.X * VAULT_SQUARE_WIDTH,
                    gridOrigin.Y + item.GridPosition.Y * VAULT_SQUARE_HEIGHT,
                    item.Definition.Width * VAULT_SQUARE_WIDTH,
                    item.Definition.Height * VAULT_SQUARE_HEIGHT);

                if (rect.Contains(mousePos)) return item;
            }

            return null;
        }

        private void HandleVisibilityLost()
        {
            CancelZenInput();
            SendCloseNpcRequest();
            _characterState?.ClearVaultItems();
            _items.Clear();
            _serverSlots.Clear();
            ClearGrid();
            _itemTextureCache.Clear();
            _bmdPreviewCache.Clear();
            _draggedItem = null;
            _hoveredItem = null;
            _pendingDropSlot = new Point(-1, -1);
            _isDragging = false;
            _pendingShow = false;
            _warmupComplete = false;
            _season6Page = 0;
        }

        public void SetVaultZen(uint amount)
        {
            _vaultZen = amount;
        }

        private void SendCloseNpcRequest()
        {
            if (_closeRequestSent) return;
            _closeRequestSent = true;
            var svc = _networkManager?.GetCharacterService();
            if (svc != null)
            {
                _ = svc.SendCloseNpcRequestAsync();
            }
        }

        private void EnsureCharacterState()
        {
            if (_characterState != null) return;

            _characterState = MuGame.Network?.GetCharacterState();
            if (_characterState != null)
            {
                _characterState.VaultItemsChanged += RefreshVaultContent;
            }
        }

        private void RefreshVaultContent()
        {
            if (_characterState == null) return;

            bool wasVisible = Visible;

            _items.Clear();
            _serverSlots.Clear();
            ClearGrid();

            var vaultItems = _characterState.GetVaultItems();
            foreach (var kv in vaultItems)
            {
                byte slot = kv.Key;
                byte[] data = kv.Value;

                int gridX = slot % Columns;
                int gridY = GetVisualRow(slot);

                var def = ItemDatabase.GetItemDefinition(data)
                    ?? new ItemDefinition(0, ItemDatabase.GetItemName(data) ?? "Unknown Item", 1, 1, "Interface/newui_item_box.tga");

                var item = new InventoryItem(def, new Point(gridX, gridY), data);
                if (data.Length > 2)
                {
                    item.Durability = data[2];
                }

                _items.Add(item);
                _serverSlots[item] = slot;
            }

            RebuildVisibleVaultGrid();

            foreach (var item in _items)
            {
                if (!string.IsNullOrEmpty(item.Definition.TexturePath) &&
                    !item.Definition.TexturePath.EndsWith(".bmd", StringComparison.OrdinalIgnoreCase))
                {
                    _ = TextureLoader.Instance.Prepare(item.Definition.TexturePath);
                }
            }

            if (_items.Count > 0 || _vaultZen > 0)
            {
                // Align left with padding before showing, then freeze position to avoid auto realignment
                ForceAlignNow();
                Align = ControlAlign.None;
                // Use deferred show - warmup happens in Update(), window shows one frame later
                // to avoid black screen flicker from render target switches during Draw().
                // Only trigger pending show if not already visible (to avoid re-triggering sound on refresh)
                if (!wasVisible)
                {
                    _pendingShow = true;
                    _warmupComplete = false;
                }
                _closeRequestSent = false;
                _isDragging = false;
            }
        }

        private bool HandleZenControlsMouseInput(Point mousePos, bool leftJustPressed)
        {
            if (_draggedItem != null)
            {
                return false;
            }

            if (_isZenInputActive)
            {
                if (!leftJustPressed)
                {
                    return false;
                }

                if (_depositHovered)
                {
                    BeginZenInput(VaultMoveMoneyRequest.VaultMoneyMoveDirection.InventoryToVault);
                    return true;
                }

                if (_withdrawHovered)
                {
                    BeginZenInput(VaultMoveMoneyRequest.VaultMoneyMoveDirection.VaultToInventory);
                    return true;
                }

                if (_zenFieldHovered)
                {
                    return true;
                }

                CancelZenInput();
                return true;
            }

            if (!leftJustPressed)
            {
                return false;
            }

            if (_depositHovered)
            {
                BeginZenInput(VaultMoveMoneyRequest.VaultMoneyMoveDirection.InventoryToVault);
                return true;
            }

            if (_withdrawHovered)
            {
                BeginZenInput(VaultMoveMoneyRequest.VaultMoneyMoveDirection.VaultToInventory);
                return true;
            }

            if (_zenFieldHovered)
            {
                BeginZenInput(_zenMoveDirection);
                return true;
            }

            return false;
        }

        private void BeginZenInput(VaultMoveMoneyRequest.VaultMoneyMoveDirection direction)
        {
            _zenMoveDirection = direction;
            if (!_isZenInputActive)
            {
                _zenInputText = string.Empty;
                _zenInputBlinkTimer = 0;
                _zenInputShowCursor = true;
            }

            _isZenInputActive = true;
        }

        private void CancelZenInput()
        {
            _isZenInputActive = false;
            _zenInputText = string.Empty;
            _zenInputBlinkTimer = 0;
            _zenInputShowCursor = false;
        }

        private void HandleZenInputKeyboard()
        {
            if (!_isZenInputActive || !Visible)
            {
                return;
            }

            if (_currentGameTime != null)
            {
                _zenInputBlinkTimer += _currentGameTime.ElapsedGameTime.TotalMilliseconds;
                if (_zenInputBlinkTimer >= CursorBlinkIntervalMs)
                {
                    _zenInputShowCursor = !_zenInputShowCursor;
                    _zenInputBlinkTimer = 0;
                }
            }

            KeyboardState keyboard = MuGame.Instance.Keyboard;
            KeyboardState previousKeyboard = MuGame.Instance.PrevKeyboard;
            Keys[] inputKeys = DesktopTextInputKeys.DigitsAndCommands;
            for (int i = 0; i < inputKeys.Length; i++)
            {
                Keys key = inputKeys[i];
                if (keyboard.IsKeyDown(key) && previousKeyboard.IsKeyUp(key))
                {
                    if (key == Keys.Back)
                    {
                        if (_zenInputText.Length > 0)
                        {
                            _zenInputText = _zenInputText[..^1];
                        }
                    }
                    else if (key == Keys.Enter)
                    {
                        Scene?.ConsumeKeyboardEnter();
                        CommitZenMove();
                        return;
                    }
                    else if (key == Keys.Escape)
                    {
                        Scene?.ConsumeKeyboardEscape();
                        CancelZenInput();
                        return;
                    }
                    else if (TryGetDigitKey(key, out char digit))
                    {
                        if (_zenInputText.Length < ZenInputMaxDigits)
                        {
                            _zenInputText += digit;
                        }
                    }
                }
            }
        }

        private int GetVisualRow(byte serverSlot)
        {
            int serverRow = serverSlot / Columns;
            return IsSeason6 ? serverRow % VisibleRows : serverRow;
        }

        private int GetServerSlotForVisualSlot(Point visualSlot)
        {
            int serverRow = IsSeason6 ? _season6Page * VisibleRows + visualSlot.Y : visualSlot.Y;
            return serverRow * Columns + visualSlot.X;
        }

        private int GetServerSlot(InventoryItem item, Point visualSlot)
        {
            return item != null && _serverSlots.TryGetValue(item, out byte serverSlot)
                ? serverSlot
                : GetServerSlotForVisualSlot(visualSlot);
        }

        private bool IsItemOnCurrentPage(InventoryItem item)
        {
            if (!IsSeason6 || item == null || !_serverSlots.TryGetValue(item, out byte serverSlot))
                return true;

            return serverSlot / Columns / VisibleRows == _season6Page;
        }

        private void RebuildVisibleVaultGrid()
        {
            ClearGrid();
            foreach (var item in _items)
            {
                if (!_serverSlots.TryGetValue(item, out byte serverSlot))
                    continue;

                item.GridPosition = new Point(serverSlot % Columns, GetVisualRow(serverSlot));
                if (IsItemOnCurrentPage(item))
                    PlaceItemOnGrid(item, item.GridPosition);
            }
        }

        private void SetSeason6Page(int page)
        {
            if (!IsSeason6)
                return;

            int nextPage = Math.Clamp(page, 0, Season6PageCount - 1);
            if (_season6Page == nextPage)
                return;

            _season6Page = nextPage;
            _hoveredItem = null;
            _hoveredSlot = new Point(-1, -1);
            _pendingDropSlot = new Point(-1, -1);
            RebuildVisibleVaultGrid();
        }

        private static bool TryGetDigitKey(Keys key, out char digit)
        {
            if (key >= Keys.D0 && key <= Keys.D9)
            {
                digit = (char)('0' + (key - Keys.D0));
                return true;
            }

            if (key >= Keys.NumPad0 && key <= Keys.NumPad9)
            {
                digit = (char)('0' + (key - Keys.NumPad0));
                return true;
            }

            digit = '\0';
            return false;
        }

        private void CommitZenMove()
        {
            if (_networkManager == null || _characterState == null)
            {
                CancelZenInput();
                return;
            }

            uint amount = 0;
            if (!string.IsNullOrWhiteSpace(_zenInputText))
            {
                _ = uint.TryParse(_zenInputText, out amount);
            }

            if (amount == 0)
            {
                CancelZenInput();
                return;
            }

            uint inventoryZen = _characterState.InventoryZen;
            uint vaultZen = _vaultZen <= 0 ? 0 : (uint)Math.Min(_vaultZen, uint.MaxValue);

            if (_zenMoveDirection == VaultMoveMoneyRequest.VaultMoneyMoveDirection.InventoryToVault)
            {
                if (amount > inventoryZen)
                {
                    RequestDialog.ShowInfo("Not enough Zen in inventory.");
                    return;
                }
            }
            else
            {
                if (amount > vaultZen)
                {
                    RequestDialog.ShowInfo("Not enough Zen in vault.");
                    return;
                }

                if (inventoryZen >= MaxZen || (ulong)inventoryZen + amount > MaxZen)
                {
                    RequestDialog.ShowInfo("Inventory Zen limit reached.");
                    return;
                }
            }

            var svc = _networkManager.GetCharacterService();
            if (svc != null)
            {
                _ = svc.SendVaultMoveMoneyAsync(_zenMoveDirection, amount);
            }

            CancelZenInput();
        }

        private void WarmupTexturesSync()
        {
            if (GraphicsManager.Instance?.Sprite == null)
                return;

            foreach (var item in _items)
            {
                int w = item.Definition.Width * VAULT_SQUARE_WIDTH;
                int h = item.Definition.Height * VAULT_SQUARE_HEIGHT;
                _ = ResolveItemTexture(item, w, h, animated: false);
            }
        }

        private void PlaceItemOnGrid(InventoryItem item, Point slot)
        {
            item.GridPosition = slot;
            for (int y = 0; y < item.Definition.Height; y++)
            {
                for (int x = 0; x < item.Definition.Width; x++)
                {
                    int gx = slot.X + x;
                    int gy = slot.Y + y;
                    if (gx >= 0 && gx < Columns && gy >= 0 && gy < Rows)
                    {
                        _itemGrid[gx, gy] = item;
                    }
                }
            }
        }

        private void RemoveItemFromGrid(InventoryItem item)
        {
            for (int y = 0; y < Rows; y++)
            {
                for (int x = 0; x < Columns; x++)
                {
                    if (_itemGrid[x, y] == item)
                    {
                        _itemGrid[x, y] = null;
                    }
                }
            }
        }

        private void ClearGrid() => Array.Clear(_itemGrid, 0, _itemGrid.Length);

        private Color? GetSlotHighlightColor(Point slot, InventoryItem draggedItem)
        {
            if (draggedItem == null || _hoveredSlot.X == -1 || _hoveredSlot.Y == -1)
            {
                return null;
            }

            if (!IsSlotInDropArea(slot, _hoveredSlot, draggedItem))
            {
                return null;
            }

            return CanPlaceAt(_hoveredSlot, draggedItem)
                ? Color.GreenYellow * 0.5f
                : Color.Red * 0.6f;
        }

        private static bool IsSlotInDropArea(Point slot, Point dropPosition, InventoryItem item)
        {
            return slot.X >= dropPosition.X &&
                   slot.X < dropPosition.X + item.Definition.Width &&
                   slot.Y >= dropPosition.Y &&
                   slot.Y < dropPosition.Y + item.Definition.Height;
        }

        private Texture2D ResolveItemTexture(InventoryItem item, int width, int height, bool animated, bool allowGenerate = true)
        {
            if (item?.Definition == null) return null;

            string texturePath = item.Definition.TexturePath;
            if (string.IsNullOrEmpty(texturePath)) return null;

            bool isBmd = texturePath.EndsWith(".bmd", StringComparison.OrdinalIgnoreCase);

            if (!isBmd)
            {
                if (_itemTextureCache.TryGetValue(texturePath, out var cached) && cached != null)
                    return cached;

                var tex = TextureLoader.Instance.GetTexture2D(texturePath);
                if (tex != null) _itemTextureCache[texturePath] = tex;
                return tex;
            }

            bool isHovered = animated;
            if (!allowGenerate)
            {
                var rendererCached = BmdPreviewRenderer.TryGetCachedPreview(item, width, height);
                if (rendererCached != null)
                    return rendererCached;
                return null;
            }

            // Material animation for non-hovered items (if enabled)
            if (!isHovered && Constants.ENABLE_ITEM_MATERIAL_ANIMATION)
            {
                try
                {
                    var mat = BmdPreviewRenderer.GetMaterialAnimatedPreview(item, width, height, _currentGameTime);
                    if (mat != null)
                    {
                        return mat;
                    }
                }
                catch { }
            }

            if (isHovered)
            {
                try
                {
                    var animatedTexture = BmdPreviewRenderer.GetAnimatedPreview(item, width, height, _currentGameTime);
                    if (animatedTexture != null)
                    {
                        return animatedTexture;
                    }
                }
                catch { return null; }
            }

            var cacheKey = (item, width, height, false);
            if (_bmdPreviewCache.TryGetValue(cacheKey, out var cachedPreview) && cachedPreview != null)
                return cachedPreview;

            // Use cached static preview
            try
            {
                var preview = BmdPreviewRenderer.GetPreview(item, width, height);
                if (preview != null)
                {
                    _bmdPreviewCache[cacheKey] = preview;
                }
                return preview;
            }
            catch { return null; }
        }

    }
}
