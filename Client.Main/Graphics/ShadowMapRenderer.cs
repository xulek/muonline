using System;
using System.Collections.Generic;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Models;
using Client.Main.Objects;
using Client.Main.Objects.Player;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Client.Main.Graphics
{
    /// <summary>
    /// Renders a single directional shadow map for the main sun light.
    /// Keeps allocations low by reusing render targets and matrices.
    /// </summary>
    public sealed class ShadowMapRenderer : IDisposable
    {
        private readonly GraphicsDevice _graphicsDevice;
        private RenderTarget2D _shadowMap;
        private int _frameCounter = 0;
        private readonly List<(ModelObject model, float distSq)> _casterCandidates = new(64);
        private readonly BoundingFrustum _lightFrustum = new BoundingFrustum(Matrix.Identity);
        private Vector3 _lastCameraPosition = new(float.NaN, float.NaN, float.NaN);
        private Vector3 _lastCameraTarget = new(float.NaN, float.NaN, float.NaN);
        private float _lastShadowDistance = float.NaN;
        private bool _forceRender = true;

        public Matrix LightView { get; private set; } = Matrix.Identity;
        public Matrix LightProjection { get; private set; } = Matrix.Identity;
        public Matrix LightViewProjection { get; private set; } = Matrix.Identity;
        // Direction from light toward the world (used both for shading and the shadow-map camera forward)
        public Vector3 LightDirection { get; private set; } = Vector3.Normalize(Constants.SUN_DIRECTION);

        public RenderTarget2D ShadowMap => _shadowMap;
        public bool IsReady => _shadowMap != null && Constants.ENABLE_SHADOW_MAPPING && Constants.SUN_ENABLED
            && !(Constants.ENABLE_DAY_NIGHT_CYCLE && SunCycleManager.IsNight);

        public ShadowMapRenderer(GraphicsDevice graphicsDevice)
        {
            _graphicsDevice = graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
            EnsureRenderTarget();
        }

        public void Dispose()
        {
            _shadowMap?.Dispose();
            _shadowMap = null;
        }

        public void EnsureRenderTarget()
        {
            int targetSize = Math.Max(256, Constants.SHADOW_MAP_SIZE);
            if (_shadowMap != null && _shadowMap.Width == targetSize && !_shadowMap.IsDisposed)
                return;

            _shadowMap?.Dispose();

            // Prefer half-float to cut bandwidth; fall back to full float if unsupported
            SurfaceFormat desiredFormat = SurfaceFormat.HalfSingle;
            try
            {
                _shadowMap = new RenderTarget2D(
                    _graphicsDevice,
                    targetSize,
                    targetSize,
                    false,
                    desiredFormat,
                    DepthFormat.Depth24Stencil8,
                    0,
                    RenderTargetUsage.DiscardContents);
            }
            catch
            {
                desiredFormat = SurfaceFormat.Single;
                _shadowMap = new RenderTarget2D(
                    _graphicsDevice,
                    targetSize,
                    targetSize,
                    false,
                    desiredFormat,
                    DepthFormat.Depth24Stencil8,
                    0,
                    RenderTargetUsage.DiscardContents);
            }
            _forceRender = true; // make sure the new target is populated before reuse
        }

        public void RenderShadowMap(WorldControl world)
        {
            var sw = Stopwatch.StartNew();
            if (world == null || !world.EnableShadows || !Constants.ENABLE_SHADOW_MAPPING || !Constants.SUN_ENABLED)
                return;

            // Skip shadow rendering at night when day-night cycle is active
            if (Constants.ENABLE_DAY_NIGHT_CYCLE && SunCycleManager.IsNight)
                return;

            var camera = Camera.Instance;
            if (camera == null)
                return;

            _frameCounter++;
            int updateInterval = Math.Max(1, Constants.SHADOW_UPDATE_INTERVAL);

            // Compute camera per-frame movement to detect fast camera motion. When the camera
            // is moving quickly, increase the effective shadow update interval to avoid
            // expensive shadow-map renders every frame which cause FPS drops during camera motion.
            float cameraDelta = Vector3.Distance(camera.Position, _lastCameraPosition);
            float targetDelta = Vector3.Distance(camera.Target, _lastCameraTarget);
            float maxDelta = Math.Max(cameraDelta, targetDelta);

            EnsureRenderTarget();

            float shadowDistance = ComputeShadowDistance(camera);
            bool cameraChanged = HasCameraChanged(camera, shadowDistance);
            float frustumGuardBand = Math.Max(5f, shadowDistance * 0.01f);

            // Increase interval temporarily when camera moves more than a few texels per-frame
            float texelWorldSize = Math.Max(1f, shadowDistance) / Math.Max(1, _shadowMap?.Width ?? Math.Max(256, Constants.SHADOW_MAP_SIZE));
            int effectiveInterval = updateInterval;
            if (maxDelta > texelWorldSize * 2f)
            {
                // Camera moving quickly: back off shadow updates to reduce GPU work
                effectiveInterval = Math.Max(effectiveInterval, 8);
            }

            // Always update when camera/light range changed; otherwise respect interval for static views
            if (!_forceRender && !cameraChanged && _frameCounter % effectiveInterval != 0)
                return;

            UpdateLightMatrices(camera, shadowDistance);
            _lastCameraPosition = camera.Position;
            _lastCameraTarget = camera.Target;
            _forceRender = false;
            _lightFrustum.Matrix = LightViewProjection;
            var lightFrustum = _lightFrustum;

            var shadowEffect = GraphicsManager.Instance.DynamicLightingEffect;
            if (shadowEffect == null || _shadowMap == null)
                return;

            var previousTargets = _graphicsDevice.GetRenderTargets();
            var previousViewport = _graphicsDevice.Viewport;

            try
            {
                _graphicsDevice.SetRenderTarget(_shadowMap);
                _graphicsDevice.Viewport = new Viewport(0, 0, _shadowMap.Width, _shadowMap.Height);
                _graphicsDevice.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.White, 1f, 0);

                shadowEffect.CurrentTechnique = shadowEffect.Techniques["ShadowCaster"];
                shadowEffect.Parameters["LightViewProjection"]?.SetValue(LightViewProjection);
                shadowEffect.Parameters["ShadowMapTexelSize"]?.SetValue(new Vector2(1f / _shadowMap.Width, 1f / _shadowMap.Height));
                shadowEffect.Parameters["ShadowBias"]?.SetValue(Constants.SHADOW_BIAS);
                shadowEffect.Parameters["ShadowNormalBias"]?.SetValue(Constants.SHADOW_NORMAL_BIAS);
                shadowEffect.Parameters["SunDirection"]?.SetValue(LightDirection);

                // Terrain as caster
                world.Terrain?.RenderShadowMap(shadowEffect, LightViewProjection);

                // Collect and sort shadow casters by distance (closest first for best quality)
                float maxDistanceSq = Constants.SHADOW_DISTANCE * Constants.SHADOW_DISTANCE;
                Vector3 focus = Camera.Instance.Target;
                if (focus == Vector3.Zero)
                    focus = Camera.Instance.Position;

                _casterCandidates.Clear();
                int maxCasters = Math.Max(1, Constants.SHADOW_MAX_CASTERS);

                // Maintain a fixed-size collection of the closest casters without sorting the whole set.
                // For small maxCasters (typical), this is faster than collecting and sorting a large list.
                var snapshot = world.Objects.GetSnapshot();
                for (int i = 0; i < snapshot.Count; i++)
                {
                    if (snapshot[i] is not ModelObject model)
                        continue;

                    if (!model.Visible || model.Status == GameControlStatus.Disposed || !model.RenderShadow)
                        continue;

                    Vector3 objPos = model.WorldPosition.Translation;
                    BoundingBox bounds = ExpandBoundingBox(model.BoundingBoxWorld, frustumGuardBand);
                    if (lightFrustum.Contains(bounds) == ContainmentType.Disjoint)
                        continue;

                    float distSq = Vector3.DistanceSquared(objPos, focus);
                    if (distSq > maxDistanceSq)
                        continue;

                    if (_casterCandidates.Count < maxCasters)
                    {
                        _casterCandidates.Add((model, distSq));
                    }
                    else
                    {
                        // Replace the farthest candidate if this one is closer
                        int worstIndex = 0;
                        float worstDist = _casterCandidates[0].distSq;
                        for (int j = 1; j < _casterCandidates.Count; j++)
                        {
                            if (_casterCandidates[j].distSq > worstDist)
                            {
                                worstDist = _casterCandidates[j].distSq;
                                worstIndex = j;
                            }
                        }

                        if (distSq < worstDist)
                            _casterCandidates[worstIndex] = (model, distSq);
                    }
                }

                // Draw selected casters (order by closeness for quality)
                if (_casterCandidates.Count > 1)
                    _casterCandidates.Sort((a, b) => a.distSq.CompareTo(b.distSq));

                int casterCount = _casterCandidates.Count;
                for (int i = 0; i < casterCount; i++)
                {
                    var (model, _) = _casterCandidates[i];
                    model.DrawShadowCaster(shadowEffect, LightViewProjection);
                }

                // Restore default technique for later draws
                shadowEffect.CurrentTechnique = shadowEffect.Techniques["DynamicLighting"];
            }
            finally
            {
                // Restore render target - use SetRenderTarget(null) for backbuffer
                if (previousTargets == null || previousTargets.Length == 0)
                    _graphicsDevice.SetRenderTarget(null);
                else
                    _graphicsDevice.SetRenderTargets(previousTargets);
                _graphicsDevice.Viewport = previousViewport;
                sw.Stop();
                // Log slow shadow renders (>8ms) for diagnostics when logging is available
                if (sw.ElapsedMilliseconds > 8)
                {
                    var logger = MuGame.AppLoggerFactory?.CreateLogger("ShadowProfiler");
                    logger?.LogWarning("Slow shadow render: {Ms}ms, casters={Casters}, shadowSize={Size}", sw.ElapsedMilliseconds, _casterCandidates.Count, _shadowMap?.Width ?? 0);
                }
            }
        }

        public void ApplyShadowParameters(Effect effect)
        {
            if (effect == null)
                return;

            bool enabled = IsReady && _shadowMap != null;
            effect.Parameters["ShadowsEnabled"]?.SetValue(enabled ? 1.0f : 0.0f);
            effect.Parameters["ShadowMap"]?.SetValue(enabled ? _shadowMap : null);
            effect.Parameters["LightViewProjection"]?.SetValue(LightViewProjection);
            effect.Parameters["ShadowMapTexelSize"]?.SetValue(_shadowMap != null
                ? new Vector2(1f / _shadowMap.Width, 1f / _shadowMap.Height)
                : Vector2.Zero);
            effect.Parameters["ShadowBias"]?.SetValue(Constants.SHADOW_BIAS);
            effect.Parameters["ShadowNormalBias"]?.SetValue(Constants.SHADOW_NORMAL_BIAS);
            // Shader expects SunDirection as light->world; it internally negates it to get the vector toward the light.
            effect.Parameters["SunDirection"]?.SetValue(LightDirection);
        }

        private float ComputeShadowDistance(Camera camera)
        {
            float configured = Math.Max(100f, Constants.SHADOW_DISTANCE);
            float viewRange = Math.Max(1f, camera.ViewFar);
            return Math.Min(configured, viewRange);
        }

        private bool HasCameraChanged(Camera camera, float shadowDistance)
        {
            if (_forceRender || float.IsNaN(_lastShadowDistance))
                return true;

            int shadowMapSize = _shadowMap?.Width ?? Math.Max(256, Constants.SHADOW_MAP_SIZE);
            float texelWorldSize = shadowDistance / shadowMapSize;
            float thresholdSq = Math.Max(1f, texelWorldSize * texelWorldSize);

            if (Vector3.DistanceSquared(camera.Position, _lastCameraPosition) > thresholdSq)
                return true;

            if (Vector3.DistanceSquared(camera.Target, _lastCameraTarget) > thresholdSq)
                return true;

            return Math.Abs(shadowDistance - _lastShadowDistance) > texelWorldSize;
        }

        private void UpdateLightMatrices(Camera camera, float shadowDistance)
        {
            Vector3 sunDir = Constants.SUN_DIRECTION;
            if (sunDir.LengthSquared() < 0.0001f)
                sunDir = new Vector3(-1f, 0f, -0.6f);
            // LightDirection points from light to the world; shader receives this as SunDirection
            LightDirection = Vector3.Normalize(sunDir);

            float orthoSize = shadowDistance;
            float nearPlane = Math.Max(1f, Constants.SHADOW_NEAR_PLANE);
            float farPlane = Math.Max(shadowDistance * 2f, Constants.SHADOW_FAR_PLANE);

            Vector3 focus = camera.Target;
            if (focus == Vector3.Zero)
                focus = camera.Position + new Vector3(0, 0, -100f);

            // Use world Z as up to match the rest of the renderer (terrain uses Z for height)
            Vector3 up = Math.Abs(Vector3.Dot(Vector3.UnitZ, LightDirection)) > 0.9f ? Vector3.UnitX : Vector3.UnitZ;

            // Build initial light view matrix to get the light space axes
            Vector3 lightPos = focus - LightDirection * shadowDistance;
            Matrix tempLightView = Matrix.CreateLookAt(lightPos, focus, up);

            // Stabilize shadow map: snap focus point to shadow map texel grid
            // This prevents "swimming" artifacts when the camera moves
            int shadowMapSize = _shadowMap?.Width ?? Math.Max(256, Constants.SHADOW_MAP_SIZE);
            float texelSize = orthoSize / shadowMapSize;

            // Transform focus to light space
            Vector3 focusLightSpace = Vector3.Transform(focus, tempLightView);

            // Snap to texel grid in light space (X and Y only, Z is depth)
            focusLightSpace.X = MathF.Floor(focusLightSpace.X / texelSize) * texelSize;
            focusLightSpace.Y = MathF.Floor(focusLightSpace.Y / texelSize) * texelSize;

            // Transform back to world space
            Matrix invTempLightView = Matrix.Invert(tempLightView);
            Vector3 snappedFocus = Vector3.Transform(focusLightSpace, invTempLightView);

            // Rebuild light matrices with snapped focus
            lightPos = snappedFocus - LightDirection * shadowDistance;

            LightView = Matrix.CreateLookAt(lightPos, snappedFocus, up);
            LightProjection = Matrix.CreateOrthographic(orthoSize, orthoSize, nearPlane, farPlane);
            LightViewProjection = LightView * LightProjection;

            _lastShadowDistance = shadowDistance;
        }

        private static BoundingBox ExpandBoundingBox(BoundingBox box, float amount)
        {
            if (amount <= 0f)
                return box;

            Vector3 margin = new Vector3(amount);
            return new BoundingBox(box.Min - margin, box.Max + margin);
        }
    }
}
