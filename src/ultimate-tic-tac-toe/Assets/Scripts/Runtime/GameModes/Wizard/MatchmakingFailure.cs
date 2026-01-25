#nullable enable

using System;

namespace Runtime.GameModes.Wizard
{
    /// <summary>
    /// User-facing matchmaking failure information.
    /// </summary>
    public sealed class MatchmakingFailure
    {
        public string Code { get; }
        public string MessageKey { get; }
        public bool IsTimeout { get; }

        public MatchmakingFailure(string code, string messageKey, bool isTimeout)
        {
            if (string.IsNullOrWhiteSpace(code))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(code));
            if (string.IsNullOrWhiteSpace(messageKey))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(messageKey));

            Code = code;
            MessageKey = messageKey;
            IsTimeout = isTimeout;
        }

        public static MatchmakingFailure Timeout() =>
            new MatchmakingFailure("matchmaking.timeout", "Errors.GameModeWizard.MatchmakingTimeout", isTimeout: true);

        public static MatchmakingFailure FromException(Exception ex)
        {
            if (ex == null)
                throw new ArgumentNullException(nameof(ex));

            if (ex is OperationCanceledException)
                return new MatchmakingFailure("matchmaking.cancelled", "Errors.GameModeWizard.MatchmakingCancelled", isTimeout: false);

            return new MatchmakingFailure("matchmaking.failed", "Errors.GameModeWizard.MatchmakingFailed", isTimeout: false);
        }
    }
}

#nullable restore
