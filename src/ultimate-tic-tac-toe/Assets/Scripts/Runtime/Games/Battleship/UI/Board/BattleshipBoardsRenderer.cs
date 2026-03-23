#nullable enable

using System;
using System.Collections.Generic;
using Runtime.Games.Battleship.Core;
using Runtime.Games.Battleship.ECS.Core;
using Runtime.Gameplay;
using UnityEngine.UIElements;

namespace Runtime.Games.Battleship.UI.Board
{
    /// <summary>
    /// Stateful renderer for opponent and own Battleship boards.
    /// Tracks previous cell marks to detect new shots and trigger animations.
    /// </summary>
    internal sealed class BattleshipBoardsRenderer
    {
        private const string _opponentMarkElementName = "Mark";
        private const string _ownMarkElementName = "OwnMark";

        private const string _shotAppearClass = "battleship-mark--shot-appear";
        private const string _cellFeedbackClass = "cell--click-feedback";

        private const long _cellFeedbackDurationMs = 140;
        private const long _shotAppearDurationMs = 450;

        private readonly IGameplayFieldUiAdapter _fieldUiAdapter;
        private readonly IBattleshipFieldUiAdapter _battleshipFieldUiAdapter;

        private BattleshipCellMark[]? _prevOpponentMarks;
        private BattleshipCellMark[]? _prevOwnMarks;

        public BattleshipBoardsRenderer(
            IGameplayFieldUiAdapter fieldUiAdapter,
            IBattleshipFieldUiAdapter battleshipFieldUiAdapter)
        {
            _fieldUiAdapter = fieldUiAdapter;
            _battleshipFieldUiAdapter = battleshipFieldUiAdapter;
        }

        public void Reset()
        {
            _prevOpponentMarks = null;
            _prevOwnMarks = null;
        }

        public void RenderOpponentBoard(IReadOnlyList<BattleshipCellMark> marks, int cellCount, int boardSize)
        {
            var prevMarks = _prevOpponentMarks;
            var newCache = new BattleshipCellMark[cellCount];

            for (var index = 0; index < cellCount; index++)
            {
                var mark = GetMarkOrUnknown(marks, index);
                newCache[index] = mark;
                var cellId = ToCellId(index, boardSize);

                if (!_fieldUiAdapter.TryGetCellView(cellId, out var cellRoot, out var markLabel) || markLabel == null)
                    continue;

                var (text, cssClass) = BattleshipBoardCellRenderer.ResolveOpponentMark(mark);
                BattleshipBoardCellRenderer.ApplyMark(markLabel, text, cssClass);
                BattleshipBoardCellRenderer.ApplyOpponentCellClass(cellRoot, mark);

                var prevMark = GetPrevMark(prevMarks, index);
               
                if (prevMarks != null && IsNewShot(prevMark, mark))
                    TriggerShotAnimation(cellRoot, _opponentMarkElementName, triggerCellFeedback: true);
            }

            _prevOpponentMarks = newCache;
        }

        public void RenderOwnBoard(
            IReadOnlyList<BattleshipCellMark> marks,
            bool[] shipOccupancy,
            int cellCount,
            int boardSize)
        {
            var prevMarks = _prevOwnMarks;
            var newCache = new BattleshipCellMark[cellCount];

            for (var index = 0; index < cellCount; index++)
            {
                var mark = GetMarkOrUnknown(marks, index);
                newCache[index] = mark;
                var hasShip = index < shipOccupancy.Length && shipOccupancy[index];
                var cellId = ToCellId(index, boardSize);

                if (!_battleshipFieldUiAdapter.TryGetOwnCellView(cellId, out var cellRoot, out var markLabel))
                    continue;

                var (text, markCssClass) = BattleshipBoardCellRenderer.ResolveOwnMark(mark, hasShip);
                BattleshipBoardCellRenderer.ApplyMark(markLabel, text, markCssClass);
                BattleshipBoardCellRenderer.ApplyOwnCellClass(cellRoot, mark, hasShip);

                var prevMark = GetPrevMark(prevMarks, index);
                
                if (prevMarks != null && IsNewShot(prevMark, mark))
                    TriggerShotAnimation(cellRoot, _ownMarkElementName, triggerCellFeedback: false);
            }

            _prevOwnMarks = newCache;
        }

        public bool[] BuildShipOccupancy(int localSlot, int cellCount, int boardSize,
            IBattleshipGameplaySnapshotProvider snapshotProvider)
        {
            var occupancy = new bool[cellCount];

            if (!snapshotProvider.TryGetFleetLayout(localSlot, out var layout)
                || !layout.IsInitialized
                || layout.Ships == null)
                return occupancy;

            for (var shipIndex = 0; shipIndex < layout.Ships.Count; shipIndex++)
            {
                MarkShipOccupancy(layout.Ships[shipIndex], occupancy, boardSize);
            }

            return occupancy;
        }

        public static int ResolveBoardSize(int cellCount)
        {
            if (cellCount <= 0)
                return BattleshipEcsBoard.DefaultBoardSize;

            var root = (int)Math.Sqrt(cellCount);
            return root * root == cellCount ? root : BattleshipEcsBoard.DefaultBoardSize;
        }

        public static CellId ToCellId(int index, int boardSize)
        {
            var row = index / boardSize;
            var col = index % boardSize;
            return new CellId(row, col);
        }

        private static BattleshipCellMark GetMarkOrUnknown(IReadOnlyList<BattleshipCellMark>? marks, int index)
        {
            if (marks == null || index < 0 || index >= marks.Count)
                return BattleshipCellMark.Unknown;
            
            return marks[index];
        }

        private static BattleshipCellMark GetPrevMark(BattleshipCellMark[]? prevMarks, int index) =>
            prevMarks != null && index < prevMarks.Length
                ? prevMarks[index]
                : BattleshipCellMark.Unknown;

        private static bool IsNewShot(BattleshipCellMark prev, BattleshipCellMark current) =>
            prev == BattleshipCellMark.Unknown && current != BattleshipCellMark.Unknown;

        private static void TriggerShotAnimation(VisualElement? cellRoot, string markElementName, bool triggerCellFeedback)
        {
            if (triggerCellFeedback && cellRoot != null)
            {
                cellRoot.AddToClassList(_cellFeedbackClass);
                
                cellRoot.schedule.Execute(() => cellRoot.RemoveFromClassList(_cellFeedbackClass))
                    .ExecuteLater(_cellFeedbackDurationMs);
            }

            var markRoot = cellRoot?.Q<VisualElement>(markElementName);
            
            if (markRoot == null)
                return;

            markRoot.AddToClassList(_shotAppearClass);
            
            markRoot.schedule.Execute(() => markRoot.RemoveFromClassList(_shotAppearClass))
                .ExecuteLater(_shotAppearDurationMs);
        }

        private static void MarkShipOccupancy(ShipPlacement ship, bool[] occupancy, int boardSize)
        {
            var length = (int)ship.Size;

            for (var segment = 0; segment < length; segment++)
            {
                var row = ship.StartCell.Major + (ship.Orientation == ShipOrientation.Vertical ? segment : 0);
                var col = ship.StartCell.Minor + (ship.Orientation == ShipOrientation.Horizontal ? segment : 0);

                if (row < 0 || row >= boardSize || col < 0 || col >= boardSize)
                    continue;

                var index = row * boardSize + col;
                
                if (index >= 0 && index < occupancy.Length)
                    occupancy[index] = true;
            }
        }
    }
}
