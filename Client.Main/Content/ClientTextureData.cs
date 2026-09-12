using Client.Data.Texture;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Content
{
    public class ClientTexture
    {
        public TextureData Info { get; set; }
        // Temporary upload data; retain only the compact source after GPU creation.
        internal byte[] PreparedRgba;
        public TextureScript Script { get; set; }
        public Texture2D Texture { get; set; }
        public DateTime LastAccessUtc { get; set; } = DateTime.UtcNow;
        public int LastAccessFrame;
    }
}
