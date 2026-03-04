#nullable enable
using Microsoft.Xna.Framework;

namespace Client.Main.Controls.UI.Game.Buffs
{
    internal readonly struct BuffIconFrame
    {
        public BuffIconFrame(string texturePath, Rectangle sourceRectangle)
        {
            TexturePath = texturePath;
            SourceRectangle = sourceRectangle;
        }

        public string TexturePath { get; }

        public Rectangle SourceRectangle { get; }
    }

    /// <summary>
    /// Buff icon atlas mapping based on Main 5.2 (CNewUIBuffWindow::RenderBuffIcon).
    /// </summary>
    internal static class BuffIconAtlas
    {
        public const int IconWidth = 20;
        public const int IconHeight = 28;

        public const string StatusTexturePath = "Interface/newui_statusicon.jpg";
        public const string Status2TexturePath = "Interface/newui_statusicon2.jpg";

        public static readonly string[] TexturePaths =
        [
            StatusTexturePath,
            Status2TexturePath
        ];

        private const int AtlasSize = 256;
        private const int IconsPerRow = 10;

        public static bool IsDebuff(byte effectId)
        {
            return effectId switch
            {
                >= 55 and <= 65 => true,
                >= 72 and <= 77 => true,
                >= 83 and <= 86 => true,
                120 => true,
                186 => true,
                _ => false
            };
        }

        public static bool ShouldRender(byte effectId)
        {
            // Main 5.2 CNewUIBuffWindow::SetDisableRenderBuff
            return effectId switch
            {
                83 => false,  // eDeBuff_FlameStrikeDamage
                84 => false,  // eDeBuff_GiganticStormDamage
                85 => false,  // eDeBuff_LightningShockDamage
                120 => false, // eDeBuff_Discharge_Stamina
                _ => true
            };
        }

        public static bool TryResolve(byte effectId, out BuffIconFrame frame)
        {
            frame = default;

            if (effectId == 0 || !ShouldRender(effectId))
            {
                return false;
            }

            int iconIndex;
            string texturePath;

            if (effectId < 81)
            {
                iconIndex = effectId - 1;
                texturePath = StatusTexturePath;
            }
            else
            {
                iconIndex = effectId - 81;
                texturePath = Status2TexturePath;
            }

            int column = iconIndex % IconsPerRow;
            int row = iconIndex / IconsPerRow;

            int x = column * IconWidth;
            int y = row * IconHeight;

            if (x < 0 || y < 0 || x + IconWidth > AtlasSize || y + IconHeight > AtlasSize)
            {
                return false;
            }

            frame = new BuffIconFrame(texturePath, new Rectangle(x, y, IconWidth, IconHeight));
            return true;
        }
    }
}
