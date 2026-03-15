#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Cysharp.Threading.Tasks;
using Runtime.Games.TicTacToe.AI.Ultimate.Core;
using Runtime.Games.TicTacToe.AI.Ultimate.Search;
using Runtime.Games.TicTacToe.Moves;
using Runtime.Games.TicTacToe.Ultimate.Rules;
using GameStatus = Runtime.Games.TicTacToe.Rules.GameStatus;

namespace Runtime.Games.TicTacToe.AI.Ultimate.Decision
{
    public sealed class UltimateBotDecisionEngine : IUltimateBotDecisionEngine
    {
        private readonly struct DecisionContext
        {
            public IReadOnlyList<CellId> Legal { get; }
            public PlayerMark[] Cells { get; }
            public MiniBoardStatus[] MiniBoards { get; }
            public AllowedMajors AllowedMajors { get; }
            public UltimateBotDifficultyProfileData Profile { get; }
            public IBotRngSession Rng { get; }
            public PlayerMark SelfMark { get; }
            public PlayerMark OpponentMark { get; }

            public DecisionContext(UltimateBotDecisionRequest request)
            {
                Legal = request.LegalMovesStable;
                Cells = request.Snapshot.Cells81.ToArray();
                MiniBoards = request.Snapshot.MiniBoards9.ToArray();
                AllowedMajors = request.Snapshot.AllowedMajors;
                Profile = request.Profile;
                Rng = request.Rng;
                SelfMark = UltimateBotBoardUtilities.SlotToMark(request.Snapshot.ActivePlayerSlot);
                OpponentMark = UltimateBotBoardUtilities.SlotToMark(1 - request.Snapshot.ActivePlayerSlot);
            }
        }

        private readonly UltimateBotHardRuleResolver _hardRuleResolver;
        private readonly UltimateBotMoveSearch _search;

        public UltimateBotDecisionEngine(IUltimateRulesEngine rules)
        {
            var rulesEngine = rules ?? throw new ArgumentNullException(nameof(rules));
            var heuristic = new UltimateBotHeuristic(rulesEngine);
            _hardRuleResolver = new UltimateBotHardRuleResolver(rulesEngine, heuristic);
            _search = new UltimateBotMoveSearch(rulesEngine, heuristic);
        }

        public UniTask<UltimateBotDecisionResult> ChooseMoveAsync(UltimateBotDecisionRequest request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            ValidateLegalMoves(request.LegalMovesStable);

            if (TryBuildSingleMoveResult(request.LegalMovesStable, out var singleMoveResult))
                return UniTask.FromResult(singleMoveResult);

            var context = new DecisionContext(request);

            return UniTask.FromResult(TryResolveHardRule(context, out var hardRuleResult) 
                ? hardRuleResult 
                : ChooseFromSearch(context, ct));
        }

        private bool TryResolveHardRule(DecisionContext context, out UltimateBotDecisionResult result)
        {
            var globalWinNow = _hardRuleResolver.FindImmediateGlobalRuleMove(
                context.Legal,
                context.Cells,
                context.MiniBoards,
                context.SelfMark,
                GameStatus.Win,
                context.SelfMark);

            if (TryApplyHardRule(globalWinNow, context.Profile.MustWinGlobalNowProbability, context.Rng, HardRuleType.GlobalWinNow, out result))
                return true;

            var globalBlockNow = _hardRuleResolver.FindOpponentGlobalThreatBlockMove(
                context.Legal,
                context.Cells,
                context.MiniBoards,
                context.OpponentMark);

            if (TryApplyHardRule(globalBlockNow, context.Profile.MustBlockGlobalNowProbability, context.Rng, HardRuleType.GlobalBlockNow, out result))
                return true;

            var localWinNow = _hardRuleResolver.FindImmediateLocalRuleMove(
                context.Legal,
                context.Cells,
                context.MiniBoards,
                context.SelfMark);

            if (TryApplyHardRule(localWinNow, context.Profile.MustWinLocalNowProbability, context.Rng, HardRuleType.LocalWinNow, out result))
                return true;

            var localBlockNow = _hardRuleResolver.FindImmediateLocalBlockMove(
                context.Legal,
                context.Cells,
                context.MiniBoards,
                context.OpponentMark);

            if (TryApplyHardRule(localBlockNow, context.Profile.MustBlockLocalNowProbability, context.Rng, HardRuleType.LocalBlockNow, out result))
                return true;

            result = default;
            return false;
        }

        private UltimateBotDecisionResult ChooseFromSearch(DecisionContext context, CancellationToken ct)
        {
            var searchResult = _search.Search(
                context.Legal,
                context.Cells,
                context.MiniBoards,
                context.AllowedMajors,
                context.Profile,
                context.SelfMark,
                context.OpponentMark,
                Stopwatch.StartNew(),
                ct);

            if (!searchResult.HasBest)
                return BuildFallbackSearchResult(context.Legal[0], searchResult);

            var bestMove = ApplyNoiseIfNeeded(context, searchResult);
            return BuildSearchResult(bestMove, searchResult);
        }

        private static void ValidateLegalMoves(IReadOnlyList<CellId> legal)
        {
            if (legal.Count == 0)
                throw new InvalidOperationException("LegalMovesStable must not be empty.");
        }

        private static bool TryBuildSingleMoveResult(IReadOnlyList<CellId> legal, out UltimateBotDecisionResult result)
        {
            if (legal.Count != 1)
            {
                result = default;
                return false;
            }

            result = BuildSingleMoveResult(legal[0]);
            return true;
        }

        private static bool TryApplyHardRule(
            CellId? move,
            float probability,
            IBotRngSession rng,
            HardRuleType ruleType,
            out UltimateBotDecisionResult result)
        {
            if (move.HasValue && ShouldApply(probability, rng))
            {
                result = BuildHardRuleResult(move.Value, ruleType);
                return true;
            }

            result = default;
            return false;
        }

        private static CellId ApplyNoiseIfNeeded(DecisionContext context, IterativeSearchResult searchResult)
        {
            var bestMove = searchResult.BestMove;
            
            if (context.Profile.Noise <= 0f)
                return bestMove;

            var topCount = Math.Min(context.Profile.TopCandidateCount, context.Legal.Count);
            
            var candidates = UltimateBotCandidateSelector.TakeTopCandidates(
                searchResult.RankedCandidates.Count > 0
                    ? searchResult.RankedCandidates
                    : new List<BotCandidateScore> { new(bestMove, 0f) },
                topCount);

            return candidates.Count <= 1 
                ? bestMove 
                : UltimateBotCandidateSelector.ApplyNoise(candidates, context.Profile, context.Rng).Move;
        }

        private static UltimateBotDecisionResult BuildFallbackSearchResult(CellId fallbackMove, IterativeSearchResult searchResult) =>
            new(
                move: fallbackMove,
                degradationReason: BotFailureReason.TimeoutFallbackLegal,
                hardRuleApplied: false,
                appliedHardRule: null,
                cutoffReason: searchResult.CutoffReason == SearchCutoffReason.Completed ? SearchCutoffReason.TimeBudgetExceeded : searchResult.CutoffReason,
                cutoffDetails: string.IsNullOrEmpty(searchResult.CutoffDetails) ? "timeout_fallback_legal" : searchResult.CutoffDetails,
                searchDepthReached: searchResult.DepthReached,
                iterationsCompleted: searchResult.IterationsCompleted);

        private static UltimateBotDecisionResult BuildSearchResult(CellId move, IterativeSearchResult searchResult) =>
            new(
                move: move,
                degradationReason: GetSearchDegradation(searchResult.CutoffReason),
                hardRuleApplied: false,
                appliedHardRule: null,
                cutoffReason: searchResult.CutoffReason,
                cutoffDetails: searchResult.CutoffDetails,
                searchDepthReached: searchResult.DepthReached,
                iterationsCompleted: searchResult.IterationsCompleted);

        private static UltimateBotDecisionResult BuildSingleMoveResult(CellId move) =>
            new(
                move: move,
                degradationReason: null,
                hardRuleApplied: false,
                appliedHardRule: null,
                cutoffReason: SearchCutoffReason.Completed,
                cutoffDetails: string.Empty,
                searchDepthReached: 1,
                iterationsCompleted: 1);

        private static BotFailureReason? GetSearchDegradation(SearchCutoffReason cutoffReason) =>
            cutoffReason == SearchCutoffReason.TimeBudgetExceeded
                ? BotFailureReason.TimeoutBest
                : null;

        private static UltimateBotDecisionResult BuildHardRuleResult(CellId move, HardRuleType type) =>
            new(
                move: move,
                degradationReason: null,
                hardRuleApplied: true,
                appliedHardRule: type,
                cutoffReason: SearchCutoffReason.Completed,
                cutoffDetails: "hard_rule",
                searchDepthReached: 1,
                iterationsCompleted: 1);

        private static bool ShouldApply(float probability, IBotRngSession rng)
        {
            if (probability <= 0f)
                return false;

            if (probability >= 1f)
                return true;

            return rng.NextFloat01() < probability;
        }
    }
}