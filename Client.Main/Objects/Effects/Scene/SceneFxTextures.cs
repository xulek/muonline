using System;
using Client.Main.Controllers;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects.Scene
{
    /// <summary>
    /// Small procedural sprites for the character scene effects (white, shape in alpha; tinted when drawn).
    /// Created once per graphics device.
    /// </summary>
    internal static class SceneFxTextures
    {
        private static Texture2D _glow, _glint, _petal, _streak, _beam, _drop, _puff;

        /// <summary>Additive blending for premultiplied sprites and colours (plain Additive would weight by alpha twice).</summary>
        public static readonly BlendState AddPremultiplied = new()
        {
            Name = "SceneFx.AddPremultiplied",
            ColorSourceBlend = Blend.One,
            ColorDestinationBlend = Blend.One,
            AlphaSourceBlend = Blend.One,
            AlphaDestinationBlend = Blend.One,
        };

        /// <summary>Soft round light with a bright core (fireflies, sparks, spray).</summary>
        public static Texture2D Glow => _glow = Valid(_glow) ?? Create(64, 64, (x, y) =>
        {
            float r = MathF.Sqrt(x * x + y * y);
            return MathF.Exp(-r * r * 5f) * 0.55f + MathF.Exp(-r * r * 30f) * 0.6f;
        });

        /// <summary>Four-pointed star glint (twinkling stars, sparkles).</summary>
        public static Texture2D Glint => _glint = Valid(_glint) ?? Create(64, 64, (x, y) =>
        {
            float r = MathF.Sqrt(x * x + y * y);
            float arms = MathF.Exp(-MathF.Abs(x) * 26f) * MathF.Exp(-MathF.Abs(y) * 2.6f)
                       + MathF.Exp(-MathF.Abs(y) * 26f) * MathF.Exp(-MathF.Abs(x) * 2.6f);
            return MathF.Min(1f, arms * 0.8f + MathF.Exp(-r * r * 40f));
        });

        /// <summary>Petal: a rounded leaf shape with a slightly brighter middle.</summary>
        public static Texture2D Petal => _petal = Valid(_petal) ?? Create(32, 32, (x, y) =>
        {
            float px = x * 1.0f, py = y * 1.9f;
            float d = px * px + py * py;
            float shape = MathHelper.Clamp((1f - d) * 6f, 0f, 1f);
            return shape * (0.75f + 0.25f * (1f - MathF.Abs(x)));
        });

        /// <summary>Shooting star: bright head on the right, tail fading to the left.</summary>
        public static Texture2D Streak => _streak = Valid(_streak) ?? Create(128, 16, (x, y) =>
        {
            float along = (x + 1f) * 0.5f;                 // 0 tail .. 1 head
            float width = MathF.Exp(-y * y * (12f + along * 30f));
            float head = MathF.Exp(-((x - 0.85f) * (x - 0.85f) * 120f + y * y * 40f));
            return MathF.Min(1f, MathF.Pow(along, 2.2f) * width + head);
        });

        /// <summary>Water drop in flight: a thin streak, bright round head on the right, short blurred tail.</summary>
        public static Texture2D Drop => _drop = Valid(_drop) ?? Create(48, 12, (x, y) =>
        {
            float along = (x + 1f) * 0.5f;                 // 0 tail .. 1 head
            float tail = MathF.Pow(along, 1.5f) * MathF.Exp(-y * y * 9f);
            float head = MathF.Exp(-((x - 0.62f) * (x - 0.62f) * 30f + y * y * 6f));
            return MathF.Min(1f, tail * 0.55f + head);
        });

        /// <summary>
        /// Mist puff: soft cloudy noise fading smoothly to nothing well inside the edges, so even a huge sprite shows
        /// no outline (the client's Steam_01 has crisp cloud edges that read as contours when scaled up).
        /// </summary>
        public static Texture2D Puff => _puff = Valid(_puff) ?? Create(128, 128, (x, y) =>
        {
            float r = MathF.Sqrt(x * x + y * y);
            float falloff = MathF.Pow(MathHelper.Clamp(1f - r, 0f, 1f), 2.2f);
            float n = Fbm(x * 2.2f + 3.1f, y * 2.2f + 7.7f);
            return falloff * (0.45f + 0.75f * n) * 0.9f;
        });

        private static float Fbm(float x, float y)
        {
            float sum = 0f, amplitude = 0.5f, norm = 0f;
            for (int octave = 0; octave < 4; octave++)
            {
                sum += amplitude * ValueNoise(x, y);
                norm += amplitude;
                x *= 2.03f; y *= 2.03f;
                amplitude *= 0.5f;
            }
            return sum / norm;
        }

        private static float ValueNoise(float x, float y)
        {
            int ix = (int)MathF.Floor(x), iy = (int)MathF.Floor(y);
            float fx = x - ix, fy = y - iy;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            float a = Hash(ix, iy), b = Hash(ix + 1, iy), c = Hash(ix, iy + 1), d = Hash(ix + 1, iy + 1);
            return MathHelper.Lerp(MathHelper.Lerp(a, b, fx), MathHelper.Lerp(c, d, fx), fy);
        }

        private static float Hash(int x, int y)
        {
            uint h = (uint)(x * 374761393 + y * 668265263);
            h = (h ^ (h >> 13)) * 1274126177u;
            return (h ^ (h >> 16)) / (float)uint.MaxValue;
        }

        /// <summary>Light column: soft across, brightest at the bottom (v = 1) and fading upwards.</summary>
        public static Texture2D Beam => _beam = Valid(_beam) ?? Create(64, 128, (x, y) =>
        {
            float across = MathF.Pow(MathF.Max(0f, 1f - MathF.Abs(x)), 2.2f);
            float up = (y + 1f) * 0.5f;                     // 0 top .. 1 bottom
            float fade = MathF.Pow(up, 1.6f) * MathHelper.Clamp((1f - up) * 12f, 0f, 1f);
            return across * fade;
        });

        private static Texture2D Valid(Texture2D texture) =>
            texture != null && !texture.IsDisposed && texture.GraphicsDevice == GraphicsManager.Instance.GraphicsDevice ? texture : null;

        /// <summary>Alpha from <paramref name="shape"/> over -1..1 coordinates, premultiplied white.</summary>
        private static Texture2D Create(int width, int height, Func<float, float, float> shape)
        {
            var data = new Color[width * height];
            for (int j = 0; j < height; j++)
            {
                for (int i = 0; i < width; i++)
                {
                    float x = (i + 0.5f) / width * 2f - 1f;
                    float y = (j + 0.5f) / height * 2f - 1f;
                    float a = MathHelper.Clamp(shape(x, y), 0f, 1f);
                    data[j * width + i] = new Color(a, a, a, a);
                }
            }

            var texture = new Texture2D(GraphicsManager.Instance.GraphicsDevice, width, height);
            texture.SetData(data);
            return texture;
        }
    }
}
