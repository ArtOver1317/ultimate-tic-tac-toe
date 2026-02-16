using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Editor.AI
{
    public sealed partial class SelfPlayWindow
    {
        private void ExportResults()
        {
            var path = EditorUtility.SaveFilePanel(
                "Export Self-Play Report",
                string.Empty,
                $"SelfPlay_{DateTime.Now:yyyyMMdd_HHmmss}.txt",
                "txt");

            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine("=== Self-Play Report ===");
            sb.AppendLine($"Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine(_isUltimate ? "Mode: Ultimate (fixed 3x3x3)" : $"Mode: Classic ({_boardSize}x{_boardSize})");
            if (!_isUltimate && _useWinLengthOverride)
            {
                sb.AppendLine($"Win Length Override: {_winLengthOverride}");
            }

            sb.AppendLine($"Matches per pair: {_matchCount}");
            sb.AppendLine($"Base Seed: {_baseSeed}");
            sb.AppendLine();

            foreach (var r in _results)
            {
                sb.AppendLine($"--- {r.Profile1Name} vs {r.Profile2Name} ---");

                if (r.ClassicReport.HasValue)
                {
                    var rpt = r.ClassicReport.Value;
                    var total = rpt.Player1Wins + rpt.Player2Wins + rpt.Draws;

                    sb.AppendLine($"  P1 Wins: {rpt.Player1Wins} ({Pct(rpt.Player1Wins, total)}%)");
                    sb.AppendLine($"  P2 Wins: {rpt.Player2Wins} ({Pct(rpt.Player2Wins, total)}%)");
                    sb.AppendLine($"  Draws:   {rpt.Draws} ({Pct(rpt.Draws, total)}%)");
                    sb.AppendLine($"  Avg ms/move: P1={rpt.AvgMsPerMoveP1:F2}, P2={rpt.AvgMsPerMoveP2:F2}");
                    sb.AppendLine($"  Tactical misses P1: win={rpt.MissedWinP1}, block={rpt.MissedBlockP1}");
                    sb.AppendLine($"  Tactical misses P2: win={rpt.MissedWinP2}, block={rpt.MissedBlockP2}");
                    sb.AppendLine($"  Total: {rpt.TotalMoves} moves, {rpt.TotalTimeMs:F0}ms");
                }
                else if (r.UltimateReport.HasValue)
                {
                    var rpt = r.UltimateReport.Value;
                    var total = rpt.WinsLeft + rpt.WinsRight + rpt.Draws;

                    sb.AppendLine($"  Left Wins:  {rpt.WinsLeft} ({Pct(rpt.WinsLeft, total)}%)");
                    sb.AppendLine($"  Right Wins: {rpt.WinsRight} ({Pct(rpt.WinsRight, total)}%)");
                    sb.AppendLine($"  Draws:      {rpt.Draws} ({Pct(rpt.Draws, total)}%)");
                    sb.AppendLine($"  Move latency ms: avg={rpt.AvgMoveMs:F2}, p50={rpt.P50MoveMs:F2}, p95={rpt.P95MoveMs:F2}");
                    sb.AppendLine($"  Fallbacks: timeoutBest={rpt.TimeoutBestCount}, timeoutFallback={rpt.TimeoutFallbackLegalCount}, inconsistent={rpt.NoLegalMovesInconsistentStateCount}");
                    sb.AppendLine($"  Meta: matches={rpt.Matches}, seedRange={rpt.SeedRangeLabel}");
                }

                sb.AppendLine();
            }

            if (!string.IsNullOrEmpty(_logText))
            {
                sb.AppendLine("--- Log ---");
                sb.AppendLine(_logText);
            }

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            Debug.Log($"[SelfPlay] Report exported to: {path}");
        }
    }
}
