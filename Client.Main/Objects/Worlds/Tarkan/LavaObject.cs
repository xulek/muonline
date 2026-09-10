using Client.Main.Content;
using Client.Main.Graphics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Threading.Tasks;

namespace Client.Main.Objects.Worlds.Tarkan
{
    public class LavaObject : ModelObject
    {
        public override async Task Load()
        {
            BlendState = BlendState.AlphaBlend;
            BlendMesh = 0;
            BlendMeshState = BlendState.Additive;
            LightEnabled = true;
            IsTransparent = false;
            Scale = 1.0f;
            Model = await BMDLoader.Instance.Prepare($"Object9/Object08.bmd");

            await base.Load();
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            float totalMs = (float)gameTime.TotalGameTime.TotalMilliseconds;
            float sine = MathF.Sin((totalMs + Angle.Z * 100f) * 0.002f) * 0.35f + 0.65f;
            BlendMeshLight = sine;
            Light = new Vector3(sine, sine * 0.6f, sine * 0.2f);
        }

        public override void Draw(GameTime gameTime)
        {
            base.Draw(gameTime);
        }
    }
}