using System;

namespace Client.Main.Networking
{
    /// <summary>
    /// Event arguments for when reconnection starts.
    /// </summary>
    public class ReconnectionStartedEventArgs : EventArgs
    {
        public int MaxAttempts { get; set; }
        public string Reason { get; set; }
    }

    /// <summary>
    /// Event arguments for reconnection progress updates.
    /// </summary>
    public class ReconnectionProgressEventArgs : EventArgs
    {
        public int CurrentAttempt { get; set; }
        public int MaxAttempts { get; set; }
        public string Status { get; set; }
    }
}