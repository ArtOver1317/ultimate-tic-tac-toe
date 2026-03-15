using System;
using Runtime.Games.TicTacToe.AI;
using Runtime.Games.TicTacToe.AI.SelfPlay;
using Runtime.Games.TicTacToe.AI.Ultimate;
using Runtime.Games.TicTacToe.AI.Ultimate.SelfPlay;
using UnityEditor;
using UnityEngine;

namespace Editor.AI
{
    internal sealed class SelfPlayWindowResultsSection
    {
        private readonly SelfPlayWindowState _state;
        private readonly SelfPlayWindowExporter _exporter;

        public SelfPlayWindowResultsSection(SelfPlayWindowState state, SelfPlayWindowExporter exporter)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _exporter = exporter ?? throw new ArgumentNullException(nameof(exporter));
        }

        public void Draw()
        {
            if (_state.Results.Count == 0 && string.IsNullOrEmpty(_state.LogText))
                return;

            EditorGUILayout.LabelField("Results", EditorStyles.boldLabel);
            _state.ResultsScroll = EditorGUILayout.BeginScrollView(_state.ResultsScroll, GUILayout.MinHeight(200));

            foreach (var result in _state.Results)
            {
                DrawMatchupResult(result);
                EditorGUILayout.Space(4);
            }

            if (!string.IsNullOrEmpty(_state.LogText))
            {
                EditorGUILayout.Space(8);
                EditorGUILayout.LabelField("Log", EditorStyles.boldLabel);
                EditorGUILayout.TextArea(_state.LogText, GUILayout.MinHeight(60));
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.Space(4);

            if (GUILayout.Button("Export to File...", GUILayout.Width(150)))
                _exporter.ExportResults();
        }

        private void DrawMatchupResult(MatchupResult result)
        {
            EditorGUILayout.LabelField(
                $"{result.Profile1Name} vs {result.Profile2Name}",
                EditorStyles.boldLabel);

            EditorGUILayout.BeginVertical("box");

            switch (result.ReportType)
            {
                case MatchupReportType.Classic:
                    DrawClassicMatchupResult(result.ClassicReport);
                    break;
                case MatchupReportType.Ultimate:
                    DrawUltimateMatchupResult(result.UltimateReport);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(result.ReportType), result.ReportType, "Unknown report type.");
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawClassicMatchupResult(SelfPlayReport report)
        {
            var total = report.Player1Wins + report.Player2Wins + report.Draws;

            EditorGUILayout.LabelField(
                $"W/D/L (P1): {report.Player1Wins}/{report.Draws}/{report.Player2Wins}  " +
                $"({Pct(report.Player1Wins, total)}% / {Pct(report.Draws, total)}% / {Pct(report.Player2Wins, total)}%)");

            EditorGUILayout.LabelField(
                $"Avg ms/move: P1={report.AvgMsPerMoveP1:F1}  P2={report.AvgMsPerMoveP2:F1}");

            EditorGUILayout.LabelField(
                $"Tactical misses: " +
                $"P1 win={report.MissedWinP1} block={report.MissedBlockP1}  " +
                $"P2 win={report.MissedWinP2} block={report.MissedBlockP2}");

            EditorGUILayout.LabelField(
                $"Total: {report.TotalMoves} moves, {report.TotalTimeMs:F0}ms");
        }

        private void DrawUltimateMatchupResult(SelfPlaySeriesReport report)
        {
            var total = report.WinsLeft + report.WinsRight + report.Draws;

            EditorGUILayout.LabelField(
                $"W/D/L (Left): {report.WinsLeft}/{report.Draws}/{report.WinsRight}  " +
                $"({Pct(report.WinsLeft, total)}% / {Pct(report.Draws, total)}% / {Pct(report.WinsRight, total)}%)");

            EditorGUILayout.LabelField(
                $"Move latency ms: avg={report.AvgMoveMs:F2} p50={report.P50MoveMs:F2} p95={report.P95MoveMs:F2}");

            EditorGUILayout.LabelField(
                $"Fallbacks: timeoutBest={report.TimeoutBestCount}, " +
                $"timeoutFallback={report.TimeoutFallbackLegalCount}, inconsistent={report.NoLegalMovesInconsistentStateCount}");

            EditorGUILayout.LabelField(
                $"Meta: matches={report.Matches}, seedRange={report.SeedRangeLabel}");
        }

        private static int Pct(int count, int total) =>
            total > 0 ? Mathf.RoundToInt((float)count / total * 100) : 0;
    }
}