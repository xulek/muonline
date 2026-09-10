namespace Client.Data.Texture
{
    public class TextureData
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public TextureSurfaceFormat Format { get; set; }
        public byte Components { get; set; }
        public bool IsCompressed { get; set; }
        public byte[] Data { get; set; } = [];

        /// <summary>
        /// Optional complete mip chain. Level 0 is the base image and subsequent entries
        /// are progressively smaller levels. Uncompressed levels use the same component
        /// count as <see cref="Data"/>; compressed levels keep their native block format.
        /// </summary>
        public byte[][] MipData { get; set; } = [];

        /// <summary>
        /// True when the mip chain was generated at runtime rather than read from the source asset.
        /// </summary>
        public bool MipDataGenerated { get; set; }
    }
}
