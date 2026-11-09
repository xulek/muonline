using Client.Main.Content;
using Microsoft.Xna.Framework.Graphics;
using System.Threading.Tasks;

namespace Client.Main.Objects.Worlds.Devias
{
    public class FlagObject : ModelObject
    {
        public override async Task Load()
        {
            // SourceMain5.2 (ZzzObject.cpp, WD_2DEVIAS) gives the flags (83-85) no blend mesh: pole and cloth are
            // ordinary meshes that write depth. As a blend mesh the cloth wrote no depth and the additive window
            // light of the building behind it was drawn through it.
            BlendState = BlendState.NonPremultiplied; // straight-alpha texture: AlphaBlend gave bright fringes
            LightEnabled = true;
            Model = await BMDLoader.Instance.Prepare($"Object3/Object{Type + 1}.bmd");
            await base.Load();
        }
    }
}
