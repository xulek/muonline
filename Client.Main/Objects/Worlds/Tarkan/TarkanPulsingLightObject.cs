using Client.Main.Content;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Threading.Tasks;

namespace Client.Main.Objects.Worlds.Tarkan
{
    /// <summary>
    /// Implements Object 04 in Tarkan: BlendMesh 0, UV V scroll, pulsating white light.
    /// SourceMain5.2 ZzzObject.cpp case 4.
    /// </summary>
    public class TarkanPulsingLightObject : ModelObject
    {
        public override async Task Load()
        {
            BlendState = BlendState.NonPremultiplied;
            BlendMesh = 0;
            BlendMeshState = BlendState.Additive;
            LightEnabled = true;
            IsTransparent = true;
            TextureCoordinateOffsetMeshIndex = 0;
            Model = await BMDLoader.Instance.Prepare("Object9/Object05.bmd");

            await base.Load();
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            float totalMs = (float)gameTime.TotalGameTime.TotalMilliseconds;
            float sine = MathF.Sin(totalMs * 0.002f) * 0.35f + 0.65f;
            BlendMeshLight = sine;
            Light = new Vector3(sine, sine, sine);

            TextureCoordinateOffset = new Vector2(0f, -((int)totalMs % 10000) * 0.0001f);
        }
    }
}
