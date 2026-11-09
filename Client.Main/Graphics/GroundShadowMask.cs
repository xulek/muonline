using System;
using Client.Main.Controllers;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Graphics
{
    /// <summary>
    /// Ground shadows seen from straight above, in a small texture around the camera target: white where lit, darker
    /// under shadows. The grass shader (GrassShadow.fx) looks it up at each blade's world position, so shadows lie on
    /// the grass instead of under it. Filled at the end of the opaque world pass, from the same shadow draws as the
    /// screen (ModelObject.EndGroundShadowBatch); only when grass is drawn and shadows exist.
    /// </summary>
    internal static class GroundShadowMask
    {
        private const int Resolution = 512;
        private const float WorldSize = 4096f;   // about 41 tiles: more than the ground visible from the default camera

        private static RenderTarget2D _target;
        private static RenderTargetBinding[] _previousTargets;
        private static Viewport _previousViewport;

        /// <summary>Shadows on grass on/off (StressHarness MU_STRESS_GRASS_SHADOW=0 measures the cost).</summary>
        public static bool Enabled { get; set; } = true;

        /// <summary>Frame in which the mask was filled; the grass ignores it in any other frame.</summary>
        public static int Frame { get; private set; } = int.MinValue;

        public static Texture2D Texture => _target;

        public static Matrix ViewProjection { get; private set; }

        /// <summary>Starts drawing into the mask (cleared to lit) and returns its view-projection.</summary>
        public static bool Begin(GraphicsDevice device, Vector3 center, out Matrix viewProjection)
        {
            viewProjection = default;
            if (device == null)
                return false;

            if (_target == null || _target.IsDisposed || _target.GraphicsDevice != device)
            {
                _target?.Dispose();
                _target = new RenderTarget2D(device, Resolution, Resolution, false, SurfaceFormat.Color, DepthFormat.None);
            }

            // Snapped to whole texels so the mask does not shimmer while the camera moves.
            float texel = WorldSize / Resolution;
            center.X = MathF.Round(center.X / texel) * texel;
            center.Y = MathF.Round(center.Y / texel) * texel;

            Matrix view = Matrix.CreateLookAt(new Vector3(center.X, center.Y, center.Z + 20000f), new Vector3(center.X, center.Y, center.Z), Vector3.UnitY);
            Matrix projection = Matrix.CreateOrthographic(WorldSize, WorldSize, 1f, 60000f);
            viewProjection = view * projection;
            ViewProjection = viewProjection;

            _previousTargets = device.GetRenderTargets();
            _previousViewport = device.Viewport;
            device.SetRenderTarget(_target);
            device.Clear(Color.White);
            return true;
        }

        public static void End(GraphicsDevice device)
        {
            device.SetRenderTargets(_previousTargets);
            device.Viewport = _previousViewport;
            _previousTargets = null;
            Frame = MuGame.FrameIndex;
        }
    }
}
