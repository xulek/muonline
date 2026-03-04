#nullable enable
using Microsoft.Xna.Framework;

namespace Client.Main.Controls.UI.Game.Common
{
    internal static class ModernHudTheme
    {
        public static readonly Color BgDarkest = new(8, 10, 14, 252);
        public static readonly Color BgDark = new(16, 20, 26, 250);
        public static readonly Color BgMid = new(24, 30, 38, 248);
        public static readonly Color BgLight = new(35, 42, 52, 245);
        public static readonly Color BgLighter = new(48, 56, 68, 240);

        public static readonly Color Accent = new(212, 175, 85);
        public static readonly Color AccentBright = new(255, 215, 120);
        public static readonly Color AccentDim = new(140, 115, 55);
        public static readonly Color AccentGlow = new(255, 200, 80, 40);

        public static readonly Color Secondary = new(90, 140, 200);
        public static readonly Color SecondaryBright = new(130, 180, 240);
        public static readonly Color SecondaryDim = new(50, 80, 120);

        public static readonly Color BorderOuter = new(5, 6, 8, 255);
        public static readonly Color BorderInner = new(60, 70, 85, 200);
        public static readonly Color BorderHighlight = new(100, 110, 130, 120);

        public static readonly Color SlotBg = new(12, 15, 20, 240);
        public static readonly Color SlotBorder = new(45, 52, 65, 180);
        public static readonly Color SlotHover = new(70, 85, 110, 150);
        public static readonly Color SlotSelected = new(212, 175, 85, 100);

        public static readonly Color TextWhite = new(240, 240, 245);
        public static readonly Color TextGold = new(255, 220, 130);
        public static readonly Color TextGray = new(160, 165, 175);
        public static readonly Color TextDark = new(100, 105, 115);

        public static readonly Color Success = new(80, 200, 120);
        public static readonly Color Warning = new(240, 180, 60);
        public static readonly Color Danger = new(220, 80, 80);
    }
}
