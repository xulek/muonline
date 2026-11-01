using System.Collections.Generic;
using Client.Main.Core.Client;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Graphics
{
    /// <summary>
    /// 8 x 8 guild mark textures, built from the 16-colour palette of the original
    /// <c>CreateGuildMark</c> (ZzzInventory.cpp). Index 0 is transparent.
    /// Must be used from the render thread.
    /// </summary>
    public static class GuildMarkTextureCache
    {
        public static readonly Color[] Palette =
        {
            Color.Transparent,
            new(0, 0, 0),
            new(128, 128, 128),
            new(255, 255, 255),
            new(255, 0, 0),
            new(255, 128, 0),
            new(255, 255, 0),
            new(128, 255, 0),
            new(0, 255, 0),
            new(0, 255, 128),
            new(0, 255, 255),
            new(0, 128, 255),
            new(0, 0, 255),
            new(128, 0, 255),
            new(255, 0, 255),
            new(255, 0, 128),
        };

        private static readonly Dictionary<uint, (byte[] Mark, Texture2D Texture)> Textures = new();

        /// <summary>Returns the mark texture of a cached guild, or null when the device is not ready.</summary>
        public static Texture2D Get(GuildInfo guild)
        {
            if (guild == null)
                return null;

            if (Textures.TryGetValue(guild.Id, out var entry) && ReferenceEquals(entry.Mark, guild.Mark) && !entry.Texture.IsDisposed)
                return entry.Texture;

            entry.Texture?.Dispose();
            var texture = Create(guild.Mark);
            if (texture == null)
                return null;

            Textures[guild.Id] = (guild.Mark, texture);
            return texture;
        }

        /// <summary>Creates an uncached texture, e.g. for the mark editor preview.</summary>
        public static Texture2D Create(byte[] mark)
        {
            var device = Controllers.GraphicsManager.Instance?.GraphicsDevice;
            if (device == null || mark == null)
                return null;

            var pixels = new Color[GuildInfo.MarkSize * GuildInfo.MarkSize];
            for (int i = 0; i < pixels.Length && i < mark.Length; i++)
                pixels[i] = Palette[mark[i] & 0x0F];

            var texture = new Texture2D(device, GuildInfo.MarkSize, GuildInfo.MarkSize);
            texture.SetData(pixels);
            return texture;
        }

        /// <summary>Draws a mark with a thin dark frame; caller provides an active SpriteBatch (PointClamp recommended).</summary>
        public static void Draw(SpriteBatch sb, Texture2D mark, Rectangle rect, float alpha = 1f)
        {
            var pixel = Controllers.GraphicsManager.Instance?.Pixel;
            if (pixel != null)
                sb.Draw(pixel, new Rectangle(rect.X - 1, rect.Y - 1, rect.Width + 2, rect.Height + 2), Color.Black * (0.6f * alpha));
            if (mark != null)
                sb.Draw(mark, rect, Color.White * alpha);
        }
    }
}
