namespace Runtime.GameModes.Wizard
{
    /// <summary>
    /// Final output built from a validated wizard session.
    /// This config is passed to gameplay/state machine to start a match.
    /// </summary>
    public sealed class GameLaunchConfig
    {
        public string GameModeId { get; }
        public IGameModeConfig ModeConfig { get; }
        public IOpponentConfig OpponentConfig { get; }

        public GameLaunchConfig(string gameModeId, IGameModeConfig modeConfig, IOpponentConfig opponentConfig)
        {
            if (string.IsNullOrWhiteSpace(gameModeId))
                throw new System.ArgumentException("Value cannot be null or whitespace.", nameof(gameModeId));
            ModeConfig = modeConfig ?? throw new System.ArgumentNullException(nameof(modeConfig));
            OpponentConfig = opponentConfig ?? throw new System.ArgumentNullException(nameof(opponentConfig));

            GameModeId = gameModeId;
        }
    }
}
