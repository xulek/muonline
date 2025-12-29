#if DEBUG
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Client.Main.DevTools
{
    /// <summary>
    /// Ultra-lightweight timing utilities using Stopwatch.GetTimestamp().
    /// Zero allocations, minimal overhead (~20ns per call).
    /// </summary>
    public static class ControlTimingWrapper
    {
        private static readonly double TicksToMs = 1000.0 / Stopwatch.Frequency;

        /// <summary>
        /// Get current high-resolution timestamp.
        /// Cost: ~20 nanoseconds.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long GetTimestamp() => Stopwatch.GetTimestamp();

        /// <summary>
        /// Calculate elapsed milliseconds between two timestamps.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double ElapsedMs(long startTicks, long endTicks)
        {
            return (endTicks - startTicks) * TicksToMs;
        }

        /// <summary>
        /// Calculate elapsed milliseconds from start to now.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double ElapsedMsSince(long startTicks)
        {
            return (Stopwatch.GetTimestamp() - startTicks) * TicksToMs;
        }
    }

    /// <summary>
    /// Scoped profiler for measuring function/code block timing.
    /// Usage: using (ProfileScope.Begin("MethodName")) { ... }
    /// Creates hierarchical timing data visible in flame graph.
    /// </summary>
    public ref struct ProfileScope
    {
        private readonly long _startTicks;
        private readonly int _scopeIndex;

        private ProfileScope(int scopeIndex)
        {
            _startTicks = Stopwatch.GetTimestamp();
            _scopeIndex = scopeIndex;
        }

        /// <summary>
        /// Begin a profiling scope. Must be used with 'using' statement.
        /// </summary>
        /// <param name="name">Name of the scope (method/function name)</param>
        /// <param name="category">Optional category (e.g., "Animation", "Render", "Physics")</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ProfileScope Begin(string name, string category = null)
        {
            var index = DevToolsCollector.Instance?.PushScope(name, category) ?? -1;
            return new ProfileScope(index);
        }

        /// <summary>
        /// End the profiling scope and record timing.
        /// </summary>
        public void Dispose()
        {
            if (_scopeIndex >= 0)
            {
                var elapsed = ControlTimingWrapper.ElapsedMsSince(_startTicks);
                DevToolsCollector.Instance?.PopScope(_scopeIndex, elapsed);
            }
        }
    }
}
#endif
