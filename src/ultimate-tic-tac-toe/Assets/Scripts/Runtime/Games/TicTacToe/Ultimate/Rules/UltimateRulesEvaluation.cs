#nullable enable

using System;
using Runtime.Games.TicTacToe.Moves;
using Runtime.Games.TicTacToe.Rules;

namespace Runtime.Games.TicTacToe.Ultimate.Rules
{
    internal static class UltimateMiniBoardStatusEvaluator
    {
        public static MiniBoardStatus Evaluate(
            ReadOnlySpan<PlayerMark> cells,
            int innerSize,
            int major,
            int minorCount)
        {
            var majorStartIndex = checked(major * minorCount);
            var winner = TryFindWinner(cells, innerSize, majorStartIndex);

            if (winner == PlayerMark.X)
                return MiniBoardStatus.WonByX;

            if (winner == PlayerMark.O)
                return MiniBoardStatus.WonByO;

            return IsFull(cells, majorStartIndex, minorCount)
                ? MiniBoardStatus.Draw
                : MiniBoardStatus.InProgress;
        }

        private static PlayerMark TryFindWinner(ReadOnlySpan<PlayerMark> cells, int innerSize, int majorStartIndex)
        {
            var rowWinner = TryFindRowWinner(cells, innerSize, majorStartIndex);
            
            if (rowWinner != PlayerMark.None)
                return rowWinner;

            var columnWinner = TryFindColumnWinner(cells, innerSize, majorStartIndex);
            
            if (columnWinner != PlayerMark.None)
                return columnWinner;

            var mainDiagonalWinner = TryFindUniformLineWinner(cells, majorStartIndex, innerSize + 1, innerSize);
            
            return mainDiagonalWinner != PlayerMark.None
                ? mainDiagonalWinner
                : TryFindUniformLineWinner(cells, majorStartIndex + innerSize - 1, innerSize - 1, innerSize);
        }

        private static PlayerMark TryFindRowWinner(ReadOnlySpan<PlayerMark> cells, int innerSize, int majorStartIndex)
        {
            for (var row = 0; row < innerSize; row++)
            {
                var winner = TryFindUniformLineWinner(cells, majorStartIndex + row * innerSize, 1, innerSize);
                
                if (winner != PlayerMark.None)
                    return winner;
            }

            return PlayerMark.None;
        }

        private static PlayerMark TryFindColumnWinner(ReadOnlySpan<PlayerMark> cells, int innerSize, int majorStartIndex)
        {
            for (var col = 0; col < innerSize; col++)
            {
                var winner = TryFindUniformLineWinner(cells, majorStartIndex + col, innerSize, innerSize);
                
                if (winner != PlayerMark.None)
                    return winner;
            }

            return PlayerMark.None;
        }

        private static PlayerMark TryFindUniformLineWinner(ReadOnlySpan<PlayerMark> cells, int startIndex, int step, int length)
        {
            var first = cells[startIndex];
            
            if (first == PlayerMark.None)
                return PlayerMark.None;

            for (var offset = 1; offset < length; offset++)
            {
                if (cells[startIndex + offset * step] != first)
                    return PlayerMark.None;
            }

            return first;
        }

        private static bool IsFull(ReadOnlySpan<PlayerMark> cells, int majorStartIndex, int minorCount)
        {
            for (var minor = 0; minor < minorCount; minor++)
            {
                if (cells[majorStartIndex + minor] == PlayerMark.None)
                    return false;
            }

            return true;
        }
    }

    internal static class UltimateBigBoardMatchEvaluator
    {
        public static UltimateMatchResult Evaluate(
            int outerSize,
            ReadOnlySpan<MiniBoardStatus> miniBoards,
            UltimateMiniBoardDelta? delta)
        {
            var rowWin = TryEvaluateRows(outerSize, miniBoards, delta);
            
            if (rowWin.HasValue)
                return rowWin.Value;

            var columnWin = TryEvaluateColumns(outerSize, miniBoards, delta);
            
            if (columnWin.HasValue)
                return columnWin.Value;

            var mainDiagonalWin = TryEvaluateMainDiagonal(outerSize, miniBoards, delta);
            
            if (mainDiagonalWin.HasValue)
                return mainDiagonalWin.Value;

            var antiDiagonalWin = TryEvaluateAntiDiagonal(outerSize, miniBoards, delta);
            
            if (antiDiagonalWin.HasValue)
                return antiDiagonalWin.Value;

            return HasAnyOpenMiniBoard(miniBoards, delta)
                ? new UltimateMatchResult(GameStatus.InProgress, PlayerMark.None, null)
                : new UltimateMatchResult(GameStatus.Draw, PlayerMark.None, null);
        }

        private static UltimateMatchResult? TryEvaluateRows(
            int outerSize,
            ReadOnlySpan<MiniBoardStatus> miniBoards,
            UltimateMiniBoardDelta? delta)
        {
            for (var row = 0; row < outerSize; row++)
            {
                var startMajor = row * outerSize;
                var winner = TryFindWinner(startMajor, 1, outerSize, miniBoards, delta);
                
                if (winner == PlayerMark.None)
                    continue;

                return CreateWinResult(winner, BuildThreeCellLine(startMajor, 1));
            }

            return null;
        }

        private static UltimateMatchResult? TryEvaluateColumns(
            int outerSize,
            ReadOnlySpan<MiniBoardStatus> miniBoards,
            UltimateMiniBoardDelta? delta)
        {
            for (var col = 0; col < outerSize; col++)
            {
                var winner = TryFindWinner(col, outerSize, outerSize, miniBoards, delta);
                
                if (winner == PlayerMark.None)
                    continue;

                return CreateWinResult(winner, BuildThreeCellLine(col, outerSize));
            }

            return null;
        }

        private static UltimateMatchResult? TryEvaluateMainDiagonal(
            int outerSize,
            ReadOnlySpan<MiniBoardStatus> miniBoards,
            UltimateMiniBoardDelta? delta)
        {
            var step = outerSize + 1;
            var winner = TryFindWinner(0, step, outerSize, miniBoards, delta);
            
            return winner == PlayerMark.None
                ? null
                : CreateWinResult(winner, BuildThreeCellLine(0, step));
        }

        private static UltimateMatchResult? TryEvaluateAntiDiagonal(
            int outerSize,
            ReadOnlySpan<MiniBoardStatus> miniBoards,
            UltimateMiniBoardDelta? delta)
        {
            var startMajor = outerSize - 1;
            var step = outerSize - 1;
            var winner = TryFindWinner(startMajor, step, outerSize, miniBoards, delta);
            
            return winner == PlayerMark.None
                ? null
                : CreateWinResult(winner, BuildThreeCellLine(startMajor, step));
        }

        private static PlayerMark TryFindWinner(
            int startMajor,
            int step,
            int length,
            ReadOnlySpan<MiniBoardStatus> miniBoards,
            UltimateMiniBoardDelta? delta)
        {
            var winner = UltimateMiniBoardState.WinnerFromStatus(UltimateMiniBoardState.ResolveStatus(startMajor, miniBoards, delta));
            
            if (winner == PlayerMark.None)
                return PlayerMark.None;

            for (var offset = 1; offset < length; offset++)
            {
                var major = startMajor + offset * step;
                var currentWinner = UltimateMiniBoardState.WinnerFromStatus(UltimateMiniBoardState.ResolveStatus(major, miniBoards, delta));
                
                if (currentWinner != winner)
                    return PlayerMark.None;
            }

            return winner;
        }

        private static bool HasAnyOpenMiniBoard(ReadOnlySpan<MiniBoardStatus> miniBoards, UltimateMiniBoardDelta? delta)
        {
            for (var major = 0; major < miniBoards.Length; major++)
            {
                if (UltimateMiniBoardState.ResolveStatus(major, miniBoards, delta) == MiniBoardStatus.InProgress)
                    return true;
            }

            return false;
        }

        private static UltimateMatchResult CreateWinResult(PlayerMark winner, UltimateBigBoardWinLine line)
            => new(GameStatus.Win, winner, line);

        private static UltimateBigBoardWinLine BuildThreeCellLine(int startMajor, int step)
            => new(startMajor, startMajor + step, startMajor + 2 * step);
    }

    internal static class UltimateAllowedMajorsResolver
    {
        public static AllowedMajors ComputeNext(
            int targetMajor,
            ReadOnlySpan<MiniBoardStatus> miniBoards,
            UltimateMiniBoardDelta? delta) =>
            UltimateMiniBoardState.ResolveStatus(targetMajor, miniBoards, delta) == MiniBoardStatus.InProgress
                ? new AllowedMajors((ushort)(1 << targetMajor))
                : ComputeAllOpen(miniBoards, delta);

        public static AllowedMajors ComputeAllOpen(ReadOnlySpan<MiniBoardStatus> miniBoards)
            => ComputeAllOpen(miniBoards, delta: null);

        private static AllowedMajors ComputeAllOpen(
            ReadOnlySpan<MiniBoardStatus> miniBoards,
            UltimateMiniBoardDelta? delta)
        {
            var mask = (ushort)0;
            
            for (var major = 0; major < miniBoards.Length; major++)
            {
                if (UltimateMiniBoardState.ResolveStatus(major, miniBoards, delta) == MiniBoardStatus.InProgress)
                    mask |= (ushort)(1 << major);
            }

            return new AllowedMajors(mask);
        }
    }

    internal static class UltimateMiniBoardState
    {
        public static MiniBoardStatus ResolveStatus(
            int major,
            ReadOnlySpan<MiniBoardStatus> miniBoards,
            UltimateMiniBoardDelta? delta)
        {
            if (delta.HasValue && delta.Value.Major == major)
                return delta.Value.NewStatus;

            return miniBoards[major];
        }

        public static PlayerMark WinnerFromStatus(MiniBoardStatus status) =>
            status switch
            {
                MiniBoardStatus.WonByX => PlayerMark.X,
                MiniBoardStatus.WonByO => PlayerMark.O,
                _ => PlayerMark.None,
            };
    }
}