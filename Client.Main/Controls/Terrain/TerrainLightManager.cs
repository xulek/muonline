using Client.Main.Controls;
using Client.Main.Core.Utilities;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Client.Main.Models;
using Client.Main.Objects;
using Client.Main.Scenes;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace Client.Main.Controls.Terrain
{
    /// <summary>
    /// Manages static and dynamic lights for terrain/object rendering.
    /// Dynamic-light state is rebuilt at a configurable fixed rate.
    /// </summary>
    public class TerrainLightManager
    {
        private readonly List<DynamicLight> _dynamicLights = new();
        private readonly HashSet<DynamicLight> _dynamicLightSet = new();
        private readonly Dictionary<WorldObject, HashSet<DynamicLight>> _lightsByOwner = new();
        private readonly List<DynamicLightSnapshot> _activeLights = new(32);
        private readonly List<DynamicLightSnapshot> _visibleLights = new(32);

        private readonly TerrainData _data;
        private readonly GameControl _parent;
        private int _activeLightsVersion;
        private int _visibleLightsVersion;
        private float _lightUpdateAccumulatorSeconds;
        private bool _forceSnapshotRefresh = true;

        public IReadOnlyList<DynamicLight> DynamicLights => _dynamicLights;
        public IReadOnlyList<DynamicLightSnapshot> ActiveLights => _activeLights;
        public IReadOnlyList<DynamicLightSnapshot> VisibleLights => _visibleLights;
        public int ActiveLightsVersion => _activeLightsVersion;
        public int VisibleLightsVersion => _visibleLightsVersion;
        public int OrphanLightsPrunedCount { get; private set; }
        public int DuplicateAddsRejectedCount { get; private set; }
        public int LastFrameRegisteredCount { get; private set; }
        public int LastFrameActiveCount { get; private set; }
        public int LastFrameVisibleCount { get; private set; }

        public TerrainLightManager(TerrainData data, GameControl parent)
        {
            _data = data;
            _parent = parent;
        }

        public void AddDynamicLight(DynamicLight light)
        {
            if (light == null)
                return;

            if (!_dynamicLightSet.Add(light))
            {
                DuplicateAddsRejectedCount++;
                return;
            }

            _dynamicLights.Add(light);
            RegisterOwnerLight(light);
            MarkSnapshotsDirty();
        }

        public void RemoveDynamicLight(DynamicLight light)
        {
            if (light == null)
                return;

            if (!_dynamicLightSet.Remove(light))
                return;

            _dynamicLights.Remove(light);
            UnregisterOwnerLight(light);
            MarkSnapshotsDirty();
        }

        public void RemoveDynamicLightsByOwner(WorldObject owner)
        {
            if (owner == null)
                return;

            if (!_lightsByOwner.TryGetValue(owner, out var lights) || lights.Count == 0)
            {
                _lightsByOwner.Remove(owner);
                return;
            }

            foreach (var light in lights)
            {
                if (_dynamicLightSet.Remove(light))
                    _dynamicLights.Remove(light);
            }

            _lightsByOwner.Remove(owner);
            MarkSnapshotsDirty();
        }

        public void CreateTerrainNormals()
        {
            _data.Normals = new Vector3[Constants.TERRAIN_SIZE * Constants.TERRAIN_SIZE];
            for (int y = 0; y < Constants.TERRAIN_SIZE; y++)
            {
                for (int x = 0; x < Constants.TERRAIN_SIZE; x++)
                {
                    int i = GetTerrainIndex(x, y);
                    var v1 = new Vector3((x + 1) * Constants.TERRAIN_SCALE, y * Constants.TERRAIN_SCALE,
                        _data.HeightMap[GetTerrainIndexRepeat(x + 1, y)].R);
                    var v2 = new Vector3((x + 1) * Constants.TERRAIN_SCALE, (y + 1) * Constants.TERRAIN_SCALE,
                        _data.HeightMap[GetTerrainIndexRepeat(x + 1, y + 1)].R);
                    var v3 = new Vector3(x * Constants.TERRAIN_SCALE, (y + 1) * Constants.TERRAIN_SCALE,
                        _data.HeightMap[GetTerrainIndexRepeat(x, y + 1)].R);
                    var v4 = new Vector3(x * Constants.TERRAIN_SCALE, y * Constants.TERRAIN_SCALE,
                        _data.HeightMap[GetTerrainIndexRepeat(x, y)].R);

                    var n1 = MathUtils.FaceNormalize(v1, v2, v3);
                    var n2 = MathUtils.FaceNormalize(v3, v4, v1);
                    _data.Normals[i] = n1 + n2;
                }
            }

            for (int i = 0; i < _data.Normals.Length; i++)
                _data.Normals[i] = Vector3.Normalize(_data.Normals[i]);
        }

        public void CreateFinalLightmap(Vector3 lightDirection)
        {
            _data.FinalLightMap = new Color[Constants.TERRAIN_SIZE * Constants.TERRAIN_SIZE];
            for (int y = 0; y < Constants.TERRAIN_SIZE; y++)
            {
                for (int x = 0; x < Constants.TERRAIN_SIZE; x++)
                {
                    int i = y * Constants.TERRAIN_SIZE + x;
                    float lum = MathHelper.Clamp(Vector3.Dot(_data.Normals[i], lightDirection) + 0.5f, 0f, 1f);
                    _data.FinalLightMap[i] = _data.LightData[i] * lum;
                }
            }
        }

        public void UpdateActiveLights(float deltaTime)
        {
            var world = _parent.World;
            if (SweepInvalidLights(world))
                MarkSnapshotsDirty();

            LastFrameRegisteredCount = _dynamicLights.Count;

            if (!Constants.ENABLE_DYNAMIC_LIGHTS || world == null || _dynamicLights.Count == 0)
            {
                ClearSnapshots();
                _lightUpdateAccumulatorSeconds = 0f;
                _forceSnapshotRefresh = true;
                return;
            }

            float safeDeltaTime = float.IsFinite(deltaTime) && deltaTime > 0f ? deltaTime : 0f;
            if (safeDeltaTime > 0f)
                _lightUpdateAccumulatorSeconds = MathF.Min(_lightUpdateAccumulatorSeconds + safeDeltaTime, 1f);

            int updateFps = Constants.ClampPerformanceFps(Constants.DYNAMIC_LIGHT_UPDATE_FPS);
            float updateInterval = 1f / updateFps;

            if (!_forceSnapshotRefresh)
            {
                if (_lightUpdateAccumulatorSeconds < updateInterval)
                    return;

                _lightUpdateAccumulatorSeconds %= updateInterval;
            }
            else
            {
                _lightUpdateAccumulatorSeconds = 0f;
            }

            _forceSnapshotRefresh = false;

            _activeLightsVersion++;
            _visibleLightsVersion++;
            _activeLights.Clear();
            _visibleLights.Clear();
            LastFrameActiveCount = 0;
            LastFrameVisibleCount = 0;

            bool isLoginScene = _parent.Scene is LoginScene;
            bool useLowQualityDistance =
                Constants.ENABLE_LOW_QUALITY_SWITCH &&
                !(isLoginScene && !Constants.ENABLE_LOW_QUALITY_IN_LOGIN_SCENE) &&
                Camera.Instance != null;

            Vector2 cam2 = Vector2.Zero;
            float lowQualityDistSq = 0f;
            if (useLowQualityDistance)
            {
                var camPos = Camera.Instance.Position;
                cam2 = new Vector2(camPos.X, camPos.Y);
                float d = Constants.LOW_QUALITY_DISTANCE;
                lowQualityDistSq = d * d;
            }

            for (int i = 0; i < _dynamicLights.Count; i++)
            {
                var light = _dynamicLights[i];
                if (!IsLightDataValid(light) || light.Intensity <= 0.001f)
                    continue;

                if (useLowQualityDistance)
                {
                    var lightPos = new Vector2(light.Position.X, light.Position.Y);
                    if (Vector2.DistanceSquared(cam2, lightPos) > lowQualityDistSq)
                        continue;
                }

                _activeLights.Add(new DynamicLightSnapshot(light.Position, light.Color, light.Radius, light.Intensity));
            }

            LastFrameActiveCount = _activeLights.Count;
            if (_activeLights.Count == 0)
                return;

            var camera = Camera.Instance;
            var frustum = camera?.Frustum;
            if (frustum == null)
            {
                _visibleLights.AddRange(_activeLights);
                LastFrameVisibleCount = _visibleLights.Count;
                return;
            }

            for (int i = 0; i < _activeLights.Count; i++)
            {
                var snapshot = _activeLights[i];
                if (IsLightVisibleInFrustum(frustum, snapshot))
                    _visibleLights.Add(snapshot);
            }

            LastFrameVisibleCount = _visibleLights.Count;
        }

        public Vector3 EvaluateDynamicLight(Vector2 position)
        {
            if (!Constants.ENABLE_DYNAMIC_LIGHTS || _activeLights.Count == 0)
                return Vector3.Zero;

            return EvaluateSnapshotLights(_activeLights, position);
        }

        public Vector3 EvaluateVisibleDynamicLight(Vector2 position)
        {
            if (!Constants.ENABLE_DYNAMIC_LIGHTS || _visibleLights.Count == 0)
                return Vector3.Zero;

            return EvaluateSnapshotLights(_visibleLights, position);
        }

        private static Vector3 EvaluateSnapshotLights(IReadOnlyList<DynamicLightSnapshot> lights, Vector2 position)
        {
            Vector3 result = Vector3.Zero;
            Vector3 negativeResult = Vector3.Zero;
            bool hasNegative = false;

            const float cpuLightScale = 150f;

            for (int i = 0; i < lights.Count; i++)
            {
                var light = lights[i];
                float radiusSq = light.Radius * light.Radius;
                if (radiusSq <= 0.0001f)
                    continue;

                var diff = new Vector2(light.Position.X, light.Position.Y) - position;
                float distSq = diff.LengthSquared();
                if (distSq > radiusSq)
                    continue;

                float t = 1f - (distSq / radiusSq);
                float factor = t * t;
                Vector3 contribution = light.Color * (cpuLightScale * light.Intensity * factor);

                if (contribution.X < 0f || contribution.Y < 0f || contribution.Z < 0f)
                {
                    if (!hasNegative)
                    {
                        negativeResult = contribution;
                        hasNegative = true;
                    }
                    else
                    {
                        negativeResult = Vector3.Min(negativeResult, contribution);
                    }
                }
                else
                {
                    result += contribution;
                }
            }

            if (hasNegative)
                result += negativeResult;

            return result;
        }

        private static bool IsLightVisibleInFrustum(BoundingFrustum frustum, in DynamicLightSnapshot light)
        {
            float baseRadius = Math.Max(light.Radius, 0.0001f);
            float guardBand = Math.Max(64f, baseRadius * 0.20f);
            float sphereRadius = baseRadius + guardBand;

            var sphere = new BoundingSphere(light.Position, sphereRadius);
            return frustum.Contains(sphere) != ContainmentType.Disjoint;
        }

        private bool SweepInvalidLights(WorldControl world)
        {
            if (_dynamicLights.Count == 0)
                return false;

            bool changed = false;
            for (int i = _dynamicLights.Count - 1; i >= 0; i--)
            {
                var light = _dynamicLights[i];
                if (!IsLightValid(light, world))
                {
                    if (light != null)
                    {
                        _dynamicLightSet.Remove(light);
                        UnregisterOwnerLight(light);
                    }

                    _dynamicLights.RemoveAt(i);
                    OrphanLightsPrunedCount++;
                    changed = true;
                }
            }

            return changed;
        }

        private void MarkSnapshotsDirty()
        {
            _forceSnapshotRefresh = true;
        }

        private void ClearSnapshots()
        {
            if (_activeLights.Count == 0 &&
                _visibleLights.Count == 0 &&
                LastFrameActiveCount == 0 &&
                LastFrameVisibleCount == 0)
            {
                return;
            }

            _activeLightsVersion++;
            _visibleLightsVersion++;
            _activeLights.Clear();
            _visibleLights.Clear();
            LastFrameActiveCount = 0;
            LastFrameVisibleCount = 0;
        }

        private static bool IsLightValid(DynamicLight light, WorldControl world)
        {
            if (light == null || world == null)
                return false;

            if (!IsLightDataValid(light))
                return false;

            var owner = light.Owner;
            if (owner == null)
                return false;

            if (owner.Status == GameControlStatus.Disposed || owner.Status == GameControlStatus.Error)
                return false;

            if (!ReferenceEquals(owner.World, world))
                return false;

            if (owner.Parent != null)
            {
                if (owner.Parent.Status == GameControlStatus.Disposed || owner.Parent.Status == GameControlStatus.Error)
                    return false;

                if (!ReferenceEquals(owner.Parent.World, world))
                    return false;
            }
            else if (!world.Objects.Contains(owner))
            {
                return false;
            }

            return true;
        }

        private static bool IsLightDataValid(DynamicLight light)
        {
            if (light == null)
                return false;

            if (!IsFinite(light.Position) || !IsFinite(light.Color))
                return false;

            if (float.IsNaN(light.Radius) || float.IsInfinity(light.Radius) || light.Radius <= 0f)
                return false;

            if (float.IsNaN(light.Intensity) || float.IsInfinity(light.Intensity))
                return false;

            return true;
        }

        private static bool IsFinite(Vector3 value)
        {
            return !(float.IsNaN(value.X) || float.IsInfinity(value.X) ||
                     float.IsNaN(value.Y) || float.IsInfinity(value.Y) ||
                     float.IsNaN(value.Z) || float.IsInfinity(value.Z));
        }

        private void RegisterOwnerLight(DynamicLight light)
        {
            var owner = light.Owner;
            if (owner == null)
                return;

            if (!_lightsByOwner.TryGetValue(owner, out var ownerLights))
            {
                ownerLights = new HashSet<DynamicLight>();
                _lightsByOwner[owner] = ownerLights;
            }

            ownerLights.Add(light);
        }

        private void UnregisterOwnerLight(DynamicLight light)
        {
            var owner = light.Owner;
            if (owner == null)
                return;

            if (!_lightsByOwner.TryGetValue(owner, out var ownerLights))
                return;

            ownerLights.Remove(light);
            if (ownerLights.Count == 0)
                _lightsByOwner.Remove(owner);
        }

        private static int GetTerrainIndex(int x, int y)
            => y * Constants.TERRAIN_SIZE + x;

        private static int GetTerrainIndexRepeat(int x, int y)
            => ((y & Constants.TERRAIN_SIZE_MASK) * Constants.TERRAIN_SIZE)
             + (x & Constants.TERRAIN_SIZE_MASK);
    }
}
