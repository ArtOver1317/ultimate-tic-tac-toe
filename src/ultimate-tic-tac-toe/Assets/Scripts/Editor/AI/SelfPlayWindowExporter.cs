using System;
using System.IO;
using System.Text;
using Runtime.Games.TicTacToe.AI;
using Runtime.Games.TicTacToe.AI.SelfPlay;
using Runtime.Games.TicTacToe.AI.Ultimate;
using Runtime.Games.TicTacToe.AI.Ultimate.SelfPlay;
using UnityEditor;
using UnityEngine;

namespace Editor.AI
{
    internal sealed class SelfPlayWindowExporter
    {
        private readonly SelfPlayWindowState _state;

        public SelfPlayWindowExporter(SelfPlayWindowState state) => _state = state ?? throw new ArgumentNullException(nameof(state));

        public void ExportResults()
        {
            var exportTime = DateTime.Now;
            var runSettings = _state.LastRunSettings ?? _state.CaptureCurrentRunSettings();
            
            var path = EditorUtility.SaveFilePanel(
                "Export Self-Play Report",
                string.Empty,
                $"SelfPlay_{exportTime:yyyyMMdd_HHmmss}.txt",
                "txt");

            if (string.IsNullOrEmpty(path))
                return;

            var sb = new StringBuilder();
            AppendExportHeader(sb, exportTime, runSettings);

            foreach (var result in _state.Results)
            {
                AppendMatchupExport(sb, result);
            }

            if (!string.IsNullOrEmpty(_state.LogText))
                AppendLogExport(sb);

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            Debug.Log($"[SelfPlay] Report exported to: {path}");
        }

        private static void AppendExportHeader(StringBuilder sb, DateTime exportTime, RunSettingsSnapshot runSettings)
        {
            sb.AppendLine("=== Self-Play Report ===");
            sb.AppendLine($"Date: {exportTime:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine(runSettings.IsUltimate ? "Mode: Ultimate (fixed 3x3x3)" : $"Mode: Classic ({runSettings.BoardSize}x{runSettings.BoardSize})");
            
            if (runSettings is { IsUltimate: false, UseWinLengthOverride: true })
                sb.AppendLine($"Win Length Override: {runSettings.WinLengthOverride}");

            sb.AppendLine($"Matches per pair: {runSettings.MatchCount}");
            sb.AppendLine($"Base Seed: {runSettings.BaseSeed}");
            sb.AppendLine();
        }

        private void AppendMatchupExport(StringBuilder sb, MatchupResult result)
        {
            sb.AppendLine($"--- {result.Profile1Name} vs {result.Profile2Name} ---");
            
            switch (result.ReportType)
            {
                case MatchupReportType.Classic:
                    AppendClassicMatchupExport(sb, result.ClassicReport);
                    break;
                case MatchupReportType.Ultimate:
                    AppendUltimateMatchupExport(sb, result.UltimateReport);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(result.ReportType), result.ReportType, "Unknown report type.");
            }

            sb.AppendLine();
        }

        private static void AppendClassicMatchupExport(StringBuilder sb, SelfPlayReport report)
        {
            var total = report.Player1Wins + report.Player2Wins + report.Draws;

            sb.AppendLine($"  P1 Wins: {report.Player1Wins} ({Pct(report.Player1Wins, total)}%)");
            sb.AppendLine($"  P2 Wins: {report.Player2Wins} ({Pct(report.Player2Wins, total)}%)");
            sb.AppendLine($"  Draws:   {report.Draws} ({Pct(report.Draws, total)}%)");
            sb.AppendLine($"  Avg ms/move: P1={report.AvgMsPerMoveP1:F2}, P2={report.AvgMsPerMoveP2:F2}");
            sb.AppendLine($"  Tactical misses P1: win={report.MissedWinP1}, block={report.MissedBlockP1}");
            sb.AppendLine($"  Tactical misses P2: win={report.MissedWinP2}, block={report.MissedBlockP2}");
            sb.AppendLine($"  Total: {report.TotalMoves} moves, {report.TotalTimeMs:F0}ms");
        }

        private static void AppendUltimateMatchupExport(StringBuilder sb, SelfPlaySeriesReport report)
        {
            var total = report.WinsLeft + report.WinsRight + report.Draws;

            sb.AppendLine($"  Left Wins:  {report.WinsLeft} ({Pct(report.WinsLeft, total)}%)");
            sb.AppendLine($"  Right Wins: {report.WinsRight} ({Pct(report.WinsRight, total)}%)");
            sb.AppendLine($"  Draws:      {report.Draws} ({Pct(report.Draws, total)}%)");
            sb.AppendLine($"  Move latency ms: avg={report.AvgMoveMs:F2}, p50={report.P50MoveMs:F2}, p95={report.P95MoveMs:F2}");
            sb.AppendLine($"  Fallbacks: timeoutBest={report.TimeoutBestCount}, timeoutFallback={report.TimeoutFallbackLegalCount}, inconsistent={report.NoLegalMovesInconsistentStateCount}");
            sb.AppendLine($"  Meta: matches={report.Matches}, seedRange={report.SeedRangeLabel}");
        }

        private void AppendLogExport(StringBuilder sb) =>
            sb.AppendLine("--- Log ---")
                .AppendLine(_state.LogText);

        private static int Pct(int count, int total) =>
            total > 0 ? Mathf.RoundToInt((float)count / total * 100) : 0;
    }
}