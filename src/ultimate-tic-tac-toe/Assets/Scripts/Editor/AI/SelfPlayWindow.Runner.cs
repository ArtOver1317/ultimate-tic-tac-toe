using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Runtime.Games.TicTacToe.AI;
using Runtime.Games.TicTacToe.AI.Ultimate;
using Runtime.Games.TicTacToe.Rules;
using Runtime.Games.TicTacToe.Ultimate.Rules;
using UnityEngine;

namespace Editor.AI
{
    public sealed partial class SelfPlayWindow
    {
        private bool HasValidProfiles()
        {
            var filled = 0;
            if (_isUltimate)
            {
                foreach (var p in _ultimateProfiles)
                {
                    if (p != null) filled++;
                }
            }
            else
            {
                foreach (var p in _classicProfiles)
                {
                    if (p != null) filled++;
                }
            }

            return filled >= 2;
        }

        private async UniTaskVoid RunAsync()
        {
            _isRunning = true;
            _results.Clear();
            _logText = string.Empty;
            _pairProgress = 0f;
            _pairProgressLabel = "Preparing...";
            _matchProgress = 0f;
            _matchProgressLabel = string.Empty;
            _moveProgress = 0f;
            _moveProgressLabel = string.Empty;
            _cts = new CancellationTokenSource();

            var sb = new StringBuilder();
            try
            {
                if (_isUltimate)
                {
                    var validUltimate = new List<UltimateBotProfile>();
                    for (var i = 0; i < _ultimateProfiles.Count; i++)
                    {
                        if (_ultimateProfiles[i] != null)
                        {
                            validUltimate.Add(_ultimateProfiles[i]);
                        }
                    }

                    var pairs = BuildRoundRobinPairs(validUltimate.Count);
                    var totalPairs = pairs.Count;

                    var rules = new UltimateRulesEngine();
                    var engine = new UltimateBotDecisionEngine(rules);
                    var rngFactory = new BotRngSessionFactory();
                    var catalog = new EditorUltimateProfileCatalog(validUltimate);
                    var runner = new UltimateBotSelfPlayRunner(catalog, engine, rngFactory, rules);

                    for (var pairIdx = 0; pairIdx < pairs.Count; pairIdx++)
                    {
                        var (a, b) = pairs[pairIdx];
                        var left = validUltimate[a];
                        var right = validUltimate[b];

                        _pairProgress = totalPairs > 0 ? (float)pairIdx / totalPairs : 0f;
                        _pairProgressLabel = $"Pairs: {pairIdx + 1}/{totalPairs} ({left.Id} vs {right.Id})";
                        _matchProgress = 0f;
                        _matchProgressLabel = "Matches: n/a";
                        _moveProgress = 0f;
                        _moveProgressLabel = "Moves: n/a";

                        var config = new SelfPlaySeriesConfig(
                            leftProfileId: left.Id,
                            rightProfileId: right.Id,
                            matches: _matchCount,
                            baseSeed: _baseSeed + pairIdx * 1000,
                            seedCount: 1);

                        var report = await runner.RunAsync(config, _cts.Token);

                        _matchProgress = 1f;
                        _moveProgress = 1f;

                        _results.Add(new MatchupResult
                        {
                            Profile1Name = left.Id,
                            Profile2Name = right.Id,
                            UltimateReport = report,
                        });

                        sb.AppendLine($"[{left.Id} vs {right.Id}] left wins={report.WinsLeft}, " +
                                      $"right wins={report.WinsRight}, draws={report.Draws}, " +
                                      $"avg={report.AvgMoveMs:F2}ms p95={report.P95MoveMs:F2}ms");
                    }

                    _pairProgress = 1f;
                    _pairProgressLabel = "Done";
                    _matchProgress = 1f;
                    _matchProgressLabel = "Matches: done";
                    _moveProgress = 1f;
                    _moveProgressLabel = "Moves: done";
                    sb.AppendLine($"\nAll {totalPairs} matchups complete.");
                }
                else
                {
                    var rules = new ClassicRulesEngine();
                    var engine = new MinimaxDecisionEngine(rules, _defaultSearchSettings);
                    var winLengthProvider = new ClassicWinLengthProvider();
                    var runner = new SelfPlayRunner(engine, rules, winLengthProvider);

                    var valid = new List<(BotProfile profile, BotSearchSettings overrideSettings)>();
                    for (var i = 0; i < _classicProfiles.Count; i++)
                    {
                        if (_classicProfiles[i] != null)
                        {
                            valid.Add((_classicProfiles[i], _classicProfileSearchOverrides[i]));
                        }
                    }

                    var pairs = BuildRoundRobinPairs(valid.Count);
                    var totalPairs = pairs.Count;

                    for (var pairIdx = 0; pairIdx < pairs.Count; pairIdx++)
                    {
                        var (a, b) = pairs[pairIdx];
                        var p1 = valid[a].profile;
                        var p2 = valid[b].profile;
                        var p1Override = valid[a].overrideSettings;
                        var p2Override = valid[b].overrideSettings;

                        _pairProgress = totalPairs > 0 ? (float)pairIdx / totalPairs : 0f;
                        _pairProgressLabel = $"Pairs: {pairIdx + 1}/{totalPairs} ({p1.Id} vs {p2.Id})";
                        _matchProgress = 0f;
                        _matchProgressLabel = "Matches: 0/0";
                        _moveProgress = 0f;
                        _moveProgressLabel = "Moves: 0/0";

                        int? winLen = _useWinLengthOverride ? _winLengthOverride : null;

                        var config = new SelfPlayConfig(
                            _boardSize,
                            p1.ToValidatedData(),
                            p2.ToValidatedData(),
                            _matchCount,
                            _baseSeed + pairIdx * 1000,
                            winLen,
                            p1Override != null ? p1Override.ToValidatedData() : null,
                            p2Override != null ? p2Override.ToValidatedData() : null);

                        var report = await runner.RunAsync(config, _cts.Token, progress =>
                        {
                            var totalMatches = Math.Max(progress.TotalMatches, 1);
                            var maxTurns = Math.Max(progress.MaxTurns, 1);
                            var currentMatch = Mathf.Clamp(progress.MatchIndex + 1, 1, totalMatches);
                            var currentTurn = Mathf.Clamp(progress.TurnIndex + 1, 1, maxTurns);

                            _matchProgress = (float)progress.MatchIndex / totalMatches;
                            _matchProgressLabel = $"Matches: {currentMatch}/{totalMatches}";

                            _moveProgress = (float)progress.TurnIndex / maxTurns;
                            _moveProgressLabel = $"Moves: {currentTurn}/{maxTurns}";
                        });

                        _matchProgress = 1f;
                        _moveProgress = 1f;

                        _results.Add(new MatchupResult
                        {
                            Profile1Name = p1.Id,
                            Profile2Name = p2.Id,
                            ClassicReport = report,
                        });

                        sb.AppendLine($"[{p1.Id} vs {p2.Id}] P1 wins={report.Player1Wins}, " +
                                      $"P2 wins={report.Player2Wins}, Draws={report.Draws}, " +
                                      $"Time={report.TotalTimeMs:F0}ms");
                    }

                    _pairProgress = 1f;
                    _pairProgressLabel = "Done";
                    _matchProgress = 1f;
                    _matchProgressLabel = "Matches: done";
                    _moveProgress = 1f;
                    _moveProgressLabel = "Moves: done";
                    sb.AppendLine($"\nAll {totalPairs} matchups complete.");
                }
            }
            catch (OperationCanceledException)
            {
                sb.AppendLine("Cancelled.");
            }
            catch (Exception ex)
            {
                sb.AppendLine($"Error: {ex.Message}");
                Debug.LogException(ex);
            }
            finally
            {
                _logText = sb.ToString();
                _isRunning = false;
                _cts?.Dispose();
                _cts = null;
                Repaint();
            }
        }

        private void EnsureOverrideSlotsCount()
        {
            while (_ultimateProfiles.Count < _classicProfiles.Count)
            {
                _ultimateProfiles.Add(null);
            }

            while (_ultimateProfiles.Count > _classicProfiles.Count)
            {
                _ultimateProfiles.RemoveAt(_ultimateProfiles.Count - 1);
            }

            while (_classicProfileSearchOverrides.Count < _classicProfiles.Count)
            {
                _classicProfileSearchOverrides.Add(null);
            }

            while (_classicProfileSearchOverrides.Count > _classicProfiles.Count)
            {
                _classicProfileSearchOverrides.RemoveAt(_classicProfileSearchOverrides.Count - 1);
            }
        }

        private static List<(int a, int b)> BuildRoundRobinPairs(int count)
        {
            var pairs = new List<(int a, int b)>();
            for (var i = 0; i < count; i++)
            {
                for (var j = i + 1; j < count; j++)
                {
                    pairs.Add((i, j));
                }
            }

            return pairs;
        }

        private sealed class EditorUltimateProfileCatalog : IUltimateBotProfileCatalog
        {
            private readonly Dictionary<string, UltimateBotDifficultyProfileData> _profiles;

            public EditorUltimateProfileCatalog(IReadOnlyList<UltimateBotProfile> profiles)
            {
                _profiles = new Dictionary<string, UltimateBotDifficultyProfileData>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < profiles.Count; i++)
                {
                    var profile = profiles[i];
                    if (profile == null)
                    {
                        continue;
                    }

                    _profiles[profile.Id] = profile.ToValidatedData();
                }
            }

            public bool TryGet(string difficultyId, out UltimateBotDifficultyProfileData profile)
            {
                if (string.IsNullOrWhiteSpace(difficultyId))
                {
                    profile = default;
                    return false;
                }

                return _profiles.TryGetValue(difficultyId.Trim(), out profile);
            }
        }
    }
}
