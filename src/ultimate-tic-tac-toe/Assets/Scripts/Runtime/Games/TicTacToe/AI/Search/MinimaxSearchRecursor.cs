#nullable enable

using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Runtime.Games.TicTacToe.Moves;
using Runtime.Games.TicTacToe.Rules;

namespace Runtime.Games.TicTacToe.AI.Search
{
    internal sealed class MinimaxSearchRecursor
    {
        private readonly IRulesEngine _rules;

        public MinimaxSearchRecursor(IRulesEngine rules) =>
            _rules = rules ?? throw new ArgumentNullException(nameof(rules));

        public async UniTask<List<(CellId move, float score)>> SearchCandidatesAsync(
            MinimaxSearchContext context,
            List<CellId> candidates)
        {
            var bestDepthScores = new List<(CellId move, float score)>(candidates.Count);

            for (var depth = context.MinDepth; depth <= context.EffectiveMaxDepth; depth++)
            {
                if (context.HasBudgetExpired())
                {
                    context.TimedOut = true;
                    break;
                }

                context.CancellationToken.ThrowIfCancellationRequested();

                var (depthScores, isComplete) = await SearchDepthAsync(context, candidates, depth);
                
                if (isComplete && depthScores.Count > 0 || depthScores.Count > 0 && bestDepthScores.Count == 0)
                    bestDepthScores = depthScores;
            }

            return bestDepthScores;
        }

        private async UniTask<(List<(CellId move, float score)> Scores, bool IsComplete)> SearchDepthAsync(
            MinimaxSearchContext context,
            List<CellId> candidates,
            int depth)
        {
            var depthScores = new List<(CellId move, float score)>(candidates.Count);

            for (var i = 0; i < candidates.Count; i++)
            {
                if (context.HasBudgetExpired())
                {
                    context.TimedOut = true;
                    return (depthScores, false);
                }

                context.CancellationToken.ThrowIfCancellationRequested();

                var move = candidates[i];
                var score = await EvaluateCandidateAsync(context, move, depth);
                depthScores.Add((move, score));
            }

            return (depthScores, true);
        }

        private async UniTask<float> EvaluateCandidateAsync(MinimaxSearchContext context, CellId move, int depth)
        {
            var idx = move.Major * context.BoardSize + move.Minor;
            context.Cells[idx] = SlotToMark(context.BotSlot);

            try
            {
                return await MinimaxAsync(
                    context,
                    depth - 1,
                    isMaximizing: false,
                    float.NegativeInfinity,
                    float.PositiveInfinity,
                    move);
            }
            finally
            {
                context.Cells[idx] = PlayerMark.None;
            }
        }

        private async UniTask<float> MinimaxAsync(
            MinimaxSearchContext context,
            int depth,
            bool isMaximizing,
            float alpha,
            float beta,
            CellId lastMove)
        {
            context.NodeCount++;

            if (await TryYieldAsync(context))
                return context.EvaluateHeuristic();

            if (TryEvaluateTerminalScore(context, depth, lastMove, out var score))
                return score;

            var moves = GetOrderedMoves(context, depth, lastMove);
            
            if (moves.Count == 0)
                return 0f;

            var currentSlot = isMaximizing ? context.BotSlot : 1 - context.BotSlot;
            var currentMark = SlotToMark(currentSlot);
            return await SearchBranchAsync(context, depth, isMaximizing, alpha, beta, currentMark, moves);
        }

        private async UniTask<bool> TryYieldAsync(MinimaxSearchContext context)
        {
            if (context.NodeCount % context.SearchSettings.YieldEveryNNodes != 0)
                return false;

            if (context.Stopwatch.ElapsedMilliseconds >= context.SafetyLimitMs)
                return true;

            context.CancellationToken.ThrowIfCancellationRequested();
            await UniTask.Yield(PlayerLoopTiming.Update, context.CancellationToken);
            return false;
        }

        private bool TryEvaluateTerminalScore(
            MinimaxSearchContext context,
            int depth,
            CellId lastMove,
            out float score)
        {
            var result = _rules.Evaluate(context.Cells, context.BoardSize, lastMove);

            if (result.Status == GameStatus.Win)
            {
                var winnerSlot = MarkToSlot(result.Winner);
                score = winnerSlot == context.BotSlot ? 1000f + depth : -1000f - depth;
                return true;
            }

            if (result.Status == GameStatus.Draw)
            {
                score = 0f;
                return true;
            }

            if (depth <= 0 || context.HasBudgetExpired())
            {
                score = context.EvaluateHeuristic();
                return true;
            }

            score = 0;
            return false;
        }

        private UniTask<float> SearchBranchAsync(
            MinimaxSearchContext context,
            int depth,
            bool isMaximizing,
            float alpha,
            float beta,
            PlayerMark currentMark,
            List<CellId> moves) =>
            isMaximizing
                ? SearchMaximizingBranchAsync(context, depth, alpha, beta, currentMark, moves)
                : SearchMinimizingBranchAsync(context, depth, alpha, beta, currentMark, moves);

        private async UniTask<float> SearchMaximizingBranchAsync(
            MinimaxSearchContext context,
            int depth,
            float alpha,
            float beta,
            PlayerMark currentMark,
            List<CellId> moves)
        {
            var bestScore = float.NegativeInfinity;

            for (var i = 0; i < moves.Count; i++)
            {
                var eval = await EvaluateBranchMoveAsync(
                    context,
                    depth,
                    moves[i],
                    currentMark,
                    nextIsMaximizing: false,
                    alpha,
                    beta);

                if (eval > bestScore)
                    bestScore = eval;

                if (eval > alpha)
                    alpha = eval;

                if (beta <= alpha)
                    break;
            }

            return bestScore;
        }

        private async UniTask<float> SearchMinimizingBranchAsync(
            MinimaxSearchContext context,
            int depth,
            float alpha,
            float beta,
            PlayerMark currentMark,
            List<CellId> moves)
        {
            var bestScore = float.PositiveInfinity;

            for (var i = 0; i < moves.Count; i++)
            {
                var eval = await EvaluateBranchMoveAsync(
                    context,
                    depth,
                    moves[i],
                    currentMark,
                    nextIsMaximizing: true,
                    alpha,
                    beta);

                if (eval < bestScore)
                    bestScore = eval;

                if (eval < beta)
                    beta = eval;

                if (beta <= alpha)
                    break;
            }

            return bestScore;
        }

        private async UniTask<float> EvaluateBranchMoveAsync(
            MinimaxSearchContext context,
            int depth,
            CellId move,
            PlayerMark currentMark,
            bool nextIsMaximizing,
            float alpha,
            float beta)
        {
            var idx = move.Major * context.BoardSize + move.Minor;
            context.Cells[idx] = currentMark;

            try
            {
                return await MinimaxAsync(context, depth - 1, nextIsMaximizing, alpha, beta, move);
            }
            finally
            {
                context.Cells[idx] = PlayerMark.None;
            }
        }

        private static List<CellId> GetOrderedMoves(MinimaxSearchContext context, int depth, CellId lastMove)
        {
            var moves = context.MoveBuffers[depth - 1];
            moves.Clear();
            MinimaxMoveOrdering.FillOrderedMoves(context.Cells, context.BoardSize, lastMove, moves);
            return moves;
        }

        private static PlayerMark SlotToMark(int slot) => slot == 0 ? PlayerMark.X : PlayerMark.O;
        private static int MarkToSlot(PlayerMark mark) => mark == PlayerMark.X ? 0 : 1;
    }
}