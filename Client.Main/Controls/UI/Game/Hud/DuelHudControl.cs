#nullable enable
using Client.Main.Controls.UI.Common;
using Client.Main.Controls.UI.Game.Common;
using Client.Main.Controllers;
using Client.Main.Core.Client;
using Client.Main.Helpers;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace Client.Main.Controls.UI.Game.Hud
{
    public sealed class DuelHudControl : UIControl
    {
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

        private const int WIDTH = 320;
        private const int HEIGHT = 82;
        private const int HEADER_HEIGHT = 26;
        private const int PANEL_PADDING = 10;

        private readonly CharacterState _characterState;
        private SpriteFont? _font;

        private RenderTarget2D? _staticSurface;
        private bool _staticSurfaceDirty = true;

        public DuelHudControl(CharacterState characterState)
        {
            _characterState = characterState ?? throw new ArgumentNullException(nameof(characterState));

            Align = ControlAlign.Top | ControlAlign.HorizontalCenter;
            Margin = new Margin { Top = 8 };
            AutoViewSize = false;
            ViewSize = new Point(WIDTH, HEIGHT);
            ControlSize = ViewSize;
            Interactive = false;
            BackgroundColor = Color.Transparent;
            BorderColor = Color.Transparent;
            Visible = false;

            _characterState.DuelStateChanged += OnDuelStateChanged;
        }

        public override void Dispose()
        {
            _characterState.DuelStateChanged -= OnDuelStateChanged;
            Client.Main.Graphics.UiRenderTargetPool.Return(_staticSurface);
            _staticSurface = null;
            base.Dispose();
        }

        private void OnDuelStateChanged()
        {
            MuGame.ScheduleOnMainThread(() =>
            {
                bool shouldBeVisible = _characterState.IsDuelActive;
                if (Visible != shouldBeVisible)
                {
                    Visible = shouldBeVisible;
                }
            });
        }

        protected override void OnScreenSizeChanged()
        {
            base.OnScreenSizeChanged();
            InvalidateStaticSurface();
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
            Visible = _characterState.IsDuelActive;
        }

        public override void Draw(GameTime gameTime)
        {
            if (!Visible) return;
            if (Status != GameControlStatus.Ready) return;

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
                _font ??= GraphicsManager.Instance.Font;

                if (_staticSurface != null && !_staticSurface.IsDisposed)
                {
                    spriteBatch.Draw(_staticSurface, DisplayRectangle, Color.White * Alpha);
                }

                DrawDynamic(spriteBatch);
            }
            finally
            {
                scope?.Dispose();
            }
        }

        private void EnsureStaticSurface()
        {
            if (!_staticSurfaceDirty && _staticSurface != null && !_staticSurface.IsDisposed)
            {
                return;
            }

            var gd = GraphicsManager.Instance.GraphicsDevice;

            Client.Main.Graphics.UiRenderTargetPool.Return(_staticSurface);
            _staticSurface = Client.Main.Graphics.UiRenderTargetPool.Rent(gd, WIDTH, HEIGHT);

            var previousTargets = gd.GetRenderTargets();
            gd.SetRenderTarget(_staticSurface);
            gd.Clear(Color.Transparent);

            var spriteBatch = GraphicsManager.Instance.Sprite;
            using (new SpriteBatchScope(spriteBatch, SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp))
            {
                DrawStatic(spriteBatch);
            }

            gd.SetRenderTargets(previousTargets);
            _staticSurfaceDirty = false;
        }

        private void InvalidateStaticSurface() => _staticSurfaceDirty = true;

        private void DrawStatic(SpriteBatch sb)
        {
            var pixel = GraphicsManager.Instance.Pixel;
            if (pixel == null)
            {
                return;
            }

            var fullRect = new Rectangle(0, 0, WIDTH, HEIGHT);
            DrawWindowBackground(sb, fullRect);

            var headerRect = new Rectangle(8, 6, WIDTH - 16, HEADER_HEIGHT);
            DrawPanel(sb, headerRect, Theme.BgMid, withGlow: true);

            sb.Draw(pixel, new Rectangle(18, 8, WIDTH - 36, 2), Theme.Accent * 0.85f);
            sb.Draw(pixel, new Rectangle(28, 10, WIDTH - 56, 1), Theme.AccentDim * 0.45f);

            if (_font != null)
            {
                const string title = "DUEL";
                float titleScale = 0.45f;
                Vector2 size = _font.MeasureString(title) * titleScale;
                Vector2 pos = new((WIDTH - size.X) / 2, headerRect.Y + (headerRect.Height - size.Y) / 2f);

                sb.Draw(pixel, new Rectangle((int)pos.X - 18, (int)pos.Y - 3, (int)size.X + 36, (int)size.Y + 6),
                    Theme.AccentGlow * 0.35f);

                sb.DrawString(_font, title, pos + new Vector2(2, 2), Color.Black * 0.55f, 0f, Vector2.Zero, titleScale, SpriteEffects.None, 0f);
                sb.DrawString(_font, title, pos, Theme.TextWhite, 0f, Vector2.Zero, titleScale, SpriteEffects.None, 0f);
            }

            var contentRect = new Rectangle(8, headerRect.Bottom + 6, WIDTH - 16, HEIGHT - headerRect.Bottom - 12);
            DrawPanel(sb, contentRect, Theme.BgDark);
        }

        private void DrawDynamic(SpriteBatch sb)
        {
            if (GraphicsManager.Instance.Pixel == null || _font == null)
            {
                return;
            }

            string p1 = _characterState.GetDuelPlayerName(CharacterState.DuelPlayerType.Hero);
            string p2 = _characterState.GetDuelPlayerName(CharacterState.DuelPlayerType.Enemy);
            int s1 = _characterState.GetDuelScore(CharacterState.DuelPlayerType.Hero);
            int s2 = _characterState.GetDuelScore(CharacterState.DuelPlayerType.Enemy);

            float hp1 = MathHelper.Clamp(_characterState.GetDuelHpRate(CharacterState.DuelPlayerType.Hero), 0, 1);
            float hp2 = MathHelper.Clamp(_characterState.GetDuelHpRate(CharacterState.DuelPlayerType.Enemy), 0, 1);
            float sd1 = MathHelper.Clamp(_characterState.GetDuelSdRate(CharacterState.DuelPlayerType.Hero), 0, 1);
            float sd2 = MathHelper.Clamp(_characterState.GetDuelSdRate(CharacterState.DuelPlayerType.Enemy), 0, 1);

            int x = DisplayRectangle.X + PANEL_PADDING;
            int y = DisplayRectangle.Y + 6 + HEADER_HEIGHT + 12;
            int innerW = WIDTH - PANEL_PADDING * 2;

            DrawNamesAndScore(sb, p1, p2, s1, s2, x, y, innerW);

            int barsY = y + 18;
            DrawBars(sb, x, barsY, innerW, hp1, sd1, hp2, sd2);
        }

        private void DrawNamesAndScore(SpriteBatch sb, string p1, string p2, int s1, int s2, int x, int y, int width)
        {
            float nameScale = 0.30f;
            float scoreScale = 0.38f;

            string left = string.IsNullOrWhiteSpace(p1) ? "Player" : p1;
            string right = string.IsNullOrWhiteSpace(p2) ? "Opponent" : p2;
            string score = $"{s1} : {s2}";

            Vector2 leftSize = _font!.MeasureString(left) * nameScale;
            Vector2 rightSize = _font.MeasureString(right) * nameScale;
            Vector2 scoreSize = _font.MeasureString(score) * scoreScale;

            Vector2 leftPos = new(x, y);
            Vector2 rightPos = new(x + width - rightSize.X, y);
            Vector2 scorePos = new(x + (width - scoreSize.X) / 2, y - 2);

            DrawText(sb, left, leftPos, Theme.TextGold, nameScale);
            DrawText(sb, right, rightPos, Theme.TextGold, nameScale);

            DrawText(sb, score, scorePos, Theme.AccentBright, scoreScale);
        }

        private void DrawBars(SpriteBatch sb, int x, int y, int width, float hp1, float sd1, float hp2, float sd2)
        {
            var pixel = Controllers.GraphicsManager.Instance.Pixel;

            int barW = (width - 10) / 2;
            int barH = 10;

            var leftRect = new Rectangle(x, y, barW, barH);
            var rightRect = new Rectangle(x + width - barW, y, barW, barH);

            DrawDualBar(sb, leftRect, hp1, sd1, mirror: false);
            DrawDualBar(sb, rightRect, hp2, sd2, mirror: true);

            // subtle divider
            sb.Draw(pixel, new Rectangle(x + (width / 2) - 1, y - 1, 2, barH + 2), Theme.BorderOuter * 0.9f);
        }

        private void DrawDualBar(SpriteBatch sb, Rectangle rect, float hp, float sd, bool mirror)
        {
            var pixel = GraphicsManager.Instance.Pixel;
            UiDrawHelper.DrawPanel(sb, rect, Theme.SlotBg, Theme.BorderInner * 0.6f, Theme.BorderOuter, Theme.BorderHighlight * 0.25f);

            int fillW = Math.Max(0, rect.Width - 4);
            int fillH = Math.Max(0, rect.Height - 4);
            var fillRect = new Rectangle(rect.X + 2, rect.Y + 2, fillW, fillH);

            int hpW = (int)Math.Round(fillW * hp);
            int sdW = (int)Math.Round(fillW * sd);

            if (mirror)
            {
                var hpRect = new Rectangle(fillRect.Right - hpW, fillRect.Y, hpW, fillH);
                var sdRect = new Rectangle(fillRect.Right - sdW, fillRect.Y, sdW, fillH);
                sb.Draw(pixel, sdRect, Theme.SecondaryDim * 0.55f);
                sb.Draw(pixel, hpRect, Theme.Danger * 0.75f);
            }
            else
            {
                var hpRect = new Rectangle(fillRect.X, fillRect.Y, hpW, fillH);
                var sdRect = new Rectangle(fillRect.X, fillRect.Y, sdW, fillH);
                sb.Draw(pixel, sdRect, Theme.SecondaryDim * 0.55f);
                sb.Draw(pixel, hpRect, Theme.Danger * 0.75f);
            }
        }

        private static void DrawWindowBackground(SpriteBatch sb, Rectangle rect)
        {
            var pixel = GraphicsManager.Instance.Pixel;
            if (pixel == null)
            {
                return;
            }

            sb.Draw(pixel, rect, Theme.BorderOuter);

            var inner = new Rectangle(rect.X + 2, rect.Y + 2, rect.Width - 4, rect.Height - 4);
            UiDrawHelper.DrawVerticalGradient(sb, inner, Theme.BgDark, Theme.BgDarkest);

            sb.Draw(pixel, new Rectangle(inner.X, inner.Y, inner.Width, 1), Theme.BorderInner * 0.5f);
            sb.Draw(pixel, new Rectangle(inner.X, inner.Y, 1, inner.Height), Theme.BorderInner * 0.3f);

            UiDrawHelper.DrawCornerAccents(sb, rect, Theme.Accent * 0.35f, size: 10, thickness: 2);
        }

        private static void DrawPanel(SpriteBatch sb, Rectangle rect, Color bgColor, bool withGlow = false)
        {
            UiDrawHelper.DrawPanel(sb, rect, bgColor,
                Theme.BorderInner * 0.8f,
                Theme.BorderOuter,
                Theme.BorderHighlight * 0.25f,
                withGlow,
                withGlow ? Theme.AccentGlow * 0.55f : (Color?)null);
        }

        private static void DrawText(SpriteBatch sb, string text, Vector2 pos, Color color, float scale)
        {
            var font = GraphicsManager.Instance.Font;
            if (font == null)
            {
                return;
            }

            sb.DrawString(font, text, pos + new Vector2(1, 1), Color.Black * 0.6f, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
            sb.DrawString(font, text, pos, color, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        }
    }
}
