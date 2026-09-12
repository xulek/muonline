using System;
using Android.Util;
using Microsoft.Extensions.Logging;

namespace MuAndroid
{
    // No file writer or blocking queue on the render thread. Release diagnostics
    // remain visible through adb logcat even when console logging is disabled.
    internal sealed class AndroidLogcatLoggerProvider : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new LogcatLogger(categoryName);
        public void Dispose() { }

        private sealed class LogcatLogger(string categoryName) : ILogger
        {
            public IDisposable BeginScope<TState>(TState state) => null;
            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning && logLevel < LogLevel.None;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
                Exception exception, Func<TState, Exception, string> formatter)
            {
                if (!IsEnabled(logLevel)) return;
                string message = $"{categoryName}: {formatter(state, exception)}";
                if (exception != null) message += $"\n{exception}";
                global::Android.Util.Log.WriteLine(logLevel == LogLevel.Warning ? LogPriority.Warn : LogPriority.Error,
                    "MuAndroid", message);
            }
        }
    }
}
