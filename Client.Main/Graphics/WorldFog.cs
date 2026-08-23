#nullable enable
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Graphics
{
    /// <summary>
    /// World-scoped linear fog state pushed into DynamicLighting.fx each frame.
    /// Disabled by default so existing worlds render unchanged; worlds like
    /// Valley of Loren opt in (source GMBattleCastle black GL_FOG 2000-2700).
    /// </summary>
    public static class WorldFog
    {
        public static bool Enabled;
        public static Vector3 Color = new(0f, 0f, 0f);
        public static float Start = 2000f;
        public static float End = 2700f;

        public static void Apply(Effect effect, Vector3 cameraPosition)
        {
            effect.Parameters["FogEnabled"]?.SetValue(Enabled ? 1f : 0f);
            if (!Enabled)
                return;

            effect.Parameters["FogColor"]?.SetValue(Color);
            effect.Parameters["FogStart"]?.SetValue(Start);
            effect.Parameters["FogEnd"]?.SetValue(End);
            effect.Parameters["FogCameraPosition"]?.SetValue(cameraPosition);
        }

        public static void Disable()
        {
            Enabled = false;
        }
    }
}
