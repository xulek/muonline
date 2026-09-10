using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(43, "Golden Budge Dragon")]
    public class GoldenBudgeDragon : BudgeDragon // Inherits from BudgeDragon
    {
        public GoldenBudgeDragon()
        {
            Scale = 0.7f; // Set according to C++ Setting_Monster

            // SourceMain5.2 RenderCharacter L8526: golden monsters get the
            // RENDER_CHROME | RENDER_BRIGHT body pass tinted (1.0, 0.5, 0.0).
            BrightOverlay = 1f;
            BrightOverlayTexturePath = "Effect/Chrome01.jpg";
            BrightOverlayTint = new Vector3(1f, 0.5f, 0f);
        }

        // SourceMain5.2 uses the same MODEL_BUDGE_DRAGON visuals; only the scale differs.
        // Sounds inherited
    }
}
