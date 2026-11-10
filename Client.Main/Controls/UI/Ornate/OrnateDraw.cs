using System;
using Client.Main.Controllers;
using Client.Main.Controls.UI.Common;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Controls.UI.Ornate
{
    /// <summary>
    /// Low-level drawing primitives of the ornate UI kit: text, window frames, insets, title plaques, dividers and
    /// stretchable plates. All colours are multiplied by <c>alpha</c> (premultiplied blending), and every primitive
    /// falls back to flat colours while its texture is not loaded.
    /// </summary>
    public static class OrnateDraw
    {
        private static Texture2D Pixel => GraphicsManager.Instance.Pixel;

        // --- Text ---------------------------------------------------------------------------------------

        public static Vector2 Measure(string text, float size)
        {
            SpriteFont font = GraphicsManager.GetUiFont(size, out float scale);
            return font == null || string.IsNullOrEmpty(text) ? Vector2.Zero : font.MeasureString(text) * scale;
        }

        public static void Text(SpriteBatch sb, string text, Vector2 position, float size, Color color,
            float alpha = 1f, bool bold = false, bool shadow = true)
        {
            if (string.IsNullOrEmpty(text))
                return;

            SpriteFont font = GraphicsManager.GetUiFont(size, out float scale);
            if (font == null)
                return;

            // Whole screen pixels (the UI transform scales virtual units): text between pixels is filtered soft.
            position = UiScaler.SnapToPixel(position);
            float pixel = UiScaler.InverseScale;
            if (shadow)
                sb.DrawString(font, text, position + new Vector2(pixel, pixel), Color.Black * (0.85f * alpha), 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
            sb.DrawString(font, text, position, color * alpha, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
            if (bold)
                sb.DrawString(font, text, position + new Vector2(pixel, 0f), color * (0.85f * alpha), 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        }

        /// <summary>
        /// Largest font size from <paramref name="size"/> down to <paramref name="minSize"/> (in half-point steps) at
        /// which <paramref name="text"/> is at most <paramref name="maxWidth"/> wide.
        /// </summary>
        public static float FitSize(string text, float size, float maxWidth, float minSize = 10f)
        {
            while (size > minSize && Measure(text, size).X > maxWidth)
                size -= 0.5f;
            return size;
        }

        /// <summary>Part of a plate between its end caps, where a label can go without covering the ornaments.</summary>
        public static Rectangle PlateInner(Texture2D tex, PlateLayout layout, Rectangle plate)
        {
            if (tex == null)
                return new Rectangle(plate.X + 6, plate.Y, plate.Width - 12, plate.Height);

            PlateGeometry g = Measure(tex, layout, plate);
            // The caps hold the pointed ends and gems; keep a little air before them.
            int left = g.LeftCap + 2, right = g.RightCap + 2;
            return new Rectangle(plate.X + left, plate.Y, Math.Max(0, plate.Width - left - right), plate.Height);
        }

        /// <summary>Text vertically centred in <paramref name="area"/>, starting at its left edge.</summary>
        public static void TextLeft(SpriteBatch sb, string text, Rectangle area, float size, Color color,
            float alpha = 1f, bool bold = false)
        {
            Vector2 measured = Measure(text, size);
            Text(sb, text, new Vector2(area.X, area.Center.Y - measured.Y * 0.5f), size, color, alpha, bold);
        }

        /// <summary>Text vertically centred in <paramref name="area"/>, ending at its right edge.</summary>
        public static void TextRight(SpriteBatch sb, string text, Rectangle area, float size, Color color,
            float alpha = 1f, bool bold = false)
        {
            Vector2 measured = Measure(text, size);
            Text(sb, text, new Vector2(area.Right - measured.X, area.Center.Y - measured.Y * 0.5f), size, color, alpha, bold);
        }

        public static void TextCentered(SpriteBatch sb, string text, Rectangle area, float size, Color color,
            float alpha = 1f, bool bold = false)
        {
            Vector2 measured = Measure(text, size);
            Text(sb, text, new Vector2(area.Center.X - measured.X * 0.5f, area.Center.Y - measured.Y * 0.5f), size, color, alpha, bold);
        }

        // --- Frames -------------------------------------------------------------------------------------

        /// <summary>Full window: drop shadow, tiled stone background, metal border and ornamental corners.</summary>
        public static void Window(SpriteBatch sb, Rectangle rect, float alpha)
        {
            SoftShadow(sb, rect, alpha);

            Tiled(sb, OrnateAssets.Background, rect, OrnateStyle.BackgroundTile, alpha, new Color(14, 16, 22));

            // Soft light from the top, darker toward the bottom.
            UiDrawHelper.DrawVerticalGradient(sb, new Rectangle(rect.X, rect.Y, rect.Width, rect.Height / 3),
                new Color(90, 70, 40) * (0.16f * alpha), Color.Transparent, 16);
            UiDrawHelper.DrawVerticalGradient(sb, new Rectangle(rect.X, rect.Bottom - rect.Height / 3, rect.Width, rect.Height / 3),
                Color.Transparent, Color.Black * (0.47f * alpha), 16);

            Border(sb, rect, alpha);
            Corners(sb, rect, OrnateStyle.CornerSize, alpha);
        }

        /// <summary>
        /// Soft drop shadow: stacked translucent rectangles approximate a blur that fades out over
        /// <paramref name="spread"/> pixels. A single offset rectangle shows as a hard dark slab beside the frame.
        /// </summary>
        public static void SoftShadow(SpriteBatch sb, Rectangle rect, float alpha, int spread = 18, int offsetY = 6)
        {
            Color layer = Color.Black * (0.045f * alpha);
            for (int i = spread; i >= 1; i -= 2)
            {
                Rectangle shadow = rect;
                shadow.Inflate(i, i);
                shadow.Y += offsetY;
                sb.Draw(Pixel, shadow, layer);
            }
        }

        /// <summary>Recessed dark area inside a window (sidebar, description box, selector).</summary>
        public static void Inset(SpriteBatch sb, Rectangle rect, float alpha, float darkness = 0.45f)
        {
            Color light = new(150, 118, 70);
            sb.Draw(Pixel, rect, Color.Black * (darkness * alpha));
            sb.Draw(Pixel, new Rectangle(rect.X, rect.Y, rect.Width, 2), Color.Black * (0.5f * alpha));
            sb.Draw(Pixel, new Rectangle(rect.X, rect.Bottom - 1, rect.Width, 1), light * (0.35f * alpha));
            sb.Draw(Pixel, new Rectangle(rect.X, rect.Y, 1, rect.Height), Color.Black * (0.5f * alpha));
            sb.Draw(Pixel, new Rectangle(rect.Right - 1, rect.Y, 1, rect.Height), light * (0.22f * alpha));
        }

        private static void Tiled(SpriteBatch sb, Texture2D texture, Rectangle rect, int tile, float alpha, Color fallback)
        {
            if (texture == null)
            {
                sb.Draw(Pixel, rect, fallback * alpha);
                return;
            }

            for (int y = rect.Y; y < rect.Bottom; y += tile)
            {
                for (int x = rect.X; x < rect.Right; x += tile)
                {
                    int w = Math.Min(tile, rect.Right - x);
                    int h = Math.Min(tile, rect.Bottom - y);
                    var src = new Rectangle(0, 0, w * texture.Width / tile, h * texture.Height / tile);
                    sb.Draw(texture, new Rectangle(x, y, w, h), src, Color.White * alpha);
                }
            }
        }

        private static void Border(SpriteBatch sb, Rectangle rect, float alpha)
        {
            Color highlight = new(214, 176, 104);
            UiDrawHelper.DrawBorder(sb, rect, Color.Black * alpha, 2);
            var metal = new Rectangle(rect.X + 2, rect.Y + 2, rect.Width - 4, rect.Height - 4);
            UiDrawHelper.DrawBorder(sb, metal, new Color(96, 72, 38) * alpha, 3);
            sb.Draw(Pixel, new Rectangle(metal.X, metal.Y + 1, metal.Width, 1), highlight * (0.85f * alpha));
            sb.Draw(Pixel, new Rectangle(metal.X + 1, metal.Y, 1, metal.Height), highlight * (0.6f * alpha));
            UiDrawHelper.DrawBorder(sb, new Rectangle(rect.X + 5, rect.Y + 5, rect.Width - 10, rect.Height - 10), Color.Black * (0.8f * alpha));
        }

        private static void Corners(SpriteBatch sb, Rectangle rect, int size, float alpha)
        {
            Texture2D corner = OrnateAssets.Corner;
            if (corner == null)
            {
                UiDrawHelper.DrawCornerAccents(sb, rect, OrnateStyle.Gold * alpha, 22, 2);
                return;
            }

            int h = size * corner.Height / corner.Width;
            // Align the corner's metal bars with the window border line (3.5px inside the edge).
            const float edge = 3.5f;
            int left = (int)MathF.Round(rect.X + edge - OrnateStyle.CornerBarX * size);
            int top = (int)MathF.Round(rect.Y + edge - OrnateStyle.CornerBarY * h);
            int right = (int)MathF.Round(rect.Right - edge - (1f - OrnateStyle.CornerBarX) * size);
            int bottom = (int)MathF.Round(rect.Bottom - edge - (1f - OrnateStyle.CornerBarY) * h);
            Color color = Color.White * alpha;

            sb.Draw(corner, new Rectangle(left, top, size, h), null, color, 0f, Vector2.Zero, SpriteEffects.None, 0f);
            sb.Draw(corner, new Rectangle(right, top, size, h), null, color, 0f, Vector2.Zero, SpriteEffects.FlipHorizontally, 0f);
            sb.Draw(corner, new Rectangle(left, bottom, size, h), null, color, 0f, Vector2.Zero, SpriteEffects.FlipVertically, 0f);
            sb.Draw(corner, new Rectangle(right, bottom, size, h), null, color, 0f, Vector2.Zero,
                SpriteEffects.FlipHorizontally | SpriteEffects.FlipVertically, 0f);
        }

        /// <summary>
        /// Winged title plaque centred on <paramref name="centerX"/>; its dark plate is <paramref name="plateHeight"/>
        /// tall. Long titles widen the plate (the wings keep their size) instead of shrinking the text.
        /// </summary>
        public static void TitlePlaque(SpriteBatch sb, string title, int centerX, int plateCenterY, int plateHeight, float alpha)
        {
            float fontSize = plateHeight * 0.46f;
            float textWidth = Measure(title, fontSize).X;
            int plateTop = plateCenterY - plateHeight / 2;
            Rectangle plate;

            Texture2D texture = OrnateAssets.Title;
            if (texture != null)
            {
                float scale = plateHeight / ((OrnateStyle.TitleBandBottom - OrnateStyle.TitleBandTop) * texture.Height);
                float naturalWidth = texture.Width * scale;
                float extra = Math.Max(0f, textWidth + 40f - naturalWidth * OrnateStyle.TitleTextWidth);
                int width = (int)MathF.Round(naturalWidth + extra);
                plate = new Rectangle(centerX - width / 2, plateTop, width, plateHeight);
                Plate(sb, texture, OrnateStyle.Title, plate, false, Color.White * alpha, overhang: 1f);
            }
            else
            {
                int width = Math.Max(280, (int)textWidth + 60);
                plate = new Rectangle(centerX - width / 2, plateTop, width, plateHeight);
                sb.Draw(Pixel, plate, new Color(24, 20, 16) * alpha);
                UiDrawHelper.DrawBorder(sb, plate, OrnateStyle.Bronze * alpha, 2);
            }

            TextCentered(sb, title, new Rectangle(plate.X, plate.Y, plate.Width, plate.Height + 2), fontSize, OrnateStyle.Gold, alpha, bold: true);
        }

        /// <summary>Ornamental divider: faded line with the gem ornament kept at its native aspect in the middle.</summary>
        public static void Divider(SpriteBatch sb, Rectangle area, float alpha)
        {
            Texture2D texture = OrnateAssets.Divider;
            if (texture == null)
            {
                UiDrawHelper.DrawHorizontalGradient(sb, new Rectangle(area.X, area.Center.Y, area.Width / 2, 1), Color.Transparent, OrnateStyle.Bronze * alpha, 16);
                UiDrawHelper.DrawHorizontalGradient(sb, new Rectangle(area.Center.X, area.Center.Y, area.Width / 2, 1), OrnateStyle.Bronze * alpha, Color.Transparent, 16);
                return;
            }

            int texW = texture.Width;
            int centerSrcX = (int)(texW * OrnateStyle.DividerCenterFrom);
            int centerSrcW = (int)(texW * (OrnateStyle.DividerCenterTo - OrnateStyle.DividerCenterFrom));
            float scale = (float)area.Height / texture.Height;
            int centerW = Math.Min(area.Width, (int)MathF.Round(centerSrcW * scale));
            int centerX = area.Center.X - centerW / 2;
            Color color = Color.White * alpha;

            sb.Draw(texture, new Rectangle(area.X, area.Y, centerX - area.X, area.Height),
                new Rectangle(0, 0, centerSrcX, texture.Height), color);
            sb.Draw(texture, new Rectangle(centerX, area.Y, centerW, area.Height),
                new Rectangle(centerSrcX, 0, centerSrcW, texture.Height), color);
            sb.Draw(texture, new Rectangle(centerX + centerW, area.Y, area.Right - centerX - centerW, area.Height),
                new Rectangle(centerSrcX + centerSrcW, 0, texW - centerSrcX - centerSrcW, texture.Height), color);
        }

        // --- Plates -------------------------------------------------------------------------------------

        /// <summary>
        /// Draws the standard button plate (<see cref="OrnateAssets.Button"/> / <see cref="OrnateAssets.ButtonHover"/>).
        /// <paramref name="overhang"/> (0..1) limits how much of the ornaments above and below the plate band is
        /// drawn, so neighbouring plates do not overlap.
        /// </summary>
        public static void Plate(SpriteBatch sb, Rectangle plate, bool highlighted, Color tint, float overhang = 0.5f) =>
            Plate(sb, highlighted ? OrnateAssets.ButtonHover : OrnateAssets.Button,
                highlighted ? OrnateStyle.ButtonHover : OrnateStyle.Button, plate, highlighted, tint, overhang);

        /// <summary>
        /// Draws any plate texture into <paramref name="plate"/>: fixed caps, a fixed centre ornament (when the layout
        /// has one) and stretched plain segments. The layout's band maps onto <paramref name="plate"/>; ornaments
        /// above and below it overhang by up to <paramref name="overhang"/> (0..1) of their height.
        /// </summary>
        public static void Plate(SpriteBatch sb, Texture2D tex, PlateLayout layout, Rectangle plate, bool highlighted,
            Color tint, float overhang = 0.5f)
        {
            if (tex == null)
            {
                float a = tint.A / 255f;
                sb.Draw(Pixel, plate, (highlighted ? new Color(70, 50, 22) : new Color(26, 24, 22)) * a);
                UiDrawHelper.DrawBorder(sb, plate, (highlighted ? OrnateStyle.Gold : OrnateStyle.Bronze) * a);
                return;
            }

            PlateGeometry g = Measure(tex, layout, plate, overhang);
            int texW = tex.Width;
            Rectangle Src(int x, int w) => new(x, g.SrcTop, Math.Max(1, w), g.SrcHeight);

            sb.Draw(tex, new Rectangle(plate.X, g.DestY, g.LeftCap, g.DestHeight), Src(0, g.LeftCapSrc), tint);
            sb.Draw(tex, new Rectangle(plate.Right - g.RightCap, g.DestY, g.RightCap, g.DestHeight), Src(texW - g.RightCapSrc, g.RightCapSrc), tint);
            int inner = plate.Width - g.LeftCap - g.RightCap;
            if (inner <= 0)
                return;

            Rectangle stretchSrc = Src((int)(layout.StretchFrom * texW), (int)((layout.StretchTo - layout.StretchFrom) * texW));
            int innerX = plate.X + g.LeftCap;
            int centerSrcX = (int)(layout.CenterFrom * texW);
            int centerSrcW = (int)((layout.CenterTo - layout.CenterFrom) * texW);
            int centerDst = (int)MathF.Round(centerSrcW * g.Scale);

            if (layout.HasCenter && centerDst + 8 <= inner)
            {
                int side = (inner - centerDst) / 2;
                sb.Draw(tex, new Rectangle(innerX, g.DestY, side, g.DestHeight), stretchSrc, tint);
                sb.Draw(tex, new Rectangle(innerX + side, g.DestY, centerDst, g.DestHeight), Src(centerSrcX, centerSrcW), tint);
                int rightX = innerX + side + centerDst;
                sb.Draw(tex, new Rectangle(rightX, g.DestY, plate.Right - g.RightCap - rightX, g.DestHeight), stretchSrc, tint);
            }
            else
            {
                sb.Draw(tex, new Rectangle(innerX, g.DestY, inner, g.DestHeight), stretchSrc, tint);
            }
        }

        /// <summary>Where a plate texture lands for a destination rectangle (shared by drawing and overlays such as sockets).</summary>
        public readonly record struct PlateGeometry(float Scale, int DestY, int DestHeight, int SrcTop, int SrcHeight,
            int LeftCapSrc, int LeftCap, int RightCapSrc, int RightCap);

        public static PlateGeometry Measure(Texture2D tex, PlateLayout layout, Rectangle plate, float overhang = 0.5f)
        {
            int texW = tex.Width;
            int texH = tex.Height;
            float bandTopPx = layout.BandTop * texH;
            float bandBottomPx = layout.BandBottom * texH;
            float scale = plate.Height / (bandBottomPx - bandTopPx);

            overhang = MathHelper.Clamp(overhang, 0f, 1f);
            int srcTop = (int)(bandTopPx * (1f - overhang));
            int srcBottom = (int)MathF.Ceiling(texH - (texH - bandBottomPx) * (1f - overhang));
            int srcH = Math.Max(1, srcBottom - srcTop);

            int leftSrc = (int)(layout.Cap * texW);
            int rightSrc = (int)(layout.RightCap * texW);
            int leftDst = Math.Min(plate.Width / 2, (int)MathF.Round(leftSrc * scale));
            int rightDst = Math.Min(plate.Width / 2, (int)MathF.Round(rightSrc * scale));

            return new PlateGeometry(scale,
                (int)MathF.Round(plate.Y - (bandTopPx - srcTop) * scale), (int)MathF.Round(srcH * scale),
                srcTop, srcH, leftSrc, leftDst, rightSrc, rightDst);
        }

        public static void Icon(SpriteBatch sb, string icon, Rectangle rect, float alpha)
        {
            Texture2D texture = OrnateAssets.Icon(icon);
            if (texture != null)
                sb.Draw(texture, rect, Color.White * alpha);
            else
                UiDrawHelper.DrawBorder(sb, rect, OrnateStyle.Bronze * alpha, 2);
        }
    }
}
