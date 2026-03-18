#nullable enable

using System;
using System.Collections.Generic;
using Runtime.Games.TicTacToe.AI.Ultimate.Core;
using Runtime.Gameplay;
using Runtime.Games.TicTacToe.Moves;
using Runtime.Games.TicTacToe.Rules;
using Runtime.Games.TicTacToe.Ultimate.Rules;

namespace Runtime.Games.TicTacToe.AI.Ultimate.Decision
{
    internal sealed class UltimateBotHeuristic
    {
        private const int _outerSize = 3;
        private const int _innerSize = 3;
        private const int _cellsPerMiniBoard = _innerSize * _innerSize;

        private const float _immediateGlobalWinScore = 25_000f;
        private const float _immediateLocalWinScore = 120f;
        private const float _immediateOpponentGlobalWinPenalty = 26_000f;
        private const float _immediateOpponentLocalWinPenalty = 140f;

        private readonly IUltimateRulesEngine _rules;

        public UltimateBotHeuristic(IUltimateRulesEngine rules) => 
            _rules = rules ?? throw new ArgumentNullException(nameof(rules));

        public float EvaluatePosition(
            PlayerMark[] cells,
            MiniBoardStatus[] miniBoards,
            IReadOnlyList<CellId> legalMoves,
            AllowedMajors allowedMajors,
            PlayerMark selfMark,
            PlayerMark opponentMark,
            EvaluationWeights weights)
        {
            var score = UltimateBotPositionalScorer.Evaluate(cells, miniBoards, allowedMajors, selfMark, opponentMark, weights);
            score += EvaluateImmediateMovePotential(legalMoves, cells, miniBoards, selfMark, opponentMark, weights);
            return score;
        }

        private float EvaluateImmediateMovePotential(
            IReadOnlyList<CellId> legalMoves,
            PlayerMark[] cells,
            MiniBoardStatus[] miniBoards,
            PlayerMark selfMark,
            PlayerMark opponentMark,
            EvaluationWeights weights)
        {
            var score = 0f;
            var localThreatScore = _immediateLocalWinScore * weights.LocalThreatWeight;
            var opponentLocalThreatScore = _immediateOpponentLocalWinPenalty * weights.LocalThreatWeight;

            for (var i = 0; i < legalMoves.Count; i++)
            {
                var move = legalMoves[i];
                score += EvaluateImmediateThreatsForMark(move, cells, miniBoards, selfMark, _immediateGlobalWinScore, localThreatScore);
                score -= EvaluateImmediateThreatsForMark(move, cells, miniBoards, opponentMark, _immediateOpponentGlobalWinPenalty, opponentLocalThreatScore);
            }

            return score;
        }

        private float EvaluateImmediateThreatsForMark(
            CellId move,
            PlayerMark[] cells,
            MiniBoardStatus[] miniBoards,
            PlayerMark mark,
            float globalThreatScore,
            float localThreatScore)
        {
            var score = 0f;

            if (IsImmediateGlobalWin(move, cells, miniBoards, mark))
                score += globalThreatScore;

            if (IsImmediateLocalWin(move, cells, miniBoards, mark))
                score += localThreatScore;

            return score;
        }

        public bool IsImmediateGlobalWin(
            CellId move,
            PlayerMark[] cells,
            MiniBoardStatus[] miniBoards,
            PlayerMark mark) =>
            TryEvaluateMoveResult(move, cells, miniBoards, mark, out var result)
            && result.Match.Status == GameStatus.Win
            && result.Match.Winner == mark;

        public bool IsImmediateLocalWin(
            CellId move,
            PlayerMark[] cells,
            MiniBoardStatus[] miniBoards,
            PlayerMark mark) =>
            TryEvaluateMoveResult(move, cells, miniBoards, mark, out var result)
            && IsWonMiniBoard(result.MiniBoardDelta, mark);

        private bool TryEvaluateMoveResult(
            CellId move,
            PlayerMark[] cells,
            MiniBoardStatus[] miniBoards,
            PlayerMark mark,
            out UltimateRulesResult result)
        {
            var idx = ToIndex(move);

            if (idx < 0 || idx >= cells.Length || cells[idx] != PlayerMark.None)
            {
                result = default;
                return false;
            }

            var localMini = CloneMiniBoards(miniBoards);
            cells[idx] = mark;

            try
            {
                result = _rules.EvaluateAfterMove(cells, _outerSize, _innerSize, move, localMini);
                return true;
            }
            catch (ArgumentException)
            {
                result = default;
                return false;
            }
            finally
            {
                cells[idx] = PlayerMark.None;
            }
        }

        private static bool IsWonMiniBoard(UltimateMiniBoardDelta? delta, PlayerMark mark)
        {
            if (!delta.HasValue)
                return false;

            return delta.Value.NewStatus == GetWonStatus(mark);
        }

        private static MiniBoardStatus GetWonStatus(PlayerMark mark) =>
            mark == PlayerMark.X ? MiniBoardStatus.WonByX : MiniBoardStatus.WonByO;

        private static MiniBoardStatus[] CloneMiniBoards(MiniBoardStatus[] source)
        {
            var copy = new MiniBoardStatus[source.Length];
            Array.Copy(source, copy, source.Length);
            return copy;
        }

        private static int ToIndex(CellId move) => move.Major * _cellsPerMiniBoard + move.Minor;
    }
}