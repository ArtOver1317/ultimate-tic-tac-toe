#nullable enable

using System;

namespace Runtime.GameModes.Wizard
{
    /// <summary>
    /// Successful matchmaking output.
    /// </summary>
    public sealed class MatchmakingResult
    {
        public string MatchId { get; }
        public string OpponentId { get; }

        public MatchmakingResult(string matchId, string opponentId)
        {
            if (string.IsNullOrWhiteSpace(matchId))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(matchId));
            if (string.IsNullOrWhiteSpace(opponentId))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(opponentId));

            MatchId = matchId;
            OpponentId = opponentId;
        }
    }
}

#nullable restore
