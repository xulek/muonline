#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Content;
using Client.Main.Controls.UI.Common;
using Client.Main.Controls.UI.Game.Common;
using Client.Main.Core.Client;
using Client.Main.Controllers;
using Client.Main.Helpers;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Controls.UI.Game.Buffs
{
    /// <summary>
    /// Single visual buff slot with framed background, centered icon, time bar, and tooltip.
    /// </summary>
    public sealed class BuffSlotControl : UIControl
    {
        private ActiveBuffState? _buff;
        private Texture2D? _iconTexture;
        private Rectangle _iconSource;
        private bool _showTooltip;

        public static readonly int DefaultSlotWidth = BuffIconAtlas.IconWidth + 4;
        public static readonly int DefaultSlotHeight = BuffIconAtlas.IconHeight + 6;

        public ActiveBuffState? Buff
        {
            get => _buff;
            set
            {
                _buff = value;
                _showTooltip = false;
                RefreshIconTexture();
            }
        }

        public BuffSlotControl()
        {
            AutoViewSize = false;
            Interactive = true;
            BackgroundColor = Color.Transparent;
            BorderColor = Color.Transparent;
            BorderThickness = 0;

            ControlSize = new Point(DefaultSlotWidth, DefaultSlotHeight);
            ViewSize = ControlSize;
        }

        public void SetSlotSize(int width, int height)
        {
            width = Math.Max(BuffIconAtlas.IconWidth + 2, width);
            height = Math.Max(BuffIconAtlas.IconHeight + 6, height);

            if (ControlSize.X == width && ControlSize.Y == height)
            {
                return;
            }

            ControlSize = new Point(width, height);
            ViewSize = ControlSize;
        }

        public override async Task Load()
        {
            foreach (string texturePath in BuffIconAtlas.TexturePaths)
            {
                await TextureLoader.Instance.Prepare(texturePath);
            }

            await base.Load();
            RefreshIconTexture();
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
            _showTooltip = IsMouseOver && _buff != null;
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

            DrawSlotFrame(spriteBatch);

            if (_buff == null)
            {
                return;
            }

            if (_iconTexture == null || _iconTexture.IsDisposed)
            {
                RefreshIconTexture();
            }

            if (_iconTexture == null || _iconTexture.IsDisposed)
            {
                return;
            }

            DrawIcon(spriteBatch);
            DrawTimeBar(spriteBatch);
        }

        public override void DrawAfter(GameTime gameTime)
        {
            base.DrawAfter(gameTime);

            if (!Visible || !_showTooltip || _buff == null)
                return;

            var spriteBatch = GraphicsManager.Instance.Sprite;
            var pixel = GraphicsManager.Instance.Pixel;
            var font = GraphicsManager.Instance.Font;
            if (spriteBatch == null || pixel == null || font == null)
                return;

            string name = string.IsNullOrEmpty(_buff.Name)
                ? BuffManager.GetBuffName((BuffEffectId)_buff.EffectId)
                : _buff.Name;
            string timeText = FormatBuffTime(_buff);
            string tooltip = string.IsNullOrWhiteSpace(_buff.ValueText)
                ? $"{name}\n{timeText}"
                : $"{name}\n{_buff.ValueText}\n{timeText}";

            float scale = 0.52f;
            Vector2 textSize = font.MeasureString(tooltip) * scale;
            int padding = 6;
            int tooltipWidth = (int)textSize.X + padding * 2;
            int tooltipHeight = (int)textSize.Y + padding * 2 + 2;

            Point mouse = MuGame.Instance.UiMouseState.Position;
            int x = Math.Clamp(mouse.X + 12, 2, Math.Max(2, UiScaler.VirtualSize.X - tooltipWidth - 2));
            int y = Math.Clamp(mouse.Y + 12, 2, Math.Max(2, UiScaler.VirtualSize.Y - tooltipHeight - 2));

            var tooltipRect = new Rectangle(x, y, tooltipWidth, tooltipHeight);
            if (SpriteBatchScope.BatchIsBegun)
            {
                DrawTooltip(spriteBatch, pixel, font, tooltipRect, tooltip, padding, scale);
            }
            else
            {
                using (new SpriteBatchScope(
                    spriteBatch,
                    SpriteSortMode.Deferred,
                    BlendState.AlphaBlend,
                    SamplerState.LinearClamp,
                    DepthStencilState.None,
                    RasterizerState.CullNone,
                    transform: UiScaler.SpriteTransform))
                {
                    DrawTooltip(spriteBatch, pixel, font, tooltipRect, tooltip, padding, scale);
                }
            }
        }

        private static void DrawTooltip(
            SpriteBatch spriteBatch,
            Texture2D pixel,
            SpriteFont font,
            Rectangle tooltipRect,
            string tooltip,
            int padding,
            float scale)
        {
            spriteBatch.Draw(pixel, tooltipRect, ModernHudTheme.BorderOuter);
            var inner = new Rectangle(tooltipRect.X + 1, tooltipRect.Y + 1,
                Math.Max(1, tooltipRect.Width - 2), Math.Max(1, tooltipRect.Height - 2));
            UiDrawHelper.DrawVerticalGradient(spriteBatch, inner,
                new Color(20, 24, 32, 248), new Color(11, 13, 18, 255));

            spriteBatch.DrawString(font, tooltip,
                new Vector2(inner.X + padding, inner.Y + padding),
                ModernHudTheme.TextWhite, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        }

        private void DrawSlotFrame(SpriteBatch spriteBatch)
        {
            var pixel = GraphicsManager.Instance.Pixel;
            if (pixel == null)
            {
                return;
            }

            Rectangle rect = DisplayRectangle;

            spriteBatch.Draw(pixel, rect, ModernHudTheme.BorderOuter * Alpha);

            Rectangle inner = new(rect.X + 1, rect.Y + 1, Math.Max(1, rect.Width - 2), Math.Max(1, rect.Height - 2));
            spriteBatch.Draw(pixel, inner, ModernHudTheme.SlotBg * Alpha);
        }

        private void DrawIcon(SpriteBatch spriteBatch)
        {
            Rectangle rect = DisplayRectangle;

            int innerWidth = Math.Max(1, rect.Width - 2);
            int innerHeight = Math.Max(1, rect.Height - 6); // leave space for time bar

            float fitScale = MathF.Min(
                innerWidth / (float)BuffIconAtlas.IconWidth,
                innerHeight / (float)BuffIconAtlas.IconHeight);

            int drawWidth = Math.Max(1, (int)MathF.Round(BuffIconAtlas.IconWidth * fitScale));
            int drawHeight = Math.Max(1, (int)MathF.Round(BuffIconAtlas.IconHeight * fitScale));

            var iconRect = new Rectangle(
                rect.X + (rect.Width - drawWidth) / 2,
                rect.Y + 1 + (innerHeight - drawHeight) / 2,
                drawWidth,
                drawHeight);

            spriteBatch.Draw(_iconTexture!, iconRect, _iconSource, Color.White * Alpha);
        }

        /// <summary>
        /// Draws a thin colored bar at the bottom of the slot showing buff age.
        /// Full = just applied, shrinking = aging.
        /// </summary>
        private static string FormatBuffTime(ActiveBuffState buff)
        {
            var remaining = buff.GetRemainingTime(DateTime.UtcNow);
            if (remaining.HasValue)
            {
                var value = remaining.Value;
                string approx = buff.DurationFromServer ? string.Empty : "~";
                if (!buff.DurationFromServer && value <= TimeSpan.Zero)
                    return "Ending soon";
                return value.TotalSeconds < 60
                    ? $"{approx}{Math.Ceiling(value.TotalSeconds)}s remaining"
                    : $"{approx}{(int)value.TotalMinutes}m {value.Seconds:00}s remaining";
            }

            var elapsed = DateTime.UtcNow - buff.ActivatedAt;
            return elapsed.TotalSeconds < 60
                ? $"{(int)elapsed.TotalSeconds}s active"
                : $"{(int)elapsed.TotalMinutes}m active";
        }

        private void DrawTimeBar(SpriteBatch spriteBatch)
        {
            var pixel = GraphicsManager.Instance.Pixel;
            if (pixel == null || _buff == null) return;

            Rectangle rect = DisplayRectangle;
            int barHeight = 3;
            var barRect = new Rectangle(rect.X + 2, rect.Bottom - barHeight - 2,
                Math.Max(1, rect.Width - 4), barHeight);

            // Color based on buff type
            Color barColor = _buff.EffectId switch
            {
                1 => new Color(255, 60, 40),   // Greater Damage - red
                2 => new Color(40, 200, 60),   // Greater Defense - green
                4 => new Color(40, 80, 255),   // Soul Barrier - blue
                8 => new Color(255, 200, 40),  // Swell Life - gold
                5 => new Color(255, 150, 40),  // Critical Damage - orange
                81 => new Color(200, 60, 255), // Berserker - purple
                _ => new Color(160, 160, 160)  // default gray
            };

            spriteBatch.Draw(pixel, barRect, ModernHudTheme.BorderOuter * Alpha);
            float ratio = 1f;
            var remaining = _buff.GetRemainingTime(DateTime.UtcNow);
            if (remaining.HasValue && _buff.Duration.HasValue && _buff.Duration.Value.TotalSeconds > 0)
                ratio = MathHelper.Clamp((float)(remaining.Value.TotalSeconds / _buff.Duration.Value.TotalSeconds), 0f, 1f);

            int innerWidth = Math.Max(1, (int)MathF.Round((barRect.Width - 2) * ratio));
            var innerBar = new Rectangle(barRect.X + 1, barRect.Y + 1,
                innerWidth, Math.Max(1, barRect.Height - 2));
            spriteBatch.Draw(pixel, innerBar, barColor * Alpha);
        }

        private void RefreshIconTexture()
        {
            _iconTexture = null;

            if (_buff == null)
            {
                return;
            }

            if (!BuffIconAtlas.TryResolve(_buff.EffectId, out var frame))
            {
                return;
            }

            _iconTexture = TextureLoader.Instance.GetTexture2D(frame.TexturePath);
            _iconSource = frame.SourceRectangle;
        }
    }
}
