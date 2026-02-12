#nullable enable

using System.Threading;
using System;
using Cysharp.Threading.Tasks;

namespace Runtime.Games.TicTacToe.AI
{
    // ── Config ──

    /// <summary>
    /// Configuration for a batch of self-play matches (ADR-5).
    /// Pure data — no ScriptableObject dependency.
    /// </summary>
    public readonly struct SelfPlayConfig
    {
        public int BoardSize { get; }
        public int? WinLengthOverride { get; }
        public BotProfileData Profile1 { get; }
        public BotProfileData Profile2 { get; }
        public BotSearchSettingsData? Player1SearchSettingsOverride { get; }
        public BotSearchSettingsData? Player2SearchSettingsOverride { get; }
        public int MatchCount { get; }
        public int BaseSeed { get; }

        public SelfPlayConfig(
            int boardSize,
            BotProfileData profile1,
            BotProfileData profile2,
            int matchCount,
            int baseSeed,
            int? winLengthOverride = null,
            BotSearchSettingsData? player1SearchSettingsOverride = null,
            BotSearchSettingsData? player2SearchSettingsOverride = null)
        {
            BoardSize = boardSize;
            Profile1 = profile1;
            Profile2 = profile2;
            MatchCount = matchCount;
            BaseSeed = baseSeed;
            WinLengthOverride = winLengthOverride;
            Player1SearchSettingsOverride = player1SearchSettingsOverride;
            Player2SearchSettingsOverride = player2SearchSettingsOverride;
        }
    }

    public readonly struct SelfPlayProgress
    {
        public int MatchIndex { get; }
        public int TotalMatches { get; }
        public int TurnIndex { get; }
        public int MaxTurns { get; }

        public SelfPlayProgress(int matchIndex, int totalMatches, int turnIndex, int maxTurns)
        {
            MatchIndex = matchIndex;
            TotalMatches = totalMatches;
            TurnIndex = turnIndex;
            MaxTurns = maxTurns;
        }
    }

    // ── Report ──

    /// <summary>
    /// Aggregated results from a self-play batch.
    /// Tactical misses counted only if profile requires 100% and engine didn't timeout.
    /// </summary>
    public readonly struct SelfPlayReport
    {
        public int Player1Wins { get; }
        public int Player2Wins { get; }
        public int Draws { get; }
        public float AvgMsPerMoveP1 { get; }
        public float AvgMsPerMoveP2 { get; }
        public int MissedWinP1 { get; }
        public int MissedWinP2 { get; }
        public int MissedBlockP1 { get; }
        public int MissedBlockP2 { get; }
        public int TotalMoves { get; }
        public double TotalTimeMs { get; }

        public SelfPlayReport(
            int player1Wins, int player2Wins, int draws,
            float avgMsPerMoveP1, float avgMsPerMoveP2,
            int missedWinP1, int missedWinP2,
            int missedBlockP1, int missedBlockP2,
            int totalMoves, double totalTimeMs)
        {
            Player1Wins = player1Wins;
            Player2Wins = player2Wins;
            Draws = draws;
            AvgMsPerMoveP1 = avgMsPerMoveP1;
            AvgMsPerMoveP2 = avgMsPerMoveP2;
            MissedWinP1 = missedWinP1;
            MissedWinP2 = missedWinP2;
            MissedBlockP1 = missedBlockP1;
            MissedBlockP2 = missedBlockP2;
            TotalMoves = totalMoves;
            TotalTimeMs = totalTimeMs;
        }
    }

    // ── Interface ──

    /// <summary>
    /// Runs pure board simulation (no ECS/UI) for bot calibration and CI (ADR-5).
    /// </summary>
    public interface ISelfPlayRunner
    {
        UniTask<SelfPlayReport> RunAsync(
            SelfPlayConfig config,
            CancellationToken ct,
            Action<SelfPlayProgress>? onProgress = null);
    }
}
