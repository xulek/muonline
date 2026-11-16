#nullable enable
using System.Threading.Tasks;
using Client.Main.Content;
using Client.Main.Graphics;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects.Support
{
    /// <summary>
    /// A BMD effect model (CreateEffect(MODEL_*)) whose transform, light and blend values are
    /// driven every tick by its owning <see cref="SourceFxEffect"/>. The owner adds it to the
    /// world and removes it in <c>OnFinish</c>.
    /// </summary>
    public sealed class SourceFxModel : ModelObject
    {
        private readonly string _modelPath;

        /// <param name="bright">RENDER_BRIGHT (additive body) when true; RENDER_TEXTURE otherwise.</param>
        public SourceFxModel(string modelPath, bool bright)
        {
            _modelPath = modelPath;
            ContinuousAnimation = true;
            AnimationSpeed = 4f;
            LightEnabled = true;
            RenderShadow = false;
            IsTransparent = true;
            AffectedByTransparency = true;
            DepthState = DepthStencilState.DepthRead;
            BlendMeshState = Blendings.OneOneAdditive;
            BlendState = bright ? Blendings.OneOneAdditive : BlendState.NonPremultiplied;
            Light = Vector3.One;
            BlendMeshLight = 1f;
        }

        protected override bool ForceTwoSidedMeshes => true;

        public override async Task Load()
        {
            try
            {
                Model = await BMDLoader.Instance.Prepare(_modelPath);
            }
            catch
            {
                Model = null;
            }

            await base.Load();
        }

        public void Remove()
        {
            if (Status == GameControlStatus.Disposed)
                return;

            if (Parent != null)
                Parent.Children.Remove(this);
            else
                World?.RemoveObject(this);
            Dispose();
        }
    }
}
