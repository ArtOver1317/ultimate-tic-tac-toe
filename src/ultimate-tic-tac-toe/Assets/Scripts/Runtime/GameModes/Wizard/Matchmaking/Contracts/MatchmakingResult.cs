#nullable enable

using System;

namespace Runtime.GameModes.Wizard.Matchmaking.Contracts
{
    /// <summary>
    /// Successful matchmaking output.
    /// </summary>
    public sealed class MatchmakingResult
    {
        public string MatchId { get; }
        public string OpponentId { get; }
        public bool IsHost { get; }

        public MatchmakingResult(string matchId, string opponentId)
            : this(matchId, opponentId, isHost: false) { }

        public MatchmakingResult(string matchId, string opponentId, bool isHost)
        {
            if (string.IsNullOrWhiteSpace(matchId))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(matchId));
            
            if (string.IsNullOrWhiteSpace(opponentId))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(opponentId));

            MatchId = matchId;
            OpponentId = opponentId;
            IsHost = isHost;
        }
    }
}