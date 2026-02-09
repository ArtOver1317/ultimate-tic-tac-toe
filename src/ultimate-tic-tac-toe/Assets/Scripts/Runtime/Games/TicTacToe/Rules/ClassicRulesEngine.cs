#nullable enable

using System;
using Runtime.Games.TicTacToe.Moves;

namespace Runtime.Games.TicTacToe.Rules
{
    /// <summary>
    /// Stateless evaluator for classic Tic-Tac-Toe.
    /// Searches for K-in-a-row in 4 directions from the last move.
    /// K(N): N=3→3, N∈{4,5}→4, N>5→5.
    /// </summary>
    public sealed class ClassicRulesEngine : IRulesEngine
    {
        // Direction vectors: (dRow, dCol).
        // Order matches WinLineDirection priority: H > V > D\ > D/.
        private static readonly (int dRow, int dCol, WinLineDirection dir)[] Directions =
        {
            (0, 1, WinLineDirection.Horizontal),
            (1, 0, WinLineDirection.Vertical),
            (1, 1, WinLineDirection.DiagonalMain),
            (1, -1, WinLineDirection.DiagonalAnti),
        };

        public GameResult Evaluate(PlayerMark[] cells, int boardSize, CellId lastMove)
        {
            if (cells == null) throw new ArgumentNullException(nameof(cells));
            if (boardSize <= 0) throw new ArgumentOutOfRangeException(nameof(boardSize));

            int row = lastMove.Major;
            int col = lastMove.Minor;
            int index = row * boardSize + col;

            PlayerMark player = cells[index];
            if (player == PlayerMark.None)
                throw new ArgumentException("cells[lastMove] must be X or O, not None.");

            int k = GetWinLength(boardSize);

            WinLine? bestLine = null;

            for (int d = 0; d < Directions.Length; d++)
            {
                var (dRow, dCol, dir) = Directions[d];
                var line = FindLine(cells, boardSize, row, col, dRow, dCol, player, k, dir);
                if (line != null)
                {
                    // First found wins due to direction priority order.
                    // If same direction (impossible with iteration order), compare by Start.
                    if (bestLine == null)
                    {
                        bestLine = line;
                    }
                    else if (IsBetter(line.Value, bestLine.Value))
                    {
                        bestLine = line;
                    }
                }
            }

            if (bestLine != null)
                return GameResult.Win(player, bestLine.Value);

            // Check for draw: no None cells remaining.
            if (IsBoardFull(cells))
                return GameResult.Draw();

            return GameResult.InProgress();
        }

        /// <summary>K(N): N=3→3, N∈{4,5}→4, N>5→5.</summary>
        internal static int GetWinLength(int boardSize) => boardSize switch
        {
            <= 0 => throw new ArgumentOutOfRangeException(nameof(boardSize)),
            <= 3 => boardSize,  // N=1→1, N=2→2, N=3→3
            <= 5 => 4,          // N=4→4, N=5→4
            _ => 5,             // N>5→5
        };

        /// <summary>
        /// Counts consecutive marks of <paramref name="player"/> from (row,col) in both
        /// directions (dRow,dCol) and (-dRow,-dCol). Returns a normalized WinLine if count >= k.
        ///
        /// Important: when the consecutive run is longer than K, we pick a deterministic segment
        /// of exact length K that always includes the last move.
        /// </summary>
        private static WinLine? FindLine(
            PlayerMark[] cells, int boardSize,
            int row, int col,
            int dRow, int dCol,
            PlayerMark player, int k,
            WinLineDirection direction)
        {
            // Count forward (excluding start cell).
            int forward = CountInDirection(cells, boardSize, row, col, dRow, dCol, player);
            // Count backward (excluding start cell).
            int backward = CountInDirection(cells, boardSize, row, col, -dRow, -dCol, player);

            int total = forward + backward + 1; // +1 for the cell itself.
            if (total < k)
                return null;

            // Determine the full run boundaries.
            int runStartRow = row - backward * dRow;
            int runStartCol = col - backward * dCol;

            // Choose a deterministic segment of exactly K that MUST include the last move.
            // Parameterization along the run (0..total-1):
            //   runStart + offset * (dRow,dCol)
            // lastMove offset from runStart is exactly `backward`.
            int lastMoveOffset = backward;

            // Smallest start offset that still includes lastMove is (lastMoveOffset - (k-1)).
            // Clamp to 0 so we stay inside the run.
            int segmentStartOffset = lastMoveOffset - (k - 1);
            if (segmentStartOffset < 0) segmentStartOffset = 0;

            int startRow = runStartRow + segmentStartOffset * dRow;
            int startCol = runStartCol + segmentStartOffset * dCol;
            int endRow = startRow + (k - 1) * dRow;
            int endCol = startCol + (k - 1) * dCol;

            // Normalize: Start ≤ End by (row, then col).
            var start = new CellId(startRow, startCol);
            var end = new CellId(endRow, endCol);
            Normalize(ref start, ref end);

            return new WinLine(start, end, direction, k);
        }

        /// <summary>
        /// Counts consecutive marks starting from (row+dRow, col+dCol) in direction (dRow, dCol).
        /// Does NOT count the starting cell itself.
        /// </summary>
        private static int CountInDirection(
            PlayerMark[] cells, int boardSize,
            int row, int col,
            int dRow, int dCol,
            PlayerMark player)
        {
            int count = 0;
            int r = row + dRow;
            int c = col + dCol;

            while (r >= 0 && r < boardSize && c >= 0 && c < boardSize
                   && cells[r * boardSize + c] == player)
            {
                count++;
                r += dRow;
                c += dCol;
            }

            return count;
        }

        /// <summary>
        /// Ensures Start ≤ End by (row, then col).
        /// </summary>
        private static void Normalize(ref CellId start, ref CellId end)
        {
            if (start.Major > end.Major || (start.Major == end.Major && start.Minor > end.Minor))
                (start, end) = (end, start);
        }

        /// <summary>
        /// Deterministic comparison: direction priority (H > V > D\ > D/),
        /// then by Start (row, col).
        /// </summary>
        private static bool IsBetter(WinLine candidate, WinLine current)
        {
            if (candidate.Direction != current.Direction)
                return candidate.Direction < current.Direction;

            // Same direction — smaller Start wins.
            if (candidate.Start.Major != current.Start.Major)
                return candidate.Start.Major < current.Start.Major;

            return candidate.Start.Minor < current.Start.Minor;
        }

        private static bool IsBoardFull(PlayerMark[] cells)
        {
            for (int i = 0; i < cells.Length; i++)
            {
                if (cells[i] == PlayerMark.None)
                    return false;
            }

            return true;
        }
    }
}
