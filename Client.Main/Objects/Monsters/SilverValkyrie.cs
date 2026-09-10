using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(52, "Silver Valkyrie")]
    public class SilverValkyrie : Valkyrie // Inherits from Valkyrie
    {
        public SilverValkyrie()
        {
            Scale = 1.4f; // Set according to C++ Setting_Monster

            // SourceMain5.2 sets no BlendMesh for SILVER_VALKYRIE (unlike VALKYRIE's 0),
            // so reset the inherited value to the engine default.
            BlendMesh = -1;

            // SourceMain5.2 RenderCharacter L8526: MONSTER_SILVER_VALKYRIE gets the
            // RENDER_CHROME | RENDER_BRIGHT body pass.
            BrightOverlay = 1f;
            BrightOverlayTexturePath = "Effect/Chrome01.jpg";
        }
        // Sounds are inherited from Valkyrie
    }
}