using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Runtime.Games.TicTacToe.AI.Ultimate.Core;
using Runtime.Games.TicTacToe.AI.Ultimate.Decision;
using Runtime.Games.TicTacToe.AI.Ultimate.Profiles;
using Runtime.Games.TicTacToe.AI.Ultimate.SelfPlay;
using Runtime.Games.TicTacToe.Ultimate.Rules;
using UnityEngine;

namespace Editor.AI
{
    internal sealed class SelfPlayUltimateModeRunner
    {
        private readonly SelfPlayWindowState _state;

        public SelfPlayUltimateModeRunner(SelfPlayWindowState state) => _state = state ?? throw new ArgumentNullException(nameof(state));

        public async UniTask RunAsync(StringBuilder logBuilder, CancellationToken cancellationToken)
        {
            var validProfiles = CollectProfiles();
            var pairs = SelfPlayWindowMatchups.BuildRoundRobinPairs(validProfiles.Count);
            var rules = new UltimateRulesEngine();
            var engine = new UltimateBotDecisionEngine(rules);
            var rngFactory = new BotRngSessionFactory();
            var catalog = new EditorUltimateProfileCatalog(validProfiles);
            var runner = new UltimateBotSelfPlayRunner(catalog, engine, rngFactory, rules);

            for (var pairIndex = 0; pairIndex < pairs.Count; pairIndex++)
            {
                var (leftIndex, rightIndex) = pairs[pairIndex];
                var left = validProfiles[leftIndex];
                var right = validProfiles[rightIndex];

                BeginPair(
                    pairIndex,
                    pairs.Count,
                    left.Id,
                    right.Id,
                    $"Matches: 0/{_state.MatchCount}",
                    $"Moves: 0/{SelfPlayWindowConstants.UltimateMaxTurnsPerMatch}");

                var report = await runner.RunAsync(
                    new SelfPlaySeriesConfig(
                        leftProfileId: left.Id,
                        rightProfileId: right.Id,
                        matches: _state.MatchCount,
                        baseSeed: GetPairBaseSeed(pairIndex),
                        seedCount: 1),
                    cancellationToken,
                    UpdateProgress);

                CompletePair();
                _state.Results.Add(new MatchupResult(left.Id, right.Id, report));
                AppendSummary(logBuilder, left.Id, right.Id, report);
            }

            CompleteRun(logBuilder, pairs.Count);
        }

        private List<UltimateBotProfile> CollectProfiles()
        {
            var profiles = new List<UltimateBotProfile>();

            for (var slotIndex = 0; slotIndex < _state.ProfileSlots.Count; slotIndex++)
            {
                var slot = _state.ProfileSlots[slotIndex];

                if (slot.UltimateProfile != null)
                    profiles.Add(slot.UltimateProfile);
            }

            return profiles;
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

        private void UpdateProgress(UltimateSelfPlayProgress progress)
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

        private static void AppendSummary(StringBuilder logBuilder, string leftProfileId, string rightProfileId, SelfPlaySeriesReport report) =>
            logBuilder.AppendLine(
                $"[{leftProfileId} vs {rightProfileId}] left wins={report.WinsLeft}, " +
                $"right wins={report.WinsRight}, draws={report.Draws}, " +
                $"avg={report.AvgMoveMs:F2}ms p95={report.P95MoveMs:F2}ms");

        private sealed class EditorUltimateProfileCatalog : IUltimateBotProfileCatalog
        {
            private readonly Dictionary<string, UltimateBotDifficultyProfileData> _profiles;

            public EditorUltimateProfileCatalog(IReadOnlyList<UltimateBotProfile> profiles)
            {
                _profiles = new Dictionary<string, UltimateBotDifficultyProfileData>(StringComparer.OrdinalIgnoreCase);

                for (var profileIndex = 0; profileIndex < profiles.Count; profileIndex++)
                {
                    var profile = profiles[profileIndex];

                    if (profile == null)
                        continue;

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