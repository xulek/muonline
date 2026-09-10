using System;
using Client.Main.Controllers;
using Client.Main.Controls.UI.Common;
using Client.Main.Controls.UI.Game.Common;
using Client.Main.Helpers;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MUnique.OpenMU.Network.Packets.ServerToClient;

namespace Client.Main.Controls.UI.Game.Hud
{
    /// <summary>
    /// Displays Devil Square countdown messages (start/close) for 30 seconds.
    /// </summary>
    public sealed class DevilSquareCountdownControl : UIControl
    {
        private const int WIDTH = 520;
        private const int HEIGHT = 64;
        private const int HEADER_HEIGHT = 24;
        private const int PANEL_PADDING = 10;
        private const float FONT_SCALE = 0.45f;
        private const float FONT_SCALE_SMALL = 0.36f;
        private const float COUNTDOWN_SECONDS = 30f;

        private static class Theme
        {
            public static Color BgDarkest => ModernHudTheme.BgDarkest;
            public static Color BgDark => ModernHudTheme.BgDark;
            public static Color BgMid => ModernHudTheme.BgMid;
            public static Color BgLight => ModernHudTheme.BgLight;
            public static Color BgLighter => ModernHudTheme.BgLighter;

            public static Color Accent => ModernHudTheme.Accent;
            public static Color AccentBright => ModernHudTheme.AccentBright;
            public static Color AccentDim => ModernHudTheme.AccentDim;
            public static Color AccentGlow => ModernHudTheme.AccentGlow;

            public static Color Secondary => ModernHudTheme.Secondary;
            public static Color SecondaryBright => ModernHudTheme.SecondaryBright;
            public static Color SecondaryDim => ModernHudTheme.SecondaryDim;

            public static Color BorderOuter => ModernHudTheme.BorderOuter;
            public static Color BorderInner => ModernHudTheme.BorderInner;
            public static Color BorderHighlight => ModernHudTheme.BorderHighlight;

            public static Color SlotBg => ModernHudTheme.SlotBg;
            public static Color SlotBorder => ModernHudTheme.SlotBorder;
            public static Color SlotHover => ModernHudTheme.SlotHover;
            public static Color SlotSelected => ModernHudTheme.SlotSelected;

            public static Color TextWhite => ModernHudTheme.TextWhite;
            public static Color TextGold => ModernHudTheme.TextGold;
            public static Color TextGray => ModernHudTheme.TextGray;
            public static Color TextDark => ModernHudTheme.TextDark;

            public static Color Success => ModernHudTheme.Success;
            public static Color Warning => ModernHudTheme.Warning;
            public static Color Danger => ModernHudTheme.Danger;
        }

        private static DevilSquareCountdownControl _instance;

        private RenderTarget2D _staticSurface;
        private bool _staticSurfaceDirty = true;
        private SpriteFont _font;

        private UpdateMiniGameState.MiniGameTypeState _state = UpdateMiniGameState.MiniGameTypeState.DevilSquareClosed;
        private string _messageTemplate = string.Empty;
        private float _startTimeSeconds;
        private float _latestTotalSeconds;
        private bool _active;

        private Rectangle _panelRect;
        private Rectangle _headerRect;

        private DevilSquareCountdownControl()
        {
            Align = ControlAlign.Bottom | ControlAlign.HorizontalCenter;
            Margin = new Margin { Bottom = 110 };
            AutoViewSize = false;
            ViewSize = new Point(WIDTH, HEIGHT);
            ControlSize = ViewSize;
            Interactive = false;
            Visible = false;
            BackgroundColor = Color.Transparent;
            BorderColor = Color.Transparent;

            BuildLayoutMetrics();
        }

        public override bool NonDisposable => true;
        public static DevilSquareCountdownControl Instance => _instance ??= new DevilSquareCountdownControl();

        public override async System.Threading.Tasks.Task Load()
        {
            await base.Load();
            _font = GraphicsManager.Instance.Font;
            InvalidateStaticSurface();
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
            _latestTotalSeconds = (float)gameTime.TotalGameTime.TotalSeconds;

            if (!_active)
            {
                Visible = false;
                return;
            }

            float elapsed = _latestTotalSeconds - _startTimeSeconds;
            if (elapsed >= COUNTDOWN_SECONDS)
            {
                _active = false;
                Visible = false;
            }
            else
            {
                Visible = true;
            }
        }

        public override void Draw(GameTime gameTime)
        {
            if (!Visible || Status != GameControlStatus.Ready) return;

            EnsureStaticSurface();

            var sb = GraphicsManager.Instance.Sprite;
            using var scope = new SpriteBatchScope(
                sb,
                SpriteSortMode.Deferred,
                BlendState.AlphaBlend,
                GraphicsManager.GetQualityLinearSamplerState(),
                transform: UiScaler.SpriteTransform);

            sb.Draw(_staticSurface, DisplayRectangle, Color.White * Alpha);
            DrawDynamicContent(sb);
        }

        public override void Dispose()
        {
            Client.Main.Graphics.UiRenderTargetPool.Return(_staticSurface);
            _staticSurface = null;
            base.Dispose();
        }

        public void StartCountdown(UpdateMiniGameState.MiniGameTypeState state)
        {
            _state = state;
            _messageTemplate = state switch
            {
                UpdateMiniGameState.MiniGameTypeState.DevilSquareClosed => "You will enter Devil Square (in {0} seconds).",
                UpdateMiniGameState.MiniGameTypeState.DevilSquareOpened => "The gate of Devil Square will close down in {0} seconds.",
                UpdateMiniGameState.MiniGameTypeState.DevilSquareRunning => "The gate of Devil Square is closing down ({0} seconds remaining).",
                _ => string.Empty
            };

            if (string.IsNullOrEmpty(_messageTemplate))
            {
                _active = false;
                Visible = false;
                return;
            }

            _startTimeSeconds = _latestTotalSeconds;
            _active = true;
            Visible = true;
        }

        protected override void OnScreenSizeChanged()
        {
            base.OnScreenSizeChanged();
            InvalidateStaticSurface();
        }

        private void BuildLayoutMetrics()
        {
            _panelRect = new Rectangle(0, 0, ControlSize.X, ControlSize.Y);
            _headerRect = new Rectangle(PANEL_PADDING, 6, ControlSize.X - PANEL_PADDING * 2, HEADER_HEIGHT);
        }

        private void EnsureStaticSurface()
        {
            if (!_staticSurfaceDirty && _staticSurface != null && !_staticSurface.IsDisposed)
                return;

            var gd = GraphicsManager.Instance?.GraphicsDevice;
            if (gd == null) return;

            Client.Main.Graphics.UiRenderTargetPool.Return(_staticSurface);
            _staticSurface = Client.Main.Graphics.UiRenderTargetPool.Rent(gd, ControlSize.X, ControlSize.Y);

            var prev = gd.GetRenderTargets();
            gd.SetRenderTarget(_staticSurface);
            gd.Clear(Color.Transparent);

            var sb = GraphicsManager.Instance.Sprite;
            using (new SpriteBatchScope(sb, SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp))
            {
                DrawStaticElements(sb);
            }

            gd.SetRenderTargets(prev);
            _staticSurfaceDirty = false;
        }

        private void InvalidateStaticSurface() => _staticSurfaceDirty = true;

        private void DrawStaticElements(SpriteBatch sb)
        {
            DrawWindowBackground(sb, _panelRect);
            DrawHeader(sb);
        }

        private void DrawWindowBackground(SpriteBatch sb, Rectangle rect)
        {
            var pixel = GraphicsManager.Instance.Pixel;
            if (pixel == null) return;

            sb.Draw(pixel, rect, Theme.BorderOuter);

            var inner = new Rectangle(rect.X + 2, rect.Y + 2, rect.Width - 4, rect.Height - 4);
            UiDrawHelper.DrawVerticalGradient(sb, inner, Theme.BgDark, Theme.BgDarkest);

            sb.Draw(pixel, new Rectangle(inner.X, inner.Y, inner.Width, 1), Theme.BorderInner * 0.5f);
            UiDrawHelper.DrawCornerAccents(sb, rect, Theme.Accent * 0.35f, size: 10, thickness: 2);
        }

        private void DrawHeader(SpriteBatch sb)
        {
            var pixel = GraphicsManager.Instance.Pixel;
            if (pixel == null) return;

            UiDrawHelper.DrawPanel(sb, _headerRect, Theme.BgMid,
                Theme.BorderInner, Theme.BorderOuter, Theme.BorderHighlight * 0.3f);

            sb.Draw(pixel, new Rectangle(_headerRect.X + 8, _headerRect.Y + 4, _headerRect.Width - 16, 2), Theme.Accent * 0.7f);
        }

        private void DrawDynamicContent(SpriteBatch sb)
        {
            if (_font == null || string.IsNullOrEmpty(_messageTemplate)) return;

            int remaining = Math.Max(0, (int)Math.Ceiling(COUNTDOWN_SECONDS - (_latestTotalSeconds - _startTimeSeconds)));
            string message = string.Format(_messageTemplate, remaining);

            float scale = FONT_SCALE;
            Vector2 size = _font.MeasureString(message) * scale;

            float x = (ControlSize.X - size.X) / 2f;
            float y = (ControlSize.Y - size.Y) / 2f + 6f;

            Vector2 pos = Translate(new Vector2(x, y));
            DrawTextWithShadow(sb, message, pos.X, pos.Y, scale, Theme.TextWhite);

            string subtitle = remaining <= 10 ? "Hurry!" : string.Empty;
            if (!string.IsNullOrEmpty(subtitle))
            {
                Vector2 subSize = _font.MeasureString(subtitle) * FONT_SCALE_SMALL;
                float subX = (ControlSize.X - subSize.X) / 2f;
                float subY = y + size.Y + 4f;
                Vector2 subPos = Translate(new Vector2(subX, subY));
                DrawTextWithShadow(sb, subtitle, subPos.X, subPos.Y, FONT_SCALE_SMALL, Theme.Warning);
            }
        }

        private void DrawTextWithShadow(SpriteBatch sb, string text, float x, float y, float scale, Color color)
        {
            sb.DrawString(_font, text, new Vector2(x + 1, y + 1), Color.Black * 0.6f, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
            sb.DrawString(_font, text, new Vector2(x, y), color, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        }

        private Rectangle Translate(Rectangle rect)
            => new(DisplayRectangle.X + rect.X, DisplayRectangle.Y + rect.Y, rect.Width, rect.Height);

        private Vector2 Translate(Vector2 pos)
            => new(pos.X + DisplayRectangle.X, pos.Y + DisplayRectangle.Y);
    }
}
