#nullable enable

using System.Collections.Generic;
using Runtime.Games.Battleship.Core;
using Runtime.Games.Battleship.ECS.Core;
using Runtime.Games.Battleship.Placement;
using Runtime.Gameplay;
using UnityEngine.UIElements;

namespace Runtime.Games.Battleship.UI.Placement
{
    internal sealed class BattleshipPlacementPreviewRenderer
    {
        private const string _placedClass = "placement-ship--placed";
        private const string _selectedClass = "placement-ship--selected";
        private const string _hoverClass = "placement-ship--hover";

        private readonly IGameplayFieldUiAdapter _fieldUiAdapter;
        private readonly IBattleshipFieldUiAdapter? _battleshipFieldUiAdapter;

        private readonly HashSet<CellId> _markedCells = new();
        private readonly HashSet<CellId> _hoverCells = new();

        public BattleshipPlacementPreviewRenderer(
            IGameplayFieldUiAdapter fieldUiAdapter,
            IBattleshipFieldUiAdapter? battleshipFieldUiAdapter)
        {
            _fieldUiAdapter = fieldUiAdapter;
            _battleshipFieldUiAdapter = battleshipFieldUiAdapter;
        }

        public void Render(IReadOnlyList<BattleshipPlacementShipState> ships, int? selectedShipId = null)
        {
            ClearShipMarks();

            foreach (var ship in ships)
            {
                RenderShipPreview(ship, selectedShipId);
            }
        }

        public void RenderHoverPreview(CellId anchorCell, in BattleshipPlacementShipState ship)
        {
            ClearHoverPreview();

            var length = (int)ship.Size;
            
            for (var segment = 0; segment < length; segment++)
            {
                var row = anchorCell.Major + (ship.Orientation == ShipOrientation.Vertical ? segment : 0);
                var col = anchorCell.Minor + (ship.Orientation == ShipOrientation.Horizontal ? segment : 0);

                if (row < 0 || row >= BattleshipEcsBoard.DefaultBoardSize
                            || col < 0 || col >= BattleshipEcsBoard.DefaultBoardSize)
                    continue;

                var cellId = new CellId(row, col);
              
                if (TryGetPlacementCellRoot(cellId, out var cellRoot))
                {
                    cellRoot.AddToClassList(_hoverClass);
                    _hoverCells.Add(cellId);
                }
            }
        }

        public void ClearHoverPreview()
        {
            foreach (var cellId in _hoverCells)
            {
                if (TryGetPlacementCellRoot(cellId, out var cellRoot))
                    cellRoot.RemoveFromClassList(_hoverClass);
            }

            _hoverCells.Clear();
        }

        public void Clear()
        {
            ClearHoverPreview();
            ClearShipMarks();
        }

        private void ClearShipMarks()
        {
            foreach (var cellId in _markedCells)
            {
                if (TryGetPlacementCellRoot(cellId, out var cellRoot))
                {
                    cellRoot.RemoveFromClassList(_placedClass);
                    cellRoot.RemoveFromClassList(_selectedClass);
                }
            }

            _markedCells.Clear();
        }

        private bool TryGetPlacementCellRoot(CellId cellId, out VisualElement cellRoot)
        {
            if (_battleshipFieldUiAdapter is { HasOwnBoard: true }
                && _battleshipFieldUiAdapter.TryGetOwnCell(cellId, out cellRoot))
                return true;

            return _fieldUiAdapter.TryGetCell(cellId, out cellRoot);
        }

        private void RenderShipPreview(in BattleshipPlacementShipState ship, int? selectedShipId)
        {
            if (!ship.IsPlaced || !ship.StartCell.HasValue)
                return;

            var startCell = ship.StartCell.Value;
            var length = (int)ship.Size;
            var cssClass = ship.ShipId == selectedShipId ? _selectedClass : _placedClass;

            for (var segment = 0; segment < length; segment++)
            {
                if (!TryResolveShipSegmentCell(ship, startCell, segment, out var cellId))
                    continue;

                if (TryGetPlacementCellRoot(cellId, out var cellRoot))
                    cellRoot.AddToClassList(cssClass);

                _markedCells.Add(cellId);
            }
        }

        private static bool TryResolveShipSegmentCell(
            in BattleshipPlacementShipState ship,
            in CellId startCell,
            int segment,
            out CellId cellId)
        {
            var row = startCell.Major + (ship.Orientation == ShipOrientation.Vertical ? segment : 0);
            var col = startCell.Minor + (ship.Orientation == ShipOrientation.Horizontal ? segment : 0);

            cellId = new CellId(row, col);

            return row is >= 0 and < BattleshipEcsBoard.DefaultBoardSize
                   && col is >= 0 and < BattleshipEcsBoard.DefaultBoardSize;
        }
    }
}