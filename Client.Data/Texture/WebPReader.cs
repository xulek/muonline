using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Client.Data.Texture
{
    /// <summary>Reads WebP images (client-bundled UI art) into premultiplied RGBA texture data.</summary>
    public class WebPReader : BaseReader<TextureData>
    {
        protected override TextureData Read(byte[] buffer) => ReadPremultiplied(buffer);

        /// <summary>
        /// Decodes an image and premultiplies its colour by alpha. The UI is drawn with premultiplied blending, and
        /// lossy WebP keeps arbitrary (2x2 chroma block) colours in fully transparent pixels; without premultiplying,
        /// those colours are added to whatever is behind the sprite and show up as square specks around edges.
        /// </summary>
        public static TextureData ReadPremultiplied(byte[] buffer)
        {
            using var image = Image.Load<Rgba32>(buffer);

            var data = new byte[image.Width * image.Height * 4];
            image.CopyPixelDataTo(data);
            for (int i = 0; i < data.Length; i += 4)
            {
                int alpha = data[i + 3];
                if (alpha == 255)
                    continue;
                data[i] = (byte)((data[i] * alpha + 127) / 255);
                data[i + 1] = (byte)((data[i + 1] * alpha + 127) / 255);
                data[i + 2] = (byte)((data[i + 2] * alpha + 127) / 255);
            }

            return new TextureData
            {
                Width = image.Width,
                Height = image.Height,
                Components = 4,
                Data = data,
                IsCompressed = false,
                Format = TextureSurfaceFormat.Color
            };
        }
    }

    /// <summary>
    /// Reads WebP world textures (remastered textures, tools/texture_remaster.py) the way the client's own formats
    /// arrive: straight alpha like OZT, and three components like OZJ when the image has no transparency, so models
    /// treat them exactly like the originals (cut-out vs opaque meshes).
    /// </summary>
    public class StraightWebPReader : BaseReader<TextureData>
    {
        protected override TextureData Read(byte[] buffer)
        {
            using var image = Image.Load<Rgba32>(buffer);
            var rgba = new byte[image.Width * image.Height * 4];
            image.CopyPixelDataTo(rgba);

            bool opaque = true;
            for (int i = 3; i < rgba.Length; i += 4)
            {
                if (rgba[i] != 255)
                {
                    opaque = false;
                    break;
                }
            }

            byte[] data = rgba;
            if (opaque)
            {
                data = new byte[image.Width * image.Height * 3];
                for (int i = 0, j = 0; i < rgba.Length; i += 4, j += 3)
                {
                    data[j] = rgba[i];
                    data[j + 1] = rgba[i + 1];
                    data[j + 2] = rgba[i + 2];
                }
            }

            return new TextureData
            {
                Width = image.Width,
                Height = image.Height,
                Components = (byte)(opaque ? 3 : 4),
                Data = data,
                IsCompressed = false,
                Format = TextureSurfaceFormat.Color
            };
        }
    }
}
