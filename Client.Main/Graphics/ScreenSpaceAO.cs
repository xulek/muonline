#nullable enable
using System;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Helpers;
using Client.Main.Models;
using Client.Main.Objects;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Graphics
{
    /// <summary>
    /// Half-resolution screen-space ambient occlusion for gameplay worlds.
    /// The scene has no readable depth buffer, so a depth pre-pass draws terrain and the nearby visible objects
    /// from the camera with the shadow-caster techniques (DynamicLighting.fx ShadowCaster*, static map objects
    /// through the shadow instancing batches). SceneBloom.fx "Ssao" turns that depth into occlusion,
    /// "AoFilteredTemporal" filters it, and the SceneBloom combine pass multiplies the scene by it.
    /// </summary>
    internal sealed class ScreenSpaceAO : IDisposable
    {
        private const float CasterDistance = 3200f;

        private readonly List<ModelObject> _instancedCasters = new();
        private RenderTarget2D? _depth;
        private RenderTarget2D? _prevDepth;
        private RenderTarget2D? _ao;
        private RenderTarget2D? _history;
        private RenderTarget2D? _historyNext;
        private Matrix _prevViewProjection;
        private bool _historyValid;
        private int _lastRenderedFrame = int.MinValue;
        private int _frame = int.MinValue;

        /// <summary>Share of last frame's occlusion kept per frame (temporal smoothing).</summary>
        public float HistoryWeight { get; set; } = 0.85f;

        public float Radius { get; set; } = 70f;
        public float Intensity { get; set; } = 1.4f;
        public float Strength { get; set; } = 0.75f;

        /// <summary>Strength after the user's SSAO slider (<see cref="Constants.SSAO_STRENGTH"/>).</summary>
        public float EffectiveStrength => Math.Clamp(Strength * Constants.SSAO_STRENGTH, 0f, 1f);

        private float EffectiveIntensity => Intensity * Math.Clamp(Constants.SSAO_STRENGTH, 0f, 2f);

        /// <summary>Blurred occlusion of the current frame (white = open), or null when not rendered.</summary>
        public Texture2D? Texture => _frame == MuGame.FrameIndex ? _history : null;

        /// <summary>Renders depth and occlusion for this frame. Must run before the scene target is bound.</summary>
        public bool Render(WorldControl world, int sceneWidth, int sceneHeight)
        {
            GraphicsDevice? device = GraphicsManager.Instance?.GraphicsDevice;
            Effect? lighting = GraphicsManager.Instance?.DynamicLightingEffect;
            Effect? post = GraphicsManager.Instance?.SceneBloomEffect;
            Camera? camera = Camera.Instance;
            if (device == null || lighting == null || post == null || camera == null || world?.Terrain == null)
                return false;

            EffectTechnique? casterTechnique = lighting.Techniques["ShadowCaster"];
            EffectTechnique? aoTechnique = post.Techniques["Ssao"];
            EffectTechnique? temporalTechnique = post.Techniques["AoFilteredTemporal"];
            if (casterTechnique == null || aoTechnique == null || temporalTechnique == null)
                return false;

            int width = Math.Max(16, sceneWidth / 2);
            int height = Math.Max(16, sceneHeight / 2);
            EnsureTargets(device, width, height);

            // Last frame's depth becomes the history depth; history is only usable from consecutive frames.
            (_depth, _prevDepth) = (_prevDepth, _depth);
            bool historyUsable = _historyValid && _lastRenderedFrame == MuGame.FrameIndex - 1;

            RenderTargetBinding[] previousTargets = device.GetRenderTargets();
            Viewport previousViewport = device.Viewport;
            BlendState previousBlend = device.BlendState;
            DepthStencilState previousDepth = device.DepthStencilState;
            RasterizerState previousRaster = device.RasterizerState;
            EffectTechnique previousTechnique = lighting.CurrentTechnique;
            Matrix viewProjection = camera.ViewProjection;

            try
            {
                // --- depth pre-pass (window depth, cleared to the far plane) ---
                device.SetRenderTarget(_depth);
                device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.White, 1f, 0);
                device.BlendState = BlendState.Opaque;
                device.DepthStencilState = DepthStencilState.Default;
                device.RasterizerState = RasterizerState.CullNone;

                lighting.CurrentTechnique = casterTechnique;
                lighting.Parameters["LightViewProjection"]?.SetValue(viewProjection);
                // Terrain is solid for occlusion: no alpha-test holes through blended tile layers.
                lighting.Parameters["ShadowOpaqueCaster"]?.SetValue(1f);
                world.Terrain.RenderShadowMap(lighting, viewProjection);
                lighting.Parameters["ShadowOpaqueCaster"]?.SetValue(0f);
                ModelObject.AmbientOcclusionDepthPass = true;
                try
                {
                    DrawCasters(world, lighting, viewProjection, camera.Target);
                }
                finally
                {
                    ModelObject.AmbientOcclusionDepthPass = false;
                }

                // --- occlusion at half size ---
                SpriteBatchScope.ForceReset();
                SpriteBatch sprite = GraphicsManager.Instance!.Sprite;
                Matrix projection = camera.Projection;

                device.SetRenderTarget(_ao);
                post.CurrentTechnique = aoTechnique;
                post.Parameters["TexelStep"]?.SetValue(new Vector2(1f / width, 1f / height));
                post.Parameters["ProjScale"]?.SetValue(new Vector2(1f / projection.M11, 1f / projection.M22));
                post.Parameters["ProjDepth"]?.SetValue(new Vector2(projection.M33, projection.M43));
                post.Parameters["AoRadius"]?.SetValue(Radius);
                post.Parameters["AoIntensity"]?.SetValue(EffectiveIntensity);
                post.Parameters["AoProjRadius"]?.SetValue(Radius * projection.M11 * 0.5f);
                sprite.Begin(SpriteSortMode.Immediate, BlendState.Opaque, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone, post);
                sprite.Draw(_depth, new Rectangle(0, 0, width, height), Color.White);
                sprite.End();

                // Filter raw AO and reproject the history in one pass.
                device.SetRenderTarget(_historyNext);
                post.CurrentTechnique = temporalTechnique;
                post.Parameters["HistoryWeight"]?.SetValue(historyUsable ? HistoryWeight : 0f);
                if (historyUsable)
                {
                    Matrix viewToPreviousClip = Matrix.Invert(camera.View) * _prevViewProjection;
                    // Only the columns read by the shader are uploaded. This also keeps the
                    // ps_3_0 uniform registers consecutive when the OpenGL shader is translated.
                    post.Parameters["ViewToPreviousClipX"]?.SetValue(new Vector4(
                        viewToPreviousClip.M11, viewToPreviousClip.M21, viewToPreviousClip.M31, viewToPreviousClip.M41));
                    post.Parameters["ViewToPreviousClipY"]?.SetValue(new Vector4(
                        viewToPreviousClip.M12, viewToPreviousClip.M22, viewToPreviousClip.M32, viewToPreviousClip.M42));
                    post.Parameters["ViewToPreviousClipW"]?.SetValue(new Vector4(
                        viewToPreviousClip.M14, viewToPreviousClip.M24, viewToPreviousClip.M34, viewToPreviousClip.M44));
                }
                SetTexture(post, "BloomTexture", "BloomSampler", _history);
                SetTexture(post, "AoTexture", "AoSampler", _depth);
                SetTexture(post, "HistoryDepthTexture", "HistoryDepthSampler", _prevDepth);
                device.SamplerStates[1] = SamplerState.LinearClamp;
                device.SamplerStates[2] = SamplerState.PointClamp;
                device.SamplerStates[3] = SamplerState.PointClamp;
                sprite.Begin(SpriteSortMode.Immediate, BlendState.Opaque, SamplerState.LinearClamp, DepthStencilState.None, RasterizerState.CullNone, post);
                sprite.Draw(_ao, new Rectangle(0, 0, width, height), Color.White);
                sprite.End();
                SetTexture(post, "BloomTexture", "BloomSampler", null);
                SetTexture(post, "AoTexture", "AoSampler", null);
                SetTexture(post, "HistoryDepthTexture", "HistoryDepthSampler", null);
                device.Textures[1] = null;
                device.Textures[2] = null;
                device.Textures[3] = null;
                (_history, _historyNext) = (_historyNext, _history);

                _prevViewProjection = viewProjection;
                _historyValid = true;
                _lastRenderedFrame = MuGame.FrameIndex;
                _frame = MuGame.FrameIndex;
                return true;
            }
            finally
            {
                lighting.CurrentTechnique = previousTechnique;
                device.SetRenderTargets(previousTargets);
                device.Viewport = previousViewport;
                device.BlendState = previousBlend;
                device.DepthStencilState = previousDepth;
                device.RasterizerState = previousRaster;
                // The pre-pass reused LightViewProjection; give the main pass its shadow matrix back.
                GraphicsManager.Instance?.ShadowMapRenderer?.ApplyShadowParameters(lighting, force: true);
            }
        }

        private static void SetTexture(Effect effect, string textureName, string samplerName, Texture2D? texture)
        {
            EffectParameter? parameter = effect.Parameters[textureName] ?? effect.Parameters[samplerName];
            parameter?.SetValue(texture);
        }

        private void DrawCasters(WorldControl world, Effect lighting, Matrix viewProjection, Vector3 focus)
        {
            var terrain = world.Terrain;
            if (terrain == null)
                return;

            var objects = world.VisibleObjects;
            float maxDistSq = CasterDistance * CasterDistance;

            try
            {
                for (int i = 0; i < objects.Count; i++)
                {
                    if (objects[i] is not ModelObject model || model.Status != GameControlStatus.Ready || !model.Visible || model.Hidden)
                        continue;

                    Vector3 p = model.WorldPosition.Translation;
                    float dx = p.X - focus.X;
                    float dy = p.Y - focus.Y;
                    if (dx * dx + dy * dy > maxDistSq)
                        continue;

                    // Leftover map placements buried under the ground must not darken the terrain above them.
                    BoundingBox bounds = model.BoundingBoxWorld;
                    Vector3 center = (bounds.Min + bounds.Max) * 0.5f;
                    if (bounds.Max.Z < terrain.RequestTerrainHeight(center.X, center.Y) - 5f)
                        continue;

                    if (ModelObject.TryQueueStaticMapShadowCaster(model))
                        _instancedCasters.Add(model);
                    else
                        model.DrawShadowCaster(lighting, viewProjection);
                }

                if (_instancedCasters.Count > 0 &&
                    !ModelObject.FlushStaticMapShadowInstancingBatches(lighting, viewProjection, world))
                {
                    foreach (var model in _instancedCasters)
                        model.DrawShadowCaster(lighting, viewProjection);
                }
            }
            finally
            {
                ModelObject.ClearStaticMapShadowInstancingQueues();
                _instancedCasters.Clear();
            }
        }

        private void EnsureTargets(GraphicsDevice device, int width, int height)
        {
            if (_depth != null && !_depth.IsDisposed && _depth.Width == width && _depth.Height == height)
                return;

            DisposeTargets();
            _depth = new RenderTarget2D(device, width, height, false, SurfaceFormat.Single, DepthFormat.Depth24, 0, RenderTargetUsage.DiscardContents);
            _prevDepth = new RenderTarget2D(device, width, height, false, SurfaceFormat.Single, DepthFormat.Depth24, 0, RenderTargetUsage.DiscardContents);
            _ao = new RenderTarget2D(device, width, height, false, SurfaceFormat.Color, DepthFormat.None);
            _history = new RenderTarget2D(device, width, height, false, SurfaceFormat.Color, DepthFormat.None);
            _historyNext = new RenderTarget2D(device, width, height, false, SurfaceFormat.Color, DepthFormat.None);
            _historyValid = false;
        }

        private void DisposeTargets()
        {
            _depth?.Dispose();
            _prevDepth?.Dispose();
            _ao?.Dispose();
            _history?.Dispose();
            _historyNext?.Dispose();
            _depth = _prevDepth = _ao = _history = _historyNext = null;
            _historyValid = false;
        }

        public void Dispose() => DisposeTargets();
    }
}
