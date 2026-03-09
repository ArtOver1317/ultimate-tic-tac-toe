#nullable enable

using System;

namespace Runtime.GameModes.Wizard.Matchmaking
{
    /// <summary>
    /// Parameters required for matchmaking search.
    /// </summary>
    public sealed class MatchmakingRequest
    {
        public string GameId { get; }
        public IGameConfig GameConfig { get; }
        public int MoveTimeLimitSeconds { get; }

        public MatchmakingRequest(string gameId, IGameConfig gameConfig)
            : this(gameId, gameConfig, 0)
        {
        }

        public MatchmakingRequest(string gameId, IGameConfig gameConfig, int moveTimeLimitSeconds)
        {
            if (string.IsNullOrWhiteSpace(gameId))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(gameId));
            
            GameConfig = gameConfig ?? throw new ArgumentNullException(nameof(gameConfig));

            GameId = gameId;
            MoveTimeLimitSeconds = moveTimeLimitSeconds >= 0 ? moveTimeLimitSeconds : 0;
        }
    }
}