#nullable enable

using System;
using Runtime.Games.TicTacToe.AI.Ultimate.Core;
using Runtime.Games.TicTacToe.AI.Ultimate.Decision;
using Runtime.Gameplay;
using Runtime.Games.TicTacToe.Moves;
using Runtime.Games.TicTacToe.Ultimate.Rules;
using GameStatus = Runtime.Gameplay.GameStatus;

namespace Runtime.Games.TicTacToe.AI.Ultimate.Search
{
    internal sealed class UltimateBotNegamaxRecursor
    {
        private const int _outerSize = 3;
        private const int _innerSize = 3;
        private const float _terminalWinScore = 1_000_000f;

        private readonly IUltimateRulesEngine _rules;
        private readonly UltimateBotHeuristic _heuristic;

        public UltimateBotNegamaxRecursor(IUltimateRulesEngine rules, UltimateBotHeuristic heuristic)
        {
            _rules = rules ?? throw new ArgumentNullException(nameof(rules));
            _heuristic = heuristic ?? throw new ArgumentNullException(nameof(heuristic));
        }

        public float ScoreAfterAppliedMove(
            PlayerMark[] cells,
            MiniBoardStatus[] currentMiniBoards,
            UltimateRulesResult rulesResult,
            int depth,
            PlayerMark currentPlayer,
            PlayerMark opponentPlayer,
            EvaluationWeights weights,
            SearchRuntime runtime,
            float alpha,
            float beta)
        {
            if (TryGetTerminalScore(rulesResult, currentPlayer, depth, out var terminalScore))
                return terminalScore;

            var nextMiniBoards = ApplyMiniBoardDelta(currentMiniBoards, rulesResult);
            
            if (depth <= 1)
                return EvaluatePosition(cells, nextMiniBoards, rulesResult.AllowedMajors, currentPlayer, opponentPlayer, weights);

            return -Negamax(
                cells,
                nextMiniBoards,
                rulesResult.AllowedMajors,
                depth - 1,
                opponentPlayer,
                currentPlayer,
                weights,
                runtime,
                -beta,
                -alpha);
        }

        private float Negamax(
            PlayerMark[] cells,
            MiniBoardStatus[] miniBoards,
            AllowedMajors allowedMajors,
            int depth,
            PlayerMark currentPlayer,
            PlayerMark opponentPlayer,
            EvaluationWeights weights,
            SearchRuntime runtime,
            float alpha,
            float beta)
        {
            if (TryResolveLeafNode(cells, miniBoards, allowedMajors, depth, currentPlayer, opponentPlayer, weights, runtime, out var leafScore))
                return leafScore;

            var legal = UltimateBotBoardUtilities.BuildLegalMoves(cells, miniBoards, allowedMajors);
            
            if (legal.Count == 0)
                return 0f;

            var best = float.NegativeInfinity;
            
            for (var i = 0; i < legal.Count; i++)
            {
                if (!ProcessNegamaxMove(
                        legal[i],
                        cells,
                        miniBoards,
                        depth,
                        currentPlayer,
                        opponentPlayer,
                        weights,
                        runtime,
                        beta,
                        ref best,
                        ref alpha))
                    break;
            }

            return float.IsNegativeInfinity(best)
                ? EvaluatePosition(cells, miniBoards, allowedMajors, currentPlayer, opponentPlayer, weights)
                : best;
        }

        private bool ProcessNegamaxMove(
            CellId move,
            PlayerMark[] cells,
            MiniBoardStatus[] miniBoards,
            int depth,
            PlayerMark currentPlayer,
            PlayerMark opponentPlayer,
            EvaluationWeights weights,
            SearchRuntime runtime,
            float beta,
            ref float best,
            ref float alpha)
        {
            if (!runtime.CanContinue())
                return false;

            if (!TryScoreChildMove(
                    move,
                    cells,
                    miniBoards,
                    depth,
                    currentPlayer,
                    opponentPlayer,
                    weights,
                    runtime,
                    alpha,
                    beta,
                    out var score))
                return true;

            UpdateSearchBounds(score, ref best, ref alpha);
            return alpha < beta;
        }

        private static void UpdateSearchBounds(float score, ref float best, ref float alpha)
        {
            if (score > best)
                best = score;

            if (score > alpha)
                alpha = score;
        }

        private bool TryResolveLeafNode(
            PlayerMark[] cells,
            MiniBoardStatus[] miniBoards,
            AllowedMajors allowedMajors,
            int depth,
            PlayerMark currentPlayer,
            PlayerMark opponentPlayer,
            EvaluationWeights weights,
            SearchRuntime runtime,
            out float score)
        {
            if (!runtime.CanContinue())
            {
                score = EvaluatePosition(cells, miniBoards, allowedMajors, currentPlayer, opponentPlayer, weights);
                return true;
            }

            runtime.CancellationToken.ThrowIfCancellationRequested();

            if (depth <= 0)
            {
                score = EvaluatePosition(cells, miniBoards, allowedMajors, currentPlayer, opponentPlayer, weights);
                return true;
            }

            score = 0;
            return false;
        }

        private bool TryScoreChildMove(
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
                
                score = ScoreChildMove(
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

        private float ScoreChildMove(
            PlayerMark[] cells,
            MiniBoardStatus[] miniBoards,
            UltimateRulesResult rulesResult,
            int depth,
            PlayerMark currentPlayer,
            PlayerMark opponentPlayer,
            EvaluationWeights weights,
            SearchRuntime runtime,
            float alpha,
            float beta)
        {
            if (TryGetTerminalScore(rulesResult, currentPlayer, depth, out var terminalScore))
                return terminalScore;

            return -Negamax(
                cells,
                ApplyMiniBoardDelta(miniBoards, rulesResult),
                rulesResult.AllowedMajors,
                depth - 1,
                opponentPlayer,
                currentPlayer,
                weights,
                runtime,
                -beta,
                -alpha);
        }

        private float EvaluatePosition(
            PlayerMark[] cells,
            MiniBoardStatus[] miniBoards,
            AllowedMajors allowedMajors,
            PlayerMark selfMark,
            PlayerMark opponentMark,
            EvaluationWeights weights)
        {
            var legalMoves = UltimateBotBoardUtilities.BuildLegalMoves(cells, miniBoards, allowedMajors);
            return _heuristic.EvaluatePosition(cells, miniBoards, legalMoves, allowedMajors, selfMark, opponentMark, weights);
        }

        private static bool TryGetTerminalScore(
            UltimateRulesResult rulesResult,
            PlayerMark currentPlayer,
            int depth,
            out float score)
        {
            if (rulesResult.Match.Status == GameStatus.Win)
            {
                score = rulesResult.Match.Winner == currentPlayer
                    ? _terminalWinScore + depth
                    : -_terminalWinScore - depth;
                
                return true;
            }

            if (rulesResult.Match.Status == GameStatus.Draw)
            {
                score = 0f;
                return true;
            }

            score = 0;
            return false;
        }

        private static MiniBoardStatus[] ApplyMiniBoardDelta(MiniBoardStatus[] miniBoards, UltimateRulesResult rulesResult)
        {
            var next = UltimateBotBoardUtilities.CloneMiniBoards(miniBoards);
            
            if (rulesResult.MiniBoardDelta.HasValue)
            {
                var delta = rulesResult.MiniBoardDelta.Value;
                next[delta.Major] = delta.NewStatus;
            }

            return next;
        }
    }
}