using Client.Main.Controllers;
using Client.Main.Graphics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace Client.Main.Helpers
{
    public static class OverheadNameplateRenderer
    {
        private readonly struct QueuedNameplate
        {
            public QueuedNameplate(SpriteFont font, Vector3 screen, string name, float? healthFraction, float renderScale,
                bool hoverLabel = false)
            {
                Font = font;
                Screen = screen;
                Name = name;
                HealthFraction = healthFraction;
                RenderScale = renderScale;
                HoverLabel = hoverLabel;
            }

            public SpriteFont Font { get; }
            public Vector3 Screen { get; }
            public string Name { get; }
            public float? HealthFraction { get; }
            public float RenderScale { get; }
            public bool HoverLabel { get; }
        }

        // 13 px: an ArialUi size, so the glyphs are drawn 1:1 (scaled glyphs go soft and uneven).
        private const float BaseNameScale = 13f / Constants.BASE_FONT_SIZE;
        private const float BaseHoverNameScale = 13f / Constants.BASE_FONT_SIZE;
        private const float BaseBarWidth = 70f;
        private const float BaseBarHeight = 6f;
        private const float BaseBarOffsetY = 2f;
        private const float BaseBarPadding = 1f;
        private const float BaseNamePaddingX = 4f;
        private const float BaseNamePaddingY = 2f;
        private const float BaseNameBarGap = 2f;
        private const int BarSegments = 8;
        private const int MaxRetainedQueueCapacity = 512;
        private static readonly List<QueuedNameplate> s_queue = new(256);
        public static int LastFrameQueuedNameplates { get; private set; }

        private static class Theme
        {
            public static readonly Color BgDarkest = new(8, 10, 14, 252);
            public static readonly Color BgDark = new(16, 20, 26, 250);
            public static readonly Color BgMid = new(24, 30, 38, 248);
            public static readonly Color BgLight = new(35, 42, 52, 245);
            public static readonly Color BgLighter = new(48, 56, 68, 240);

            public static readonly Color Accent = new(212, 175, 85);
            public static readonly Color AccentBright = new(255, 215, 120);
            public static readonly Color AccentDim = new(140, 115, 55);
            public static readonly Color AccentGlow = new(255, 200, 80, 40);

            public static readonly Color Secondary = new(90, 140, 200);
            public static readonly Color SecondaryBright = new(130, 180, 240);
            public static readonly Color SecondaryDim = new(50, 80, 120);

            public static readonly Color BorderOuter = new(5, 6, 8, 255);
            public static readonly Color BorderInner = new(60, 70, 85, 200);
            public static readonly Color BorderHighlight = new(100, 110, 130, 120);

            public static readonly Color SlotBg = new(12, 15, 20, 240);
            public static readonly Color SlotBorder = new(45, 52, 65, 180);
            public static readonly Color SlotHover = new(70, 85, 110, 150);
            public static readonly Color SlotSelected = new(212, 175, 85, 100);

            public static readonly Color TextWhite = new(240, 240, 245);
            public static readonly Color TextGold = new(255, 220, 130);
            public static readonly Color TextGray = new(160, 165, 175);
            public static readonly Color TextDark = new(100, 105, 115);

            public static readonly Color Success = new(80, 200, 120);
            public static readonly Color Warning = new(240, 180, 60);
            public static readonly Color Danger = new(220, 80, 80);
        }

        public static bool TryProject(BoundingBox bounds, float zOffset, out Vector3 screen)
        {
            Vector3 anchor = new(
                (bounds.Min.X + bounds.Max.X) * 0.5f,
                (bounds.Min.Y + bounds.Max.Y) * 0.5f,
                bounds.Max.Z + zOffset);

            screen = MuGame.Instance.GraphicsDevice.Viewport.Project(
                anchor,
                Camera.Instance.Projection,
                Camera.Instance.View,
                Matrix.Identity);

            return screen.Z >= 0f && screen.Z <= 1f;
        }

        public static void BeginFrame()
        {
            s_queue.Clear();
        }

        public static void EnqueueNameplate(SpriteFont font, Vector3 screen, string name, float? healthFraction, float renderScale)
        {
            if (font == null || string.IsNullOrWhiteSpace(name))
                return;

            s_queue.Add(new QueuedNameplate(font, screen, name, healthFraction, renderScale));
        }

        /// <summary>
        /// Queues the hover name of an NPC/player. Like the nameplates it is drawn with the interface, at the window's
        /// resolution, so render scale neither blurs nor resizes the text.
        /// </summary>
        public static void EnqueueHoverLabel(Vector3 screen, string name, float renderScale)
        {
            if (string.IsNullOrWhiteSpace(name))
                return;

            s_queue.Add(new QueuedNameplate(null, screen, name, null, renderScale, hoverLabel: true));
        }

        /// <param name="targetScale">Size of the target drawn into relative to the scene target the positions were
        /// projected in (1 / render scale when drawn on the window).</param>
        public static void FlushQueuedNameplates(SpriteBatch spriteBatch, float targetScale = 1f)
        {
            if (spriteBatch == null || s_queue.Count == 0)
                return;

            SpriteBatchScope scope = default;
            bool hasScope = false;
            if (!SpriteBatchScope.BatchIsBegun)
            {
                // The UI fonts are built with premultiplied alpha; NonPremultiplied would apply the glyph
                // coverage twice and leave thin, ragged edges. Glyphs are drawn 1:1, hence point sampling.
                scope = new SpriteBatchScope(
                    spriteBatch,
                    SpriteSortMode.Deferred,
                    BlendState.AlphaBlend,
                    SamplerState.PointClamp,
                    DepthStencilState.None);
                hasScope = true;
            }

            try
            {
                for (int i = 0; i < s_queue.Count; i++)
                {
                    var entry = s_queue[i];
                    var screen = entry.Screen;
                    screen.X *= targetScale;
                    screen.Y *= targetScale;
                    if (entry.HoverLabel)
                    {
                        DrawHoverLabel(spriteBatch, screen, entry.Name, entry.RenderScale * targetScale);
                        continue;
                    }

                    DrawNameplate(
                        spriteBatch,
                        entry.Font,
                        screen,
                        entry.Name,
                        entry.HealthFraction,
                        entry.RenderScale * targetScale);
                }
            }
            finally
            {
                if (hasScope)
                    scope.Dispose();
                LastFrameQueuedNameplates = s_queue.Count;
                s_queue.Clear();
                if (s_queue.Capacity > MaxRetainedQueueCapacity)
                    s_queue.Capacity = MaxRetainedQueueCapacity;
            }
        }

        public static void DrawNameplate(SpriteBatch spriteBatch, SpriteFont font, Vector3 screen, string name, float? healthFraction, float renderScale)
        {
            if (spriteBatch == null || font == null || string.IsNullOrEmpty(name))
                return;

            var pixel = GraphicsManager.Instance.Pixel;
            if (pixel == null)
                return;

            float scale = MathF.Max(renderScale, 0.1f);
            // Small text: a font rasterised near its size on screen instead of the base font shrunk to ~40%.
            font = GetCrispFont(Constants.BASE_FONT_SIZE * BaseNameScale * scale, out float nameScale);
            Vector2 nameSize = font.MeasureString(name) * nameScale;

            float padX = MathF.Max(1f, BaseNamePaddingX * scale);
            float padY = MathF.Max(1f, BaseNamePaddingY * scale);
            bool hasBar = healthFraction.HasValue;
            float barWidth = MathF.Max(BaseBarWidth * scale, nameSize.X + padX * 2f);
            float barHeight = hasBar ? MathF.Max(1f, BaseBarHeight * scale) : 0f;
            float barOffset = MathF.Max(1f, BaseBarOffsetY * scale);
            float barPadding = MathF.Max(1f, BaseBarPadding * scale);
            float nameGap = MathF.Max(1f, BaseNameBarGap * scale);

            float barBottomY = screen.Y - barOffset;
            float barTopY = barBottomY - barHeight;

            float nameBgHeight = nameSize.Y + padY * 2f;
            float nameBgWidth = nameSize.X + padX * 2f;

            int barX = (int)MathF.Round(screen.X - barWidth * 0.5f);
            int barY = (int)MathF.Round(barTopY);
            int barW = Math.Max(1, (int)MathF.Round(barWidth));
            int barH = Math.Max(1, (int)MathF.Round(barHeight));

            int nameX = (int)MathF.Round(screen.X - nameBgWidth * 0.5f);
            float nameBaseY = hasBar ? (barTopY - nameGap) : (screen.Y - barOffset);
            int nameY = (int)MathF.Round(nameBaseY - nameBgHeight);
            int nameW = Math.Max(1, (int)MathF.Round(nameBgWidth));
            int nameH = Math.Max(1, (int)MathF.Round(nameBgHeight));

            Rectangle nameRect = new(nameX, nameY, nameW, nameH);
            Rectangle barRect = new(barX, barY, barW, barH);

            var viewport = spriteBatch.GraphicsDevice.Viewport;
            int minX = Math.Min(nameRect.Left, barRect.Left);
            int maxX = Math.Max(nameRect.Right, barRect.Right);
            int minY = Math.Min(nameRect.Top, barRect.Top);
            int maxY = Math.Max(nameRect.Bottom, barRect.Bottom);
            if (maxX < 0 || minX > viewport.Width || maxY < 0 || minY > viewport.Height)
                return;

            int pad = Math.Max(0, (int)MathF.Round(barPadding));
            int innerWidth = Math.Max(0, barRect.Width - pad * 2);
            int innerHeight = Math.Max(1, barRect.Height - pad * 2);
            float hpFill = Math.Clamp(healthFraction ?? 0f, 0f, 1f);
            int fillWidth = Math.Max(0, (int)MathF.Round(innerWidth * hpFill));

            Rectangle fillRect = new(
                barRect.X + pad,
                barRect.Y + pad,
                fillWidth,
                innerHeight);

            float depth = MathHelper.Clamp(screen.Z, 0f, 1f);
            Vector2 textPos = new(MathF.Round(nameRect.X + padX), MathF.Round(nameRect.Y + padY));

            spriteBatch.Draw(pixel, nameRect, null, Theme.BgDarkest * 0.85f, 0f, Vector2.Zero, SpriteEffects.None, depth);
            DrawBorder(spriteBatch, pixel, nameRect, Theme.BorderOuter * 0.9f, depth);

            spriteBatch.DrawString(font, name, textPos + new Vector2(1f, 1f), Color.Black * 0.7f, 0f, Vector2.Zero, nameScale, SpriteEffects.None, depth);
            spriteBatch.DrawString(font, name, textPos, Theme.TextWhite, 0f, Vector2.Zero, nameScale, SpriteEffects.None, depth);

            if (hasBar)
            {
                spriteBatch.Draw(pixel, barRect, null, Color.Black * 0.7f, 0f, Vector2.Zero, SpriteEffects.None, depth);
                if (fillRect.Width > 0)
                    spriteBatch.Draw(pixel, fillRect, null, Theme.Danger, 0f, Vector2.Zero, SpriteEffects.None, depth);

                if (innerWidth > 0 && BarSegments > 1)
                {
                    float segmentWidth = innerWidth / (float)BarSegments;
                    for (int i = 1; i < BarSegments; i++)
                    {
                        int x = barRect.X + pad + (int)MathF.Round(segmentWidth * i);
                        spriteBatch.Draw(pixel,
                            new Rectangle(x, barRect.Y + pad, 1, innerHeight),
                            null,
                            Color.Black * 0.35f,
                            0f,
                            Vector2.Zero,
                            SpriteEffects.None,
                            depth);
                    }
                }

                DrawBorder(spriteBatch, pixel, barRect, Color.Black * 0.8f, depth);
                spriteBatch.Draw(pixel,
                    new Rectangle(barRect.X + 1, barRect.Y + 1, Math.Max(0, barRect.Width - 2), 1),
                    null,
                    new Color(100, 110, 130) * (72f / 255f),
                    0f,
                    Vector2.Zero,
                    SpriteEffects.None,
                    depth);
            }
        }

        /// <summary>Screen font for <paramref name="pixels"/>; a scale within a few percent of 1 is snapped to 1.</summary>
        private static SpriteFont GetCrispFont(float pixels, out float scale)
        {
            SpriteFont font = GraphicsManager.GetScreenFont(pixels, out scale);
            if (MathF.Abs(scale - 1f) < 0.06f)
                scale = 1f;
            return font;
        }

        private static void DrawHoverLabel(SpriteBatch spriteBatch, Vector3 screen, string name, float renderScale)
        {
            var pixel = GraphicsManager.Instance.Pixel;
            if (pixel == null || pixel.IsDisposed)
                return;

            float scale = MathF.Max(renderScale, 0.1f);
            var font = GetCrispFont(Constants.BASE_FONT_SIZE * BaseHoverNameScale * scale, out float fontScale);
            Vector2 size = font.MeasureString(name) * fontScale;
            Vector2 textPos = new(MathF.Round(screen.X - size.X * 0.5f), MathF.Round(screen.Y - size.Y));

            int padX = Math.Max(1, (int)MathF.Round(4f * scale));
            int padY = Math.Max(1, (int)MathF.Round(2f * scale));
            var bgRect = new Rectangle(
                (int)textPos.X - padX,
                (int)textPos.Y - padY,
                (int)MathF.Ceiling(size.X) + padX * 2,
                (int)MathF.Ceiling(size.Y) + padY * 2);

            var viewport = spriteBatch.GraphicsDevice.Viewport;
            if (bgRect.Right < 0 || bgRect.Left > viewport.Width || bgRect.Bottom < 0 || bgRect.Top > viewport.Height)
                return;

            // Premultiplied colours (AlphaBlend).
            spriteBatch.Draw(pixel, bgRect, new Color(30, 50, 70) * (150f / 255f));
            spriteBatch.DrawString(font, name, textPos + Vector2.One, Color.Black * 0.7f, 0f, Vector2.Zero, fontScale,
                SpriteEffects.None, 0f);
            spriteBatch.DrawString(font, name, textPos, new Color(176, 224, 230, 255), 0f, Vector2.Zero, fontScale,
                SpriteEffects.None, 0f);
        }

        private static void DrawBorder(SpriteBatch spriteBatch, Texture2D pixel, Rectangle rect, Color color, float depth)
        {
            if (rect.Width <= 0 || rect.Height <= 0)
                return;

            spriteBatch.Draw(pixel, new Rectangle(rect.X, rect.Y, rect.Width, 1), null, color, 0f, Vector2.Zero, SpriteEffects.None, depth);
            spriteBatch.Draw(pixel, new Rectangle(rect.X, rect.Bottom - 1, rect.Width, 1), null, color, 0f, Vector2.Zero, SpriteEffects.None, depth);
            spriteBatch.Draw(pixel, new Rectangle(rect.X, rect.Y, 1, rect.Height), null, color, 0f, Vector2.Zero, SpriteEffects.None, depth);
            spriteBatch.Draw(pixel, new Rectangle(rect.Right - 1, rect.Y, 1, rect.Height), null, color, 0f, Vector2.Zero, SpriteEffects.None, depth);
        }
    }
}
