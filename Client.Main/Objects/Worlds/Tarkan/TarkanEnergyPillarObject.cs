using Client.Main.Content;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Threading.Tasks;

namespace Client.Main.Objects.Worlds.Tarkan
{
    /// <summary>
    /// Implements energy pillars in Tarkan (Objects 61, 65, 66): BlendMesh 1, UV V scroll, pulsating yellow light.
    /// SourceMain5.2 ZzzObject.cpp cases 61, 65, 66.
    /// </summary>
    public class TarkanEnergyPillarObject : ModelObject
    {
        public override async Task Load()
        {
            BlendState = BlendState.AlphaBlend;
            BlendMesh = 1;
            BlendMeshState = BlendState.Additive;
            LightEnabled = true;
            IsTransparent = false;
            TextureCoordinateOffsetMeshIndex = 1;

            var idx = (Type + 1).ToString().PadLeft(2, '0');
            Model = await BMDLoader.Instance.Prepare($"Object9/Object{idx}.bmd");

            await base.Load();
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            float totalMs = (float)gameTime.TotalGameTime.TotalMilliseconds;
            float sine = MathF.Sin(totalMs * 0.002f) * 0.35f + 0.65f;
            BlendMeshLight = sine;
            Light = new Vector3(sine, sine * 0.6f, sine * 0.2f);

            TextureCoordinateOffset = new Vector2(0f, -((int)totalMs % 1000) * 0.001f);
        }
    }
}
