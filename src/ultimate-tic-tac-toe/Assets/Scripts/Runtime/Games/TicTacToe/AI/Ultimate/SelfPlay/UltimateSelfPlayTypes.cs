#nullable enable

using System;

namespace Runtime.Games.TicTacToe.AI.Ultimate.SelfPlay
{
    public readonly struct SelfPlaySeriesConfig
    {
        public string LeftProfileId { get; }
        public string RightProfileId { get; }
        public int Matches { get; }
        public int BaseSeed { get; }
        public int SeedCount { get; }

        public SelfPlaySeriesConfig(string leftProfileId, string rightProfileId, int matches, int baseSeed, int seedCount)
        {
            LeftProfileId = leftProfileId ?? throw new ArgumentNullException(nameof(leftProfileId));
            RightProfileId = rightProfileId ?? throw new ArgumentNullException(nameof(rightProfileId));
            Matches = matches;
            BaseSeed = baseSeed;
            SeedCount = seedCount;
        }
    }

    public readonly struct UltimateSelfPlayProgress
    {
        public int MatchIndex { get; }
        public int TotalMatches { get; }
        public int TurnIndex { get; }
        public int MaxTurns { get; }

        public UltimateSelfPlayProgress(int matchIndex, int totalMatches, int turnIndex, int maxTurns)
        {
            MatchIndex = matchIndex;
            TotalMatches = totalMatches;
            TurnIndex = turnIndex;
            MaxTurns = maxTurns;
        }
    }

    public readonly struct SelfPlaySeriesReport
    {
        public string SeedRangeLabel { get; }
        public string LeftProfileVersion { get; }
        public string RightProfileVersion { get; }
        public string LeftProfileHash { get; }
        public string RightProfileHash { get; }

        public int Matches { get; }
        public int WinsLeft { get; }
        public int WinsRight { get; }
        public int Draws { get; }

        public float AvgMoveMs { get; }
        public float P50MoveMs { get; }
        public float P95MoveMs { get; }

        public int TimeoutBestCount { get; }
        public int TimeoutFallbackLegalCount { get; }
        public int NoLegalMovesInconsistentStateCount { get; }

        public SelfPlaySeriesReport(
            string? seedRangeLabel,
            string? leftProfileVersion,
            string? rightProfileVersion,
            string? leftProfileHash,
            string? rightProfileHash,
            int matches,
            int winsLeft,
            int winsRight,
            int draws,
            float avgMoveMs,
            float p50MoveMs,
            float p95MoveMs,
            int timeoutBestCount,
            int timeoutFallbackLegalCount,
            int noLegalMovesInconsistentStateCount)
        {
            SeedRangeLabel = seedRangeLabel ?? string.Empty;
            LeftProfileVersion = leftProfileVersion ?? string.Empty;
            RightProfileVersion = rightProfileVersion ?? string.Empty;
            LeftProfileHash = leftProfileHash ?? string.Empty;
            RightProfileHash = rightProfileHash ?? string.Empty;
            Matches = matches;
            WinsLeft = winsLeft;
            WinsRight = winsRight;
            Draws = draws;
            AvgMoveMs = avgMoveMs;
            P50MoveMs = p50MoveMs;
            P95MoveMs = p95MoveMs;
            TimeoutBestCount = timeoutBestCount;
            TimeoutFallbackLegalCount = timeoutFallbackLegalCount;
            NoLegalMovesInconsistentStateCount = noLegalMovesInconsistentStateCount;
        }
    }
}
