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

        /// <summary>
        /// General distance fog of game worlds (<see cref="Constants.DISTANCE_FOG_START"/> /
        /// <see cref="Constants.DISTANCE_FOG_END"/>), used when no world-specific fog is active.
        /// </summary>
        public static bool DistanceFogEnabled;
        public static Vector3 DistanceColor = new(0f, 0f, 0f);

        public static void Apply(Effect effect, Vector3 cameraPosition)
        {
            bool distanceFog = !Enabled && DistanceFogEnabled;
            effect.Parameters["FogEnabled"]?.SetValue(Enabled || distanceFog ? 1f : 0f);
            if (!Enabled && !distanceFog)
                return;

            effect.Parameters["FogColor"]?.SetValue(Enabled ? Color : DistanceColor);
            effect.Parameters["FogStart"]?.SetValue(Enabled ? Start : Constants.DISTANCE_FOG_START);
            effect.Parameters["FogEnd"]?.SetValue(Enabled ? End : Constants.DISTANCE_FOG_END);
            effect.Parameters["FogCameraPosition"]?.SetValue(cameraPosition);
        }

        /// <summary>
        /// Same fog for MonoGame's built-in effects (AlphaTestEffect / BasicEffect draw alpha-blended map objects such
        /// as the Icarus walls). Their fog uses view depth instead of radial distance; at the far plane both reach
        /// full fog, so objects fade out instead of being cut off at the render limit.
        /// </summary>
        public static void Apply(IEffectFog effect)
        {
            bool distanceFog = !Enabled && DistanceFogEnabled;
            effect.FogEnabled = Enabled || distanceFog;
            if (!effect.FogEnabled)
                return;

            effect.FogColor = Enabled ? Color : DistanceColor;
            effect.FogStart = Enabled ? Start : Constants.DISTANCE_FOG_START;
            effect.FogEnd = Enabled ? End : Constants.DISTANCE_FOG_END;
        }

        /// <summary>
        /// Turns the fog off (<paramref name="enabled"/> = false) or re-applies the current world fog on every shared
        /// world effect. Off-screen renders that reuse these effects (inventory item previews) must disable it:
        /// their models sit far from the world camera and would come out fully fogged, i.e. black.
        /// </summary>
        public static void SetOnSharedEffects(bool enabled)
        {
            var graphics = Controllers.GraphicsManager.Instance;
            if (graphics == null)
                return;

            if (enabled)
            {
                Vector3 cameraPosition = Camera.Instance.Position;
                if (graphics.DynamicLightingEffect != null) Apply(graphics.DynamicLightingEffect, cameraPosition);
                if (graphics.ItemMaterialEffect != null) Apply(graphics.ItemMaterialEffect, cameraPosition);
                if (graphics.MonsterMaterialEffect != null) Apply(graphics.MonsterMaterialEffect, cameraPosition);
                if (graphics.AlphaTestEffect3D != null) Apply(graphics.AlphaTestEffect3D);
                if (graphics.BasicEffect3D != null) Apply(graphics.BasicEffect3D);
                return;
            }

            graphics.DynamicLightingEffect?.Parameters["FogEnabled"]?.SetValue(0f);
            graphics.ItemMaterialEffect?.Parameters["FogEnabled"]?.SetValue(0f);
            graphics.MonsterMaterialEffect?.Parameters["FogEnabled"]?.SetValue(0f);
            if (graphics.AlphaTestEffect3D != null) graphics.AlphaTestEffect3D.FogEnabled = false;
            if (graphics.BasicEffect3D != null) graphics.BasicEffect3D.FogEnabled = false;
        }

        public static void Disable()
        {
            Enabled = false;
        }
    }
}
