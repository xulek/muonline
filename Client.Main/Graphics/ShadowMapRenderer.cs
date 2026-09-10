using System;
using System.Collections.Generic;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Models;
using Client.Main.Objects;
using Client.Main.Objects.Player;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Runtime.CompilerServices;

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
        private readonly List<WorldObject> _nearbyObjects = new(256);
        private readonly PriorityQueue<ModelObject, float> _closestCasters = new();
        private readonly List<ModelObject> _selectedCasters = new(128);
        private readonly List<ModelObject> _staticInstancedCasters = new(128);
        private readonly HashSet<ModelObject> _renderedCasters = new();
        private readonly BoundingFrustum _lightFrustum = new BoundingFrustum(Matrix.Identity);
        private readonly ConditionalWeakTable<Effect, ShadowEffectBindings> _effectBindings = new();
        private Vector3 _lastCameraPosition = new(float.NaN, float.NaN, float.NaN);
        private Vector3 _lastCameraTarget = new(float.NaN, float.NaN, float.NaN);
        private float _lastShadowDistance = float.NaN;
        private bool _forceRender = true;
        private bool _shadowCasterSupported;
        private long _lastWorldInstanceId = long.MinValue;
        private int _lastWorldGeometryVersion = int.MinValue;
        private ulong _currentCasterSignature;
        private ulong _renderedCasterSignature = ulong.MaxValue;
        private bool _casterSelectionValid;
        private long _shadowParameterVersion = 1;


        private sealed class ShadowEffectBindings
        {
            public ShadowEffectBindings(Effect effect)
            {
                ShadowCasterTechnique = FindTechnique(effect, "ShadowCaster");
                DynamicLightingTechnique = FindTechnique(effect, "DynamicLighting");
                LightViewProjection = effect.Parameters["LightViewProjection"];
                ShadowMapTexelSize = effect.Parameters["ShadowMapTexelSize"];
                ShadowBias = effect.Parameters["ShadowBias"];
                ShadowNormalBias = effect.Parameters["ShadowNormalBias"];
                SunDirection = effect.Parameters["SunDirection"];
                ShadowsEnabled = effect.Parameters["ShadowsEnabled"];
                ShadowMap = effect.Parameters["ShadowMap"];
            }

            public EffectTechnique ShadowCasterTechnique { get; }
            public EffectTechnique DynamicLightingTechnique { get; }
            public EffectParameter LightViewProjection { get; }
            public EffectParameter ShadowMapTexelSize { get; }
            public EffectParameter ShadowBias { get; }
            public EffectParameter ShadowNormalBias { get; }
            public EffectParameter SunDirection { get; }
            public EffectParameter ShadowsEnabled { get; }
            public EffectParameter ShadowMap { get; }
            public long LastAppliedVersion = long.MinValue;
            public bool LastAppliedEnabled;

            private static EffectTechnique FindTechnique(Effect effect, string name)
            {
                for (int i = 0; i < effect.Techniques.Count; i++)
                {
                    EffectTechnique technique = effect.Techniques[i];
                    if (string.Equals(technique.Name, name, StringComparison.Ordinal))
                        return technique;
                }

                return null;
            }
        }

        public Matrix LightView { get; private set; } = Matrix.Identity;
        public Matrix LightProjection { get; private set; } = Matrix.Identity;
        public Matrix LightViewProjection { get; private set; } = Matrix.Identity;
        // Direction from light toward the world (used both for shading and the shadow-map camera forward)
        public Vector3 LightDirection { get; private set; } = Vector3.Normalize(Constants.SUN_DIRECTION);

        public RenderTarget2D ShadowMap => _shadowMap;
        public bool IsReady => _shadowMap != null && Constants.ENABLE_SHADOW_MAPPING && Constants.SUN_ENABLED
            && !(Constants.ENABLE_DAY_NIGHT_CYCLE && SunCycleManager.IsNight);

        /// <summary>
        /// Returns true only when the actor (or its modular root) is represented in the
        /// currently rendered shadow map. A globally ready shadow target does not imply
        /// that every nearby NPC was selected when the caster budget was reached.
        /// </summary>
        public bool HasRenderedCaster(ModelObject model)
        {
            if (!IsReady || model == null)
                return false;

            ModelObject root = model;
            while (root.Parent is ModelObject parentModel)
                root = parentModel;

            return _renderedCasters.Contains(root);
        }

        public ShadowMapRenderer(GraphicsDevice graphicsDevice)
        {
            _graphicsDevice = graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
            // Allocate the shadow target lazily when shadow mapping is actually rendered.
        }

        public void Dispose()
        {
            _shadowMap?.Dispose();
            _shadowMap = null;
            _shadowCasterSupported = false;
            _renderedCasters.Clear();
            _staticInstancedCasters.Clear();
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
            unchecked { _shadowParameterVersion++; }
            _renderedCasters.Clear();
            _forceRender = true; // make sure the new target is populated before reuse
        }

        public void RenderShadowMap(WorldControl world)
        {
            if (world == null || !world.EnableShadows || !Constants.ENABLE_SHADOW_MAPPING || !Constants.SUN_ENABLED)
            {
                _renderedCasters.Clear();
                return;
            }

            // Skip shadow rendering at night when day-night cycle is active
            if (Constants.ENABLE_DAY_NIGHT_CYCLE && SunCycleManager.IsNight)
            {
                _renderedCasters.Clear();
                return;
            }

            var camera = Camera.Instance;
            if (camera == null)
            {
                _renderedCasters.Clear();
                return;
            }

            _frameCounter++;
            int updateInterval = Math.Max(1, Constants.SHADOW_UPDATE_INTERVAL);

            EnsureRenderTarget();

            bool worldChanged = world.WorldInstanceId != _lastWorldInstanceId;
            if (worldChanged)
            {
                _lastWorldInstanceId = world.WorldInstanceId;
                _lastWorldGeometryVersion = int.MinValue;
                _casterSelectionValid = false;
                _renderedCasters.Clear();
                _forceRender = true;
            }

            float shadowDistance = ComputeShadowDistance(camera);
            bool cameraChanged = HasCameraChanged(camera, shadowDistance);
            bool lightDirectionChanged = HasLightDirectionChanged();
            bool lightViewChanged = cameraChanged || lightDirectionChanged;
            float frustumGuardBand = Math.Max(5f, shadowDistance * 0.01f);

            int worldGeometryVersion = world.CommitWorldGeometryVersionForRender();
            bool worldGeometryChanged = worldGeometryVersion != _lastWorldGeometryVersion;

            // Geometry versions are world-local and coalesced per render cycle. Refresh the
            // bounded local selection only when the active world actually changed.
            // A changed light view already requires a redraw. Select once, below, after
            // committing its matrix instead of querying/sorting candidates twice.
            if (!_forceRender && !lightViewChanged && (worldGeometryChanged || !_casterSelectionValid))
            {
                BuildCasterSelection(world, camera, shadowDistance, frustumGuardBand, skipLightFrustum: false);
                _lastWorldGeometryVersion = worldGeometryVersion;
            }
            else if (!_forceRender && !lightViewChanged)
            {
                // Animation and rotation changes do not necessarily increment the world's
                // spatial geometry tick. Rehash only the already bounded caster list so
                // animated nearby shadows remain current without scanning the whole world.
                _currentCasterSignature = ComputeCasterSignature(_selectedCasters);
            }

            bool relevantCastersChanged = _currentCasterSignature != _renderedCasterSignature;
            if (!_forceRender && !lightViewChanged && !relevantCastersChanged)
                return;

            // Throttle by preset interval. A pending signature remains different from the
            // rendered signature, so a deferred update is not accidentally lost.
            if (!_forceRender && _frameCounter % updateInterval != 0)
                return;

            var shadowEffect = GraphicsManager.Instance.DynamicLightingEffect;
            if (shadowEffect == null || _shadowMap == null)
            {
                _renderedCasters.Clear();
                return;
            }

            ShadowEffectBindings bindings = _effectBindings.GetValue(
                shadowEffect,
                static effect => new ShadowEffectBindings(effect));
            _shadowCasterSupported = bindings.ShadowCasterTechnique != null;
            if (!_shadowCasterSupported)
            {
                _renderedCasters.Clear();
                _renderedCasterSignature = _currentCasterSignature;
                _forceRender = false;
                return;
            }

            bool rebuildSelectionForNewLightView = _forceRender || lightViewChanged;
            UpdateLightMatrices(camera, shadowDistance);
            if (rebuildSelectionForNewLightView)
            {
                BuildCasterSelection(world, camera, shadowDistance, frustumGuardBand, skipLightFrustum: false);
                _lastWorldGeometryVersion = worldGeometryVersion;
            }
            _lastCameraPosition = camera.Position;
            _lastCameraTarget = camera.Target;
            _forceRender = false;
            _lightFrustum.Matrix = LightViewProjection;

            var previousTargets = _graphicsDevice.GetRenderTargets();
            var previousViewport = _graphicsDevice.Viewport;

            try
            {
                _graphicsDevice.SetRenderTarget(_shadowMap);
                _graphicsDevice.Viewport = new Viewport(0, 0, _shadowMap.Width, _shadowMap.Height);
                _graphicsDevice.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.White, 1f, 0);

                shadowEffect.CurrentTechnique = bindings.ShadowCasterTechnique;
                bindings.LightViewProjection?.SetValue(LightViewProjection);
                bindings.ShadowMapTexelSize?.SetValue(new Vector2(1f / _shadowMap.Width, 1f / _shadowMap.Height));
                bindings.ShadowBias?.SetValue(Constants.SHADOW_BIAS);
                bindings.ShadowNormalBias?.SetValue(Constants.SHADOW_NORMAL_BIAS);
                bindings.SunDirection?.SetValue(LightDirection);

                // Terrain as caster
                world.Terrain?.RenderShadowMap(shadowEffect, LightViewProjection);

                _renderedCasters.Clear();
                _staticInstancedCasters.Clear();
                for (int i = 0; i < _selectedCasters.Count; i++)
                {
                    ModelObject caster = _selectedCasters[i];
                    bool queuedForInstancing = false;
                    try
                    {
                        queuedForInstancing = ModelObject.TryQueueStaticMapShadowCaster(caster);
                    }
                    catch
                    {
                        // The normal caster path below remains the compatibility fallback.
                    }

                    if (queuedForInstancing)
                    {
                        _staticInstancedCasters.Add(caster);
                        continue;
                    }

                    if (caster.DrawShadowCaster(shadowEffect, LightViewProjection) > 0)
                        _renderedCasters.Add(caster);
                }

                if (_staticInstancedCasters.Count > 0)
                {
                    bool instancedDrawSucceeded = ModelObject.FlushStaticMapShadowInstancingBatches(
                        shadowEffect,
                        LightViewProjection,
                        world);

                    if (instancedDrawSucceeded)
                    {
                        ModelObject.RegisterStaticMapShadowInstancedObjects(_staticInstancedCasters.Count);
                        for (int i = 0; i < _staticInstancedCasters.Count; i++)
                            _renderedCasters.Add(_staticInstancedCasters[i]);
                    }
                    else
                    {
                        // A shader, texture or shared buffer may disappear between selection and
                        // rendering. Fall back to the original per-object caster path instead of
                        // leaving holes in the shadow map.
                        for (int i = 0; i < _staticInstancedCasters.Count; i++)
                        {
                            ModelObject caster = _staticInstancedCasters[i];
                            if (caster.DrawShadowCaster(shadowEffect, LightViewProjection) > 0)
                                _renderedCasters.Add(caster);
                        }
                    }
                }

                _staticInstancedCasters.Clear();
                _renderedCasterSignature = _currentCasterSignature;

                // Restore the regular technique when this effect exposes one.
                if (bindings.DynamicLightingTechnique != null)
                    shadowEffect.CurrentTechnique = bindings.DynamicLightingTechnique;
            }
            finally
            {
                // Restore render target - use SetRenderTarget(null) for backbuffer
                if (previousTargets == null || previousTargets.Length == 0)
                    _graphicsDevice.SetRenderTarget(null);
                else
                    _graphicsDevice.SetRenderTargets(previousTargets);
                _graphicsDevice.Viewport = previousViewport;
            }
        }

        private void BuildCasterSelection(
            WorldControl world,
            Camera camera,
            float shadowDistance,
            float frustumGuardBand,
            bool skipLightFrustum)
        {
            _lightFrustum.Matrix = LightViewProjection;
            float maxDistance = Math.Min(shadowDistance, Math.Max(100f, Constants.SHADOW_DISTANCE));
            float maxDistanceSq = maxDistance * maxDistance;
            Vector3 focus = camera.Target;
            if (focus == Vector3.Zero)
                focus = camera.Position;

            int maxCasters = Math.Max(1, Constants.SHADOW_MAX_CASTERS);
            _nearbyObjects.Clear();
            _closestCasters.Clear();
            _selectedCasters.Clear();
            world.CollectObjectsNear(focus, maxDistance + frustumGuardBand, _nearbyObjects);

            for (int i = 0; i < _nearbyObjects.Count; i++)
            {
                TryQueueCasterCandidate(
                    _nearbyObjects[i],
                    _lightFrustum,
                    frustumGuardBand,
                    focus,
                    maxDistanceSq,
                    maxCasters,
                    _closestCasters,
                    !skipLightFrustum);
            }

            while (_closestCasters.TryDequeue(out ModelObject model, out _))
                _selectedCasters.Add(model);

            _selectedCasters.Sort(static (left, right) =>
                RuntimeHelpers.GetHashCode(left).CompareTo(RuntimeHelpers.GetHashCode(right)));
            _currentCasterSignature = ComputeCasterSignature(_selectedCasters);
            _casterSelectionValid = true;
        }

        private static ulong ComputeCasterSignature(List<ModelObject> casters)
        {
            const ulong offset = 1469598103934665603UL;
            const ulong prime = 1099511628211UL;
            const float quantization = 4f;
            ulong hash = offset;

            for (int i = 0; i < casters.Count; i++)
            {
                ModelObject caster = casters[i];
                uint identity = unchecked((uint)RuntimeHelpers.GetHashCode(caster));
                uint modelIdentity = unchecked((uint)(caster.Model != null
                    ? RuntimeHelpers.GetHashCode(caster.Model)
                    : 0));
                hash ^= identity;
                hash *= prime;
                hash ^= modelIdentity;
                hash *= prime;
                hash ^= caster.AnimationPoseVersion;
                hash *= prime;

                if (caster.CanUseCompactStaticMapShadowSignature)
                {
                    hash ^= caster.TransformVersion;
                    hash *= prime;
                    continue;
                }

                Matrix world = caster.WorldPosition;
                Vector3 position = world.Translation;
                int qx = (int)MathF.Round(position.X / quantization);
                int qy = (int)MathF.Round(position.Y / quantization);
                int qz = (int)MathF.Round(position.Z / quantization);
                int qM11 = (int)MathF.Round(world.M11 * 128f);
                int qM12 = (int)MathF.Round(world.M12 * 128f);
                int qM21 = (int)MathF.Round(world.M21 * 128f);
                int qM22 = (int)MathF.Round(world.M22 * 128f);

                // Modular player/NPC actors load and swap body-part models independently.
                // Include direct child readiness/model state so the shadow map refreshes
                // when armor, boots, helm, wings or weapons become drawable.
                var children = caster.Children.GetSnapshotArray();
                int childCount = children.Length;
                hash ^= unchecked((uint)childCount);
                hash *= prime;
                for (int childIndex = 0; childIndex < childCount; childIndex++)
                {
                    if (children[childIndex] is not ModelObject child)
                        continue;

                    uint childIdentity = unchecked((uint)RuntimeHelpers.GetHashCode(child));
                    uint childModelIdentity = unchecked((uint)(child.Model != null
                        ? RuntimeHelpers.GetHashCode(child.Model)
                        : 0));
                    uint childState = unchecked((uint)child.Status) |
                                      (child.Hidden ? 1u << 8 : 0u) |
                                      (child.RenderShadow ? 1u << 9 : 0u);

                    hash ^= childIdentity;
                    hash *= prime;
                    hash ^= childModelIdentity;
                    hash *= prime;
                    hash ^= childState;
                    hash *= prime;
                }

                hash ^= unchecked((uint)qx);
                hash *= prime;
                hash ^= unchecked((uint)qy);
                hash *= prime;
                hash ^= unchecked((uint)qz);
                hash *= prime;
                hash ^= unchecked((uint)qM11);
                hash *= prime;
                hash ^= unchecked((uint)qM12);
                hash *= prime;
                hash ^= unchecked((uint)qM21);
                hash *= prime;
                hash ^= unchecked((uint)qM22);
                hash *= prime;
            }

            hash ^= unchecked((uint)casters.Count);
            return hash * prime;
        }

        public void ApplyShadowParameters(Effect effect, bool force = false)
        {
            if (effect == null)
                return;

            bool enabled = IsReady && _shadowMap != null && _shadowCasterSupported;
            ShadowEffectBindings bindings = _effectBindings.GetValue(effect, static value => new ShadowEffectBindings(value));
            if (!force &&
                bindings.LastAppliedVersion == _shadowParameterVersion &&
                bindings.LastAppliedEnabled == enabled)
            {
                return;
            }

            bindings.ShadowsEnabled?.SetValue(enabled ? 1.0f : 0.0f);
            bindings.ShadowMap?.SetValue(enabled ? _shadowMap : null);
            bindings.LightViewProjection?.SetValue(LightViewProjection);
            bindings.ShadowMapTexelSize?.SetValue(_shadowMap != null
                ? new Vector2(1f / _shadowMap.Width, 1f / _shadowMap.Height)
                : Vector2.Zero);
            bindings.ShadowBias?.SetValue(Constants.SHADOW_BIAS);
            bindings.ShadowNormalBias?.SetValue(Constants.SHADOW_NORMAL_BIAS);
            // Shader expects SunDirection as light->world; it internally negates it to get the vector toward the light.
            bindings.SunDirection?.SetValue(LightDirection);
            bindings.LastAppliedVersion = _shadowParameterVersion;
            bindings.LastAppliedEnabled = enabled;
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
            // Light view snaps focus to texel grid, so sub-2-texel camera moves produce no
            // visible change. Loosen threshold to skip redundant re-renders during slow pans.
            float positionTexels = texelWorldSize * 2f;
            float thresholdSq = Math.Max(1f, positionTexels * positionTexels);

            if (Vector3.DistanceSquared(camera.Position, _lastCameraPosition) > thresholdSq)
                return true;

            if (Vector3.DistanceSquared(camera.Target, _lastCameraTarget) > thresholdSq)
                return true;

            return Math.Abs(shadowDistance - _lastShadowDistance) > texelWorldSize;
        }

        private bool HasLightDirectionChanged()
        {
            Vector3 direction = Constants.SUN_DIRECTION;
            if (direction.LengthSquared() < 0.0001f)
                direction = new Vector3(-1f, 0f, -0.6f);
            direction.Normalize();

            return Vector3.Dot(direction, LightDirection) < 0.99999f;
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
            unchecked { _shadowParameterVersion++; }

            _lastShadowDistance = shadowDistance;
        }

        private static BoundingBox ExpandBoundingBox(BoundingBox box, float amount)
        {
            if (amount <= 0f)
                return box;

            Vector3 margin = new Vector3(amount);
            return new BoundingBox(box.Min - margin, box.Max + margin);
        }

        private static void TryQueueCasterCandidate(
            WorldObject worldObject,
            BoundingFrustum lightFrustum,
            float frustumGuardBand,
            Vector3 focus,
            float maxDistanceSq,
            int maxCasters,
            PriorityQueue<ModelObject, float> destination,
            bool testLightFrustum)
        {
            if (worldObject is not ModelObject model)
                return;

            if (!model.Visible || model.Status == GameControlStatus.Disposed || !model.RenderShadow)
                return;

            Vector3 objectPosition = model.WorldPosition.Translation;
            float distanceSquared = Vector3.DistanceSquared(objectPosition, focus);
            if (distanceSquared > maxDistanceSq)
                return;

            if (testLightFrustum)
            {
                BoundingBox bounds = ExpandBoundingBox(model.BoundingBoxWorld, frustumGuardBand);
                if (lightFrustum.Contains(bounds) == ContainmentType.Disjoint)
                    return;
            }

            // Keep player/NPC actor shadows stable when the caster budget is saturated.
            // The real distance still gates the candidate above; this weighted score only
            // decides which in-range actors survive the bounded selection.
            float selectionScore = distanceSquared;
            if (model is PlayerObject player)
                selectionScore *= player.IsMainWalker ? 0.05f : 0.35f;
            else if (model is NPCObject)
                selectionScore *= 0.55f;

            // PriorityQueue dequeues the smallest priority. Negating the score keeps the
            // least important selected caster at the head, so it can be replaced in O(log N).
            float priority = -selectionScore;
            if (destination.Count < maxCasters)
            {
                destination.Enqueue(model, priority);
                return;
            }

            destination.TryPeek(out _, out float farthestPriority);
            float worstSelectionScore = -farthestPriority;
            if (selectionScore >= worstSelectionScore)
                return;

            destination.Dequeue();
            destination.Enqueue(model, priority);
        }
    }
}
