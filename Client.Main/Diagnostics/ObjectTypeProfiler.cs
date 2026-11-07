using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

namespace Client.Main.Diagnostics
{
    /// <summary>
    /// Diagnostics only: CPU time of world object Update/Draw/DrawAfter calls grouped by object type.
    /// Disabled unless a diagnostic tool (stress scenes, MU_STRESS_TRACE) turns it on.
    /// </summary>
    public static class ObjectTypeProfiler
    {
        public enum Phase : byte { Update, Draw, DrawAfter, CrowdSide, ChildUpdate, ChildDraw, ChildDrawAfter }

        public static bool Enabled;

        private static readonly Dictionary<(Type Type, Phase Phase), (long Ticks, int Calls, long Draws)> Totals = new();

        // Device draw count at each open Start; calls nest (children inside their parent's draw).
        private static readonly long[] DrawCountStack = new long[64];
        private static int _drawCountDepth;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long Start()
        {
            if (!Enabled)
                return 0;
#if !PERFORMANCE_RELEASE
            if (Controllers.RenderPassProfiler.GpuSync)
                Controllers.RenderPassProfiler.WaitForGpu();
#endif
            if (_drawCountDepth < DrawCountStack.Length)
                DrawCountStack[_drawCountDepth] = CurrentDrawCount();
            _drawCountDepth++;
            return Stopwatch.GetTimestamp();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Stop(object obj, Phase phase, long started)
        {
            if (started == 0 || obj == null)
                return;
#if !PERFORMANCE_RELEASE
            if (Controllers.RenderPassProfiler.GpuSync)
                Controllers.RenderPassProfiler.WaitForGpu();
#endif

            long elapsed = Stopwatch.GetTimestamp() - started;
            long draws = 0;
            if (_drawCountDepth > 0)
            {
                _drawCountDepth--;
                if (_drawCountDepth < DrawCountStack.Length)
                    draws = Math.Max(0, CurrentDrawCount() - DrawCountStack[_drawCountDepth]);
            }

            var key = (obj.GetType(), phase);
            Totals.TryGetValue(key, out var total);
            Totals[key] = (total.Ticks + elapsed, total.Calls + 1, total.Draws + draws);
        }

        private static long CurrentDrawCount() =>
            Controllers.GraphicsManager.Instance?.GraphicsDevice?.Metrics.DrawCount ?? 0;

        /// <summary>Top entries since the last call ("Type.Phase=ms/calls"), then resets the totals.</summary>
        public static string TakeTop(int count, int frames)
        {
            if (Totals.Count == 0)
                return string.Empty;

            var entries = new List<KeyValuePair<(Type Type, Phase Phase), (long Ticks, int Calls, long Draws)>>(Totals);
            entries.Sort((a, b) => b.Value.Ticks.CompareTo(a.Value.Ticks));
            var sb = new StringBuilder();
            double perFrame = 1000.0 / Stopwatch.Frequency / Math.Max(1, frames);
            for (int i = 0; i < entries.Count && i < count; i++)
            {
                var e = entries[i];
                sb.Append(e.Key.Type.Name).Append('.').Append(e.Key.Phase).Append('=')
                  .Append((e.Value.Ticks * perFrame).ToString("F2", System.Globalization.CultureInfo.InvariantCulture))
                  .Append('/').Append(e.Value.Calls / Math.Max(1, frames))
                  .Append('/').Append(e.Value.Draws / Math.Max(1, frames)).Append(' ');
            }
            Totals.Clear();
            // A draw that threw skips its Stop; do not let such entries leak into the next interval.
            _drawCountDepth = 0;
            return sb.ToString();
        }
    }
}
