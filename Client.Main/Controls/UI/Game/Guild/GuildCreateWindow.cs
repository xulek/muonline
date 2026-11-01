#nullable enable
using System;
using System.Text;
using Client.Main.Controllers;
using Client.Main.Controls.UI.Common;
using Client.Main.Controls.UI.Game.Common;
using Client.Main.Controls.UI.Game.Inventory;
using Client.Main.Core.Client;
using Client.Main.Graphics;
using Client.Main.Models;
using Client.Main.Helpers;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Client.Main.Controls.UI.Game.Guild
{
    /// <summary>
    /// Guild creation window, modelled on NewUIGuildMakeWindow.cpp: guild name (max 8 characters) and an
    /// 8 x 8 mark painted with the 16-colour guild palette. Opened by the server (0x55) after the guild master NPC dialog.
    /// </summary>
    public sealed class GuildCreateWindow : UIControl
    {
        private const int Pad = 14;
        private const int HeaderHeight = 48;
        private const int WindowWidth = 300;
        private const int Cell = 20;
        private const int MarkPixels = GuildInfo.MarkSize * Cell;
        private const int NameLabelY = HeaderHeight + 8;
        private const int NameFieldY = NameLabelY + 16;
        private const int EditorY = NameFieldY + 36;
        private const int PaletteY = EditorY + MarkPixels + 12;
        private const int PaletteCell = (WindowWidth - Pad * 2) / 8;
        private const int HintY = PaletteY + PaletteCell * 2 + 8;
        private const int ButtonY = HintY + 22;
        private const int WindowHeight = ButtonY + 32 + Pad;

        private static readonly Rectangle CloseLocal = new(WindowWidth - 34, 12, 22, 22);
        private static readonly Rectangle EditorLocal = new(Pad, EditorY, MarkPixels, MarkPixels);
        private static readonly Rectangle PreviewLocal = new(Pad + MarkPixels + 20, EditorY + 24, 64, 64);
        private static readonly Rectangle CreateLocal = new(Pad, ButtonY, (WindowWidth - Pad * 2 - 6) / 2, 32);
        private static readonly Rectangle CancelLocal = new(Pad + (WindowWidth - Pad * 2 - 6) / 2 + 6, ButtonY, (WindowWidth - Pad * 2 - 6) / 2, 32);

        private readonly TextFieldControl _name;
        private readonly byte[] _mark = new byte[GuildInfo.MarkSize * GuildInfo.MarkSize];
        private Texture2D? _preview;
        private bool _previewDirty = true;
        private byte _color = 3;
        private bool _closeHovered, _createHovered, _cancelHovered;
        private bool _waiting;
        private bool _nameFocused;
        private bool _isDragging;
        private Point _dragOffset;
        private string? _error;

        public GuildCreateWindow()
        {
            AutoViewSize = false;
            Interactive = true;
            ControlSize = ViewSize = new Point(WindowWidth, WindowHeight);
            Align = ControlAlign.HorizontalCenter | ControlAlign.VerticalCenter;
            Visible = false;

            _name = TextFieldControl.Create();
            _name.X = Pad;
            _name.Y = NameFieldY;
            _name.ViewSize = new Point(WindowWidth - Pad * 2, 24);
            _name.FontSize = 12f;
            _name.TextColor = ModernHudTheme.TextWhite;
            _name.BackgroundColor = ModernHudTheme.SlotBg;
            _name.BorderColor = ModernHudTheme.BorderInner;
            _name.BorderThickness = 1;
            _name.ValueChanged += (_, _) => SanitizeName();
            _name.EnterKeyPressed += (_, _) => MuGame.ScheduleOnMainThread(Create);
            Controls.Add(_name);
        }

        public override void Dispose()
        {
            _preview?.Dispose();
            base.Dispose();
        }

        // ─────────────────────────────── Visibility ───────────────────────────────

        public void Show()
        {
            _name.Value = string.Empty;
            Array.Clear(_mark);
            _previewDirty = true;
            _waiting = false;
            _error = null;
            Visible = true;
            BringToFront();
            FocusName();
        }

        /// <summary>Closes the window without notifying the server.</summary>
        public void Hide()
        {
            BlurName();
            _isDragging = false;
            Visible = false;
        }

        /// <summary>Cancels the creation like the original Cancel button (0x57).</summary>
        public void Cancel()
        {
            if (!Visible) return;
            Hide();
            var service = MuGame.Network?.GetCharacterService();
            if (service != null) _ = service.SendCancelGuildCreationAsync();
        }

        public void OnCreationResult(bool success)
        {
            _waiting = false;
            if (success) Hide();
        }

        private Rectangle Translate(Rectangle local)
            => new(DisplayRectangle.X + local.X, DisplayRectangle.Y + local.Y, local.Width, local.Height);

        private static Rectangle PaletteLocal(int index)
            => new(Pad + index % 8 * PaletteCell, PaletteY + index / 8 * PaletteCell, PaletteCell - 2, PaletteCell - 2);

        // ─────────────────────────────── Name ───────────────────────────────

        private void SanitizeName()
        {
            var filtered = new StringBuilder();
            foreach (char c in _name.Value)
                if (char.IsLetterOrDigit(c) && c < 128) filtered.Append(c);
            string value = filtered.ToString();
            if (value.Length > GuildManager.MaxGuildNameLength) value = value[..GuildManager.MaxGuildNameLength];
            if (value != _name.Value) _name.Value = value;
            _error = null;
        }

        private void FocusName()
        {
            _nameFocused = true;
            if (!_name.IsFocused) _name.Focus();
            if (Scene != null) Scene.FocusControl = _name;
        }

        private void BlurName()
        {
            _nameFocused = false;
            _name.Blur();
            if (Scene?.FocusControl == _name) Scene.FocusControl = null;
        }

        private string? GetBlocker()
        {
            string name = _name.Value.Trim();
            if (name.Length < 2) return "The guild name must have 2 to 8 characters.";
            if (Array.TrueForAll(_mark, p => p == 0)) return "Paint your guild mark first.";
            return null;
        }

        private void Create()
        {
            if (!Visible || _waiting) return;
            if (GetBlocker() is { } blocker)
            {
                _error = blocker;
                return;
            }

            var service = MuGame.Network?.GetCharacterService();
            if (service == null) return;
            _waiting = true;
            _error = null;
            _ = service.SendGuildCreateAsync(_name.Value.Trim(), GuildInfo.PackMark(_mark));
        }

        // ─────────────────────────────── Update ───────────────────────────────

        public override void Update(GameTime gameTime)
        {
            if (Visible && _nameFocused)
            {
                if (!_name.IsFocused) _name.Focus();
                if (Scene != null && Scene.FocusControl != _name) Scene.FocusControl = _name;
            }

            base.Update(gameTime);
            if (!Visible) return;

            if (MuGame.Instance.Keyboard.IsKeyDown(Keys.Escape) && MuGame.Instance.PrevKeyboard.IsKeyUp(Keys.Escape))
            {
                Scene?.ConsumeKeyboardEscape();
                Cancel();
                return;
            }

            var mouse = MuGame.Instance.UiMouseState;
            var previous = MuGame.Instance.PrevUiMouseState;
            Point position = mouse.Position;
            bool leftDown = mouse.LeftButton == ButtonState.Pressed;
            bool rightDown = mouse.RightButton == ButtonState.Pressed;
            bool leftPressed = leftDown && previous.LeftButton == ButtonState.Released;

            _name.BorderColor = _nameFocused ? ModernHudTheme.AccentBright : ModernHudTheme.BorderInner;

            if (_isDragging)
            {
                if (leftDown) { X = position.X - _dragOffset.X; Y = position.Y - _dragOffset.Y; }
                else _isDragging = false;
                return;
            }

            _closeHovered = Translate(CloseLocal).Contains(position);
            _createHovered = Translate(CreateLocal).Contains(position);
            _cancelHovered = Translate(CancelLocal).Contains(position);

            if (!DisplayRectangle.Contains(position)) return;

            // Painting: hold left to paint with the selected colour, right to erase.
            var editor = Translate(EditorLocal);
            if (!_waiting && (leftDown || rightDown) && editor.Contains(position))
            {
                Scene?.SetMouseInputConsumed();
                int x = (position.X - editor.X) / Cell, y = (position.Y - editor.Y) / Cell;
                int index = y * GuildInfo.MarkSize + x;
                byte value = leftDown ? _color : (byte)0;
                if ((uint)index < (uint)_mark.Length && _mark[index] != value)
                {
                    _mark[index] = value;
                    _previewDirty = true;
                    _error = null;
                }
                return;
            }

            if (!leftPressed) return;
            Scene?.SetMouseInputConsumed();

            if (_name.DisplayRectangle.Contains(position)) { FocusName(); return; }
            BlurName();

            if (_closeHovered || _cancelHovered) { Cancel(); return; }
            if (_createHovered) { Create(); return; }

            for (int i = 0; i < GuildMarkTextureCache.Palette.Length; i++)
            {
                if (!Translate(PaletteLocal(i)).Contains(position)) continue;
                _color = (byte)i;
                return;
            }

            if (Translate(new Rectangle(0, 0, WindowWidth, HeaderHeight)).Contains(position))
            {
                _isDragging = true;
                _dragOffset = new Point(position.X - X, position.Y - Y);
                Align = ControlAlign.None;
            }
        }

        // ─────────────────────────────── Draw ───────────────────────────────

        public override void Draw(GameTime gameTime)
        {
            if (!Visible || Status != GameControlStatus.Ready) return;
            var sb = GraphicsManager.Instance.Sprite;
            var pixel = GraphicsManager.Instance.Pixel;
            if (sb == null || pixel == null) return;

            if (_previewDirty)
            {
                _preview?.Dispose();
                _preview = GuildMarkTextureCache.Create(_mark);
                _previewDirty = false;
            }

            var scope = PlayerShopUi.BeginUiBatch();
            try
            {
                PlayerShopUi.DrawWindow(sb, DisplayRectangle, Alpha);
                PlayerShopUi.DrawHeader(sb, Translate(new Rectangle(0, 0, WindowWidth, HeaderHeight)), "CREATE GUILD", null, Alpha);
                PlayerShopUi.DrawCloseButton(sb, Translate(CloseLocal), _closeHovered, Alpha);

                PlayerShopUi.DrawText(sb, "Guild name", Translate(new Rectangle(Pad, NameLabelY, 0, 0)).Location.ToVector2(), ModernHudTheme.TextGray, 11f, Alpha);
                string counter = $"{_name.Value.Length}/{GuildManager.MaxGuildNameLength}";
                var counterSize = PlayerShopUi.Measure(counter, 10f);
                PlayerShopUi.DrawText(sb, counter, new Vector2(DisplayRectangle.Right - Pad - counterSize.X, DisplayRectangle.Y + NameLabelY + 1),
                    ModernHudTheme.TextGray * 0.8f, 10f, Alpha);

                // Mark editor grid.
                var editor = Translate(EditorLocal);
                PlayerShopUi.DrawPanel(sb, new Rectangle(editor.X - 2, editor.Y - 2, editor.Width + 4, editor.Height + 4), ModernHudTheme.BgDarkest, Alpha);
                for (int y = 0; y < GuildInfo.MarkSize; y++)
                for (int x = 0; x < GuildInfo.MarkSize; x++)
                {
                    var cell = new Rectangle(editor.X + x * Cell, editor.Y + y * Cell, Cell, Cell);
                    byte value = _mark[y * GuildInfo.MarkSize + x];
                    Color background = (x + y) % 2 == 0 ? new Color(40, 40, 46) : new Color(30, 30, 36);
                    sb.Draw(pixel, cell, (value == 0 ? background : GuildMarkTextureCache.Palette[value]) * Alpha);
                }
                for (int i = 1; i < GuildInfo.MarkSize; i++)
                {
                    sb.Draw(pixel, new Rectangle(editor.X + i * Cell, editor.Y, 1, editor.Height), Color.Black * 0.35f * Alpha);
                    sb.Draw(pixel, new Rectangle(editor.X, editor.Y + i * Cell, editor.Width, 1), Color.Black * 0.35f * Alpha);
                }

                // Preview.
                var preview = Translate(PreviewLocal);
                PlayerShopUi.DrawText(sb, "Preview", new Vector2(preview.X, preview.Y - 20), ModernHudTheme.TextGray, 11f, Alpha);
                PlayerShopUi.DrawPanel(sb, new Rectangle(preview.X - 3, preview.Y - 3, preview.Width + 6, preview.Height + 6), ModernHudTheme.BgDarkest, Alpha);
                var small = new Rectangle(preview.X + 20, preview.Bottom + 14, 24, 24);
                PlayerShopUi.DrawPanel(sb, new Rectangle(small.X - 3, small.Y - 3, small.Width + 6, small.Height + 6), ModernHudTheme.BgDarkest, Alpha);

                // Palette.
                for (int i = 0; i < GuildMarkTextureCache.Palette.Length; i++)
                {
                    var swatch = Translate(PaletteLocal(i));
                    if (i == 0)
                    {
                        sb.Draw(pixel, swatch, new Color(40, 40, 46) * Alpha);
                        PlayerShopUi.DrawTextCentered(sb, "X", swatch, ModernHudTheme.TextGray, 11f, Alpha);
                    }
                    else
                    {
                        sb.Draw(pixel, swatch, GuildMarkTextureCache.Palette[i] * Alpha);
                    }
                    UiDrawHelper.DrawBorder(sb, swatch, (i == _color ? ModernHudTheme.AccentBright : ModernHudTheme.BorderOuter) * Alpha, i == _color ? 2 : 1);
                }

                string hint = _error ?? (_waiting ? "Waiting for server..." : "Left click: paint   Right click: erase");
                Color hintColor = _error != null ? ModernHudTheme.Danger : _waiting ? ModernHudTheme.TextGold : ModernHudTheme.TextGray;
                PlayerShopUi.DrawTextCentered(sb, hint, Translate(new Rectangle(Pad, HintY, WindowWidth - Pad * 2, 16)), hintColor, 11f, Alpha);

                bool canCreate = !_waiting && GetBlocker() == null;
                PlayerShopUi.DrawButton(sb, Translate(CreateLocal), "CREATE", _createHovered, canCreate, ModernHudTheme.Success, Alpha, 14f);
                PlayerShopUi.DrawButton(sb, Translate(CancelLocal), "CANCEL", _cancelHovered, true, ModernHudTheme.Danger, Alpha, 14f);
            }
            finally
            {
                scope?.Dispose();
            }

            if (_preview != null)
            {
                using var markScope = new SpriteBatchScope(sb, SpriteSortMode.Deferred, BlendState.NonPremultiplied, SamplerState.PointClamp,
                    DepthStencilState.None, RasterizerState.CullNone, null, UiScaler.SpriteTransform);
                var preview = Translate(PreviewLocal);
                sb.Draw(_preview, preview, Color.White * Alpha);
                sb.Draw(_preview, new Rectangle(preview.X + 20, preview.Bottom + 14, 24, 24), Color.White * Alpha);
            }

            base.Draw(gameTime);

            if (_name.Value.Length == 0 && !_name.IsFocused)
            {
                var placeholder = PlayerShopUi.BeginUiBatch();
                try
                {
                    PlayerShopUi.DrawText(sb, "2-8 letters or digits", new Vector2(_name.DisplayRectangle.X + 6, _name.DisplayRectangle.Y + 5),
                        ModernHudTheme.TextGray * 0.7f, 11f, Alpha, shadow: false);
                }
                finally
                {
                    placeholder?.Dispose();
                }
            }
        }
    }
}
