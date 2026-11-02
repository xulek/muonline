using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Graphics
{
    /// <summary>
    /// GPU-skinned vertex of a merged player mesh (ItemMaterial BasicColorDrawing_CompositeSkinned):
    /// the regular skinned layout plus the atlas tile and the item parameters of the part it belongs to.
    /// </summary>
    public struct PlayerCompositeVertex : IVertexType
    {
        public Vector3 Position;
        public Color Color;
        public Vector3 Normal;
        public Vector2 TextureCoordinate;
        public Vector2 BoneIndices;
        /// <summary>xy = tile origin, zw = texture size, in atlas texture coordinates.</summary>
        public Vector4 AtlasRect;
        /// <summary>x = item options (level | excellent | low chrome mode), y = excellent, z = ancient, w = part index.</summary>
        public Vector4 ItemData;

        public static readonly VertexDeclaration VertexDeclaration = new VertexDeclaration(
            new VertexElement(0, VertexElementFormat.Vector3, VertexElementUsage.Position, 0),
            new VertexElement(12, VertexElementFormat.Color, VertexElementUsage.Color, 0),
            new VertexElement(16, VertexElementFormat.Vector3, VertexElementUsage.Normal, 0),
            new VertexElement(28, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 0),
            new VertexElement(36, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 1),
            new VertexElement(44, VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 2),
            new VertexElement(60, VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 3)
        );

        VertexDeclaration IVertexType.VertexDeclaration => VertexDeclaration;
    }
}
