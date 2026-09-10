using Client.Main.Content;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Threading.Tasks;

namespace Client.Main.Objects.Worlds.Tarkan
{
    /// <summary>
    /// Implements Object 78 in Tarkan: Throne / chair object allowing characters to sit.
    /// SourceMain5.2 ZzzObject.cpp case 78.
    /// </summary>
    public class TarkanThroneObject : ModelObject
    {
        public override async Task Load()
        {
            BlendState = BlendState.NonPremultiplied;
            Model = await BMDLoader.Instance.Prepare("Object9/Object79.bmd");
            await base.Load();
        }
    }
}
