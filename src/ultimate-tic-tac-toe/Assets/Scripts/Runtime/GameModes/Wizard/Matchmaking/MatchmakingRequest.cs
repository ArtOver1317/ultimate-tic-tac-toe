#nullable enable

using System;

namespace Runtime.GameModes.Wizard
{
    /// <summary>
    /// Parameters required for matchmaking search.
    /// </summary>
    public sealed class MatchmakingRequest
    {
        public string GameId { get; }
        public IGameConfig GameConfig { get; }

        public MatchmakingRequest(string gameId, IGameConfig gameConfig)
        {
            if (string.IsNullOrWhiteSpace(gameId))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(gameId));
            
            GameConfig = gameConfig ?? throw new ArgumentNullException(nameof(gameConfig));

            GameId = gameId;
        }
    }
}