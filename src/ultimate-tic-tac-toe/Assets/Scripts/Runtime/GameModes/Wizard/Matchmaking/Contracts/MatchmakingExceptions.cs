#nullable enable

using System;

namespace Runtime.GameModes.Wizard.Matchmaking.Contracts
{
    public sealed class MatchmakingCancelAckTimeoutException : TimeoutException
    {
        public MatchmakingCancelAckTimeoutException(string message)
            : base(message) { }
    }

    public sealed class ConnectionLostException : Exception
    {
        public ConnectionLostException(string message)
            : base(message) { }

        public ConnectionLostException(string message, Exception innerException)
            : base(message, innerException) { }
    }
}