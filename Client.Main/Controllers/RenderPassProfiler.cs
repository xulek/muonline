using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Client.Main.Controllers;

/// <summary>
/// Lightweight render-thread profiler for the major CPU-side render passes.
/// It measures command preparation/submission time and main-thread allocations,
/// not true GPU execution time.
/// </summary>
public static class RenderPassProfiler
{
    public readonly record struct PassToken(long Timestamp, long AllocatedBytes);

    public readonly record struct Snapshot(
        double SceneDrawMs,
        double SceneDrawAllocatedKb,
        double SceneAfterMs,
        double SceneAfterAllocatedKb,
        double PostProcessMs,
        double PostProcessAllocatedKb,
        double FrameworkDrawMs,
        double FrameworkDrawAllocatedKb,
        double ShadowMs,
        double ShadowAllocatedKb,
        double WorldBaseMs,
        double WorldBaseAllocatedKb,
        double WorldObjectsMs,
        double WorldObjectsAllocatedKb,
        double TerrainOpaqueMs,
        double TerrainOpaqueAllocatedKb,
        double TerrainAfterMs,
        double TerrainAfterAllocatedKb,
        double PreviewMs,
        double PreviewAllocatedKb,
        int PreviewRenders,
        int PreviewCacheHits,
        int PreviewCacheMisses,
        int PreviewBudgetSkips);

#if PERFORMANCE_RELEASE
    public static bool Enabled => false;
    public static Snapshot Current => default;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void BeginFrame(bool enabled) { }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static PassToken Start() => default;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void AddSceneDraw(PassToken token) { }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void AddSceneAfter(PassToken token) { }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void AddPostProcess(PassToken token) { }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void AddFrameworkDraw(PassToken token) { }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void AddShadow(PassToken token) { }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void AddWorldBase(PassToken token) { }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void AddWorldObjects(PassToken token) { }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void AddTerrainOpaque(PassToken token) { }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void AddTerrainAfter(PassToken token) { }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void RecordPreviewCacheHit() { }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void RecordPreviewCacheMiss() { }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void RecordPreviewBudgetSkip() { }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void AddPreviewRender(PassToken token) { }
#else
    private static double _sceneDrawMs;
    private static double _sceneDrawAllocatedKb;
    private static double _sceneAfterMs;
    private static double _sceneAfterAllocatedKb;
    private static double _postProcessMs;
    private static double _postProcessAllocatedKb;
    private static double _frameworkDrawMs;
    private static double _frameworkDrawAllocatedKb;
    private static double _shadowMs;
    private static double _shadowAllocatedKb;
    private static double _worldBaseMs;
    private static double _worldBaseAllocatedKb;
    private static double _worldObjectsMs;
    private static double _worldObjectsAllocatedKb;
    private static double _terrainOpaqueMs;
    private static double _terrainOpaqueAllocatedKb;
    private static double _terrainAfterMs;
    private static double _terrainAfterAllocatedKb;
    private static double _previewMs;
    private static double _previewAllocatedKb;
    private static int _previewRenders;
    private static int _previewCacheHits;
    private static int _previewCacheMisses;
    private static int _previewBudgetSkips;

    public static bool Enabled { get; private set; }

    public static Snapshot Current => new(
        _sceneDrawMs,
        _sceneDrawAllocatedKb,
        _sceneAfterMs,
        _sceneAfterAllocatedKb,
        _postProcessMs,
        _postProcessAllocatedKb,
        _frameworkDrawMs,
        _frameworkDrawAllocatedKb,
        _shadowMs,
        _shadowAllocatedKb,
        _worldBaseMs,
        _worldBaseAllocatedKb,
        _worldObjectsMs,
        _worldObjectsAllocatedKb,
        _terrainOpaqueMs,
        _terrainOpaqueAllocatedKb,
        _terrainAfterMs,
        _terrainAfterAllocatedKb,
        _previewMs,
        _previewAllocatedKb,
        _previewRenders,
        _previewCacheHits,
        _previewCacheMisses,
        _previewBudgetSkips);

    public static void BeginFrame(bool enabled)
    {
        Enabled = enabled;
        if (!enabled)
            return;

        _sceneDrawMs = 0d;
        _sceneDrawAllocatedKb = 0d;
        _sceneAfterMs = 0d;
        _sceneAfterAllocatedKb = 0d;
        _postProcessMs = 0d;
        _postProcessAllocatedKb = 0d;
        _frameworkDrawMs = 0d;
        _frameworkDrawAllocatedKb = 0d;
        _shadowMs = 0d;
        _shadowAllocatedKb = 0d;
        _worldBaseMs = 0d;
        _worldBaseAllocatedKb = 0d;
        _worldObjectsMs = 0d;
        _worldObjectsAllocatedKb = 0d;
        _terrainOpaqueMs = 0d;
        _terrainOpaqueAllocatedKb = 0d;
        _terrainAfterMs = 0d;
        _terrainAfterAllocatedKb = 0d;
        _previewMs = 0d;
        _previewAllocatedKb = 0d;
        _previewRenders = 0;
        _previewCacheHits = 0;
        _previewCacheMisses = 0;
        _previewBudgetSkips = 0;
    }

    /// <summary>
    /// Diagnostics (MU_GPU_SYNC=1): wait for the GPU at every pass boundary, so pass times include the
    /// GPU execution of their commands instead of only the CPU submission. Serializes CPU and GPU, so the
    /// frame rate is lower; compare passes with each other, not with an unsynchronized run.
    /// </summary>
    public static readonly bool GpuSync = Environment.GetEnvironmentVariable("MU_GPU_SYNC") == "1";
    private static Microsoft.Xna.Framework.Graphics.OcclusionQuery _gpuFence;
    public static double GpuFenceTimeoutCount;

    public static void WaitForGpu()
    {
        var device = Client.Main.Controllers.GraphicsManager.Instance?.GraphicsDevice;
        if (device == null)
            return;

        _gpuFence ??= new Microsoft.Xna.Framework.Graphics.OcclusionQuery(device);
        _gpuFence.Begin();
        _gpuFence.End();
        long started = Stopwatch.GetTimestamp();
        while (!_gpuFence.IsComplete)
        {
            if (Stopwatch.GetElapsedTime(started).TotalMilliseconds > 100)
            {
                GpuFenceTimeoutCount++;
                return;
            }
        }
    }

    public static PassToken Start()
    {
        if (!Enabled)
            return default;
        if (GpuSync)
            WaitForGpu();
        return new PassToken(Stopwatch.GetTimestamp(), GC.GetAllocatedBytesForCurrentThread());
    }

    private static void Measure(PassToken token, ref double elapsedMs, ref double allocatedKb)
    {
        if (!Enabled || token.Timestamp == 0L)
            return;

        if (GpuSync)
            WaitForGpu();

        elapsedMs += Stopwatch.GetElapsedTime(token.Timestamp).TotalMilliseconds;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - token.AllocatedBytes;
        if (allocated > 0L)
            allocatedKb += allocated / 1024d;
    }

    public static void AddSceneDraw(PassToken token) => Measure(token, ref _sceneDrawMs, ref _sceneDrawAllocatedKb);
    public static void AddSceneAfter(PassToken token) => Measure(token, ref _sceneAfterMs, ref _sceneAfterAllocatedKb);
    public static void AddPostProcess(PassToken token) => Measure(token, ref _postProcessMs, ref _postProcessAllocatedKb);
    public static void AddFrameworkDraw(PassToken token) => Measure(token, ref _frameworkDrawMs, ref _frameworkDrawAllocatedKb);
    public static void AddShadow(PassToken token) => Measure(token, ref _shadowMs, ref _shadowAllocatedKb);
    public static void AddWorldBase(PassToken token) => Measure(token, ref _worldBaseMs, ref _worldBaseAllocatedKb);
    public static void AddWorldObjects(PassToken token) => Measure(token, ref _worldObjectsMs, ref _worldObjectsAllocatedKb);
    public static void AddTerrainOpaque(PassToken token) => Measure(token, ref _terrainOpaqueMs, ref _terrainOpaqueAllocatedKb);
    public static void AddTerrainAfter(PassToken token) => Measure(token, ref _terrainAfterMs, ref _terrainAfterAllocatedKb);

    public static void RecordPreviewCacheHit()
    {
        if (Enabled)
            _previewCacheHits++;
    }

    public static void RecordPreviewCacheMiss()
    {
        if (Enabled)
            _previewCacheMisses++;
    }

    public static void RecordPreviewBudgetSkip()
    {
        if (Enabled)
            _previewBudgetSkips++;
    }

    public static void AddPreviewRender(PassToken token)
    {
        if (!Enabled || token.Timestamp == 0L)
            return;

        Measure(token, ref _previewMs, ref _previewAllocatedKb);
        _previewRenders++;
    }
#endif
}
