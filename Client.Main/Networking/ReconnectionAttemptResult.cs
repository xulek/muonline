using System;
using MUnique.OpenMU.Network.Packets.ServerToClient;

namespace Client.Main.Networking
{
    /// <summary>
    /// Result returned by a reconnection attempt initiated by the ConnectionHealthMonitor.
    /// </summary>
    public readonly struct ReconnectionAttemptResult
    {
        private ReconnectionAttemptResult(bool isSuccess, bool isCancelled, bool sessionRestored, string failureReason, LoginResponse.LoginResult? loginFailure)
        {
            IsSuccess = isSuccess;
            IsCancelled = isCancelled;
            SessionRestored = sessionRestored;
            FailureReason = failureReason;
            LoginFailure = loginFailure;
        }

        public bool IsSuccess { get; }
        public bool IsCancelled { get; }
        public bool SessionRestored { get; }
        public string FailureReason { get; }
        public LoginResponse.LoginResult? LoginFailure { get; }

        public static ReconnectionAttemptResult Succeeded(bool restoredSession) =>
            new(true, false, restoredSession, null, null);

        public static ReconnectionAttemptResult Cancelled() =>
            new(false, true, false, "Cancelled", null);

        public static ReconnectionAttemptResult Failed(string reason, LoginResponse.LoginResult? loginFailure = null) =>
            new(false, false, false, string.IsNullOrWhiteSpace(reason) ? "Unknown error" : reason, loginFailure);
    }
}
