#nullable enable
using System.Threading.Tasks;
using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects.SkillFx
{
    /// <summary>
    /// One loaded BMD drawn many times per frame at different transforms (the SourceMain
    /// "one OBJECT per CreateEffect" model parts) without creating a world object per instance.
    /// Owned by an effect: set <see cref="WorldObject.World"/>, Load, Update each frame, Dispose.
    /// </summary>
    public sealed class SkillFxModelRenderer : ModelObject
    {
        private readonly string _modelPath;

        /// <param name="bright">RENDER_BRIGHT: whole body drawn additively (no terrain light).</param>
        public SkillFxModelRenderer(string modelPath, bool bright, int blendMesh = -1, int hiddenMesh = -1)
        {
            _modelPath = modelPath;
            HiddenMesh = hiddenMesh;
            BlendMesh = blendMesh;
            ContinuousAnimation = true;
            AnimationSpeed = 1f;
            LightEnabled = false;
            RenderShadow = false;
            IsTransparent = true;
            AffectedByTransparency = true;
            DepthState = DepthStencilState.DepthRead;
            BlendState = bright ? Blendings.OneOneAdditive : BlendState.AlphaBlend;
            BlendMeshState = Blendings.OneOneAdditive;
            BlendMeshLight = 1f;
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare(_modelPath);
            await base.Load();
        }

        public void DrawInstance(Vector3 position, Vector3 angleRadians, float scale, Vector3 light,
            float blendMeshLight = 1f, float alpha = 1f)
        {
            if (Status != GameControlStatus.Ready || Model == null || scale <= 0.001f)
                return;

            Position = position;
            Angle = angleRadians;
            Scale = scale;
            Light = light;
            Alpha = MathHelper.Clamp(alpha, 0f, 1f);
            BlendMeshLight = blendMeshLight;

            GraphicsDevice device = GraphicsDevice;
            RasterizerState previousRasterizer = device.RasterizerState;
            try
            {
                device.RasterizerState = RasterizerState.CullNone;
                GraphicsManager.Instance.AlphaTestEffect3D.View = Camera.Instance.View;
                GraphicsManager.Instance.AlphaTestEffect3D.Projection = Camera.Instance.Projection;
                GraphicsManager.Instance.AlphaTestEffect3D.World = WorldPosition;
                DrawModel(true);
            }
            finally
            {
                device.RasterizerState = previousRasterizer;
            }
        }
    }
}
