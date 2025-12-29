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
                Process = data.Process
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
}
#endif
