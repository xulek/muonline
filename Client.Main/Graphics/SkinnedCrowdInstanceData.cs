using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Graphics
{
    /// <summary>
    /// Per-instance data for multi-pose GPU-skinned crowd rendering.
    /// PaletteData.X stores the row in the frame-local bone-palette texture.
    /// </summary>
    public struct SkinnedCrowdInstanceData : IVertexType
    {
        public Matrix World;
        public Color Color;
        public Vector2 PaletteData;

        public SkinnedCrowdInstanceData(Matrix world, Color color, int paletteRow)
        {
            World = world;
            Color = color;
            PaletteData = new Vector2(paletteRow, 0f);
        }

        public static readonly VertexDeclaration VertexDeclaration = new VertexDeclaration(
            new VertexElement(0, VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 2),
            new VertexElement(16, VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 3),
            new VertexElement(32, VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 4),
            new VertexElement(48, VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 5),
            new VertexElement(64, VertexElementFormat.Color, VertexElementUsage.Color, 1),
            new VertexElement(68, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 6));

        VertexDeclaration IVertexType.VertexDeclaration => VertexDeclaration;
    }

    internal struct SkinnedEquipmentInstanceData
    {
        public Matrix World;
        // The prepared bone-palette base, item level/options, Excellent and Ancient flags.
        public Vector4 PaletteAndOptions;

        public static readonly VertexDeclaration VertexDeclaration = new(
            new VertexElement(0, VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 2),
            new VertexElement(16, VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 3),
            new VertexElement(32, VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 4),
            new VertexElement(48, VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 5),
            new VertexElement(64, VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 6));
    }
}
