using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Runtime.Games.TicTacToe.AI;
using Runtime.Games.TicTacToe.Rules;
using UnityEngine;

namespace Editor.AI
{
    internal sealed class SelfPlayClassicModeRunner
    {
        private readonly SelfPlayWindowState _state;

        public SelfPlayClassicModeRunner(SelfPlayWindowState state) => _state = state ?? throw new ArgumentNullException(nameof(state));

        public async UniTask RunAsync(StringBuilder logBuilder, CancellationToken cancellationToken)
        {
            var rules = new ClassicRulesEngine();
            var engine = new MinimaxDecisionEngine(rules, _state.DefaultSearchSettings);
            var winLengthProvider = new ClassicWinLengthProvider();
            var runner = new SelfPlayRunner(engine, rules, winLengthProvider);
            var participants = CollectParticipants();
            var pairs = SelfPlayWindowMatchups.BuildRoundRobinPairs(participants.Count);

            for (var pairIndex = 0; pairIndex < pairs.Count; pairIndex++)
            {
                var (leftIndex, rightIndex) = pairs[pairIndex];
                var left = participants[leftIndex];
                var right = participants[rightIndex];

                BeginPair(pairIndex, pairs.Count, left.profile.Id, right.profile.Id, $"Matches: 0/{_state.MatchCount}", "Moves: 0/0");

                var winLength = _state.UseWinLengthOverride ? _state.WinLengthOverride : (int?)null;
                
                var report = await runner.RunAsync(
                    new SelfPlayConfig(
                        _state.BoardSize,
                        left.profile.ToValidatedData(),
                        right.profile.ToValidatedData(),
                        _state.MatchCount,
                        GetPairBaseSeed(pairIndex),
                        winLength,
                        left.overrideSettings != null ? left.overrideSettings.ToValidatedData() : null,
                        right.overrideSettings != null ? right.overrideSettings.ToValidatedData() : null),
                    cancellationToken,
                    UpdateProgress);

                CompletePair();
                _state.Results.Add(new MatchupResult(left.profile.Id, right.profile.Id, report));
                AppendSummary(logBuilder, left.profile.Id, right.profile.Id, report);
            }

            CompleteRun(logBuilder, pairs.Count);
        }

        private List<(BotProfile profile, BotSearchSettings overrideSettings)> CollectParticipants()
        {
            var participants = new List<(BotProfile profile, BotSearchSettings overrideSettings)>();

            for (var slotIndex = 0; slotIndex < _state.ProfileSlots.Count; slotIndex++)
            {
                var slot = _state.ProfileSlots[slotIndex];

                if (slot.ClassicProfile != null)
                    participants.Add((slot.ClassicProfile, slot.ClassicSearchOverride));
            }

            return participants;
        }

        private void BeginPair(int pairIndex, int totalPairs, string leftProfileId, string rightProfileId, string matchLabel, string moveLabel)
        {
            _state.PairProgress = totalPairs > 0 ? (float)pairIndex / totalPairs : 0f;
            _state.PairProgressLabel = $"Pairs: {pairIndex + 1}/{totalPairs} ({leftProfileId} vs {rightProfileId})";
            _state.MatchProgress = 0f;
            _state.MatchProgressLabel = matchLabel;
            _state.MoveProgress = 0f;
            _state.MoveProgressLabel = moveLabel;
        }

        private void CompletePair()
        {
            _state.MatchProgress = 1f;
            _state.MoveProgress = 1f;
        }

        private void CompleteRun(StringBuilder logBuilder, int totalPairs)
        {
            _state.PairProgress = 1f;
            _state.PairProgressLabel = "Done";
            _state.MatchProgress = 1f;
            _state.MatchProgressLabel = "Matches: done";
            _state.MoveProgress = 1f;
            _state.MoveProgressLabel = "Moves: done";
            logBuilder.AppendLine();
            logBuilder.AppendLine($"All {totalPairs} matchups complete.");
        }

        private void UpdateProgress(SelfPlayProgress progress)
        {
            var totalMatches = Math.Max(progress.TotalMatches, 1);
            var maxTurns = Math.Max(progress.MaxTurns, 1);
            var currentMatch = Mathf.Clamp(progress.MatchIndex + 1, 1, totalMatches);
            var currentTurn = Mathf.Clamp(progress.TurnIndex + 1, 1, maxTurns);

            _state.MatchProgress = (float)progress.MatchIndex / totalMatches;
            _state.MatchProgressLabel = $"Matches: {currentMatch}/{totalMatches}";
            _state.MoveProgress = (float)progress.TurnIndex / maxTurns;
            _state.MoveProgressLabel = $"Moves: {currentTurn}/{maxTurns}";
        }

        private int GetPairBaseSeed(int pairIndex) =>
            _state.BaseSeed + pairIndex * SelfPlayWindowConstants.PairSeedStride;

        private static void AppendSummary(StringBuilder logBuilder, string leftProfileId, string rightProfileId, SelfPlayReport report) =>
            logBuilder.AppendLine(
                $"[{leftProfileId} vs {rightProfileId}] P1 wins={report.Player1Wins}, " +
                $"P2 wins={report.Player2Wins}, Draws={report.Draws}, " +
                $"Time={report.TotalTimeMs:F0}ms");
    }
}