using System;
using Client.Main.Controllers;
using Client.Main.Helpers;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Graphics
{
    /// <summary>
    /// Glow, colour grade and vignette for one world (used by the character selection scene). The world is drawn into
    /// a private target between <see cref="Begin"/> and <see cref="End"/>; End blurs its bright parts at quarter size
    /// and writes the graded result to the target that was bound before, so UI drawn afterwards is not affected.
    /// </summary>
    internal sealed class SceneBloom : IDisposable
    {
        private RenderTarget2D _scene;
        private RenderTarget2D _half;
        private RenderTarget2D _quarterA;
        private RenderTarget2D _quarterB;
        private RenderTargetBinding[] _previousTargets;
        private Viewport _previousViewport;
        private bool _active;

        public float Threshold { get; set; } = 0.62f;
        public float Knee { get; set; } = 0.25f;
        public float Intensity { get; set; } = 0.9f;
        public Vector3 Tint { get; set; } = Vector3.One;
        public float Saturation { get; set; } = 1.08f;
        public float Contrast { get; set; } = 1.05f;
        public Vector3 Lift { get; set; } = Vector3.Zero;
        public float Vignette { get; set; } = 0.45f;

        /// <summary>Redirects drawing into the private target (cleared to <paramref name="clearColor"/>).</summary>
        public bool Begin(GraphicsDevice device, Color clearColor)
        {
            _active = false;
            if (device == null || GraphicsManager.Instance?.SceneBloomEffect == null)
                return false;

            _previousTargets = device.GetRenderTargets();
            _previousViewport = device.Viewport;

            int width, height, samples;
            if (_previousTargets.Length > 0 && _previousTargets[0].RenderTarget is RenderTarget2D target)
            {
                width = target.Width;
                height = target.Height;
                samples = target.MultiSampleCount;
            }
            else
            {
                width = device.PresentationParameters.BackBufferWidth;
                height = device.PresentationParameters.BackBufferHeight;
                samples = device.PresentationParameters.MultiSampleCount;
            }

            if (width < 16 || height < 16)
                return false;

            EnsureTargets(device, width, height, samples);
            device.SetRenderTarget(_scene);
            device.Clear(clearColor);
            _active = true;
            return true;
        }

        /// <summary>Glow passes and the graded image written back to the previous target.</summary>
        public void End(GraphicsDevice device)
        {
            if (!_active)
                return;
            _active = false;

            Effect effect = GraphicsManager.Instance.SceneBloomEffect;
            SpriteBatch sprite = GraphicsManager.Instance.Sprite;
            SpriteBatchScope.ForceReset();

            Pass(device, sprite, effect, "Extract", _scene, _half, new Vector2(1f / _scene.Width, 1f / _scene.Height));
            Pass(device, sprite, effect, "Extract", _half, _quarterA, new Vector2(1f / _half.Width, 1f / _half.Height));
            for (int i = 0; i < 2; i++)
            {
                float spread = 1f + i;
                Pass(device, sprite, effect, "Blur", _quarterA, _quarterB, new Vector2(spread / _quarterA.Width, 0f));
                Pass(device, sprite, effect, "Blur", _quarterB, _quarterA, new Vector2(0f, spread / _quarterA.Height));
            }

            device.SetRenderTargets(_previousTargets);
            device.Viewport = _previousViewport;
            _previousTargets = null;

            effect.CurrentTechnique = effect.Techniques["Combine"];
            effect.Parameters["BloomIntensity"]?.SetValue(Intensity);
            effect.Parameters["BloomTint"]?.SetValue(Tint);
            effect.Parameters["Saturation"]?.SetValue(Saturation);
            effect.Parameters["Contrast"]?.SetValue(Contrast);
            effect.Parameters["Lift"]?.SetValue(Lift);
            effect.Parameters["Vignette"]?.SetValue(Vignette);
            device.Textures[1] = _quarterA;
            device.SamplerStates[1] = SamplerState.LinearClamp;
            sprite.Begin(SpriteSortMode.Immediate, BlendState.Opaque, SamplerState.LinearClamp, DepthStencilState.None, RasterizerState.CullNone, effect);
            sprite.Draw(_scene, new Rectangle(0, 0, _previousViewport.Width, _previousViewport.Height), Color.White);
            sprite.End();
            device.Textures[1] = null;
        }

        private void Pass(GraphicsDevice device, SpriteBatch sprite, Effect effect, string technique, RenderTarget2D source, RenderTarget2D destination, Vector2 texelStep)
        {
            device.SetRenderTarget(destination);
            effect.CurrentTechnique = effect.Techniques[technique];
            effect.Parameters["Threshold"]?.SetValue(Threshold);
            effect.Parameters["Knee"]?.SetValue(Knee);
            effect.Parameters["TexelStep"]?.SetValue(texelStep);
            sprite.Begin(SpriteSortMode.Immediate, BlendState.Opaque, SamplerState.LinearClamp, DepthStencilState.None, RasterizerState.CullNone, effect);
            sprite.Draw(source, new Rectangle(0, 0, destination.Width, destination.Height), Color.White);
            sprite.End();
        }

        private void EnsureTargets(GraphicsDevice device, int width, int height, int samples)
        {
            if (_scene != null && !_scene.IsDisposed && _scene.Width == width && _scene.Height == height && _scene.MultiSampleCount == samples)
                return;

            DisposeTargets();
            _scene = new RenderTarget2D(device, width, height, false, SurfaceFormat.Color, DepthFormat.Depth24Stencil8, samples, RenderTargetUsage.DiscardContents);
            _half = new RenderTarget2D(device, Math.Max(1, width / 2), Math.Max(1, height / 2), false, SurfaceFormat.Color, DepthFormat.None);
            _quarterA = new RenderTarget2D(device, Math.Max(1, width / 4), Math.Max(1, height / 4), false, SurfaceFormat.Color, DepthFormat.None);
            _quarterB = new RenderTarget2D(device, Math.Max(1, width / 4), Math.Max(1, height / 4), false, SurfaceFormat.Color, DepthFormat.None);
        }

        private void DisposeTargets()
        {
            _scene?.Dispose();
            _half?.Dispose();
            _quarterA?.Dispose();
            _quarterB?.Dispose();
            _scene = _half = _quarterA = _quarterB = null;
        }

        public void Dispose() => DisposeTargets();
    }
}
