namespace Client.Data.Texture
{
    public class OZDReader : BaseReader<TextureData>
    {
        public class DecoderSettings
        {
            public bool UseParallel { get; set; } = false;
        }

        public OZDReader() { }

        protected override TextureData Read(byte[] buffer)
        {
            buffer = ModulusCryptor.ModulusCryptor.Decrypt(buffer);
            if (buffer[0] == 'D' && buffer[1] == 'D' && buffer[2] == 'S' && buffer[3] == ' ')
                return ReadDDS(buffer);

            throw new ApplicationException("Invalid OZD file");
        }

        private TextureData ReadDDS(byte[] buffer)
        {
            if (buffer == null || buffer.Length < 128)
                throw new ApplicationException("Invalid DDS data");

            var header = buffer.AsSpan(0, 128);
            using var br = new BinaryReader(new MemoryStream(header.ToArray()));

            var signature = br.ReadString(4);
            if (!string.Equals(signature, "DDS ", StringComparison.Ordinal))
                throw new ApplicationException("Invalid DDS signature");

            var headerSize = br.ReadInt32();
            var flags = br.ReadInt32();
            var height = br.ReadInt32();
            var width = br.ReadInt32();
            var pitchOrLinearSize = br.ReadInt32();
            var depth = br.ReadInt32();
            var mipMapCount = br.ReadInt32();

            if (headerSize != 124 || width <= 0 || height <= 0)
                throw new ApplicationException("Invalid DDS header");

            br.BaseStream.Seek(84, SeekOrigin.Begin);
            var pixelFormat = br.ReadString(4);

            TextureSurfaceFormat surfaceFormat = pixelFormat switch
            {
                "DXT1" => TextureSurfaceFormat.Dxt1,
                "DXT3" => TextureSurfaceFormat.Dxt3,
                "DXT5" => TextureSurfaceFormat.Dxt5,
                _ => throw new ApplicationException($"Unsupported DDS format: {pixelFormat}")
            };

            byte[][] mipLevels = SplitMipLevels(
                buffer.AsSpan(128),
                width,
                height,
                Math.Max(1, mipMapCount),
                surfaceFormat);

            return new TextureData
            {
                Width = width,
                Height = height,
                Format = surfaceFormat,
                IsCompressed = true,
                Data = mipLevels[0],
                MipData = mipLevels,
                MipDataGenerated = false
            };
        }

        private static byte[][] SplitMipLevels(
            ReadOnlySpan<byte> payload,
            int width,
            int height,
            int requestedMipCount,
            TextureSurfaceFormat format)
        {
            int blockBytes = format == TextureSurfaceFormat.Dxt1 ? 8 : 16;
            int maxMipCount = 1;
            for (int w = width, h = height; w > 1 || h > 1;)
            {
                w = Math.Max(1, w / 2);
                h = Math.Max(1, h / 2);
                maxMipCount++;
            }

            int mipCount = Math.Min(Math.Max(1, requestedMipCount), maxMipCount);
            var levels = new List<byte[]>(mipCount);
            int offset = 0;
            int levelWidth = width;
            int levelHeight = height;

            for (int level = 0; level < mipCount; level++)
            {
                int blocksWide = Math.Max(1, (levelWidth + 3) / 4);
                int blocksHigh = Math.Max(1, (levelHeight + 3) / 4);
                int levelSize = checked(blocksWide * blocksHigh * blockBytes);

                if (offset + levelSize > payload.Length)
                {
                    if (level == 0)
                        throw new ApplicationException("DDS payload is truncated");
                    break;
                }

                levels.Add(payload.Slice(offset, levelSize).ToArray());
                offset += levelSize;
                levelWidth = Math.Max(1, levelWidth / 2);
                levelHeight = Math.Max(1, levelHeight / 2);
            }

            if (levels.Count == 0)
                throw new ApplicationException("DDS contains no texture data");

            return levels.ToArray();
        }
    }
}
