#nullable enable

using System;
using Runtime.GameModes.Wizard;
using Runtime.GameModes.Wizard.Configs;
using Runtime.GameModes.Wizard.Matchmaking;

namespace Runtime.PlayerStatistics
{
    public sealed class MatchKeyMapper
    {
        public bool TryMap(GameLaunchConfig config, out MatchKey key)
        {
            key = null!;

            if (config == null)
                return false;

            if (!TryMapOpponent(config.OpponentConfig, out var opponentType, out var botDifficultyId))
                return false;

            key = new MatchKey(config.GameId, opponentType, botDifficultyId);
            return true;
        }

        public MatchKey Map(GameLaunchConfig config)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            if (!TryMapOpponent(config.OpponentConfig, out var opponentType, out var botDifficultyId))
                throw new NotSupportedException($"Unsupported opponent config type: {config.OpponentConfig?.GetType().Name ?? "<null>"}");

            return new MatchKey(config.GameId, opponentType, botDifficultyId);
        }

        private static bool TryMapOpponent(IOpponentConfig opponentConfig, out StatisticsOpponentType opponentType, out string? botDifficultyId)
        {
            opponentType = default;
            botDifficultyId = null;

            switch (opponentConfig)
            {
                case LocalHumanConfig:
                    opponentType = StatisticsOpponentType.HotSeat;
                    return true;
                case BotOpponentConfig bot:
                    opponentType = StatisticsOpponentType.Bot;
                    botDifficultyId = bot.DifficultyId;
                    return true;
                case DirectInviteConfig:
                case MatchmakingConfig:
                    opponentType = StatisticsOpponentType.Online;
                    return true;
                default:
                    return false;
            }
        }
    }
}

#nullable restore