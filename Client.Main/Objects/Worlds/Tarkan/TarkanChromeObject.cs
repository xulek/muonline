using Client.Main.Content;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Threading.Tasks;

namespace Client.Main.Objects.Worlds.Tarkan
{
    /// <summary>
    /// Implements shiny chrome rendering for Object 81 in Tarkan.
    /// SourceMain5.2 ZzzObject.cpp case 81.
    /// </summary>
    public class TarkanChromeObject : ModelObject
    {
        public override async Task Load()
        {
            BlendState = BlendState.NonPremultiplied;
            BlendMesh = 0;
            BlendMeshState = BlendState.Additive;
            IsTransparent = true;
            Model = await BMDLoader.Instance.Prepare("Object9/Object82.bmd");

            await base.Load();
        }
    }
}
