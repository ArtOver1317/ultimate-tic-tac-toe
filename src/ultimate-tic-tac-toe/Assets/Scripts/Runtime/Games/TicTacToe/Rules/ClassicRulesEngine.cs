#nullable enable

using System;
using Runtime.Games.TicTacToe.AI.Core;
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
        private static readonly (int dRow, int dCol, WinLineDirection dir)[] _directions =
        {
            (0, 1, WinLineDirection.Horizontal),
            (1, 0, WinLineDirection.Vertical),
            (1, 1, WinLineDirection.DiagonalMain),
            (1, -1, WinLineDirection.DiagonalAnti),
        };

        public GameResult Evaluate(PlayerMark[] cells, int boardSize, CellId lastMove)
        {
            if (cells == null) 
                throw new ArgumentNullException(nameof(cells));
            
            if (boardSize <= 0) 
                throw new ArgumentOutOfRangeException(nameof(boardSize));

            var row = lastMove.Major;
            var col = lastMove.Minor;
            var index = row * boardSize + col;

            var player = cells[index];
            
            if (player == PlayerMark.None)
                throw new ArgumentException("cells[lastMove] must be X or O, not None.");

            var k = GetWinLength(boardSize);

            WinLine? bestLine = null;

            foreach (var (dRow, dCol, dir) in _directions)
            {
                var line = FindLine(cells, boardSize, row, col, dRow, dCol, player, k, dir);
                
                if (line != null)
                {
                    // Direction priority follows Directions order: H > V > D\ > D/.
                    if (bestLine == null)
                        bestLine = line;
                    else if (IsBetter(line.Value, bestLine.Value)) 
                        bestLine = line;
                }
            }

            if (bestLine != null)
                return GameResult.Win(player, bestLine.Value);

            // Check for draw: no None cells remaining.
            return IsBoardFull(cells) ? GameResult.Draw() : GameResult.InProgress();
        }

        /// <summary>K(N): N=3→3, N∈{4,5}→4, N>5→5.</summary>
        internal static int GetWinLength(int boardSize) => boardSize switch
        {
            <= 0 => throw new ArgumentOutOfRangeException(nameof(boardSize)),
            <= 3 => boardSize, // N=1→1, N=2→2, N=3→3
            <= 5 => 4, // N=4→4, N=5→4
            _ => 5, // N>5→5
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
            var forward = CountInDirection(cells, boardSize, row, col, dRow, dCol, player);
            // Count backward (excluding start cell).
            var backward = CountInDirection(cells, boardSize, row, col, -dRow, -dCol, player);

            var total = forward + backward + 1; // +1 for the cell itself.
            
            if (total < k)
                return null;

            // Determine the full run boundaries.
            var runStartRow = row - backward * dRow;
            var runStartCol = col - backward * dCol;

            // Choose a deterministic segment of exactly K that MUST include the last move.
            // Parameterization along the run (0..total-1):
            //   runStart + offset * (dRow,dCol)
            // Smallest start offset that still includes lastMove is backward - (k - 1).
            // Clamp to 0 so we stay inside the run.
            var segmentStartOffset = backward - (k - 1);
            if (segmentStartOffset < 0) segmentStartOffset = 0;

            var startRow = runStartRow + segmentStartOffset * dRow;
            var startCol = runStartCol + segmentStartOffset * dCol;
            var endRow = startRow + (k - 1) * dRow;
            var endCol = startCol + (k - 1) * dCol;

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
            var count = 0;
            var r = row + dRow;
            var c = col + dCol;

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
            for (var i = 0; i < cells.Length; i++)
            {
                if (cells[i] == PlayerMark.None)
                    return false;
            }

            return true;
        }
    }

    /// <summary>
    /// Exposes the same K(N) policy used by <see cref="ClassicRulesEngine"/> (ADR-11).
    /// AI consumes this contract; K is never hard-coded in AI code.
    /// </summary>
    public sealed class ClassicWinLengthProvider : IClassicWinLengthProvider
    {
        public int GetWinLength(int boardSize) => ClassicRulesEngine.GetWinLength(boardSize);
    }
}