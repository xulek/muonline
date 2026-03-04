#nullable enable
using System;
using Client.Main.Controls.UI.Common;
using Client.Main.Controls.UI.Game.Common;
using Client.Main.Core.Client;
using Client.Main.Core.Utilities;
using Client.Main.Controllers;
using Client.Main.Helpers;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Controls.UI.Game.Map
{
    /// <summary>
    /// Compact single-line location bar showing map name and coordinates.
    /// </summary>
    public sealed class CurrentLocationControl : UIControl
    {
        private readonly CharacterState _characterState;

        private Point _lastVirtualSize = Point.Zero;
        private SpriteFont? _font;
        private string _mapName = string.Empty;

        private float _mapScale = 0.55f;
        private float _coordsScale = 0.48f;

        private const int BaseX = 12;
        private const int BaseY = 10;
        private const int BaseWidth = 280;
        private const int BaseHeight = 28;
        private const int PadX = 10;
        private const int PadY = 5;

        public CurrentLocationControl(CharacterState characterState)
        {
            _characterState = characterState;

            AutoViewSize = false;
            Interactive = false;
            BackgroundColor = Color.Transparent;
            BorderColor = Color.Transparent;
            BorderThickness = 0;

            RefreshLayout();
            RefreshData();
        }

        public Point GetBuffAnchor(int gap)
        {
            var rect = DisplayRectangle;
            return new Point(rect.Right + gap, rect.Y);
        }

        protected override void OnScreenSizeChanged()
        {
            base.OnScreenSizeChanged();
            _lastVirtualSize = Point.Zero;
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
            RefreshLayout();
            RefreshData();
        }

        public override void Draw(GameTime gameTime)
        {
            if (Status != GameControlStatus.Ready || !Visible)
            {
                return;
            }

            var spriteBatch = GraphicsManager.Instance.Sprite;
            if (spriteBatch == null)
            {
                return;
            }

            SpriteBatchScope? scope = null;
            if (!SpriteBatchScope.BatchIsBegun)
            {
                scope = new SpriteBatchScope(
                    spriteBatch,
                    SpriteSortMode.Deferred,
                    BlendState.AlphaBlend,
                    SamplerState.LinearClamp,
                    transform: UiScaler.SpriteTransform);
            }

            try
            {
                _font ??= GraphicsManager.Instance.Font;
                if (_font == null)
                {
                    return;
                }

                DrawBar(spriteBatch);
            }
            finally
            {
                scope?.Dispose();
            }
        }

        private void RefreshLayout()
        {
            Point virtualSize = UiScaler.VirtualSize;
            if (virtualSize == _lastVirtualSize)
            {
                return;
            }

            _lastVirtualSize = virtualSize;

            float scaleX = virtualSize.X / 1024f;
            float scaleY = virtualSize.Y / 768f;
            float scale = Math.Clamp(MathF.Min(scaleX, scaleY), 0.82f, 1.35f);

            X = ScaleValue(BaseX, scale);
            Y = ScaleValue(BaseY, scale);

            int width = ScaleValue(BaseWidth, scale);
            int height = ScaleValue(BaseHeight, scale);
            ControlSize = new Point(width, height);
            ViewSize = ControlSize;

            _mapScale = Math.Clamp(0.54f * scale, 0.46f, 0.72f);
            _coordsScale = Math.Clamp(0.47f * scale, 0.40f, 0.62f);
        }

        private void RefreshData()
        {
            _mapName = MapDatabase.GetMapName(_characterState.MapId);
        }

        private void DrawBar(SpriteBatch spriteBatch)
        {
            var pixel = GraphicsManager.Instance.Pixel;
            if (pixel == null)
            {
                return;
            }

            Rectangle rect = DisplayRectangle;

            spriteBatch.Draw(pixel, rect, ModernHudTheme.BorderOuter);

            var inner = new Rectangle(rect.X + 1, rect.Y + 1,
                Math.Max(1, rect.Width - 2), Math.Max(1, rect.Height - 2));
            UiDrawHelper.DrawVerticalGradient(spriteBatch, inner,
                ModernHudTheme.BgDark, ModernHudTheme.BgDarkest);

            spriteBatch.Draw(pixel,
                new Rectangle(inner.X + 1, inner.Y, Math.Max(1, inner.Width - 2), 1),
                ModernHudTheme.Accent * 0.6f * Alpha);

            spriteBatch.Draw(pixel,
                new Rectangle(inner.X, inner.Y + 1, inner.Width, 1),
                ModernHudTheme.BorderInner * 0.3f * Alpha);

            float wScale = Math.Max(0.82f, ViewSize.X / (float)BaseWidth);
            int padX = ScaleValue(PadX, wScale);

            string coords = $"X:{_characterState.PositionX}  Y:{_characterState.PositionY}";
            Vector2 coordsSize = _font!.MeasureString(coords) * _coordsScale;
            float coordsX = rect.Right - padX - coordsSize.X;
            float coordsY = rect.Y + (rect.Height - coordsSize.Y) / 2f;

            spriteBatch.DrawString(_font, coords, new Vector2(coordsX + 1, coordsY + 1),
                Color.Black * 0.6f * Alpha, 0f, Vector2.Zero, _coordsScale, SpriteEffects.None, 0f);
            spriteBatch.DrawString(_font, coords, new Vector2(coordsX, coordsY),
                ModernHudTheme.TextGray * Alpha, 0f, Vector2.Zero, _coordsScale, SpriteEffects.None, 0f);

            int separatorGap = ScaleValue(6, wScale);
            int mapMaxWidth = Math.Max(1, (int)(coordsX - rect.X - padX - separatorGap));
            string clippedMap = ClipTextWithEllipsis(_font, _mapName, _mapScale, mapMaxWidth);

            if (!string.IsNullOrEmpty(clippedMap))
            {
                Vector2 mapSize = _font.MeasureString(clippedMap) * _mapScale;
                float mapX = rect.X + padX;
                float mapY = rect.Y + (rect.Height - mapSize.Y) / 2f;

                spriteBatch.DrawString(_font, clippedMap, new Vector2(mapX + 1, mapY + 1),
                    Color.Black * 0.6f * Alpha, 0f, Vector2.Zero, _mapScale, SpriteEffects.None, 0f);
                spriteBatch.DrawString(_font, clippedMap, new Vector2(mapX, mapY),
                    ModernHudTheme.TextGold * Alpha, 0f, Vector2.Zero, _mapScale, SpriteEffects.None, 0f);
            }

            UiDrawHelper.DrawCornerAccents(spriteBatch, rect,
                ModernHudTheme.Accent * 0.3f * Alpha, size: 6, thickness: 1);
        }

        private static int ScaleValue(int value, float scale)
        {
            return Math.Max(1, (int)MathF.Round(value * scale));
        }

        private static string ClipTextWithEllipsis(SpriteFont font, string text, float scale, int maxWidth)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            if (font.MeasureString(text).X * scale <= maxWidth)
            {
                return text;
            }

            const string ellipsis = "...";
            if (font.MeasureString(ellipsis).X * scale > maxWidth)
            {
                return string.Empty;
            }

            int left = 0;
            int right = text.Length;
            while (left < right)
            {
                int mid = (left + right + 1) / 2;
                string probe = text[..mid] + ellipsis;
                if (font.MeasureString(probe).X * scale <= maxWidth)
                {
                    left = mid;
                }
                else
                {
                    right = mid - 1;
                }
            }

            return left > 0 ? text[..left] + ellipsis : ellipsis;
        }
    }
}
