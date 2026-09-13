using System;
using System.Buffers.Binary;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Content
{
    internal static class AndroidEffectLoader
    {
        public static Effect Load(ContentManager content, string assetName)
        {
            // Content.mgcb emits uncompressed, single-effect XNBs. Read their MGFX
            // payload without replacing MonoGame's global AOT reader registry.
            using var stream = TitleContainer.OpenStream(Path.Combine(content.RootDirectory, assetName + ".xnb"));
            using var reader = new BinaryReader(stream);
            if (!reader.ReadBytes(3).AsSpan().SequenceEqual("XNB"u8))
                throw new ContentLoadException($"Invalid effect XNB: {assetName}");
            reader.ReadByte(); // platform; the MGFX profile is validated below/by Effect
            if (reader.ReadByte() != 5 || (reader.ReadByte() & 0xC0) != 0)
                throw new ContentLoadException($"Expected uncompressed XNB v5: {assetName}");
            reader.ReadInt32(); // XNB length
            if (reader.Read7BitEncodedInt() != 1 ||
                !reader.ReadString().StartsWith("Microsoft.Xna.Framework.Content.EffectReader,", StringComparison.Ordinal) ||
                reader.ReadInt32() != 0 || reader.Read7BitEncodedInt() != 0 || reader.Read7BitEncodedInt() != 1)
                throw new ContentLoadException($"Unexpected effect XNB layout: {assetName}");

            int length = reader.ReadInt32();
            byte[] code = reader.ReadBytes(length);
            if (code.Length != length)
                throw new ContentLoadException($"Incomplete effect: {assetName}");

            PromoteFragmentPrecision(code);
            var service = (IGraphicsDeviceService)content.ServiceProvider.GetService(typeof(IGraphicsDeviceService));
            return new Effect(service.GraphicsDevice, code) { Name = assetName };
        }

        internal static void PromoteFragmentPrecision(byte[] code)
        {
            // MonoGame 3.8.5's MojoShader output defaults to mediump in GLES pixel
            // shaders. MU uses world-space distances: even 500 squared exceeds
            // float16's range. Desktop GL ignores these precision declarations.
            // Replace only the generated declaration, keeping all MGFX lengths intact.
            ReadOnlySpan<byte> original = "precision mediump float;"u8;
            ReadOnlySpan<byte> replacement = "precision highp   float;"u8;
            bool changed = false;
            Span<byte> remaining = code;
            int index;
            while ((index = remaining.IndexOf(original)) >= 0)
            {
                replacement.CopyTo(remaining[index..]);
                remaining = remaining[(index + original.Length)..];
                changed = true;
            }

            if (changed)
            {
                // MGFX v10/v11 store an effect-cache key after signature/version/profile.
                // Re-key the modified payload so an unmodified cached Effect cannot win.
                if (code.Length < 10 || !code.AsSpan(0, 4).SequenceEqual("MGFX"u8) ||
                    (code[4] != 10 && code[4] != 11) || code[5] != 0)
                    throw new ContentLoadException("Unsupported MGFX layout for GLES precision correction.");

                uint hash = 2166136261;
                foreach (byte value in code.AsSpan(10))
                    hash = unchecked((hash ^ value) * 16777619);
                BinaryPrimitives.WriteUInt32LittleEndian(code.AsSpan(6, 4), hash);
            }
        }
    }
}
