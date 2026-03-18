#nullable enable

using System;
using Runtime.Games.TicTacToe.AI.Profiles;
using Runtime.Gameplay;

namespace Runtime.Games.TicTacToe.AI.Search
{
    internal static class MinimaxHeuristic
    {
        public static float EvaluatePosition(PlayerMark[] cells, int boardSize, int winLength, int botSlot, EvaluationWeights weights)
        {
            var botMark = SlotToMark(botSlot);
            var opponentMark = SlotToMark(1 - botSlot);

            return EvaluateLinePatterns(cells, boardSize, winLength, botMark, opponentMark, weights)
                   + EvaluateCenterControl(cells, boardSize, botMark, weights.CenterWeight);
        }

        private static float EvaluateLinePatterns(
            PlayerMark[] cells,
            int boardSize,
            int winLength,
            PlayerMark botMark,
            PlayerMark opponentMark,
            EvaluationWeights weights)
        {
            var score = 0f;

            for (var row = 0; row < boardSize; row++)
            {
                for (var col = 0; col < boardSize; col++)
                {
                    score += EvaluateCellDirections(cells, boardSize, winLength, row, col, botMark, opponentMark, weights);
                }
            }

            return score;
        }

        private static float EvaluateCellDirections(
            PlayerMark[] cells,
            int boardSize,
            int winLength,
            int row,
            int col,
            PlayerMark botMark,
            PlayerMark opponentMark,
            EvaluationWeights weights) =>
            EvaluateLine(cells, boardSize, winLength, row, col, 0, 1, botMark, opponentMark, weights)
            + EvaluateLine(cells, boardSize, winLength, row, col, 1, 0, botMark, opponentMark, weights)
            + EvaluateLine(cells, boardSize, winLength, row, col, 1, 1, botMark, opponentMark, weights)
            + EvaluateLine(cells, boardSize, winLength, row, col, 1, -1, botMark, opponentMark, weights);

        private static float EvaluateCenterControl(
            PlayerMark[] cells,
            int boardSize,
            PlayerMark botMark,
            float centerWeight)
        {
            var score = 0f;
            var center = (boardSize - 1) / 2f;
            var maxDistance = Math.Max(center * 2f, 1f);

            for (var row = 0; row < boardSize; row++)
            {
                for (var col = 0; col < boardSize; col++)
                {
                    var mark = cells[row * boardSize + col];
                    
                    if (mark == PlayerMark.None)
                        continue;

                    var centrality = CalculateCentrality(row, col, center, maxDistance);
                    
                    score += mark == botMark
                        ? centrality * centerWeight
                        : -centrality * centerWeight;
                }
            }

            return score;
        }

        private static float CalculateCentrality(int row, int col, float center, float maxDistance)
        {
            var distance = Math.Abs(row - center) + Math.Abs(col - center);
            return 1f - distance / maxDistance;
        }

        private static float EvaluateLine(
            PlayerMark[] cells,
            int boardSize,
            int winLength,
            int startRow,
            int startCol,
            int dRow,
            int dCol,
            PlayerMark botMark,
            PlayerMark opponentMark,
            EvaluationWeights weights)
        {
            if (!IsLineInBounds(boardSize, winLength, startRow, startCol, dRow, dCol))
                return 0f;

            var (botCount, opponentCount) = CountLineMarks(
                cells,
                boardSize,
                winLength,
                startRow,
                startCol,
                dRow,
                dCol,
                botMark,
                opponentMark);

            return ScoreLine(botCount, opponentCount, weights);
        }

        private static bool IsLineInBounds(
            int boardSize,
            int winLength,
            int startRow,
            int startCol,
            int dRow,
            int dCol)
        {
            var endRow = startRow + (winLength - 1) * dRow;
            var endCol = startCol + (winLength - 1) * dCol;

            return endRow >= 0
                   && endRow < boardSize
                   && endCol >= 0
                   && endCol < boardSize;
        }

        private static (int BotCount, int OpponentCount) CountLineMarks(
            PlayerMark[] cells,
            int boardSize,
            int winLength,
            int startRow,
            int startCol,
            int dRow,
            int dCol,
            PlayerMark botMark,
            PlayerMark opponentMark)
        {
            var botCount = 0;
            var opponentCount = 0;

            for (var i = 0; i < winLength; i++)
            {
                var row = startRow + i * dRow;
                var col = startCol + i * dCol;
                var mark = cells[row * boardSize + col];

                if (mark == botMark)
                    botCount++;
                else if (mark == opponentMark)
                    opponentCount++;
            }

            return (botCount, opponentCount);
        }

        private static float ScoreLine(int botCount, int opponentCount, EvaluationWeights weights)
        {
            if (botCount > 0 && opponentCount > 0)
                return 0f;

            if (botCount > 0)
                return weights.AttackWeight * MathF.Pow(10f, botCount - 1);

            if (opponentCount > 0)
                return -(weights.DefenseWeight * MathF.Pow(10f, opponentCount - 1));

            return 0f;
        }

        private static PlayerMark SlotToMark(int slot) => slot == 0 ? PlayerMark.X : PlayerMark.O;
    }
}