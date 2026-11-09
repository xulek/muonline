using Client.Main.Content;
using System.Threading.Tasks;

namespace Client.Main.Objects.Worlds.SelectWrold
{
    /// <summary>
    /// Star field panels of the character scene. They are flat cards whose front faces away from the camera, so with
    /// back-face culling they were never drawn; the scene showed the plain background through the nebula instead.
    /// </summary>
    public class SkyObject : ModelObject
    {
        protected override bool ForceTwoSidedMeshes => true;

        public override async Task Load()
        {
            var idx = (Type + 1).ToString().PadLeft(2, '0');
            Model = await BMDLoader.Instance.Prepare($"Object94/Object{idx}.bmd");
            LightEnabled = false;
            await base.Load();
        }
    }
}
