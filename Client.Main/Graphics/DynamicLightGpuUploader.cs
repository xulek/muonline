using Client.Main.Controls;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Client.Main.Graphics
{
    /// <summary>
    /// Shared dynamic-light selection and GPU upload path for terrain, objects and instancing.
    /// </summary>
    public sealed class DynamicLightGpuUploader
    {
        private readonly struct SelectionCacheKey : IEquatable<SelectionCacheKey>
        {
            public SelectionCacheKey(int listId, int version, int cellX, int cellY, int radiusBucket)
            {
                ListId = listId;
                Version = version;
                CellX = cellX;
                CellY = cellY;
                RadiusBucket = radiusBucket;
            }

            private int ListId { get; }
            private int Version { get; }
            private int CellX { get; }
            private int CellY { get; }
            private int RadiusBucket { get; }

            public bool Equals(SelectionCacheKey other) =>
                ListId == other.ListId && Version == other.Version &&
                CellX == other.CellX && CellY == other.CellY &&
                RadiusBucket == other.RadiusBucket;

            public override bool Equals(object obj) => obj is SelectionCacheKey other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(ListId, Version, CellX, CellY, RadiusBucket);
        }

        private sealed class SelectionCacheEntry
        {
            public required int[] CandidateIndices { get; init; }
            public int CandidateCount { get; init; }
            public int LastFrame;
        }

        private const int MaxSelectionCacheEntries = 2048;
        private const int SelectionCacheMaxIdleFrames = 180;
        private static readonly Dictionary<SelectionCacheKey, SelectionCacheEntry> _selectionCache = new(512);
        private static int _selectionCacheListId;
        private static int _selectionCacheVersion;
        private static int _lastSelectionCachePruneFrame = int.MinValue;
        private static readonly List<SelectionCacheKey> _staleSelectionKeys = new(64);
        private sealed class EffectBindings
        {
            public EffectBindings(Effect effect)
            {
                LightPosInvRadius = effect.Parameters["LightPosInvRadius"];
                LightColorIntensity = effect.Parameters["LightColorIntensity"];
                ActiveLightCount = effect.Parameters["ActiveLightCount"];
            }

            public EffectParameter LightPosInvRadius { get; }
            public EffectParameter LightColorIntensity { get; }
            public EffectParameter ActiveLightCount { get; }
            public long LastSelectionToken = long.MinValue;

            public int ResolveCapacity(int fallbackCapacity)
            {
                int fallback = Math.Max(1, fallbackCapacity);
                int positionCapacity = GetEffectArrayCapacity(LightPosInvRadius, fallback);
                int colorCapacity = GetEffectArrayCapacity(LightColorIntensity, fallback);
                return Math.Max(1, Math.Min(positionCapacity, colorCapacity));
            }
        }

        private static readonly ConditionalWeakTable<Effect, EffectBindings> _effectBindings = new();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static EffectBindings GetBindings(Effect effect) =>
            _effectBindings.GetValue(effect, static value => new EffectBindings(value));

        internal static long GetAppliedSelectionToken(Effect effect) => GetBindings(effect).LastSelectionToken;

        private readonly int _fallbackCapacity;
        private readonly float _minInfluence;

        private Vector4[] _lightPosInvRadius = Array.Empty<Vector4>();
        private Vector4[] _lightColorIntensity = Array.Empty<Vector4>();
        private int[] _selectedIndices = Array.Empty<int>();
        private float[] _selectedScores = Array.Empty<float>();

        public DynamicLightGpuUploader(int fallbackCapacity = 32, float minInfluence = 0.001f)
        {
            _fallbackCapacity = Math.Max(1, fallbackCapacity);
            _minInfluence = Math.Max(0f, minInfluence);
        }

        public int Upload(
            Effect effect,
            IReadOnlyList<DynamicLightSnapshot> lights,
            Vector2 focus,
            int maxLights,
            float focusRadius = 0f,
            int lightsVersion = 0,
            float cacheCellSize = 0f)
        {
            if (effect == null)
                return 0;

            int capacity = ResolveEffectCapacity(effect, _fallbackCapacity);
            EnsureCapacity(capacity);

            if (lights == null || lights.Count == 0 || maxLights <= 0)
            {
                ApplyToEffect(effect, selectionToken: 0, activeLightCount: 0);
                return 0;
            }

            int budget = Math.Min(capacity, maxLights);
            int selectedCount;
            long selectionToken = long.MinValue;

            // GPU effect updates are submitted from the game/render thread. Keep the shared
            // spatial selection cache single-threaded and bypass it for any unexpected worker
            // call instead of paying a monitor enter for every rendered model.
            if (MuGame.IsMainThread && lightsVersion > 0 && cacheCellSize > 0f && float.IsFinite(cacheCellSize))
            {
                SelectionCacheEntry cached = GetOrCreateCachedSelection(
                    lights,
                    lightsVersion,
                    focus,
                    Math.Max(0f, focusRadius),
                    cacheCellSize);
                selectedCount = SelectRelevantLights(
                    lights,
                    focus,
                    Math.Max(0f, focusRadius),
                    budget,
                    cached.CandidateIndices,
                    cached.CandidateCount);
                selectionToken = CalculateSelectionToken(
                    RuntimeHelpers.GetHashCode(lights),
                    lightsVersion,
                    budget,
                    _selectedIndices,
                    selectedCount);
            }
            else
            {
                selectedCount = SelectRelevantLights(lights, focus, Math.Max(0f, focusRadius), budget);
            }

            for (int i = 0; i < selectedCount; i++)
            {
                int idx = _selectedIndices[i];
                if ((uint)idx >= (uint)lights.Count)
                    continue;

                var light = lights[idx];
                float radius = Math.Max(light.Radius, 0.0001f);
                float intensity = Math.Max(0f, light.Intensity);
                _lightPosInvRadius[i] = new Vector4(light.Position, 1f / radius);
                _lightColorIntensity[i] = new Vector4(light.Color, intensity);
            }

            ApplyToEffect(effect, selectionToken, selectedCount);
            return selectedCount;
        }

        public void Clear(Effect effect)
        {
            if (effect == null)
                return;

            ApplyToEffect(effect, selectionToken: 0, activeLightCount: 0);
        }

        private SelectionCacheEntry GetOrCreateCachedSelection(
            IReadOnlyList<DynamicLightSnapshot> lights,
            int lightsVersion,
            Vector2 focus,
            float focusRadius,
            float cacheCellSize)
        {
            float safeCellSize = Math.Max(1f, cacheCellSize);
            float invCell = 1f / safeCellSize;
            int cellX = (int)MathF.Floor(focus.X * invCell);
            int cellY = (int)MathF.Floor(focus.Y * invCell);
            int radiusBucket = (int)MathF.Ceiling(focusRadius / safeCellSize);
            int listId = RuntimeHelpers.GetHashCode(lights);
            var key = new SelectionCacheKey(listId, lightsVersion, cellX, cellY, radiusBucket);
            int frame = MuGame.FrameIndex;

            EnsureSelectionCacheGeneration(listId, lightsVersion);
            if (_selectionCache.TryGetValue(key, out var cached))
            {
                cached.LastFrame = frame;
                return cached;
            }

            float expandedRadius = radiusBucket * safeCellSize;
            float minX = cellX * safeCellSize - expandedRadius;
            float minY = cellY * safeCellSize - expandedRadius;
            float maxX = (cellX + 1) * safeCellSize + expandedRadius;
            float maxY = (cellY + 1) * safeCellSize + expandedRadius;

            int[] candidates = ArrayPool<int>.Shared.Rent(Math.Max(1, lights.Count));
            int candidateCount = 0;
            for (int i = 0; i < lights.Count; i++)
            {
                var light = lights[i];
                if (light.Intensity <= 0f || !IsFinite(light.Position) || !IsFinite(light.Color) ||
                    light.Radius <= 0.0001f)
                {
                    continue;
                }

                float nearestX = MathHelper.Clamp(light.Position.X, minX, maxX);
                float nearestY = MathHelper.Clamp(light.Position.Y, minY, maxY);
                float dx = light.Position.X - nearestX;
                float dy = light.Position.Y - nearestY;
                if (dx * dx + dy * dy < light.Radius * light.Radius)
                    candidates[candidateCount++] = i;
            }

            var created = new SelectionCacheEntry
            {
                CandidateIndices = candidates,
                CandidateCount = candidateCount,
                LastFrame = frame
            };

            // No second lookup is needed: only the main/render thread can enter the cache.
            _selectionCache[key] = created;
            PruneSelectionCache(frame);
            return created;
        }

        private static long CalculateSelectionToken(
            int listId,
            int version,
            int budget,
            int[] selectedIndices,
            int selectedCount)
        {
            unchecked
            {
                const ulong offsetBasis = 14695981039346656037UL;
                const ulong prime = 1099511628211UL;
                ulong hash = offsetBasis;
                hash = (hash ^ (uint)listId) * prime;
                hash = (hash ^ (uint)version) * prime;
                hash = (hash ^ (uint)budget) * prime;
                hash = (hash ^ (uint)selectedCount) * prime;
                for (int i = 0; i < selectedCount; i++)
                    hash = (hash ^ (uint)selectedIndices[i]) * prime;

                long token = (long)(hash & 0x7FFFFFFFFFFFFFFFUL);
                return token == 0 ? 1 : token;
            }
        }

        private static void EnsureSelectionCacheGeneration(int listId, int version)
        {
            if (_selectionCacheListId == listId && _selectionCacheVersion == version)
                return;

            foreach (var entry in _selectionCache.Values)
                ArrayPool<int>.Shared.Return(entry.CandidateIndices, clearArray: false);

            _selectionCache.Clear();
            _selectionCacheListId = listId;
            _selectionCacheVersion = version;
        }

        private static void PruneSelectionCache(int frame)
        {
            // Every new cell calls this method. Scan idle entries only once on a cleanup
            // frame, but always enforce the capacity limit, even again in the same frame.
            if (_selectionCache.Count <= MaxSelectionCacheEntries &&
                (frame % 120 != 0 || _lastSelectionCachePruneFrame == frame))
                return;

            _lastSelectionCachePruneFrame = frame;
            _staleSelectionKeys.Clear();
            foreach (var pair in _selectionCache)
            {
                if (_selectionCache.Count - _staleSelectionKeys.Count <= MaxSelectionCacheEntries &&
                    frame - pair.Value.LastFrame <= SelectionCacheMaxIdleFrames)
                {
                    continue;
                }
                _staleSelectionKeys.Add(pair.Key);
            }

            for (int i = 0; i < _staleSelectionKeys.Count; i++)
            {
                SelectionCacheKey key = _staleSelectionKeys[i];
                if (_selectionCache.Remove(key, out var removed))
                    ArrayPool<int>.Shared.Return(removed.CandidateIndices, clearArray: false);
            }
        }

        public static int ResolveEffectCapacity(Effect effect, int fallbackCapacity)
        {
            if (effect == null)
                return Math.Max(1, fallbackCapacity);

            return GetBindings(effect).ResolveCapacity(fallbackCapacity);
        }

        private static int GetEffectArrayCapacity(EffectParameter parameter, int fallback)
        {
            if (parameter?.Elements == null || parameter.Elements.Count <= 0)
                return fallback;

            return parameter.Elements.Count;
        }

        private void EnsureCapacity(int capacity)
        {
            if (_lightPosInvRadius.Length != capacity)
            {
                _lightPosInvRadius = new Vector4[capacity];
                _lightColorIntensity = new Vector4[capacity];
            }

            if (_selectedIndices.Length != capacity)
            {
                _selectedIndices = new int[capacity];
                _selectedScores = new float[capacity];
            }
        }

        private static bool IsFinite(in Vector3 value)
        {
            return !(float.IsNaN(value.X) || float.IsInfinity(value.X) ||
                     float.IsNaN(value.Y) || float.IsInfinity(value.Y) ||
                     float.IsNaN(value.Z) || float.IsInfinity(value.Z));
        }

        private int SelectRelevantLights(
            IReadOnlyList<DynamicLightSnapshot> lights,
            Vector2 focus,
            float focusRadius,
            int budget,
            int[] candidateIndices = null,
            int candidateCount = 0)
        {
            if (budget <= 0)
                return 0;

            int selected = 0;
            float weakestScore = float.MaxValue;
            int weakestIndex = 0;
            int iterationCount = candidateIndices == null ? lights.Count : candidateCount;

            for (int candidate = 0; candidate < iterationCount; candidate++)
            {
                int i = candidateIndices == null ? candidate : candidateIndices[candidate];
                if ((uint)i >= (uint)lights.Count)
                    continue;

                var light = lights[i];
                if (light.Intensity <= 0f || !IsFinite(light.Position) || !IsFinite(light.Color))
                    continue;

                float radius = light.Radius;
                float radiusSq = radius * radius;
                if (radiusSq <= 0.0001f)
                    continue;

                var lightPos = new Vector2(light.Position.X, light.Position.Y);
                float distSq = Vector2.DistanceSquared(lightPos, focus);
                float combinedRadius = radius + focusRadius;
                float combinedRadiusSq = combinedRadius * combinedRadius;
                if (distSq >= combinedRadiusSq)
                    continue;

                float edgeDistance = 0f;
                if (focusRadius > 0f)
                {
                    float dist = MathF.Sqrt(distSq);
                    edgeDistance = MathF.Max(0f, dist - focusRadius);
                }
                else
                {
                    edgeDistance = MathF.Sqrt(distSq);
                }

                float edgeDistanceSq = edgeDistance * edgeDistance;
                float score = (1f - edgeDistanceSq / radiusSq) * light.Intensity;
                if (score <= _minInfluence)
                    continue;

                if (selected < budget)
                {
                    _selectedIndices[selected] = i;
                    _selectedScores[selected] = score;
                    if (score < weakestScore)
                    {
                        weakestScore = score;
                        weakestIndex = selected;
                    }

                    selected++;
                }
                else if (score > weakestScore)
                {
                    _selectedIndices[weakestIndex] = i;
                    _selectedScores[weakestIndex] = score;

                    weakestScore = _selectedScores[0];
                    weakestIndex = 0;
                    for (int j = 1; j < selected; j++)
                    {
                        float s = _selectedScores[j];
                        if (s < weakestScore)
                        {
                            weakestScore = s;
                            weakestIndex = j;
                        }
                    }
                }
            }

            SortSelectedByScoreDesc(selected);
            return selected;
        }

        private void SortSelectedByScoreDesc(int count)
        {
            for (int i = 1; i < count; i++)
            {
                float keyScore = _selectedScores[i];
                int keyIdx = _selectedIndices[i];
                int j = i - 1;
                while (j >= 0 && _selectedScores[j] < keyScore)
                {
                    _selectedScores[j + 1] = _selectedScores[j];
                    _selectedIndices[j + 1] = _selectedIndices[j];
                    j--;
                }

                _selectedScores[j + 1] = keyScore;
                _selectedIndices[j + 1] = keyIdx;
            }
        }

        private void ApplyToEffect(Effect effect, long selectionToken, int activeLightCount)
        {
            if (effect == null)
                return;

            EffectBindings bindings = GetBindings(effect);
            if (selectionToken != long.MinValue && bindings.LastSelectionToken == selectionToken)
                return;

            int safeCount = Math.Max(0, activeLightCount);
            bindings.ActiveLightCount?.SetValue(safeCount);

            // The shader reads only [0, ActiveLightCount). Do not clear or re-upload the
            // unused tail of the fixed-size arrays; zero-light draws need only one integer.
            if (safeCount > 0)
            {
                bindings.LightPosInvRadius?.SetValue(_lightPosInvRadius);
                bindings.LightColorIntensity?.SetValue(_lightColorIntensity);
            }

            bindings.LastSelectionToken = selectionToken;
        }
    }
}
