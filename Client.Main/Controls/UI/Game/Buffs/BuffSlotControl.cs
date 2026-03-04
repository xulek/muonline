#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Content;
using Client.Main.Controls.UI.Game.Common;
using Client.Main.Core.Client;
using Client.Main.Controllers;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Controls.UI.Game.Buffs
{
    /// <summary>
    /// Single visual buff slot with framed background and centered icon.
    /// </summary>
    public sealed class BuffSlotControl : UIControl
    {
        private ActiveBuffState? _buff;
        private Texture2D? _iconTexture;
        private Rectangle _iconSource;

        public static readonly int DefaultSlotWidth = BuffIconAtlas.IconWidth + 4;
        public static readonly int DefaultSlotHeight = BuffIconAtlas.IconHeight + 4;

        public ActiveBuffState? Buff
        {
            get => _buff;
            set
            {
                _buff = value;
                RefreshIconTexture();
            }
        }

        public BuffSlotControl()
        {
            AutoViewSize = false;
            Interactive = false;
            BackgroundColor = Color.Transparent;
            BorderColor = Color.Transparent;
            BorderThickness = 0;

            ControlSize = new Point(DefaultSlotWidth, DefaultSlotHeight);
            ViewSize = ControlSize;
        }

        public void SetSlotSize(int width, int height)
        {
            width = Math.Max(BuffIconAtlas.IconWidth + 2, width);
            height = Math.Max(BuffIconAtlas.IconHeight + 2, height);

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
            int innerHeight = Math.Max(1, rect.Height - 2);

            float fitScale = MathF.Min(
                innerWidth / (float)BuffIconAtlas.IconWidth,
                innerHeight / (float)BuffIconAtlas.IconHeight);

            int drawWidth = Math.Max(1, (int)MathF.Round(BuffIconAtlas.IconWidth * fitScale));
            int drawHeight = Math.Max(1, (int)MathF.Round(BuffIconAtlas.IconHeight * fitScale));

            var iconRect = new Rectangle(
                rect.X + (rect.Width - drawWidth) / 2,
                rect.Y + (rect.Height - drawHeight) / 2,
                drawWidth,
                drawHeight);

            spriteBatch.Draw(_iconTexture!, iconRect, _iconSource, Color.White * Alpha);
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
