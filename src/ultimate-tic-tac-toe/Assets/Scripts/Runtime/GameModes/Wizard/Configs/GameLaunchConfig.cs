using GameLog = Runtime.Infrastructure.Logging.GameLog;

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
        public int MoveTimeLimitSeconds { get; }

        public GameLaunchConfig(
            string gameId,
            IGameConfig gameConfig,
            IOpponentConfig opponentConfig,
            int moveTimeLimitSeconds = 0)
        {
            if (string.IsNullOrWhiteSpace(gameId))
                throw new System.ArgumentException("Value cannot be null or whitespace.", nameof(gameId));
            
            GameConfig = gameConfig ?? throw new System.ArgumentNullException(nameof(gameConfig));
            OpponentConfig = opponentConfig ?? throw new System.ArgumentNullException(nameof(opponentConfig));

            GameId = gameId;
            MoveTimeLimitSeconds = NormalizeMoveTimeLimitSeconds(moveTimeLimitSeconds);
        }

        private static int NormalizeMoveTimeLimitSeconds(int moveTimeLimitSeconds)
        {
            if (moveTimeLimitSeconds >= 0)
                return moveTimeLimitSeconds;

            GameLog.Warning($"[GameLaunchConfig] Negative {nameof(moveTimeLimitSeconds)} value '{moveTimeLimitSeconds}' was clamped to 0.");
            return 0;
        }
    }
}
