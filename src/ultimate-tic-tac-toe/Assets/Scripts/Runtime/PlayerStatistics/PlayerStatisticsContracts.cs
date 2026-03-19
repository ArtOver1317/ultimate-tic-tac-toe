#nullable enable

using System;
using System.Collections.Generic;
using Runtime.Gameplay.Shared;

namespace Runtime.PlayerStatistics
{
    public enum MatchOutcome
    {
        Win = 0,
        Loss = 1,
        Draw = 2,
    }

    public enum StatisticsOpponentType
    {
        HotSeat = 0,
        Bot = 1,
        Online = 2,
    }

    public sealed class MatchKey : IEquatable<MatchKey>
    {
        public string GameId { get; }
        public StatisticsOpponentType OpponentType { get; }
        public string? BotDifficultyId { get; }

        public MatchKey(string gameId, StatisticsOpponentType opponentType, string? botDifficultyId)
        {
            if (string.IsNullOrWhiteSpace(gameId))
                throw new ArgumentException("GameId must not be null, empty, or whitespace.", nameof(gameId));

            GameId = gameId.Trim();
            OpponentType = opponentType;
           
            BotDifficultyId = opponentType == StatisticsOpponentType.Bot
                ? NormalizeBotDifficultyId(botDifficultyId)
                : null;
        }

        public bool Equals(MatchKey? other)
        {
            if (other == null)
                return false;

            return string.Equals(GameId, other.GameId, StringComparison.Ordinal)
                   && OpponentType == other.OpponentType
                   && string.Equals(BotDifficultyId, other.BotDifficultyId, StringComparison.Ordinal);
        }

        public override bool Equals(object? obj) => obj is MatchKey other && Equals(other);

        public override int GetHashCode()
        {
            var gameIdHash = StringComparer.Ordinal.GetHashCode(GameId);
            var botDifficultyHash = BotDifficultyId == null
                ? 0
                : StringComparer.Ordinal.GetHashCode(BotDifficultyId);

            return HashCode.Combine(gameIdHash, (int)OpponentType, botDifficultyHash);
        }

        private static string? NormalizeBotDifficultyId(string? botDifficultyId)
        {
            if (botDifficultyId == null)
                return null;

            var trimmed = botDifficultyId.Trim();
            return trimmed.Length == 0 ? null : trimmed;
        }
    }

    public sealed class StatisticsRecord
    {
        public int Wins { get; }
        public int Losses { get; }
        public int Draws { get; }

        public StatisticsRecord(int wins, int losses, int draws)
        {
            if (wins < 0)
                throw new ArgumentOutOfRangeException(nameof(wins), wins, "Must be greater than or equal to zero.");

            if (losses < 0)
                throw new ArgumentOutOfRangeException(nameof(losses), losses, "Must be greater than or equal to zero.");

            if (draws < 0)
                throw new ArgumentOutOfRangeException(nameof(draws), draws, "Must be greater than or equal to zero.");

            Wins = wins;
            Losses = losses;
            Draws = draws;
        }
    }

    public sealed class StatisticsEntry
    {
        public MatchKey Key { get; }
        public StatisticsRecord Record { get; }

        public StatisticsEntry(MatchKey key, StatisticsRecord record)
        {
            Key = key ?? throw new ArgumentNullException(nameof(key));
            Record = record ?? throw new ArgumentNullException(nameof(record));
        }
    }

    public interface IPlayerStatisticsService
    {
        void RecordMatch(MatchKey key, MatchOutcome outcome);

        IReadOnlyList<StatisticsEntry> GetEntriesSnapshot();
    }

    public interface IMatchOutcomeResolver
    {
        bool TryResolveOutcome(
            RoundFinishedEvent evt,
            StatisticsOpponentType opponentType,
            bool isLocalPlayerHost,
            out MatchOutcome outcome);
    }
}