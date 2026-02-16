using UnityEditor;
using UnityEngine;

namespace Editor.AI
{
    public sealed partial class SelfPlayWindow
    {
        private void DrawMatchupResult(MatchupResult r)
        {
            if (r.ClassicReport.HasValue)
            {
                var report = r.ClassicReport.Value;
                var total = report.Player1Wins + report.Player2Wins + report.Draws;

                EditorGUILayout.LabelField(
                    $"{r.Profile1Name} vs {r.Profile2Name}",
                    EditorStyles.boldLabel);

                EditorGUILayout.BeginVertical("box");

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

                EditorGUILayout.EndVertical();
            }

            if (r.UltimateReport.HasValue)
            {
                var ultimateReport = r.UltimateReport.Value;
                var ultimateTotal = ultimateReport.WinsLeft + ultimateReport.WinsRight + ultimateReport.Draws;

                EditorGUILayout.LabelField(
                    $"{r.Profile1Name} vs {r.Profile2Name}",
                    EditorStyles.boldLabel);

                EditorGUILayout.BeginVertical("box");

                EditorGUILayout.LabelField(
                    $"W/D/L (Left): {ultimateReport.WinsLeft}/{ultimateReport.Draws}/{ultimateReport.WinsRight}  " +
                    $"({Pct(ultimateReport.WinsLeft, ultimateTotal)}% / {Pct(ultimateReport.Draws, ultimateTotal)}% / {Pct(ultimateReport.WinsRight, ultimateTotal)}%)");

                EditorGUILayout.LabelField(
                    $"Move latency ms: avg={ultimateReport.AvgMoveMs:F2} p50={ultimateReport.P50MoveMs:F2} p95={ultimateReport.P95MoveMs:F2}");

                EditorGUILayout.LabelField(
                    $"Fallbacks: timeoutBest={ultimateReport.TimeoutBestCount}, " +
                    $"timeoutFallback={ultimateReport.TimeoutFallbackLegalCount}, inconsistent={ultimateReport.NoLegalMovesInconsistentStateCount}");

                EditorGUILayout.LabelField(
                    $"Meta: matches={ultimateReport.Matches}, seedRange={ultimateReport.SeedRangeLabel}");

                EditorGUILayout.EndVertical();
            }
        }

        private static int Pct(int count, int total) =>
            total > 0 ? Mathf.RoundToInt((float)count / total * 100) : 0;
    }
}
