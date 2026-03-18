#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Runtime.Games.TicTacToe.AI.Core;
using Runtime.Games.TicTacToe.AI.Profiles;
using Runtime.Gameplay;
using Runtime.Games.TicTacToe.Moves;
using Runtime.Games.TicTacToe.Rules;
using Debug = UnityEngine.Debug;

namespace Runtime.Games.TicTacToe.AI.Search
{
    /// <summary>
    /// MVP decision engine: hard rules → minimax + alpha-beta + iterative deepening.
    /// Supports arbitrary N×N boards with search scaling (ADR-8, ADR-13).
    /// </summary>
    public sealed class MinimaxDecisionEngine : IBotDecisionEngine
    {
        private readonly IRulesEngine _rules;
        private readonly BotSearchSettingsData _searchSettings;
        private readonly MinimaxSearchRecursor _recursor;

        public MinimaxDecisionEngine(IRulesEngine rules)
            : this(rules, null) { }

        public MinimaxDecisionEngine(IRulesEngine rules, BotSearchSettings? searchSettings)
        {
            _rules = rules ?? throw new ArgumentNullException(nameof(rules));
            _recursor = new MinimaxSearchRecursor(_rules);
            
            _searchSettings = searchSettings != null
                ? searchSettings.ToValidatedData()
                : BotSearchSettingsData.FastPveDefault;
        }

        public async UniTask<CellId> ChooseMoveAsync(
            BotDecisionRequest request,
            BotProfileData profile,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            if (request.LegalMoves.Count == 0)
                throw new InvalidOperationException("No legal moves available.");

            if (request.LegalMoves.Count == 1)
                return request.LegalMoves[0];

            var searchSettings = request.SearchSettingsOverride ?? _searchSettings;

            if (TryChooseHardRuleMove(request, profile, out var hardRuleMove))
                return hardRuleMove;

            var searchContext = new MinimaxSearchContext(request, profile, searchSettings, ct);
            var candidates = MinimaxMoveOrdering.FilterCandidates(request, profile.TopCandidateCount, searchSettings);
            var scored = await _recursor.SearchCandidatesAsync(searchContext, candidates);

            LogSearchDiagnostics(searchContext, profile.EnableDiagnostics);

            return scored.Count > 0
                ? MinimaxCandidateSelector.SelectFromCandidates(scored, profile, request.Rng)
                : candidates[0];
        }

        private bool TryChooseHardRuleMove(
            BotDecisionRequest request,
            BotProfileData profile,
            out CellId move) =>
            TryChooseImmediateRuleMove(request, request.ActivePlayerSlot, profile.MustWinNowProbability, out move) 
            || TryChooseImmediateRuleMove(request, 1 - request.ActivePlayerSlot, profile.MustBlockNowProbability, out move);

        private bool TryChooseImmediateRuleMove(
            BotDecisionRequest request,
            int forSlot,
            float probability,
            out CellId move)
        {
            move = default;

            var immediateMove = FindImmediateMove(request, forSlot);
            
            if (immediateMove == null || !ShouldExecuteHardRule(probability, request.Rng))
                return false;

            move = immediateMove.Value;
            return true;
        }

        private CellId? FindImmediateMove(BotDecisionRequest request, int forSlot)
        {
            var mark = SlotToMark(forSlot);
            var cells = request.Cells;
            var boardSize = request.BoardSize;

            for (var i = 0; i < request.LegalMoves.Count; i++)
            {
                var move = request.LegalMoves[i];
                var idx = move.Major * boardSize + move.Minor;
                var prev = cells[idx];
                cells[idx] = mark;

                try
                {
                    var result = _rules.Evaluate(cells, boardSize, move);
                    
                    if (result.Status == GameStatus.Win && result.Winner == mark)
                        return move;
                }
                finally
                {
                    cells[idx] = prev;
                }
            }

            return null;
        }

        private static bool ShouldExecuteHardRule(float probability, IBotRandom rng)
        {
            if (probability >= 1f) 
                return true;
            
            if (probability <= 0f) 
                return false;
            
            return rng.NextFloat01() < probability;
        }

        private static void LogSearchDiagnostics(MinimaxSearchContext searchContext, bool enableDiagnostics)
        {
            if (searchContext.TimedOut && enableDiagnostics)
                Debug.Log($"[Bot] Time budget exhausted at {searchContext.Stopwatch.ElapsedMilliseconds}ms (budget={searchContext.BudgetMs}ms)");

            if (searchContext.Stopwatch.ElapsedMilliseconds > searchContext.SafetyLimitMs)
                Debug.LogError($"[Bot] Safety limit exceeded: {searchContext.Stopwatch.ElapsedMilliseconds}ms > {searchContext.SafetyLimitMs}ms");
        }

        private static PlayerMark SlotToMark(int slot) => slot == 0 ? PlayerMark.X : PlayerMark.O;
    }
}