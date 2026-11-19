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
        internal const int MinimumSceneDimension = 16;
        internal static bool IsAvailable => GraphicsManager.Instance?.SceneBloomEffect is { IsDisposed: false };

        private RenderTarget2D _scene;
        private RenderTarget2D _half;
        private RenderTarget2D _quarterA;
        private RenderTarget2D _quarterB;
        private int _requestedSampleCount;
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
        /// <summary>Unsharp-mask strength applied to the scene in the combine pass (0 = off).</summary>
        public float Sharpen { get; set; }

        /// <summary>Occlusion multiplied into the scene this frame (white = none); null disables it.</summary>
        public Texture2D AmbientOcclusion { get; set; }
        public float AmbientOcclusionStrength { get; set; }

        /// <summary>Redirects drawing into the private target (cleared to <paramref name="clearColor"/>).</summary>
        public bool Begin(GraphicsDevice device, Color clearColor, int? geometrySamples = null)
        {
            _active = false;
            if (device == null || !IsAvailable)
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

            if (width < MinimumSceneDimension || height < MinimumSceneDimension)
                return false;

            EnsureTargets(device, width, height, geometrySamples ?? samples);
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

            var sceneTexel = new Vector2(1f / _scene.Width, 1f / _scene.Height);
            if (Constants.ENABLE_FUSED_BLOOM_EXTRACT && (_scene.Width & 3) == 0 && (_scene.Height & 3) == 0 &&
                effect.Techniques["ExtractQuarter"] != null)
            {
                Pass(device, sprite, effect, "ExtractQuarter", _scene, _quarterA, sceneTexel);
            }
            else
            {
                _half ??= new RenderTarget2D(device, Math.Max(1, _scene.Width / 2), Math.Max(1, _scene.Height / 2),
                    false, SurfaceFormat.Color, DepthFormat.None);
                Pass(device, sprite, effect, "Extract", _scene, _half, sceneTexel);
                Pass(device, sprite, effect, "Extract", _half, _quarterA, new Vector2(1f / _half.Width, 1f / _half.Height));
            }
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
            effect.Parameters["Sharpen"]?.SetValue(Sharpen);
            bool ao = AmbientOcclusion != null && !AmbientOcclusion.IsDisposed && AmbientOcclusionStrength > 0f;
            effect.Parameters["AoStrength"]?.SetValue(ao ? AmbientOcclusionStrength : 0f);
            Texture2D aoTexture = ao ? AmbientOcclusion : GraphicsManager.Instance.Pixel;
            device.SamplerStates[2] = SamplerState.LinearClamp;
            effect.Parameters["TexelStep"]?.SetValue(new Vector2(1f / _scene.Width, 1f / _scene.Height));
            device.SamplerStates[1] = SamplerState.LinearClamp;
            // EffectPass.Apply rebinds every sampler slot from its effect parameter (null when unset), so the extra
            // inputs must go through the parameters; DX names the textures, OpenGL the samplers.
            SetTexture(effect, "BloomTexture", "BloomSampler", _quarterA);
            SetTexture(effect, "AoTexture", "AoSampler", aoTexture);
            device.Textures[1] = _quarterA;
            device.Textures[2] = aoTexture;
            sprite.Begin(SpriteSortMode.Immediate, BlendState.Opaque, SamplerState.LinearClamp, DepthStencilState.None, RasterizerState.CullNone, effect);
            sprite.Draw(_scene, new Rectangle(0, 0, _previousViewport.Width, _previousViewport.Height), Color.White);
            sprite.End();
            device.Textures[1] = null;
            device.Textures[2] = null;
            SetTexture(effect, "BloomTexture", "BloomSampler", null);
            SetTexture(effect, "AoTexture", "AoSampler", null);
        }

        private static void SetTexture(Effect effect, string textureName, string samplerName, Texture2D texture)
        {
            EffectParameter parameter = effect.Parameters[textureName] ?? effect.Parameters[samplerName];
            parameter?.SetValue(texture);
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
            if (_scene != null && !_scene.IsDisposed && _scene.Width == width && _scene.Height == height &&
                _requestedSampleCount == samples)
                return;

            DisposeTargets();
            // PreserveContents: gameplay worlds switch to other targets mid-frame (ground shadow mask, part atlas)
            // and come back; a DiscardContents target is cleared on rebind, which wiped the terrain and objects.
            _scene = new RenderTarget2D(device, width, height, false, SurfaceFormat.Color, DepthFormat.Depth24Stencil8, samples, RenderTargetUsage.PreserveContents);
            _quarterA = new RenderTarget2D(device, Math.Max(1, width / 4), Math.Max(1, height / 4), false, SurfaceFormat.Color, DepthFormat.None);
            _quarterB = new RenderTarget2D(device, Math.Max(1, width / 4), Math.Max(1, height / 4), false, SurfaceFormat.Color, DepthFormat.None);
            _requestedSampleCount = samples;
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
