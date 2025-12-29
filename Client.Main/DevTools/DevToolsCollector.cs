#if DEBUG
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Objects;

namespace Client.Main.DevTools
{
    /// <summary>
    /// Thread-safe metrics collector with minimal overhead.
    /// Uses fixed-size arrays and structs to avoid per-frame allocations.
    /// Measures its own overhead to ensure transparency.
    /// </summary>
    public sealed class DevToolsCollector
    {
        public static DevToolsCollector Instance { get; private set; }

        // Ring buffer for frame history (lock-free read, single-writer)
        private const int FrameHistorySize = 300; // ~5 seconds at 60fps
        private readonly FrameMetricsData[] _frameHistory = new FrameMetricsData[FrameHistorySize];
        private int _frameHistoryIndex = 0;

        // Double-buffered current frame (game thread writes, web thread reads)
        private FrameMetricsData _currentFrame;
        private FrameMetricsData _lastCompletedFrame;
        private readonly object _frameLock = new();
        private readonly ProfileScopeJson[] _scopeHistory = new ProfileScopeJson[FrameHistorySize];
        private readonly List<HotspotEntry>[] _hotspotHistory = new List<HotspotEntry>[FrameHistorySize];
        private List<HotspotEntry> _lastHotspots = new();

        // Per-object timing (fixed array, no allocations)
        private const int MaxTrackedObjects = 512;
        private readonly ObjectTimingEntry[] _objectTimings = new ObjectTimingEntry[MaxTrackedObjects];
        private int _objectCount = 0;

        // Per-function profiling scopes (fixed array)
        private const int MaxScopes = 256;
        private readonly ProfileScopeEntry[] _scopes = new ProfileScopeEntry[MaxScopes];
        private int _scopeCount = 0;
        private readonly int[] _scopeStack = new int[64]; // Max nesting depth
        private int _scopeStackTop = -1;
        private ProfileScopeJson _lastScopeTree;
        private readonly object _scopeLock = new();

        // Timing for profiler overhead measurement
        private long _frameProfilerStart;
        private double _accumulatedOverheadMs;

        // Process metrics sampling
        private const int ProcessSampleIntervalMs = 1000;
        private readonly Process _process = Process.GetCurrentProcess();
        private ProcessMetricsData _lastProcessMetrics;
        private long _lastProcessSampleTick;
        private TimeSpan _lastProcessCpuTime;
#if WINDOWS
        private GpuUsageSampler _gpuUsageSampler;
#endif

        // Recording state
        private bool _isRecording;
        private List<FrameMetricsData> _recordingBuffer;
        private readonly object _recordingLock = new();

        // Memory tracking (for delta calculation)
        private int _lastGen0, _lastGen1, _lastGen2;
        private long _lastHeapSize;
        private int _consecutiveGrowthFrames;

        // Scope stats aggregation (persistent across frames)
        private readonly Dictionary<string, ScopeStatsEntry> _scopeStats = new();
        private readonly object _scopeStatsLock = new();

        // Render stats per frame (reset each frame)
        private RenderMetricsData _frameRenderStats;

        private DevToolsCollector()
        {
            // Pre-initialize arrays
            for (int i = 0; i < FrameHistorySize; i++)
                _frameHistory[i] = new FrameMetricsData();
            for (int i = 0; i < MaxTrackedObjects; i++)
                _objectTimings[i] = new ObjectTimingEntry();
            for (int i = 0; i < MaxScopes; i++)
                _scopes[i] = new ProfileScopeEntry();
        }

        public static void Initialize()
        {
            if (!Constants.ENABLE_DEVTOOLS) return;
            Instance ??= new DevToolsCollector();
        }

        public static void Shutdown()
        {
            Instance = null;
        }

        #region Frame Lifecycle (called from game thread)

        /// <summary>
        /// Called at the start of each frame. Resets per-frame data.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void BeginFrame(int frameIndex)
        {
            _frameProfilerStart = Stopwatch.GetTimestamp();
            _accumulatedOverheadMs = 0;

            _currentFrame = new FrameMetricsData
            {
                FrameIndex = frameIndex,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            // Reset object tracking
            _objectCount = 0;
            for (int i = 0; i < MaxTrackedObjects; i++)
                _objectTimings[i].IsValid = false;

            // Reset scope tracking
            _scopeCount = 0;
            _scopeStackTop = -1;
            for (int i = 0; i < MaxScopes; i++)
                _scopes[i].IsValid = false;

            // Reset per-frame render stats
            _frameRenderStats = default;

            _accumulatedOverheadMs += ControlTimingWrapper.ElapsedMsSince(_frameProfilerStart);
        }

        private long _updateStart;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void BeginUpdate()
        {
            var start = Stopwatch.GetTimestamp();
            _updateStart = start;
            _accumulatedOverheadMs += ControlTimingWrapper.ElapsedMsSince(start);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EndUpdate()
        {
            var start = Stopwatch.GetTimestamp();
            _currentFrame.UpdateTimeMs = ControlTimingWrapper.ElapsedMs(_updateStart, start);
            _accumulatedOverheadMs += ControlTimingWrapper.ElapsedMsSince(start);
        }

        private long _drawStart;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void BeginDraw()
        {
            var start = Stopwatch.GetTimestamp();
            _drawStart = start;
            _accumulatedOverheadMs += ControlTimingWrapper.ElapsedMsSince(start);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EndDraw()
        {
            var start = Stopwatch.GetTimestamp();
            _currentFrame.DrawTimeMs = ControlTimingWrapper.ElapsedMs(_drawStart, start);
            _accumulatedOverheadMs += ControlTimingWrapper.ElapsedMsSince(start);
        }

        /// <summary>
        /// Called at the end of each frame. Finalizes metrics and swaps buffers.
        /// </summary>
        public void EndFrame()
        {
            var start = Stopwatch.GetTimestamp();

            _currentFrame.TotalFrameTimeMs = ControlTimingWrapper.ElapsedMs(_frameProfilerStart, start);
            _currentFrame.FPS = FPSCounter.Instance?.FPS ?? 0;
            _currentFrame.FpsAvg = FPSCounter.Instance?.FPS_AVG ?? 0;

            // Capture subsystem metrics from existing sources
            CaptureSubsystemMetrics();

            // Capture process metrics (CPU/GPU/RAM)
            CaptureProcessMetrics();

            // Capture memory metrics (GC/heap)
            CaptureMemoryMetrics();

            // Capture render metrics breakdown
            CaptureRenderMetrics();

            // Build scope tree for this frame
            BuildScopeTree();

            // Calculate profiler overhead
            _accumulatedOverheadMs += ControlTimingWrapper.ElapsedMsSince(start);
            _currentFrame.ProfilerOverheadMs = _accumulatedOverheadMs;

            var hotspotsSnapshot = BuildHotspotSnapshot(10);
            ProfileScopeJson scopeSnapshot;
            lock (_scopeLock)
            {
                scopeSnapshot = _lastScopeTree;
            }

            // Swap buffers (brief lock)
            lock (_frameLock)
            {
                _lastCompletedFrame = _currentFrame;
                _frameHistory[_frameHistoryIndex] = _currentFrame;
                _scopeHistory[_frameHistoryIndex] = scopeSnapshot;
                _hotspotHistory[_frameHistoryIndex] = hotspotsSnapshot;
                _frameHistoryIndex = (_frameHistoryIndex + 1) % FrameHistorySize;
            }

            _lastHotspots = hotspotsSnapshot;

            // Add to recording if active
            if (_isRecording)
            {
                lock (_recordingLock)
                {
                    _recordingBuffer?.Add(_currentFrame);
                }
            }
        }

        private void CaptureSubsystemMetrics()
        {
            // Get metrics from existing game sources (same as DebugPanel)
            var scene = MuGame.Instance?.ActiveScene;
            if (scene?.World is WalkableWorldControl walkable)
            {
                var terrain = walkable.Terrain;
                if (terrain != null)
                {
                    var tm = terrain.FrameMetrics;
                    _currentFrame.Terrain = new TerrainMetricsData
                    {
                        DrawCalls = tm.DrawCalls,
                        DrawnTriangles = tm.DrawnTriangles,
                        DrawnBlocks = tm.DrawnBlocks,
                        DrawnCells = tm.DrawnCells
                    };

                    _currentFrame.RenderSettings.TerrainGpuLighting = terrain.IsGpuTerrainLighting;
                }

                var om = walkable.ObjectMetrics;
                _currentFrame.Objects = new ObjectMetricsData
                {
                    DrawnTotal = om.DrawnTotal,
                    TotalObjects = om.TotalObjects,
                    CulledByFrustum = om.CulledByFrustum,
                    StaticChunksVisible = om.StaticChunksVisible,
                    StaticChunksTotal = om.StaticChunksTotal,
                    StaticChunksCulled = om.StaticChunksCulled,
                    StaticObjectsCulledByChunk = om.StaticObjectsCulledByChunk
                };
            }

            // BMD metrics
            var bmd = Content.BMDLoader.Instance;
            if (bmd != null)
            {
                _currentFrame.Bmd = new BmdMetricsData
                {
                    VBUpdates = bmd.LastFrameVBUpdates,
                    IBUploads = bmd.LastFrameIBUploads,
                    VerticesTransformed = bmd.LastFrameVerticesTransformed,
                    MeshesProcessed = bmd.LastFrameMeshesProcessed,
                    CacheHits = bmd.LastFrameCacheHits,
                    CacheMisses = bmd.LastFrameCacheMisses
                };
            }

            // Pool metrics
            var poolStats = ModelObject.GetPoolingStats();
            _currentFrame.Pool = new PoolMetricsData
            {
                Rents = (int)poolStats.Rents,
                Returns = (int)poolStats.Returns
            };

            // Render settings
            var gm = Controllers.GraphicsManager.Instance;
            if (gm != null)
            {
                _currentFrame.RenderSettings.FXAAEnabled = gm.IsFXAAEnabled;
                _currentFrame.RenderSettings.AlphaRGBEnabled = gm.IsAlphaRGBEnabled;
            }
            _currentFrame.RenderSettings.ObjectGpuLighting = Constants.ENABLE_DYNAMIC_LIGHTING_SHADER;
            _currentFrame.RenderSettings.BatchSortingEnabled = Constants.ENABLE_BATCH_OPTIMIZED_SORTING;
        }

        #endregion

        #region Render Stats Recording (called from Draw methods)

        /// <summary>
        /// Record a model draw call. Called from ModelObject.Draw().
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RecordModelDraw(int triangles = 0)
        {
            _frameRenderStats.DrawCallsModels++;
            _frameRenderStats.TrianglesModels += triangles;
        }

        /// <summary>
        /// Record an effect draw call. Called from effect/particle Draw().
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RecordEffectDraw(int triangles = 0)
        {
            _frameRenderStats.DrawCallsEffects++;
            _frameRenderStats.TrianglesEffects += triangles;
        }

        /// <summary>
        /// Record a UI draw call. Called from SpriteBatch wrapper.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RecordUIDraw()
        {
            _frameRenderStats.DrawCallsUI++;
        }

        /// <summary>
        /// Record a texture switch. Called from GraphicsDevice wrapper.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RecordTextureSwitch()
        {
            _frameRenderStats.TextureSwitches++;
        }

        /// <summary>
        /// Record a shader switch. Called from Effect.Apply wrapper.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RecordShaderSwitch()
        {
            _frameRenderStats.ShaderSwitches++;
        }

        /// <summary>
        /// Record a blend state change.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RecordBlendStateChange()
        {
            _frameRenderStats.BlendStateChanges++;
        }

        /// <summary>
        /// Record batched draw calls (merged into one).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RecordBatchMerge(int mergedCount)
        {
            _frameRenderStats.BatchesMerged += mergedCount;
        }

        #endregion

        #region Object Timing (called from WorldObject.Update/Draw)

        /// <summary>
        /// Record timing for a world object. Called from WorldObject instrumentation.
        /// Uses object hash as key, no allocations.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RecordObjectTiming(int objectHash, string name, string typeName, float posX, float posY, double updateMs, double drawMs, double animMs)
        {
            if (_objectCount >= MaxTrackedObjects) return;

            // Find existing or use next slot
            int slot = -1;
            for (int i = 0; i < _objectCount; i++)
            {
                if (_objectTimings[i].HashCode == objectHash)
                {
                    slot = i;
                    break;
                }
            }

            if (slot < 0)
            {
                slot = _objectCount++;
            }

            ref var entry = ref _objectTimings[slot];
            entry.HashCode = objectHash;
            entry.Name = name;
            entry.TypeName = typeName;
            entry.PositionX = posX;
            entry.PositionY = posY;
            entry.UpdateMs = updateMs;
            entry.DrawMs = drawMs;
            entry.AnimationMs = animMs;
            entry.IsValid = true;
        }

        #endregion

        #region Scope Profiling (called from game thread via ProfileScope)

        /// <summary>
        /// Push a new profiling scope onto the stack. Returns scope index.
        /// Called at the start of a code block being profiled.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int PushScope(string name, string category)
        {
            if (_scopeCount >= MaxScopes) return -1;

            int index = _scopeCount++;
            int parentIndex = _scopeStackTop >= 0 ? _scopeStack[_scopeStackTop] : -1;
            int depth = _scopeStackTop + 1;

            ref var scope = ref _scopes[index];
            scope.Name = name;
            scope.Category = category;
            scope.ParentIndex = parentIndex;
            scope.Depth = depth;
            scope.DurationMs = 0;
            scope.IsValid = true;

            // Push onto stack
            if (_scopeStackTop < _scopeStack.Length - 1)
            {
                _scopeStack[++_scopeStackTop] = index;
            }

            return index;
        }

        /// <summary>
        /// Pop a profiling scope and record its duration.
        /// Called at the end of a code block being profiled.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void PopScope(int scopeIndex, double durationMs)
        {
            if (scopeIndex < 0 || scopeIndex >= MaxScopes) return;

            _scopes[scopeIndex].DurationMs = durationMs;

            // Aggregate stats for this scope
            var name = _scopes[scopeIndex].Name;
            var category = _scopes[scopeIndex].Category;
            if (name != null)
            {
                lock (_scopeStatsLock)
                {
                    var key = category != null ? $"{category}:{name}" : name;
                    if (!_scopeStats.TryGetValue(key, out var stats))
                    {
                        stats = new ScopeStatsEntry { Name = name, Category = category };
                        _scopeStats[key] = stats;
                    }
                    stats.RecordSample(durationMs);
                }
            }

            // Pop from stack
            if (_scopeStackTop >= 0)
            {
                _scopeStackTop--;
            }
        }

        private void CaptureProcessMetrics()
        {
            var nowTick = Environment.TickCount64;
            if (_lastProcessSampleTick != 0 && nowTick - _lastProcessSampleTick < ProcessSampleIntervalMs)
            {
                _currentFrame.Process = _lastProcessMetrics;
                return;
            }

            try { _process.Refresh(); } catch { }

            var cpuTime = _process.TotalProcessorTime;
            double cpuPercent = _lastProcessMetrics.CpuPercent;

            if (_lastProcessSampleTick > 0)
            {
                var elapsedMs = nowTick - _lastProcessSampleTick;
                var deltaCpuMs = (cpuTime - _lastProcessCpuTime).TotalMilliseconds;
                if (elapsedMs > 0)
                {
                    cpuPercent = (deltaCpuMs / (elapsedMs * Environment.ProcessorCount)) * 100.0;
                    if (double.IsNaN(cpuPercent) || double.IsInfinity(cpuPercent) || cpuPercent < 0)
                        cpuPercent = 0;
                    if (cpuPercent > 100)
                        cpuPercent = 100;
                }
            }

            _lastProcessSampleTick = nowTick;
            _lastProcessCpuTime = cpuTime;

            double ramMb = 0;
            try { ramMb = _process.WorkingSet64 / (1024.0 * 1024.0); } catch { }

            bool gpuAvailable = false;
            double gpuPercent = 0;
#if WINDOWS
            _gpuUsageSampler ??= new GpuUsageSampler();
            if (_gpuUsageSampler.TryGetUsage(_process.Id, out var gpuValue))
            {
                gpuAvailable = true;
                gpuPercent = gpuValue;
            }
#endif

            var metrics = new ProcessMetricsData
            {
                CpuPercent = cpuPercent,
                RamMb = ramMb,
                GpuPercent = gpuPercent,
                GpuAvailable = gpuAvailable
            };

            _lastProcessMetrics = metrics;
            _currentFrame.Process = metrics;
        }

        private void CaptureMemoryMetrics()
        {
            // Get current GC collection counts
            int gen0 = GC.CollectionCount(0);
            int gen1 = GC.CollectionCount(1);
            int gen2 = GC.CollectionCount(2);

            // Get memory sizes
            long heapSize = GC.GetTotalMemory(false);
            long allocated = GC.GetTotalAllocatedBytes(false);

            // Calculate deltas
            int gen0Delta = gen0 - _lastGen0;
            int gen1Delta = gen1 - _lastGen1;
            int gen2Delta = gen2 - _lastGen2;
            long heapDelta = heapSize - _lastHeapSize;

            // Track consecutive growth for leak detection
            if (heapDelta > 0)
                _consecutiveGrowthFrames++;
            else
                _consecutiveGrowthFrames = 0;

            bool isLeaking = _consecutiveGrowthFrames > 60; // ~1 second at 60fps

            _currentFrame.Memory = new MemoryMetricsData
            {
                Gen0Collections = gen0,
                Gen1Collections = gen1,
                Gen2Collections = gen2,
                Gen0Delta = gen0Delta,
                Gen1Delta = gen1Delta,
                Gen2Delta = gen2Delta,
                HeapSizeBytes = heapSize,
                AllocatedBytes = allocated,
                HeapDeltaBytes = heapDelta,
                IsLeaking = isLeaking,
                ConsecutiveGrowthFrames = _consecutiveGrowthFrames
            };

            // Store for next frame delta calculation
            _lastGen0 = gen0;
            _lastGen1 = gen1;
            _lastGen2 = gen2;
            _lastHeapSize = heapSize;
        }

        private void CaptureRenderMetrics()
        {
            // Copy terrain triangles from existing terrain metrics
            _frameRenderStats.TrianglesTerrain = _currentFrame.Terrain.DrawnTriangles;
            _frameRenderStats.DrawCallsTerrain = _currentFrame.Terrain.DrawCalls;

            // Calculate totals
            _frameRenderStats.DrawCallsTotal = _frameRenderStats.DrawCallsTerrain
                + _frameRenderStats.DrawCallsModels
                + _frameRenderStats.DrawCallsEffects
                + _frameRenderStats.DrawCallsUI;

            // Calculate batch efficiency
            int totalPotential = _frameRenderStats.DrawCallsTotal + _frameRenderStats.BatchesMerged;
            _frameRenderStats.BatchEfficiency = totalPotential > 0
                ? (float)_frameRenderStats.BatchesMerged / totalPotential
                : 0f;

            _currentFrame.Render = _frameRenderStats;
        }

        /// <summary>
        /// Build hierarchical scope tree from flat array.
        /// Called at end of frame.
        /// </summary>
        private void BuildScopeTree()
        {
            if (_scopeCount == 0)
            {
                lock (_scopeLock) { _lastScopeTree = null; }
                return;
            }

            // Build tree from flat array
            var nodeMap = new Dictionary<int, ProfileScopeJson>();
            var roots = new List<ProfileScopeJson>();

            for (int i = 0; i < _scopeCount; i++)
            {
                if (!_scopes[i].IsValid) continue;

                var node = new ProfileScopeJson
                {
                    Name = _scopes[i].Name,
                    Category = _scopes[i].Category,
                    DurationMs = _scopes[i].DurationMs,
                    SelfMs = _scopes[i].DurationMs, // Will be adjusted below
                    Children = new List<ProfileScopeJson>()
                };
                nodeMap[i] = node;

                if (_scopes[i].ParentIndex < 0)
                {
                    roots.Add(node);
                }
            }

            // Link children to parents and calculate self time
            for (int i = 0; i < _scopeCount; i++)
            {
                if (!_scopes[i].IsValid) continue;

                int parentIndex = _scopes[i].ParentIndex;
                if (parentIndex >= 0 && nodeMap.TryGetValue(parentIndex, out var parent))
                {
                    parent.Children.Add(nodeMap[i]);
                    parent.SelfMs -= nodeMap[i].DurationMs;
                }
            }

            // Store as single root or wrapper
            lock (_scopeLock)
            {
                if (roots.Count == 1)
                {
                    _lastScopeTree = roots[0];
                }
                else if (roots.Count > 1)
                {
                    _lastScopeTree = new ProfileScopeJson
                    {
                        Name = "Frame",
                        Category = "root",
                        DurationMs = roots.Sum(r => r.DurationMs),
                        SelfMs = 0,
                        Children = roots
                    };
                }
                else
                {
                    _lastScopeTree = null;
                }
            }
        }

        /// <summary>
        /// Get the scope tree for the last frame. Thread-safe.
        /// </summary>
        public ProfileScopeJson GetScopeTree()
        {
            lock (_scopeLock)
            {
                return _lastScopeTree;
            }
        }

        /// <summary>
        /// Get aggregated scope statistics. Thread-safe.
        /// </summary>
        public List<ScopeStatsEntry> GetScopeStats()
        {
            lock (_scopeStatsLock)
            {
                return _scopeStats.Values.ToList();
            }
        }

        /// <summary>
        /// Reset scope statistics. Called via API.
        /// </summary>
        public void ResetScopeStats()
        {
            lock (_scopeStatsLock)
            {
                _scopeStats.Clear();
            }
        }

        #endregion

        #region Data Access (called from web server thread)

        /// <summary>
        /// Get the last completed frame. Thread-safe.
        /// </summary>
        public FrameMetricsData GetLastFrame()
        {
            lock (_frameLock)
            {
                return _lastCompletedFrame;
            }
        }

        /// <summary>
        /// Get frame history. Returns copy to avoid race conditions.
        /// </summary>
        public FrameMetricsData[] GetFrameHistory()
        {
            lock (_frameLock)
            {
                var result = new FrameMetricsData[FrameHistorySize];
                // Copy in chronological order (oldest first)
                int readIndex = _frameHistoryIndex;
                for (int i = 0; i < FrameHistorySize; i++)
                {
                    result[i] = _frameHistory[readIndex];
                    readIndex = (readIndex + 1) % FrameHistorySize;
                }
                return result;
            }
        }

        /// <summary>
        /// Get top N slowest objects from last completed frame.
        /// </summary>
        public List<HotspotEntry> GetHotspots(int count = 10)
        {
            var snapshot = _lastHotspots ?? new List<HotspotEntry>();
            if (count >= snapshot.Count)
                return snapshot;

            var result = new List<HotspotEntry>(count);
            for (int i = 0; i < count && i < snapshot.Count; i++)
                result.Add(snapshot[i]);
            return result;
        }

        public bool TryGetFrameSnapshot(int frameIndex, out FrameMetricsData frame, out ProfileScopeJson scopeTree, out List<HotspotEntry> hotspots)
        {
            lock (_frameLock)
            {
                for (int i = 0; i < FrameHistorySize; i++)
                {
                    if (_frameHistory[i].FrameIndex == frameIndex)
                    {
                        frame = _frameHistory[i];
                        scopeTree = _scopeHistory[i];
                        hotspots = _hotspotHistory[i];
                        return true;
                    }
                }
            }

            frame = default;
            scopeTree = null;
            hotspots = null;
            return false;
        }

        public bool TryGetFrameSnapshotByOffset(int offset, out FrameMetricsData frame, out ProfileScopeJson scopeTree, out List<HotspotEntry> hotspots)
        {
            frame = default;
            scopeTree = null;
            hotspots = null;

            if (offset < 0 || offset >= FrameHistorySize)
                return false;

            lock (_frameLock)
            {
                int index = _frameHistoryIndex - 1 - offset;
                if (index < 0)
                    index += FrameHistorySize;

                frame = _frameHistory[index];
                scopeTree = _scopeHistory[index];
                hotspots = _hotspotHistory[index];
            }

            return frame.FrameIndex > 0;
        }

        #endregion

        private List<HotspotEntry> BuildHotspotSnapshot(int count)
        {
            var result = new List<HotspotEntry>(count);

            if (_objectCount <= 0)
                return result;

            var entries = new List<ObjectTimingEntry>(_objectCount);
            for (int i = 0; i < _objectCount && i < MaxTrackedObjects; i++)
            {
                if (_objectTimings[i].IsValid)
                    entries.Add(_objectTimings[i]);
            }

            if (entries.Count == 0)
                return result;

            entries.Sort((a, b) => (b.UpdateMs + b.DrawMs).CompareTo(a.UpdateMs + a.DrawMs));

            int takeCount = Math.Min(count, entries.Count);
            for (int i = 0; i < takeCount; i++)
            {
                var e = entries[i];
                result.Add(new HotspotEntry
                {
                    Id = e.HashCode.ToString(),
                    Name = e.Name ?? $"Object_{e.HashCode:X8}",
                    Type = e.TypeName ?? "WorldObject",
                    PositionX = e.PositionX,
                    PositionY = e.PositionY,
                    TotalMs = e.UpdateMs + e.DrawMs,
                    UpdateMs = e.UpdateMs,
                    DrawMs = e.DrawMs,
                    AnimMs = e.AnimationMs
                });
            }

            return result;
        }

        #region Recording

        public void StartRecording()
        {
            lock (_recordingLock)
            {
                _recordingBuffer = new List<FrameMetricsData>(1800); // 30 seconds @ 60fps
                _isRecording = true;
            }
        }

        public List<FrameMetricsData> StopRecording()
        {
            lock (_recordingLock)
            {
                _isRecording = false;
                var result = _recordingBuffer;
                _recordingBuffer = null;
                return result ?? new List<FrameMetricsData>();
            }
        }

        public bool IsRecording => _isRecording;

        #endregion
    }
}
#endif
