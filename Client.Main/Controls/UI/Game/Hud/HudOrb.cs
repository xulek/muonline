#nullable enable
using System;
using Client.Main.Controllers;
using Client.Main.Controls.UI.Common;
using Client.Main.Controls.UI.Ornate;
using Client.Main.Helpers;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Controls.UI.Game.Hud
{
    /// <summary>
    /// Liquid resource orb of the Modern HUD. Drawn by <c>HudOrb.fx</c> (waving surface, swirling liquid, bubbles,
    /// glass highlight, a pale band for recently lost resource); where the effect or the noise texture is missing, a
    /// flat sphere is drawn column by column instead.
    /// </summary>
    internal static class HudOrb
    {
        public readonly record struct Palette(Vector3 Deep, Vector3 Bright, Vector3 Trail);

        public static readonly Palette Health = new(new Vector3(0.30f, 0.015f, 0.02f), new Vector3(1.0f, 0.22f, 0.12f), new Vector3(1.0f, 0.78f, 0.55f));
        public static readonly Palette Mana = new(new Vector3(0.02f, 0.05f, 0.30f), new Vector3(0.25f, 0.58f, 1.0f), new Vector3(0.75f, 0.88f, 1.0f));

        public const string NoiseTexture = "Hud/liquid";

        /// <param name="fill">Liquid level 0..1.</param>
        /// <param name="trail">Level of the recently lost amount (>= fill while it drains).</param>
        /// <param name="pulse">Extra brightness, for the low-resource warning.</param>
        public static void Draw(SpriteBatch sb, Rectangle rect, float fill, float trail, Palette palette, float pulse, float time, float alpha)
        {
            fill = MathHelper.Clamp(fill, 0f, 1f);
            trail = MathHelper.Clamp(trail, fill, 1f);

            Effect? effect = GraphicsManager.Instance.HudOrbEffect;
            Texture2D? noise = OrnateAssets.Get(NoiseTexture);
            if (effect == null || noise == null)
            {
                DrawFallback(sb, rect, fill, trail, palette, pulse, alpha);
                return;
            }

            effect.Parameters["Fill"]?.SetValue(fill);
            effect.Parameters["Trail"]?.SetValue(trail);
            effect.Parameters["Time"]?.SetValue(time);
            effect.Parameters["Pulse"]?.SetValue(pulse);
            effect.Parameters["EdgeSoftness"]?.SetValue(2.4f / Math.Max(8f, rect.Width * UiScaler.Scale));
            effect.Parameters["DeepColor"]?.SetValue(palette.Deep);
            effect.Parameters["BrightColor"]?.SetValue(palette.Bright);
            effect.Parameters["TrailColor"]?.SetValue(palette.Trail);

            // One batch per orb: effect parameters are read when the batch flushes.
            using (new SpriteBatchScope(sb, SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearWrap,
                       effect: effect, transform: UiScaler.SpriteTransform))
            {
                sb.Draw(noise, rect, Color.White * alpha);
            }
        }

        private static void DrawFallback(SpriteBatch sb, Rectangle rect, float fill, float trail, Palette palette, float pulse, float alpha)
        {
            Texture2D pixel = GraphicsManager.Instance.Pixel;
            float radius = rect.Width / 2f;
            float cx = rect.X + radius, cy = rect.Y + radius;
            float level = rect.Bottom - fill * rect.Height;
            float trailLevel = rect.Bottom - trail * rect.Height;
            Color deep = new Color(palette.Deep) * (1f + pulse);
            Color bright = new Color(palette.Bright) * (1f + pulse);
            Color glass = new Color(palette.Deep * 0.12f + new Vector3(0.02f));

            for (int x = 0; x < rect.Width; x++)
            {
                float dx = x + 0.5f - radius;
                float half = MathF.Sqrt(Math.Max(0f, radius * radius - dx * dx));
                int top = (int)MathF.Round(cy - half), bottom = (int)MathF.Round(cy + half);
                if (bottom <= top)
                    continue;

                int liquidTop = Math.Clamp((int)MathF.Round(level), top, bottom);
                int trailTop = Math.Clamp((int)MathF.Round(trailLevel), top, liquidTop);
                sb.Draw(pixel, new Rectangle(rect.X + x, top, 1, trailTop - top), glass * alpha);
                if (liquidTop > trailTop)
                    sb.Draw(pixel, new Rectangle(rect.X + x, trailTop, 1, liquidTop - trailTop), new Color(palette.Trail) * (0.5f * alpha));
                if (bottom > liquidTop)
                    UiDrawHelper.DrawVerticalGradient(sb, new Rectangle(rect.X + x, liquidTop, 1, bottom - liquidTop), bright * alpha, deep * alpha, 8);
            }
        }
    }
}
