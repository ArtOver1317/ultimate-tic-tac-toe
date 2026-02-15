#nullable enable

using System;
using Runtime.Games.TicTacToe.Moves;
using Runtime.Games.TicTacToe.Rules;

namespace Runtime.Games.TicTacToe.Ultimate.Rules
{
    public sealed class UltimateRulesEngine : IUltimateRulesEngine
    {
        public AllowedMajors ComputeInitialAllowed(ReadOnlySpan<MiniBoardStatus> miniBoards)
        {
            ValidateMiniBoardsLength(miniBoards, nameof(miniBoards));
            return ComputeAllOpenAllowed(miniBoards);
        }

        public UltimateRulesResult EvaluateAfterMove(
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
            {
                throw new ArgumentException($"cells length must be {expectedCellCount} for {outerSize}x{innerSize} ultimate board.", nameof(cells));
            }

            if (miniBoards.Length != majorCount)
            {
                throw new ArgumentException($"miniBoards length must be {majorCount}.", nameof(miniBoards));
            }

            if (lastMove.Major < 0 || lastMove.Major >= majorCount)
            {
                throw new ArgumentOutOfRangeException(nameof(lastMove), $"lastMove.Major must be in [0..{majorCount - 1}].");
            }

            if (lastMove.Minor < 0 || lastMove.Minor >= minorCount)
            {
                throw new ArgumentOutOfRangeException(nameof(lastMove), $"lastMove.Minor must be in [0..{minorCount - 1}].");
            }

            var lastMoveIndex = checked(lastMove.Major * minorCount + lastMove.Minor);
            if (cells[lastMoveIndex] == PlayerMark.None)
            {
                throw new ArgumentException("cells[lastMove] must be X or O, not None.", nameof(cells));
            }

            var miniStatus = EvaluateMiniBoardStatus(cells, innerSize, lastMove.Major, minorCount);

            UltimateMiniBoardDelta? delta = null;
            if (miniStatus != miniBoards[lastMove.Major])
            {
                delta = new UltimateMiniBoardDelta(lastMove.Major, miniStatus);
            }

            var match = EvaluateMatch(outerSize, miniBoards, delta);
            var allowedMajors = ComputeAllowedMajors(lastMove.Minor, miniBoards, delta);

            return new UltimateRulesResult(match, allowedMajors, delta);
        }

        private static void ValidateBoardSizes(int outerSize, int innerSize)
        {
            if (outerSize != 3)
            {
                throw new ArgumentOutOfRangeException(nameof(outerSize), "outerSize must be 3 for Ultimate Tic-Tac-Toe.");
            }

            if (innerSize != 3)
            {
                throw new ArgumentOutOfRangeException(nameof(innerSize), "innerSize must be 3 for Ultimate Tic-Tac-Toe.");
            }
        }

        private static void ValidateMiniBoardsLength(ReadOnlySpan<MiniBoardStatus> miniBoards, string paramName)
        {
            if (miniBoards.Length != 9)
            {
                throw new ArgumentException("miniBoards length must be 9.", paramName);
            }
        }

        private static MiniBoardStatus EvaluateMiniBoardStatus(
            ReadOnlySpan<PlayerMark> cells,
            int innerSize,
            int major,
            int minorCount)
        {
            var majorStartIndex = checked(major * minorCount);
            var winner = TryFindMiniBoardWinner(cells, innerSize, majorStartIndex);
            if (winner == PlayerMark.X)
            {
                return MiniBoardStatus.WonByX;
            }

            if (winner == PlayerMark.O)
            {
                return MiniBoardStatus.WonByO;
            }

            for (var minor = 0; minor < minorCount; minor++)
            {
                if (cells[majorStartIndex + minor] == PlayerMark.None)
                {
                    return MiniBoardStatus.InProgress;
                }
            }

            return MiniBoardStatus.Draw;
        }

        private static PlayerMark TryFindMiniBoardWinner(ReadOnlySpan<PlayerMark> cells, int innerSize, int majorStartIndex)
        {
            for (var row = 0; row < innerSize; row++)
            {
                var rowStart = majorStartIndex + row * innerSize;
                var first = cells[rowStart];
                if (first == PlayerMark.None)
                {
                    continue;
                }

                var allSame = true;
                for (var col = 1; col < innerSize; col++)
                {
                    if (cells[rowStart + col] != first)
                    {
                        allSame = false;
                        break;
                    }
                }

                if (allSame)
                {
                    return first;
                }
            }

            for (var col = 0; col < innerSize; col++)
            {
                var first = cells[majorStartIndex + col];
                if (first == PlayerMark.None)
                {
                    continue;
                }

                var allSame = true;
                for (var row = 1; row < innerSize; row++)
                {
                    var idx = majorStartIndex + row * innerSize + col;
                    if (cells[idx] != first)
                    {
                        allSame = false;
                        break;
                    }
                }

                if (allSame)
                {
                    return first;
                }
            }

            var mainFirst = cells[majorStartIndex];
            if (mainFirst != PlayerMark.None)
            {
                var allSame = true;
                for (var i = 1; i < innerSize; i++)
                {
                    var idx = majorStartIndex + i * innerSize + i;
                    if (cells[idx] != mainFirst)
                    {
                        allSame = false;
                        break;
                    }
                }

                if (allSame)
                {
                    return mainFirst;
                }
            }

            var antiFirst = cells[majorStartIndex + innerSize - 1];
            if (antiFirst != PlayerMark.None)
            {
                var allSame = true;
                for (var i = 1; i < innerSize; i++)
                {
                    var idx = majorStartIndex + i * innerSize + (innerSize - 1 - i);
                    if (cells[idx] != antiFirst)
                    {
                        allSame = false;
                        break;
                    }
                }

                if (allSame)
                {
                    return antiFirst;
                }
            }

            return PlayerMark.None;
        }

        private static UltimateMatchResult EvaluateMatch(
            int outerSize,
            ReadOnlySpan<MiniBoardStatus> miniBoards,
            UltimateMiniBoardDelta? delta)
        {
            for (var row = 0; row < outerSize; row++)
            {
                var startMajor = row * outerSize;
                var winner = WinnerFromMiniStatus(GetMiniStatusAt(startMajor, miniBoards, delta));
                if (winner == PlayerMark.None)
                {
                    continue;
                }

                var isLine = true;
                for (var col = 1; col < outerSize; col++)
                {
                    var major = startMajor + col;
                    if (WinnerFromMiniStatus(GetMiniStatusAt(major, miniBoards, delta)) != winner)
                    {
                        isLine = false;
                        break;
                    }
                }

                if (isLine)
                {
                    return new UltimateMatchResult(
                        GameStatus.Win,
                        winner,
                        new UltimateBigBoardWinLine(startMajor, startMajor + 1, startMajor + 2));
                }
            }

            for (var col = 0; col < outerSize; col++)
            {
                var winner = WinnerFromMiniStatus(GetMiniStatusAt(col, miniBoards, delta));
                if (winner == PlayerMark.None)
                {
                    continue;
                }

                var isLine = true;
                for (var row = 1; row < outerSize; row++)
                {
                    var major = row * outerSize + col;
                    if (WinnerFromMiniStatus(GetMiniStatusAt(major, miniBoards, delta)) != winner)
                    {
                        isLine = false;
                        break;
                    }
                }

                if (isLine)
                {
                    return new UltimateMatchResult(
                        GameStatus.Win,
                        winner,
                        new UltimateBigBoardWinLine(col, outerSize + col, 2 * outerSize + col));
                }
            }

            var mainDiagonalWinner = WinnerFromMiniStatus(GetMiniStatusAt(0, miniBoards, delta));
            if (mainDiagonalWinner != PlayerMark.None)
            {
                var isLine = true;
                for (var i = 1; i < outerSize; i++)
                {
                    var major = i * outerSize + i;
                    if (WinnerFromMiniStatus(GetMiniStatusAt(major, miniBoards, delta)) != mainDiagonalWinner)
                    {
                        isLine = false;
                        break;
                    }
                }

                if (isLine)
                {
                    return new UltimateMatchResult(
                        GameStatus.Win,
                        mainDiagonalWinner,
                        new UltimateBigBoardWinLine(0, outerSize + 1, outerSize * outerSize - 1));
                }
            }

            var antiStart = outerSize - 1;
            var antiDiagonalWinner = WinnerFromMiniStatus(GetMiniStatusAt(antiStart, miniBoards, delta));
            if (antiDiagonalWinner != PlayerMark.None)
            {
                var isLine = true;
                for (var i = 1; i < outerSize; i++)
                {
                    var major = i * outerSize + (outerSize - 1 - i);
                    if (WinnerFromMiniStatus(GetMiniStatusAt(major, miniBoards, delta)) != antiDiagonalWinner)
                    {
                        isLine = false;
                        break;
                    }
                }

                if (isLine)
                {
                    return new UltimateMatchResult(
                        GameStatus.Win,
                        antiDiagonalWinner,
                        new UltimateBigBoardWinLine(outerSize - 1, outerSize + (outerSize - 2), outerSize * (outerSize - 1)));
                }
            }

            var anyOpen = false;
            for (var major = 0; major < miniBoards.Length; major++)
            {
                if (GetMiniStatusAt(major, miniBoards, delta) == MiniBoardStatus.InProgress)
                {
                    anyOpen = true;
                    break;
                }
            }

            if (anyOpen)
            {
                return new UltimateMatchResult(GameStatus.InProgress, PlayerMark.None, null);
            }

            return new UltimateMatchResult(GameStatus.Draw, PlayerMark.None, null);
        }

        private static AllowedMajors ComputeAllowedMajors(
            int targetMajor,
            ReadOnlySpan<MiniBoardStatus> miniBoards,
            UltimateMiniBoardDelta? delta)
        {
            if (GetMiniStatusAt(targetMajor, miniBoards, delta) == MiniBoardStatus.InProgress)
            {
                return new AllowedMajors((ushort)(1 << targetMajor));
            }

            var mask = (ushort)0;
            for (var major = 0; major < miniBoards.Length; major++)
            {
                if (GetMiniStatusAt(major, miniBoards, delta) == MiniBoardStatus.InProgress)
                {
                    mask |= (ushort)(1 << major);
                }
            }

            return new AllowedMajors(mask);
        }

        private static AllowedMajors ComputeAllOpenAllowed(ReadOnlySpan<MiniBoardStatus> miniBoards)
        {
            var mask = (ushort)0;
            for (var major = 0; major < miniBoards.Length; major++)
            {
                if (miniBoards[major] == MiniBoardStatus.InProgress)
                {
                    mask |= (ushort)(1 << major);
                }
            }

            return new AllowedMajors(mask);
        }

        private static MiniBoardStatus GetMiniStatusAt(
            int major,
            ReadOnlySpan<MiniBoardStatus> miniBoards,
            UltimateMiniBoardDelta? delta)
        {
            if (delta.HasValue && delta.Value.Major == major)
            {
                return delta.Value.NewStatus;
            }

            return miniBoards[major];
        }

        private static PlayerMark WinnerFromMiniStatus(MiniBoardStatus status)
        {
            return status switch
            {
                MiniBoardStatus.WonByX => PlayerMark.X,
                MiniBoardStatus.WonByO => PlayerMark.O,
                _ => PlayerMark.None,
            };
        }
    }
}