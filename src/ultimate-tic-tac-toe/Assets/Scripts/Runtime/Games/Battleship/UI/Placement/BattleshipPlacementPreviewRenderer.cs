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
        private const string _previewMarkGlyph = "■";

        private readonly IGameplayFieldUiAdapter _fieldUiAdapter;
        private readonly IBattleshipFieldUiAdapter? _battleshipFieldUiAdapter;

        public BattleshipPlacementPreviewRenderer(
            IGameplayFieldUiAdapter fieldUiAdapter,
            IBattleshipFieldUiAdapter? battleshipFieldUiAdapter)
        {
            _fieldUiAdapter = fieldUiAdapter;
            _battleshipFieldUiAdapter = battleshipFieldUiAdapter;
        }

        public void Render(IReadOnlyList<BattleshipPlacementShipState> ships)
        {
            Clear();

            foreach (var ship in ships)
            {
                RenderShipPreview(ship);
            }
        }

        public void Clear()
        {
            for (var row = 0; row < BattleshipEcsBoard.DefaultBoardSize; row++)
            {
                for (var col = 0; col < BattleshipEcsBoard.DefaultBoardSize; col++)
                {
                    if (TryGetPlacementCellView(new CellId(row, col), out var markLabel) && markLabel != null)
                        markLabel.text = string.Empty;
                }
            }
        }

        private bool TryGetPlacementCellView(CellId cellId, out Label? markLabel)
        {
            markLabel = null;

            if (_battleshipFieldUiAdapter is { HasOwnBoard: true }
                && _battleshipFieldUiAdapter.TryGetOwnCellView(cellId, out _, out var ownMarkLabel))
            {
                markLabel = ownMarkLabel;
                return true;
            }

            if (_fieldUiAdapter.TryGetCellView(cellId, out _, out var gameplayMarkLabel) && gameplayMarkLabel != null)
            {
                markLabel = gameplayMarkLabel;
                return true;
            }

            return false;
        }

        private void RenderShipPreview(in BattleshipPlacementShipState ship)
        {
            if (!ship.IsPlaced || !ship.StartCell.HasValue)
                return;

            var startCell = ship.StartCell.Value;
            var length = (int)ship.Size;

            for (var segment = 0; segment < length; segment++)
            {
                if (!TryResolveShipSegmentCell(ship, startCell, segment, out var cellId))
                    continue;

                SetMarkText(cellId, _previewMarkGlyph);
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

        private void SetMarkText(CellId cellId, string text)
        {
            if (TryGetPlacementCellView(cellId, out var markLabel) && markLabel != null)
                markLabel.text = text;
        }
    }
}