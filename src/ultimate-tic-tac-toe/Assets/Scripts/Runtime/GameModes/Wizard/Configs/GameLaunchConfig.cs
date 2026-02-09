namespace Runtime.GameModes.Wizard
{
    /// <summary>
    /// Final output built from a validated wizard session.
    /// This config is passed to gameplay/state machine to start a match.
    /// </summary>
    public sealed class GameLaunchConfig
    {
        public string GameId { get; }
        public IGameConfig GameConfig { get; }
        public IOpponentConfig OpponentConfig { get; }

        public GameLaunchConfig(string gameId, IGameConfig gameConfig, IOpponentConfig opponentConfig)
        {
            if (string.IsNullOrWhiteSpace(gameId))
                throw new System.ArgumentException("Value cannot be null or whitespace.", nameof(gameId));
            
            GameConfig = gameConfig ?? throw new System.ArgumentNullException(nameof(gameConfig));
            OpponentConfig = opponentConfig ?? throw new System.ArgumentNullException(nameof(opponentConfig));

            GameId = gameId;
        }
    }
}
