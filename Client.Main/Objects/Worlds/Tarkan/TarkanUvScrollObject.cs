using Client.Main.Content;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Threading.Tasks;

namespace Client.Main.Objects.Worlds.Tarkan
{
    /// <summary>
    /// Implements animated UV scrolling objects in Tarkan (Objects 2, 11, 12, 13, 72, 73, 75, 79).
    /// SourceMain5.2 ZzzObject.cpp.
    /// </summary>
    public class TarkanUvScrollObject : ModelObject
    {
        public override async Task Load()
        {
            BlendState = BlendState.NonPremultiplied;

            if (Type == 2 || Type == 72)
            {
                BlendMesh = 0;
                BlendMeshState = BlendState.Additive;
                TextureCoordinateOffsetMeshIndex = 0;
            }
            else
            {
                TextureCoordinateOffsetMeshIndex = -1; // all meshes or first mesh
            }

            var idx = (Type + 1).ToString().PadLeft(2, '0');
            Model = await BMDLoader.Instance.Prepare($"Object9/Object{idx}.bmd");

            await base.Load();
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            float totalMs = (float)gameTime.TotalGameTime.TotalMilliseconds;

            switch (Type)
            {
                case 2:
                    TextureCoordinateOffset = new Vector2(-((int)totalMs % 1000) * 0.001f, 0f);
                    break;
                case 12:
                    float uv12 = -((int)totalMs % 50000) * 0.00005f;
                    TextureCoordinateOffset = new Vector2(uv12, uv12);
                    break;
                case 11:
                case 13:
                case 72:
                case 73:
                case 75:
                case 79:
                    TextureCoordinateOffset = new Vector2(0f, -((int)totalMs % 10000) * 0.0002f);
                    break;
            }
        }
    }
}
