namespace Runtime.GameModes.Wizard
{
    public sealed class MatchmakingConfig : IOpponentConfig
    {
        public string MatchId { get; }
        public string OpponentId { get; }

        public MatchmakingConfig(string matchId, string opponentId)
        {
            if (string.IsNullOrWhiteSpace(matchId))
                throw new System.ArgumentException("Value cannot be null or whitespace.", nameof(matchId));
            if (string.IsNullOrWhiteSpace(opponentId))
                throw new System.ArgumentException("Value cannot be null or whitespace.", nameof(opponentId));

            MatchId = matchId;
            OpponentId = opponentId;
        }
    }
}
