using Microsoft.Xna.Framework;

namespace Client.Main.Controls.UI.Ornate
{
    /// <summary>
    /// Horizontal/vertical layout of a plate texture as fractions of its size. The plate band (BandTop..BandBottom)
    /// is mapped onto the destination rectangle; ornaments above and below it overhang. Caps and the centre ornament
    /// keep their aspect, the plain Stretch segment fills the rest. Measure new art with
    /// <c>python tools/ui_assets.py measure &lt;file&gt;</c>.
    /// </summary>
    public readonly record struct PlateLayout(
        float BandTop, float BandBottom, float Cap,
        float StretchFrom, float StretchTo, float CenterFrom, float CenterTo, float CapRight = -1f)
    {
        /// <summary>Right cap width; equals <see cref="Cap"/> unless the plate is asymmetric.</summary>
        public float RightCap => CapRight < 0f ? Cap : CapRight;

        public bool HasCenter => CenterTo > CenterFrom;
    }

    /// <summary>Palette and measured art metrics of the ornate UI kit.</summary>
    public static class OrnateStyle
    {
        public static readonly Color Gold = new(255, 214, 128);
        public static readonly Color GoldDim = new(196, 160, 92);
        public static readonly Color Bronze = new(132, 100, 54);
        public static readonly Color BronzeDark = new(110, 84, 46);
        public static readonly Color TextLight = new(236, 230, 214);
        public static readonly Color TextMuted = new(158, 150, 136);
        public static readonly Color TextDisabled = new(98, 92, 84);
        public static readonly Color Danger = new(240, 128, 112);
        public static readonly Color DangerHover = new(255, 170, 150);

        // Measured on Controls/button and Controls/button_hover.
        public static readonly PlateLayout Button = new(0.089f, 0.911f, 0.125f, 0.15f, 0.44f, 0.465f, 0.535f);
        public static readonly PlateLayout ButtonHover = new(0.245f, 0.755f, 0.125f, 0.14f, 0.30f, 0.335f, 0.665f);

        // Measured on Controls/button_primary(_hover): wings on the caps, ornament top and bottom centre.
        public static readonly PlateLayout ButtonPrimary = new(0.231f, 0.776f, 0.16f, 0.20f, 0.38f, 0.40f, 0.60f);
        public static readonly PlateLayout ButtonPrimaryHover = new(0.236f, 0.758f, 0.17f, 0.20f, 0.33f, 0.34f, 0.66f);

        // Measured on Controls/card(_selected): emblem socket on the left cap, small corner cap on the right.
        public static readonly PlateLayout Card = new(0.226f, 0.774f, 0.25f, 0.30f, 0.85f, 0f, 0f, CapRight: 0.06f);
        public static readonly PlateLayout CardSelected = new(0.221f, 0.840f, 0.25f, 0.30f, 0.85f, 0f, 0f, CapRight: 0.06f);

        // Measured on Controls/progress_frame: the band is the dark channel inside the frame.
        public static readonly PlateLayout ProgressFrame = new(0.423f, 0.756f, 0.125f, 0.15f, 0.37f, 0.40f, 0.60f);
        /// <summary>Horizontal inset of the progress channel from each end of the frame (fraction of texture width).</summary>
        public const float ProgressChannelInset = 0.0605f;

        /// <summary>
        /// Emblem socket of the card textures: centre X/Y (fractions of the texture) and inner diameter (fraction of
        /// width). Measured with <c>python tools/ui_assets.py measure &lt;card&gt; --ring 0.13,0.5</c>.
        /// </summary>
        public static readonly Vector3 CardSocket = new(0.134f, 0.497f, 0.160f);
        public static readonly Vector3 CardSelectedSocket = new(0.122f, 0.518f, 0.152f);

        // Frame/title: band = dark plate between the wings; caps = wings + plate ends, centre = gem ornament.
        public const float TitleBandTop = 80f / 250f;
        public const float TitleBandBottom = 179f / 250f;
        public static readonly PlateLayout Title = new(TitleBandTop, TitleBandBottom, 0.21f, 0.23f, 0.35f, 0.37f, 0.63f);
        /// <summary>Part of the title texture's width usable for text (between the wing caps).</summary>
        public const float TitleTextWidth = 0.56f;

        // Frame/corner: centre lines of the vertical and horizontal metal bars.
        public const float CornerBarX = 25f / 160f;
        public const float CornerBarY = 32f / 158f;

        // Frame/divider: the gem ornament kept at native aspect in the middle.
        public const float DividerCenterFrom = 0.36f;
        public const float DividerCenterTo = 0.64f;

        public const int CornerSize = 76;
        public const int BackgroundTile = 192;
    }
}
