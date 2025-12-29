#if DEBUG
using System.Text.Json.Serialization;

namespace Client.Main.DevTools
{
    /// <summary>
    /// Snapshot of a UI control in the hierarchy.
    /// Allocated only when serializing for web API (not per-frame).
    /// </summary>
    public class ControlSnapshot
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("type")]
        public string TypeName { get; set; }

        [JsonPropertyName("parentId")]
        public string ParentId { get; set; }

        /// <summary>Total update time including all children</summary>
        [JsonPropertyName("updateMs")]
        public double LastUpdateMs { get; set; }

        /// <summary>Total draw time including all children</summary>
        [JsonPropertyName("drawMs")]
        public double LastDrawMs { get; set; }

        /// <summary>This control's own update time (excluding children)</summary>
        [JsonPropertyName("selfUpdateMs")]
        public double SelfUpdateMs { get; set; }

        /// <summary>This control's own draw time (excluding children)</summary>
        [JsonPropertyName("selfDrawMs")]
        public double SelfDrawMs { get; set; }

        [JsonPropertyName("visible")]
        public bool Visible { get; set; }

        [JsonPropertyName("interactive")]
        public bool Interactive { get; set; }

        [JsonPropertyName("childCount")]
        public int ChildCount { get; set; }

        [JsonPropertyName("children")]
        public List<ControlSnapshot> Children { get; set; }
    }

    /// <summary>
    /// Snapshot of a world object with timing data.
    /// Allocated only when serializing for web API.
    /// </summary>
    public class WorldObjectSnapshot
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("objectId")]
        public ushort ObjectId { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("x")]
        public float X { get; set; }

        [JsonPropertyName("y")]
        public float Y { get; set; }

        [JsonPropertyName("updateMs")]
        public double UpdateTimeMs { get; set; }

        [JsonPropertyName("drawMs")]
        public double DrawTimeMs { get; set; }

        [JsonPropertyName("animMs")]
        public double AnimationTimeMs { get; set; }

        [JsonPropertyName("model")]
        public string ModelPath { get; set; }

        [JsonPropertyName("action")]
        public string CurrentAction { get; set; }

        [JsonPropertyName("visible")]
        public bool IsVisible { get; set; }

        [JsonPropertyName("inFrustum")]
        public bool IsInFrustum { get; set; }

        [JsonPropertyName("avgUpdateMs")]
        public double AvgUpdateMs { get; set; }

        [JsonPropertyName("avgDrawMs")]
        public double AvgDrawMs { get; set; }

        [JsonPropertyName("maxUpdateMs")]
        public double MaxUpdateMs { get; set; }

        [JsonPropertyName("maxDrawMs")]
        public double MaxDrawMs { get; set; }
    }

    /// <summary>
    /// Frame metrics - captured once per frame using struct to avoid allocations.
    /// Only converted to class when serializing for web.
    /// </summary>
    public struct FrameMetricsData
    {
        public int FrameIndex;
        public double TotalFrameTimeMs;
        public double UpdateTimeMs;
        public double DrawTimeMs;
        public double FPS;
        public double FpsAvg;
        public long Timestamp;

        // Phase breakdown
        public double SceneUpdateMs;
        public double SceneDrawMs;
        public double WorldUpdateMs;
        public double WorldDrawMs;

        // Budget tracking
        public const double FrameBudgetMs = 16.67;
        public double BudgetUsedPercent => (TotalFrameTimeMs / FrameBudgetMs) * 100.0;
        public bool IsOverBudget => TotalFrameTimeMs > FrameBudgetMs;

        // Subsystem metrics (copied from existing sources)
        public TerrainMetricsData Terrain;
        public ObjectMetricsData Objects;
        public BmdMetricsData Bmd;
        public PoolMetricsData Pool;
        public RenderSettingsData RenderSettings;
        public ProcessMetricsData Process;

        // Memory metrics
        public MemoryMetricsData Memory;

        // Detailed render metrics
        public RenderMetricsData Render;

        // Network metrics
        public NetworkMetricsData Network;

        // Profiler overhead tracking
        public double ProfilerOverheadMs;
    }

    /// <summary>
    /// Terrain rendering metrics (matches TerrainControl.FrameMetrics).
    /// </summary>
    public struct TerrainMetricsData
    {
        [JsonPropertyName("drawCalls")]
        public int DrawCalls;
        [JsonPropertyName("drawnTriangles")]
        public int DrawnTriangles;
        [JsonPropertyName("drawnBlocks")]
        public int DrawnBlocks;
        [JsonPropertyName("drawnCells")]
        public int DrawnCells;
    }

    /// <summary>
    /// Object rendering metrics (matches WalkableWorldControl.ObjectMetrics).
    /// </summary>
    public struct ObjectMetricsData
    {
        [JsonPropertyName("drawnTotal")]
        public int DrawnTotal;
        [JsonPropertyName("totalObjects")]
        public int TotalObjects;
        [JsonPropertyName("culledByFrustum")]
        public int CulledByFrustum;
        [JsonPropertyName("staticChunksVisible")]
        public int StaticChunksVisible;
        [JsonPropertyName("staticChunksTotal")]
        public int StaticChunksTotal;
        [JsonPropertyName("staticChunksCulled")]
        public int StaticChunksCulled;
        [JsonPropertyName("staticObjectsCulledByChunk")]
        public int StaticObjectsCulledByChunk;
    }

    /// <summary>
    /// BMD loader metrics (from BMDLoader.Instance).
    /// </summary>
    public struct BmdMetricsData
    {
        [JsonPropertyName("vbUpdates")]
        public int VBUpdates;
        [JsonPropertyName("ibUploads")]
        public int IBUploads;
        [JsonPropertyName("verticesTransformed")]
        public int VerticesTransformed;
        [JsonPropertyName("meshesProcessed")]
        public int MeshesProcessed;
        [JsonPropertyName("cacheHits")]
        public int CacheHits;
        [JsonPropertyName("cacheMisses")]
        public int CacheMisses;
    }

    /// <summary>
    /// Object pooling metrics (from ModelObject.GetPoolingStats).
    /// </summary>
    public struct PoolMetricsData
    {
        [JsonPropertyName("rents")]
        public int Rents;
        [JsonPropertyName("returns")]
        public int Returns;
        [JsonPropertyName("leaks")]
        public int Leaks => Rents - Returns;
    }

    /// <summary>
    /// Current render settings.
    /// </summary>
    public struct RenderSettingsData
    {
        [JsonPropertyName("fxaaEnabled")]
        public bool FXAAEnabled;
        [JsonPropertyName("alphaRgbEnabled")]
        public bool AlphaRGBEnabled;
        [JsonPropertyName("terrainGpuLighting")]
        public bool TerrainGpuLighting;
        [JsonPropertyName("objectGpuLighting")]
        public bool ObjectGpuLighting;
        [JsonPropertyName("batchSortingEnabled")]
        public bool BatchSortingEnabled;
    }

    /// <summary>
    /// Memory and GC metrics. Uses zero-overhead GC APIs.
    /// </summary>
    public struct MemoryMetricsData
    {
        // GC collection counts (cumulative this session)
        [JsonPropertyName("gen0")]
        public int Gen0Collections;
        [JsonPropertyName("gen1")]
        public int Gen1Collections;
        [JsonPropertyName("gen2")]
        public int Gen2Collections;

        // GC deltas (per frame)
        [JsonPropertyName("gen0Delta")]
        public int Gen0Delta;
        [JsonPropertyName("gen1Delta")]
        public int Gen1Delta;
        [JsonPropertyName("gen2Delta")]
        public int Gen2Delta;

        // Memory sizes
        [JsonPropertyName("heapBytes")]
        public long HeapSizeBytes;
        [JsonPropertyName("allocatedBytes")]
        public long AllocatedBytes;

        // Trend tracking
        [JsonPropertyName("heapDelta")]
        public long HeapDeltaBytes;
        [JsonPropertyName("isLeaking")]
        public bool IsLeaking;
        [JsonPropertyName("leakFrames")]
        public int ConsecutiveGrowthFrames;
    }

    /// <summary>
    /// Detailed render metrics by category.
    /// </summary>
    public struct RenderMetricsData
    {
        // Draw calls by category
        [JsonPropertyName("dcTerrain")]
        public int DrawCallsTerrain;
        [JsonPropertyName("dcModels")]
        public int DrawCallsModels;
        [JsonPropertyName("dcEffects")]
        public int DrawCallsEffects;
        [JsonPropertyName("dcUI")]
        public int DrawCallsUI;
        [JsonPropertyName("dcTotal")]
        public int DrawCallsTotal;

        // Triangles by category
        [JsonPropertyName("triTerrain")]
        public int TrianglesTerrain;
        [JsonPropertyName("triModels")]
        public int TrianglesModels;
        [JsonPropertyName("triEffects")]
        public int TrianglesEffects;

        // State changes
        [JsonPropertyName("texSwitches")]
        public int TextureSwitches;
        [JsonPropertyName("shaderSwitches")]
        public int ShaderSwitches;
        [JsonPropertyName("blendChanges")]
        public int BlendStateChanges;

        // Batching efficiency
        [JsonPropertyName("batchesMerged")]
        public int BatchesMerged;
        [JsonPropertyName("batchEfficiency")]
        public float BatchEfficiency;
    }

    /// <summary>
    /// Aggregated scope statistics across frames.
    /// </summary>
    public class ScopeStatsEntry
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("category")]
        public string Category { get; set; }

        [JsonPropertyName("calls")]
        public int CallCount { get; set; }

        [JsonPropertyName("totalMs")]
        public double TotalMs { get; set; }

        [JsonPropertyName("minMs")]
        public double MinMs { get; set; } = double.MaxValue;

        [JsonPropertyName("maxMs")]
        public double MaxMs { get; set; }

        [JsonPropertyName("avgMs")]
        public double AvgMs => CallCount > 0 ? TotalMs / CallCount : 0;

        [JsonPropertyName("lastMs")]
        public double LastMs { get; set; }

        // Ring buffer for P95 calculation (last 100 samples)
        [JsonIgnore]
        public double[] RecentSamples { get; set; } = new double[100];
        [JsonIgnore]
        public int SampleIndex { get; set; }
        [JsonIgnore]
        public int SampleCount { get; set; }

        [JsonPropertyName("p95Ms")]
        public double P95Ms { get; set; }

        public void RecordSample(double durationMs)
        {
            CallCount++;
            TotalMs += durationMs;
            LastMs = durationMs;
            if (durationMs < MinMs) MinMs = durationMs;
            if (durationMs > MaxMs) MaxMs = durationMs;

            // Add to ring buffer
            RecentSamples[SampleIndex] = durationMs;
            SampleIndex = (SampleIndex + 1) % 100;
            if (SampleCount < 100) SampleCount++;

            // Calculate P95 periodically (every 10 samples to avoid sorting overhead)
            if (CallCount % 10 == 0) UpdateP95();
        }

        private void UpdateP95()
        {
            if (SampleCount < 5) { P95Ms = MaxMs; return; }
            var sorted = new double[SampleCount];
            Array.Copy(RecentSamples, sorted, SampleCount);
            Array.Sort(sorted);
            int p95Index = (int)(SampleCount * 0.95);
            P95Ms = sorted[Math.Min(p95Index, SampleCount - 1)];
        }
    }

    /// <summary>
    /// Process-level metrics for the current client.
    /// </summary>
    public struct ProcessMetricsData
    {
        [JsonPropertyName("cpuPercent")]
        public double CpuPercent;
        [JsonPropertyName("gpuPercent")]
        public double GpuPercent;
        [JsonPropertyName("gpuAvailable")]
        public bool GpuAvailable;
        [JsonPropertyName("ramMb")]
        public double RamMb;
    }

    /// <summary>
    /// JSON-serializable version of FrameMetrics for web API.
    /// </summary>
    public class FrameMetricsJson
    {
        [JsonPropertyName("frame")]
        public int FrameIndex { get; set; }

        [JsonPropertyName("totalMs")]
        public double TotalFrameTimeMs { get; set; }

        [JsonPropertyName("updateMs")]
        public double UpdateTimeMs { get; set; }

        [JsonPropertyName("drawMs")]
        public double DrawTimeMs { get; set; }

        [JsonPropertyName("fps")]
        public double FPS { get; set; }

        [JsonPropertyName("fpsAvg")]
        public double FpsAvg { get; set; }

        [JsonPropertyName("timestamp")]
        public long Timestamp { get; set; }

        [JsonPropertyName("budgetPercent")]
        public double BudgetUsedPercent { get; set; }

        [JsonPropertyName("overBudget")]
        public bool IsOverBudget { get; set; }

        [JsonPropertyName("profilerOverheadMs")]
        public double ProfilerOverheadMs { get; set; }

        [JsonPropertyName("terrain")]
        public TerrainMetricsData Terrain { get; set; }

        [JsonPropertyName("objects")]
        public ObjectMetricsData Objects { get; set; }

        [JsonPropertyName("bmd")]
        public BmdMetricsData Bmd { get; set; }

        [JsonPropertyName("pool")]
        public PoolMetricsData Pool { get; set; }

        [JsonPropertyName("render")]
        public RenderSettingsData RenderSettings { get; set; }

        [JsonPropertyName("process")]
        public ProcessMetricsData Process { get; set; }

        [JsonPropertyName("memory")]
        public MemoryMetricsData Memory { get; set; }

        [JsonPropertyName("renderStats")]
        public RenderMetricsData RenderStats { get; set; }

        [JsonPropertyName("network")]
        public NetworkMetricsData Network { get; set; }

        public static FrameMetricsJson FromData(in FrameMetricsData data)
        {
            return new FrameMetricsJson
            {
                FrameIndex = data.FrameIndex,
                TotalFrameTimeMs = data.TotalFrameTimeMs,
                UpdateTimeMs = data.UpdateTimeMs,
                DrawTimeMs = data.DrawTimeMs,
                FPS = data.FPS,
                FpsAvg = data.FpsAvg,
                Timestamp = data.Timestamp,
                BudgetUsedPercent = data.BudgetUsedPercent,
                IsOverBudget = data.IsOverBudget,
                ProfilerOverheadMs = data.ProfilerOverheadMs,
                Terrain = data.Terrain,
                Objects = data.Objects,
                Bmd = data.Bmd,
                Pool = data.Pool,
                RenderSettings = data.RenderSettings,
                Process = data.Process,
                Memory = data.Memory,
                RenderStats = data.Render,
                Network = data.Network
            };
        }
    }

    /// <summary>
    /// Per-object timing entry - stored in fixed array, no allocations.
    /// </summary>
    public struct ObjectTimingEntry
    {
        public int HashCode;           // RuntimeHelpers.GetHashCode
        public string Name;            // Display name or type name
        public string TypeName;        // Full type name
        public float PositionX;        // World position X
        public float PositionY;        // World position Y
        public double UpdateMs;
        public double DrawMs;
        public double AnimationMs;
        public string ModelName;       // BMD model name
        public int CurrentAction;      // Animation action index
        public bool IsValid;
    }

    /// <summary>
    /// WebSocket message wrapper.
    /// </summary>
    public class WebSocketMessage
    {
        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("data")]
        public object Data { get; set; }
    }

    /// <summary>
    /// Hotspot entry for top slowest objects.
    /// </summary>
    public class HotspotEntry
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("posX")]
        public float PositionX { get; set; }

        [JsonPropertyName("posY")]
        public float PositionY { get; set; }

        [JsonPropertyName("totalMs")]
        public double TotalMs { get; set; }

        [JsonPropertyName("updateMs")]
        public double UpdateMs { get; set; }

        [JsonPropertyName("drawMs")]
        public double DrawMs { get; set; }

        [JsonPropertyName("animMs")]
        public double AnimMs { get; set; }

        [JsonPropertyName("model")]
        public string ModelName { get; set; }

        [JsonPropertyName("action")]
        public int CurrentAction { get; set; }
    }

    /// <summary>
    /// Profile scope entry for per-function timing.
    /// Stored in fixed array, supports hierarchical nesting.
    /// </summary>
    public struct ProfileScopeEntry
    {
        public string Name;
        public string Category;
        public int ParentIndex;     // -1 for root scopes
        public int Depth;
        public double DurationMs;
        public bool IsValid;
    }

    /// <summary>
    /// JSON-serializable scope data for flame graph.
    /// </summary>
    public class ProfileScopeJson
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("category")]
        public string Category { get; set; }

        [JsonPropertyName("durationMs")]
        public double DurationMs { get; set; }

        [JsonPropertyName("selfMs")]
        public double SelfMs { get; set; }

        [JsonPropertyName("children")]
        public List<ProfileScopeJson> Children { get; set; }
    }

    /// <summary>
    /// Network metrics - packets, bandwidth, latency.
    /// </summary>
    public struct NetworkMetricsData
    {
        // Per-second rates (calculated from rolling window)
        [JsonPropertyName("rxPkts")]
        public int PacketsReceivedPerSec;
        [JsonPropertyName("txPkts")]
        public int PacketsSentPerSec;
        [JsonPropertyName("rxBytes")]
        public int BytesReceivedPerSec;
        [JsonPropertyName("txBytes")]
        public int BytesSentPerSec;

        // Totals (session cumulative)
        [JsonPropertyName("totalRxPkts")]
        public long TotalPacketsReceived;
        [JsonPropertyName("totalTxPkts")]
        public long TotalPacketsSent;
        [JsonPropertyName("totalRxBytes")]
        public long TotalBytesReceived;
        [JsonPropertyName("totalTxBytes")]
        public long TotalBytesSent;

        // Latency
        [JsonPropertyName("pingMs")]
        public int PingMs;
        [JsonPropertyName("connected")]
        public bool IsConnected;
    }

    /// <summary>
    /// Single packet entry for recent packets list.
    /// </summary>
    public class PacketEntry
    {
        [JsonPropertyName("time")]
        public long Timestamp { get; set; }

        [JsonPropertyName("dir")]
        public string Direction { get; set; } // "RX" or "TX"

        [JsonPropertyName("code")]
        public string Code { get; set; } // e.g. "C1 F3 01"

        [JsonPropertyName("size")]
        public int Size { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } // Packet type name if known
    }
}
#endif
