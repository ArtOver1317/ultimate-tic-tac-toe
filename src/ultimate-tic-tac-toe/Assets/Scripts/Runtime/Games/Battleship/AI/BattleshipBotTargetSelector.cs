#nullable enable

using System;
using System.Collections.Generic;
using Runtime.Games.Battleship.Core;
using Runtime.Games.Battleship.ECS.Core;
using Runtime.Gameplay;

namespace Runtime.Games.Battleship.AI
{
    internal sealed class BattleshipBotTargetSelector
    {
        private readonly List<int> _unknownTargetIndices = new(100);
        private readonly List<int> _finishTargetIndices = new(16);
        private readonly HashSet<int> _finishTargetIndexSet = new();

        public bool TryChooseTarget(IReadOnlyList<BattleshipCellMark> marks, Random? random, out CellId cellId)
        {
            cellId = default;

            if (!TryResolveBoardSize(marks, out var boardSize))
                return false;

            if (TryChooseFinishTarget(marks, boardSize, random, out var finishPick))
            {
                cellId = ToCellId(finishPick, boardSize);
                return true;
            }

            return TryChooseUnknownTarget(marks, boardSize, random, out cellId);
        }

        private static bool TryResolveBoardSize(IReadOnlyList<BattleshipCellMark>? marks, out int boardSize)
        {
            boardSize = 0;
            
            if (marks == null || marks.Count == 0)
                return false;

            boardSize = ResolveBoardSize(marks.Count);
            return true;
        }

        private bool TryChooseUnknownTarget(
            IReadOnlyList<BattleshipCellMark> marks,
            int boardSize,
            Random? random,
            out CellId cellId)
        {
            cellId = default;
            CollectUnknownTargets(marks);
            
            if (_unknownTargetIndices.Count == 0)
                return false;

            var pick = random != null ? _unknownTargetIndices[random.Next(0, _unknownTargetIndices.Count)] : _unknownTargetIndices[0];
            cellId = ToCellId(pick, boardSize);
            return true;
        }

        private void CollectUnknownTargets(IReadOnlyList<BattleshipCellMark> marks)
        {
            _unknownTargetIndices.Clear();
            
            for (var i = 0; i < marks.Count; i++)
            {
                if (marks[i] == BattleshipCellMark.Unknown)
                    _unknownTargetIndices.Add(i);
            }
        }

        private bool TryChooseFinishTarget(
            IReadOnlyList<BattleshipCellMark> marks,
            int boardSize,
            Random? random,
            out int targetIndex)
        {
            targetIndex = -1;

            _finishTargetIndices.Clear();
            _finishTargetIndexSet.Clear();

            for (var index = 0; index < marks.Count; index++)
            {
                if (marks[index] != BattleshipCellMark.Hit)
                    continue;

                var row = index / boardSize;
                var col = index % boardSize;

                var hasHorizontalHit = IsHitAt(marks, boardSize, row, col - 1) || IsHitAt(marks, boardSize, row, col + 1);
                
                if (hasHorizontalHit)
                {
                    TryAddFinishCandidate(marks, boardSize, row, col - 1);
                    TryAddFinishCandidate(marks, boardSize, row, col + 1);
                }

                var hasVerticalHit = IsHitAt(marks, boardSize, row - 1, col) || IsHitAt(marks, boardSize, row + 1, col);
                
                if (hasVerticalHit)
                {
                    TryAddFinishCandidate(marks, boardSize, row - 1, col);
                    TryAddFinishCandidate(marks, boardSize, row + 1, col);
                }
            }

            if (_finishTargetIndices.Count == 0)
                CollectAdjacentFinishCandidates(marks, boardSize);

            if (_finishTargetIndices.Count == 0)
                return false;

            targetIndex = random != null
                ? _finishTargetIndices[random.Next(0, _finishTargetIndices.Count)]
                : _finishTargetIndices[0];
            
            return true;
        }

        private void CollectAdjacentFinishCandidates(IReadOnlyList<BattleshipCellMark> marks, int boardSize)
        {
            for (var index = 0; index < marks.Count; index++)
            {
                if (marks[index] != BattleshipCellMark.Hit)
                    continue;

                var row = index / boardSize;
                var col = index % boardSize;

                TryAddFinishCandidate(marks, boardSize, row - 1, col);
                TryAddFinishCandidate(marks, boardSize, row + 1, col);
                TryAddFinishCandidate(marks, boardSize, row, col - 1);
                TryAddFinishCandidate(marks, boardSize, row, col + 1);
            }
        }

        private void TryAddFinishCandidate(IReadOnlyList<BattleshipCellMark> marks, int boardSize, int row, int col)
        {
            if (row < 0 || col < 0 || row >= boardSize || col >= boardSize)
                return;

            var index = row * boardSize + col;
            
            if (index < 0 || index >= marks.Count)
                return;

            if (marks[index] != BattleshipCellMark.Unknown)
                return;

            if (!_finishTargetIndexSet.Add(index))
                return;

            _finishTargetIndices.Add(index);
        }

        private static int ResolveBoardSize(int cellCount)
        {
            if (cellCount <= 0)
                return BattleshipEcsBoard.DefaultBoardSize;

            var boardSize = (int)Math.Sqrt(cellCount);
            return boardSize > 0 ? boardSize : BattleshipEcsBoard.DefaultBoardSize;
        }

        private static bool IsHitAt(IReadOnlyList<BattleshipCellMark> marks, int boardSize, int row, int col)
        {
            if (row < 0 || col < 0 || row >= boardSize || col >= boardSize)
                return false;

            var index = row * boardSize + col;
            
            return index >= 0
                   && index < marks.Count
                   && marks[index] == BattleshipCellMark.Hit;
        }

        private static CellId ToCellId(int index, int boardSize)
        {
            var row = index / boardSize;
            var col = index % boardSize;
            return new CellId(row, col);
        }
    }
}