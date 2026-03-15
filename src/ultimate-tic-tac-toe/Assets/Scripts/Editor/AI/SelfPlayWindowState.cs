using System;
using System.Collections.Generic;
using System.Threading;
using Runtime.Games.TicTacToe.AI;
using Runtime.Games.TicTacToe.AI.Profiles;
using Runtime.Games.TicTacToe.AI.SelfPlay;
using Runtime.Games.TicTacToe.AI.Ultimate;
using Runtime.Games.TicTacToe.AI.Ultimate.Profiles;
using Runtime.Games.TicTacToe.AI.Ultimate.SelfPlay;
using UnityEngine;

namespace Editor.AI
{
    internal static class SelfPlayWindowConstants
    {
        public const string DefaultSearchSettingsPath = "Assets/Content/AI/BotSearchSettings_Default.asset";
        public const int MinimumProfileSlotCount = 2;
        public const int PairSeedStride = 1000;
        public const int UltimateMaxTurnsPerMatch = 81;
    }

    internal static class SelfPlayWindowMatchups
    {
        public static List<(int leftIndex, int rightIndex)> BuildRoundRobinPairs(int count)
        {
            var pairs = new List<(int leftIndex, int rightIndex)>();

            for (var leftIndex = 0; leftIndex < count; leftIndex++)
            {
                for (var rightIndex = leftIndex + 1; rightIndex < count; rightIndex++)
                {
                    pairs.Add((leftIndex, rightIndex));
                }
            }

            return pairs;
        }
    }

    internal sealed class SelfPlayWindowState
    {
        public int BoardSize { get; set; } = 3;

        public bool IsUltimate { get; set; }

        public int MatchCount { get; set; } = 20;

        public int BaseSeed { get; set; } = 42;

        public int WinLengthOverride { get; set; }

        public bool UseWinLengthOverride { get; set; }

        public List<ProfileSlot> ProfileSlots { get; } = new() { new(), new() };

        public BotSearchSettings DefaultSearchSettings { get; set; }

        public Vector2 ScrollPosition { get; set; }

        public Vector2 ResultsScroll { get; set; }

        public bool IsRunning { get; set; }

        public CancellationTokenSource CancellationSource { get; set; }

        public float PairProgress { get; set; }

        public string PairProgressLabel { get; set; } = string.Empty;

        public float MatchProgress { get; set; }

        public string MatchProgressLabel { get; set; } = string.Empty;

        public float MoveProgress { get; set; }

        public string MoveProgressLabel { get; set; } = string.Empty;

        public List<MatchupResult> Results { get; } = new();

        public string LogText { get; set; } = string.Empty;

        public RunSettingsSnapshot? LastRunSettings { get; set; }

        public RunSettingsSnapshot CaptureCurrentRunSettings() =>
            new(IsUltimate, BoardSize, UseWinLengthOverride, WinLengthOverride, MatchCount, BaseSeed);
    }

    internal readonly struct RunSettingsSnapshot
    {
        public RunSettingsSnapshot(bool isUltimate, int boardSize, bool useWinLengthOverride, int winLengthOverride, int matchCount, int baseSeed)
        {
            IsUltimate = isUltimate;
            BoardSize = boardSize;
            UseWinLengthOverride = useWinLengthOverride;
            WinLengthOverride = winLengthOverride;
            MatchCount = matchCount;
            BaseSeed = baseSeed;
        }

        public bool IsUltimate { get; }

        public int BoardSize { get; }

        public bool UseWinLengthOverride { get; }

        public int WinLengthOverride { get; }

        public int MatchCount { get; }

        public int BaseSeed { get; }
    }

    internal enum MatchupReportType
    {
        Classic,
        Ultimate,
    }

    internal sealed class ProfileSlot
    {
        public BotProfile ClassicProfile { get; set; }

        public UltimateBotProfile UltimateProfile { get; set; }

        public BotSearchSettings ClassicSearchOverride { get; set; }
    }

    internal readonly struct MatchupResult
    {
        private readonly SelfPlayReport _classicReport;
        private readonly SelfPlaySeriesReport _ultimateReport;

        public MatchupResult(string profile1Name, string profile2Name, SelfPlayReport classicReport)
        {
            Profile1Name = profile1Name;
            Profile2Name = profile2Name;
            ReportType = MatchupReportType.Classic;
            _classicReport = classicReport;
            _ultimateReport = default;
        }

        public MatchupResult(string profile1Name, string profile2Name, SelfPlaySeriesReport ultimateReport)
        {
            Profile1Name = profile1Name;
            Profile2Name = profile2Name;
            ReportType = MatchupReportType.Ultimate;
            _classicReport = default;
            _ultimateReport = ultimateReport;
        }

        public string Profile1Name { get; }

        public string Profile2Name { get; }

        public MatchupReportType ReportType { get; }

        public SelfPlayReport ClassicReport =>
            ReportType == MatchupReportType.Classic
                ? _classicReport
                : throw new InvalidOperationException("Classic report is not available for ultimate results.");

        public SelfPlaySeriesReport UltimateReport =>
            ReportType == MatchupReportType.Ultimate
                ? _ultimateReport
                : throw new InvalidOperationException("Ultimate report is not available for classic results.");
    }
}