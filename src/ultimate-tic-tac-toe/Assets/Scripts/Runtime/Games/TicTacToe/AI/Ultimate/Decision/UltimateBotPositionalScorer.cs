#nullable enable

using Runtime.Games.TicTacToe.AI.Ultimate.Core;
using Runtime.Gameplay;
using Runtime.Games.TicTacToe.Moves;
using Runtime.Games.TicTacToe.Ultimate.Rules;

namespace Runtime.Games.TicTacToe.AI.Ultimate.Decision
{
    internal static class UltimateBotPositionalScorer
    {
        private const int _outerSize = 3;
        private const int _innerSize = 3;
        private const int _centerMiniBoardIndex = (_outerSize * _outerSize - 1) / 2;
        private const int _cellsPerMiniBoard = _innerSize * _innerSize;

        private const float _centerMiniBoardControlScore = 1.2f;
        private const float _centerMiniBoardFlexibilityScore = 3f;
        private const float _singleMiniBoardControlScore = 18f;
        private const float _opponentMiniBoardControlScore = 20f;
        private const float _singleThreatLineScore = 18f;
        private const float _opponentSingleThreatLineScore = 22f;
        private const float _doubleThreatSelfScore = 95f;
        private const float _doubleThreatOpponentScore = 120f;
        private const float _emptyLineControlScore = 2f;

        private static readonly int[][] _globalLines =
        {
            new[] { 0, 1, 2 },
            new[] { 3, 4, 5 },
            new[] { 6, 7, 8 },
            new[] { 0, 3, 6 },
            new[] { 1, 4, 7 },
            new[] { 2, 5, 8 },
            new[] { 0, 4, 8 },
            new[] { 2, 4, 6 },
        };

        public static float Evaluate(
            PlayerMark[] cells,
            MiniBoardStatus[] miniBoards,
            AllowedMajors allowedMajors,
            PlayerMark selfMark,
            PlayerMark opponentMark,
            EvaluationWeights weights)
        {
            var score = EvaluateGlobalMiniBoardPotential(miniBoards, selfMark, opponentMark, weights);
            score += EvaluateCenterMiniBoardControl(cells, miniBoards, selfMark, opponentMark, weights);
            score += EvaluateCenterMiniBoardFlexibility(allowedMajors, weights);
            return score;
        }

        private static float EvaluateGlobalMiniBoardPotential(
            MiniBoardStatus[] miniBoards,
            PlayerMark selfMark,
            PlayerMark opponentMark,
            EvaluationWeights weights)
        {
            var selfWonStatus = GetWonStatus(selfMark);
            var opponentWonStatus = GetWonStatus(opponentMark);

            return EvaluateMiniBoardControl(miniBoards, selfWonStatus, opponentWonStatus, weights)
                   + EvaluateGlobalLinePotential(miniBoards, selfWonStatus, opponentWonStatus, weights);
        }

        private static float EvaluateCenterMiniBoardControl(
            PlayerMark[] cells,
            MiniBoardStatus[] miniBoards,
            PlayerMark selfMark,
            PlayerMark opponentMark,
            EvaluationWeights weights)
        {
            if (miniBoards[_centerMiniBoardIndex] != MiniBoardStatus.InProgress)
                return 0f;

            var score = 0f;
            const int centerStartIndex = _centerMiniBoardIndex * _cellsPerMiniBoard;

            for (var minor = 0; minor < _cellsPerMiniBoard; minor++)
            {
                score += EvaluateCenterCellControl(cells[centerStartIndex + minor], selfMark, opponentMark, weights);
            }

            return score;
        }

        private static float EvaluateCenterCellControl(
            PlayerMark cellMark,
            PlayerMark selfMark,
            PlayerMark opponentMark,
            EvaluationWeights weights)
        {
            if (cellMark == selfMark)
                return _centerMiniBoardControlScore * weights.GlobalControlWeight;

            if (cellMark == opponentMark)
                return -_centerMiniBoardControlScore * weights.GlobalControlWeight;

            return 0f;
        }

        private static float EvaluateCenterMiniBoardFlexibility(AllowedMajors allowedMajors, EvaluationWeights weights) =>
            allowedMajors.ContainsMajor(_centerMiniBoardIndex)
                ? _centerMiniBoardFlexibilityScore * weights.FlexibilityWeight
                : 0f;

        private static MiniBoardStatus GetWonStatus(PlayerMark mark) =>
            mark == PlayerMark.X ? MiniBoardStatus.WonByX : MiniBoardStatus.WonByO;

        private static float EvaluateMiniBoardControl(
            MiniBoardStatus[] miniBoards,
            MiniBoardStatus selfWonStatus,
            MiniBoardStatus opponentWonStatus,
            EvaluationWeights weights)
        {
            var score = 0f;

            for (var i = 0; i < miniBoards.Length; i++)
            {
                var status = miniBoards[i];

                if (status == selfWonStatus)
                    score += _singleMiniBoardControlScore * weights.GlobalThreatWeight;
                else if (status == opponentWonStatus)
                    score -= _opponentMiniBoardControlScore * weights.GlobalThreatWeight;
            }

            return score;
        }

        private static float EvaluateGlobalLinePotential(
            MiniBoardStatus[] miniBoards,
            MiniBoardStatus selfWonStatus,
            MiniBoardStatus opponentWonStatus,
            EvaluationWeights weights)
        {
            var score = 0f;

            for (var lineIndex = 0; lineIndex < _globalLines.Length; lineIndex++)
            {
                score += EvaluateLinePotential(_globalLines[lineIndex], miniBoards, selfWonStatus, opponentWonStatus, weights);
            }

            return score;
        }

        private static float EvaluateLinePotential(
            int[] line,
            MiniBoardStatus[] miniBoards,
            MiniBoardStatus selfWonStatus,
            MiniBoardStatus opponentWonStatus,
            EvaluationWeights weights)
        {
            CountLineOwnership(line, miniBoards, selfWonStatus, opponentWonStatus, out var selfCount, out var opponentCount);

            var score = 0f;

            if (opponentCount == 0)
                score += GetSelfLineScore(selfCount, weights);

            if (selfCount == 0)
                score -= GetOpponentLineScore(opponentCount, weights);

            return score;
        }

        private static void CountLineOwnership(
            int[] line,
            MiniBoardStatus[] miniBoards,
            MiniBoardStatus selfWonStatus,
            MiniBoardStatus opponentWonStatus,
            out int selfCount,
            out int opponentCount)
        {
            selfCount = 0;
            opponentCount = 0;

            for (var i = 0; i < line.Length; i++)
            {
                var status = miniBoards[line[i]];

                if (status == selfWonStatus)
                    selfCount++;
                else if (status == opponentWonStatus)
                    opponentCount++;
            }
        }

        private static float GetSelfLineScore(int selfCount, EvaluationWeights weights) =>
            selfCount switch
            {
                2 => _doubleThreatSelfScore * weights.GlobalThreatWeight,
                1 => _singleThreatLineScore * weights.GlobalThreatWeight,
                _ => _emptyLineControlScore * weights.GlobalControlWeight,
            };

        private static float GetOpponentLineScore(int opponentCount, EvaluationWeights weights) =>
            opponentCount switch
            {
                2 => _doubleThreatOpponentScore * weights.GlobalThreatWeight,
                1 => _opponentSingleThreatLineScore * weights.GlobalThreatWeight,
                _ => _emptyLineControlScore * weights.GlobalControlWeight,
            };
    }
}