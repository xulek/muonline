using System.Runtime.CompilerServices;

namespace Client.Main.Controllers;

/// <summary>
/// Diagnostics: device draw calls and texture changes attributed to the world render steps.
/// Each <see cref="Mark"/> charges the draws issued since the previous mark to the given step.
/// Active only while <see cref="RenderPassProfiler"/> is enabled; compiled out in performance builds.
/// </summary>
public static class RenderDrawCounter
{
    public enum Step
    {
        Other,
        Terrain,
        StaticMap,
        Solid,
        Transparent,
        After,
        Effects,
        Overlay,
        Count
    }

#if PERFORMANCE_RELEASE
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void BeginFrame() { }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Mark(Step step) { }

    public static long GetDraws(Step step) => 0;
    public static long GetTextures(Step step) => 0;
#else
    private static readonly long[] Draws = new long[(int)Step.Count];
    private static readonly long[] Textures = new long[(int)Step.Count];
    private static long _lastDraws, _lastTextures;

    public static void BeginFrame()
    {
        System.Array.Clear(Draws);
        System.Array.Clear(Textures);
        var device = GraphicsManager.Instance?.GraphicsDevice;
        if (device == null)
            return;
        var metrics = device.Metrics;
        _lastDraws = metrics.DrawCount;
        _lastTextures = metrics.TextureCount;
    }

    public static void Mark(Step step)
    {
        if (!RenderPassProfiler.Enabled)
            return;
        var device = GraphicsManager.Instance?.GraphicsDevice;
        if (device == null)
            return;
        var metrics = device.Metrics;
        Draws[(int)step] += metrics.DrawCount - _lastDraws;
        Textures[(int)step] += metrics.TextureCount - _lastTextures;
        _lastDraws = metrics.DrawCount;
        _lastTextures = metrics.TextureCount;
    }

    public static long GetDraws(Step step) => Draws[(int)step];
    public static long GetTextures(Step step) => Textures[(int)step];
#endif
}
