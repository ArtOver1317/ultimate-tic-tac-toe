#nullable enable

using System;

namespace Runtime.PlayerStatistics
{
    [Serializable]
    internal sealed class StatisticsEntryDto
    {
        public string? gameId;
        public string? opponentType;
        public string? botDifficultyId;
        public int wins;
        public int losses;
        public int draws;
    }
}

#nullable restore