#if WINDOWS
using System.Diagnostics;

namespace Client.Main.DevTools
{
    internal sealed class GpuUsageSampler
    {
        private readonly PerformanceCounterCategory _gpuCategory = new("GPU Engine");
        private readonly List<PerformanceCounter> _counters = new();
        private int _pid;
        private long _lastRefreshTick;
        private const int RefreshIntervalMs = 5000;

        public bool TryGetUsage(int pid, out double usagePercent)
        {
            usagePercent = 0;

            try
            {
                var now = Environment.TickCount64;
                if (_counters.Count == 0 || _pid != pid || now - _lastRefreshTick > RefreshIntervalMs)
                {
                    BuildCounters(pid);
                    _lastRefreshTick = now;
                }

                if (_counters.Count == 0)
                    return false;

                double sum = 0;
                for (int i = 0; i < _counters.Count; i++)
                {
                    try { sum += _counters[i].NextValue(); }
                    catch { }
                }

                usagePercent = Math.Clamp(sum, 0, 100);
                return true;
            }
            catch
            {
                usagePercent = 0;
                return false;
            }
        }

        private void BuildCounters(int pid)
        {
            DisposeCounters();
            _pid = pid;

            string pidToken = "pid_" + pid;
            string[] instanceNames;
            try { instanceNames = _gpuCategory.GetInstanceNames(); }
            catch { instanceNames = Array.Empty<string>(); }

            for (int i = 0; i < instanceNames.Length; i++)
            {
                var name = instanceNames[i];
                if (name.IndexOf(pidToken, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                try
                {
                    var counter = new PerformanceCounter("GPU Engine", "Utilization Percentage", name, true);
                    _counters.Add(counter);
                }
                catch { }
            }

            for (int i = 0; i < _counters.Count; i++)
            {
                try { _counters[i].NextValue(); } catch { }
            }
        }

        private void DisposeCounters()
        {
            for (int i = 0; i < _counters.Count; i++)
            {
                try { _counters[i].Dispose(); } catch { }
            }
            _counters.Clear();
        }
    }
}
#endif
