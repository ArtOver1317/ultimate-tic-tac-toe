#nullable enable

using System;
using System.Collections.Generic;
using Runtime.Games.TicTacToe.AI.Ultimate.Core;
using Runtime.Games.TicTacToe.AI.Ultimate.Decision;
using Runtime.Gameplay;
using Runtime.Games.TicTacToe.Moves;
using Runtime.Games.TicTacToe.Ultimate.Rules;

namespace Runtime.Games.TicTacToe.AI.Ultimate.Search
{
    internal sealed class UltimateBotNegamaxSearch
    {
        private const int _outerSize = 3;
        private const int _innerSize = 3;

        private readonly IUltimateRulesEngine _rules;
        private readonly UltimateBotNegamaxRecursor _recursor;

        public UltimateBotNegamaxSearch(IUltimateRulesEngine rules, UltimateBotHeuristic heuristic)
        {
            _rules = rules ?? throw new ArgumentNullException(nameof(rules));
            _recursor = new UltimateBotNegamaxRecursor(_rules, heuristic ?? throw new ArgumentNullException(nameof(heuristic)));
        }

        public DepthSearchResult SearchBestMoveAtDepth(
            IReadOnlyList<CellId> legal,
            PlayerMark[] cells,
            MiniBoardStatus[] miniBoards,
            AllowedMajors allowedMajors,
            int depth,
            PlayerMark selfMark,
            PlayerMark opponentMark,
            EvaluationWeights weights,
            SearchRuntime runtime)
        {
            var bestMove = legal[0];
            var bestScore = float.NegativeInfinity;
            var hasBest = false;
            var evaluated = 0;
            var alpha = float.NegativeInfinity;
            const float beta = float.PositiveInfinity;
            var ranked = new List<BotCandidateScore>(legal.Count);

            for (var i = 0; i < legal.Count; i++)
            {
                if (!runtime.CanContinue())
                    break;

                runtime.CancellationToken.ThrowIfCancellationRequested();

                var move = legal[i];
                
                if (!TryScoreRootMove(
                        move,
                        cells,
                        miniBoards,
                        depth,
                        selfMark,
                        opponentMark,
                        weights,
                        runtime,
                        alpha,
                        beta,
                        out var score))
                    continue;

                evaluated++;

                if (!hasBest || score > bestScore)
                {
                    bestScore = score;
                    bestMove = move;
                    hasBest = true;
                }

                ranked.Add(new BotCandidateScore(move, score));

                if (score > alpha)
                    alpha = score;
            }

            ranked.Sort(UltimateBotCandidateSelector.CompareDeterministically);
            return new DepthSearchResult(hasBest, bestMove, evaluated, ranked);
        }

        private bool TryScoreRootMove(
            CellId move,
            PlayerMark[] cells,
            MiniBoardStatus[] miniBoards,
            int depth,
            PlayerMark currentPlayer,
            PlayerMark opponentPlayer,
            EvaluationWeights weights,
            SearchRuntime runtime,
            float alpha,
            float beta,
            out float score)
        {
            score = 0;

            var idx = UltimateBotBoardUtilities.ToIndex(move);
            
            if (idx < 0 || idx >= cells.Length || cells[idx] != PlayerMark.None)
                return false;

            var localMini = UltimateBotBoardUtilities.CloneMiniBoards(miniBoards);
            cells[idx] = currentPlayer;

            try
            {
                UltimateRulesResult rulesResult;
                
                try
                {
                    rulesResult = _rules.EvaluateAfterMove(cells, _outerSize, _innerSize, move, localMini);
                }
                catch (ArgumentException)
                {
                    return false;
                }

                runtime.IncrementNode();
                
                score = _recursor.ScoreAfterAppliedMove(
                    cells,
                    miniBoards,
                    rulesResult,
                    depth,
                    currentPlayer,
                    opponentPlayer,
                    weights,
                    runtime,
                    alpha,
                    beta);
                
                return true;
            }
            finally
            {
                cells[idx] = PlayerMark.None;
            }
        }
    }
}