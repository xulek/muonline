using Client.Main.Content;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Threading.Tasks;

namespace Client.Main.Objects.Worlds.SelectWrold
{
    public class LightObject : ModelObject
    {
        public override async Task Load()
        {
            var idx = (Type + 1).ToString().PadLeft(2, '0');
            Model = await BMDLoader.Instance.Prepare($"Object94/Object{idx}.bmd");
            BlendState = BlendState.AlphaBlend;
            BlendMesh = 2;
            BlendMeshState = BlendState.Additive;
            LightEnabled = true;
            IsTransparent = true;
            Scale = 1.1f;
            Alpha = 0.5f;
            _phase = (Position.X + Position.Y) * 0.013f;
            await base.Load();
        }

        // Light shafts breathe slowly, each with its own phase.
        private float _phase;

        public override void Draw(GameTime gameTime)
        {
            float t = (float)(gameTime.TotalGameTime.TotalSeconds % 3600.0);
            Alpha = 0.5f * (0.7f + 0.3f * MathF.Sin(t * 0.7f + _phase) + 0.08f * MathF.Sin(t * 2.3f + _phase * 3f));

            // Save the current DepthStencilState
            DepthStencilState previousDepthState = MuGame.Instance.GraphicsDevice.DepthStencilState;
            // Set DepthStencilState to disable depth buffer writes for transparent objects
            MuGame.Instance.GraphicsDevice.DepthStencilState = MuGame.Instance.DisableDepthMask;

            base.Draw(gameTime);

            // Restore the previous DepthStencilState
            MuGame.Instance.GraphicsDevice.DepthStencilState = previousDepthState;
        }
    }
}
