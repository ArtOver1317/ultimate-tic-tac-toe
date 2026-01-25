#nullable enable

using System;

namespace Runtime.GameModes.Wizard
{
    /// <summary>
    /// Parameters required for matchmaking search.
    /// </summary>
    public sealed class MatchmakingRequest
    {
        public string GameModeId { get; }
        public IGameModeConfig ModeConfig { get; }

        public MatchmakingRequest(string gameModeId, IGameModeConfig modeConfig)
        {
            if (string.IsNullOrWhiteSpace(gameModeId))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(gameModeId));
            ModeConfig = modeConfig ?? throw new ArgumentNullException(nameof(modeConfig));

            GameModeId = gameModeId;
        }
    }
}

#nullable restore
