using System;

namespace Runtime.GameModes.Wizard
{
    public sealed class MatchmakingConfig : IOpponentConfig
    {
        public string MatchId { get; }
        public string OpponentId { get; }
        public bool IsHost { get; }

        public MatchmakingConfig(string matchId, string opponentId, bool isHost = false)
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
