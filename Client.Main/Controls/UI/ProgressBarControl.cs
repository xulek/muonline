using System;
using Client.Main.Models;
using Microsoft.Xna.Framework;

namespace Client.Main.Controls.UI
{
    /// <summary>Texture-based bar for HUD gauges (HP/MP orbs); fills a part of its texture by <see cref="Percentage"/>.</summary>
    public class ProgressBarControl : TextureControl
    {
        public float Percentage { get; set; } = 1f;
        public bool Vertical { get; set; }
        public bool Inverse { get; set; }

        public ProgressBarControl()
        {
            AutoViewSize = false;
            Interactive = false;
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            if (!Visible || Texture == null || Texture.IsDisposed)
            {
                if (ViewSize != Point.Zero) ViewSize = Point.Zero;
                if (TextureRectangle != Rectangle.Empty) TextureRectangle = Rectangle.Empty;
                return;
            }

            float clampedPercentage = MathHelper.Clamp(Percentage, 0f, 1f);
            int sourceWidth = Texture.Width;
            int sourceHeight = Texture.Height;

            if (!Vertical)
            {
                int calculatedWidth = (int)(sourceWidth * clampedPercentage);
                var currentTextureRect = Inverse
                    ? new Rectangle(sourceWidth - calculatedWidth, 0, calculatedWidth, sourceHeight)
                    : new Rectangle(0, 0, calculatedWidth, sourceHeight);

                TextureRectangle = Rectangle.Intersect(currentTextureRect, Texture.Bounds);
                ViewSize = new Point((int)(TextureRectangle.Width * Scale), (int)(sourceHeight * Scale));
            }
            else
            {
                int calculatedHeight = (int)(sourceHeight * clampedPercentage);
                var currentTextureRect = Inverse
                    ? new Rectangle(0, sourceHeight - calculatedHeight, sourceWidth, calculatedHeight)
                    : new Rectangle(0, 0, sourceWidth, calculatedHeight);

                TextureRectangle = Rectangle.Intersect(currentTextureRect, Texture.Bounds);
                ViewSize = new Point((int)(sourceWidth * Scale), (int)(TextureRectangle.Height * Scale));
            }

            ViewSize = new Point(Math.Max(0, ViewSize.X), Math.Max(0, ViewSize.Y));
        }

        public override void Draw(GameTime gameTime)
        {
            if (Status != GameControlStatus.Ready || !Visible)
                return;

            base.Draw(gameTime);
        }
    }
}
