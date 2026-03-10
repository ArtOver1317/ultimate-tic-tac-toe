#nullable enable

using System;

namespace Runtime.GameModes.Wizard.Matchmaking.Contracts
{
    /// <summary>
    /// User-facing matchmaking failure information.
    /// </summary>
    public sealed class MatchmakingFailure
    {
        public string Code { get; }
        public string MessageKey { get; }
        public bool IsTimeout { get; }
        public MatchmakingTerminalReason TerminalReason { get; }

        public MatchmakingFailure(
            string code,
            string messageKey,
            bool isTimeout,
            MatchmakingTerminalReason terminalReason = MatchmakingTerminalReason.None)
        {
            if (string.IsNullOrWhiteSpace(code))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(code));

            if (string.IsNullOrWhiteSpace(messageKey))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(messageKey));

            Code = code;
            MessageKey = messageKey;
            IsTimeout = isTimeout;
            TerminalReason = terminalReason;
        }

        public static MatchmakingFailure Timeout() =>
            new("matchmaking.timeout", "Errors.GameWizard.MatchmakingTimeout", isTimeout: true);

        public static MatchmakingFailure Terminal(MatchmakingTerminalReason reason) =>
            reason switch
            {
                MatchmakingTerminalReason.SearchTimedOut => new MatchmakingFailure(
                    "matchmaking.terminal.timeout",
                    "Errors.GameWizard.MatchmakingTimeout",
                    isTimeout: true,
                    terminalReason: reason),
                MatchmakingTerminalReason.CancelAckTimeout => new MatchmakingFailure(
                    "matchmaking.terminal.cancel_ack_timeout",
                    "Errors.GameWizard.MatchmakingFailed",
                    isTimeout: false,
                    terminalReason: reason),
                MatchmakingTerminalReason.ConnectionLost => new MatchmakingFailure(
                    "matchmaking.terminal.connection_lost",
                    "Errors.Network.ConnectionLost",
                    isTimeout: false,
                    terminalReason: reason),
                MatchmakingTerminalReason.SessionStartFailed => new MatchmakingFailure(
                    "matchmaking.terminal.session_start_failed",
                    "Errors.GameWizard.MatchmakingFailed",
                    isTimeout: false,
                    terminalReason: reason),
                _ => new MatchmakingFailure(
                    "matchmaking.terminal.failed",
                    "Errors.GameWizard.MatchmakingFailed",
                    isTimeout: false,
                    terminalReason: MatchmakingTerminalReason.None),
            };

        public static MatchmakingFailure FromException(Exception ex)
        {
            if (ex == null)
                throw new ArgumentNullException(nameof(ex));

            return ex is OperationCanceledException 
                ? new MatchmakingFailure("matchmaking.cancelled", "Errors.GameWizard.MatchmakingCancelled", isTimeout: false) 
                : new MatchmakingFailure("matchmaking.failed", "Errors.GameWizard.MatchmakingFailed", isTimeout: false);
        }
    }

    public enum MatchmakingTerminalReason
    {
        None = 0,
        SearchTimedOut = 1,
        CancelAckTimeout = 2,
        ConnectionLost = 3,
        SessionStartFailed = 4,
    }
}