#nullable enable

using System;
using Runtime.Gameplay;
using Runtime.Games.TicTacToe.Moves;

namespace Runtime.Games.TicTacToe.Ultimate.Rules
{
    public sealed class UltimateRulesEngine : IUltimateRulesEngine
    {
        public AllowedMajors ComputeInitialAllowed(ReadOnlySpan<MiniBoardStatus> miniBoards)
        {
            UltimateRulesInputValidator.ValidateMiniBoardsLength(miniBoards, nameof(miniBoards));
            return UltimateAllowedMajorsResolver.ComputeAllOpen(miniBoards);
        }

        public UltimateRulesResult EvaluateAfterMove(
            ReadOnlySpan<PlayerMark> cells,
            int outerSize,
            int innerSize,
            CellId lastMove,
            ReadOnlySpan<MiniBoardStatus> miniBoards)
        {
            var minorCount = UltimateRulesInputValidator.ValidateEvaluationInput(
                cells,
                outerSize,
                innerSize,
                lastMove,
                miniBoards);

            var nextMiniStatus = UltimateMiniBoardStatusEvaluator.Evaluate(
                cells,
                innerSize,
                lastMove.Major,
                minorCount);

            UltimateMiniBoardDelta? delta = null;
            
            if (nextMiniStatus != miniBoards[lastMove.Major]) 
                delta = new UltimateMiniBoardDelta(lastMove.Major, nextMiniStatus);

            var match = UltimateBigBoardMatchEvaluator.Evaluate(outerSize, miniBoards, delta);
            var allowedMajors = UltimateAllowedMajorsResolver.ComputeNext(lastMove.Minor, miniBoards, delta);

            return new UltimateRulesResult(match, allowedMajors, delta);
        }
    }

    internal static class UltimateRulesInputValidator
    {
        public static int ValidateEvaluationInput(
            ReadOnlySpan<PlayerMark> cells,
            int outerSize,
            int innerSize,
            CellId lastMove,
            ReadOnlySpan<MiniBoardStatus> miniBoards)
        {
            ValidateBoardSizes(outerSize, innerSize);

            var majorCount = checked(outerSize * outerSize);
            var minorCount = checked(innerSize * innerSize);
            var expectedCellCount = checked(majorCount * minorCount);

            if (cells.Length != expectedCellCount) 
                throw new ArgumentException($"cells length must be {expectedCellCount} for {outerSize}x{innerSize} ultimate board.", nameof(cells));

            if (miniBoards.Length != majorCount) 
                throw new ArgumentException($"miniBoards length must be {majorCount}.", nameof(miniBoards));

            if (lastMove.Major < 0 || lastMove.Major >= majorCount) 
                throw new ArgumentOutOfRangeException(nameof(lastMove), $"lastMove.Major must be in [0..{majorCount - 1}].");

            if (lastMove.Minor < 0 || lastMove.Minor >= minorCount) 
                throw new ArgumentOutOfRangeException(nameof(lastMove), $"lastMove.Minor must be in [0..{minorCount - 1}].");

            var lastMoveIndex = checked(lastMove.Major * minorCount + lastMove.Minor);
            
            return cells[lastMoveIndex] == PlayerMark.None 
                ? throw new ArgumentException("cells[lastMove] must be X or O, not None.", nameof(cells)) 
                : minorCount;
        }

        public static void ValidateMiniBoardsLength(ReadOnlySpan<MiniBoardStatus> miniBoards, string paramName)
        {
            if (miniBoards.Length != UltimateBoardConstants.MajorCount) 
                throw new ArgumentException($"miniBoards length must be {UltimateBoardConstants.MajorCount}.", paramName);
        }

        private static void ValidateBoardSizes(int outerSize, int innerSize)
        {
            if (outerSize != UltimateBoardConstants.OuterSize) 
                throw new ArgumentOutOfRangeException(nameof(outerSize), $"outerSize must be {UltimateBoardConstants.OuterSize} for Ultimate Tic-Tac-Toe.");

            if (innerSize != UltimateBoardConstants.InnerSize) 
                throw new ArgumentOutOfRangeException(nameof(innerSize), $"innerSize must be {UltimateBoardConstants.InnerSize} for Ultimate Tic-Tac-Toe.");
        }
    }
}