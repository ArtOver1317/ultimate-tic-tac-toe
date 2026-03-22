#nullable enable

using System.Collections.Generic;
using Runtime.Games.Battleship.ECS.Core;
using Runtime.Games.Battleship.Placement;
using Runtime.Gameplay;
using UnityEngine.UIElements;

namespace Runtime.Games.Battleship.UI.Placement
{
    /// <summary>
    /// Manages pointer-hover preview during ship placement.
    /// Owns the cell-id map and pointer event subscriptions.
    /// </summary>
    internal sealed class BattleshipPlacementHoverHandler
    {
        private readonly IGameplayFieldUiAdapter _fieldUiAdapter;
        private readonly IBattleshipFieldUiAdapter? _battleshipFieldUiAdapter;
        private readonly BattleshipPlacementService _placementService;
        private readonly BattleshipPlacementPreviewRenderer _previewRenderer;
        private readonly Dictionary<VisualElement, CellId> _cellIdByRoot = new();

        internal BattleshipPlacementHoverHandler(
            IGameplayFieldUiAdapter fieldUiAdapter,
            IBattleshipFieldUiAdapter? battleshipFieldUiAdapter,
            BattleshipPlacementService placementService,
            BattleshipPlacementPreviewRenderer previewRenderer)
        {
            _fieldUiAdapter = fieldUiAdapter;
            _battleshipFieldUiAdapter = battleshipFieldUiAdapter;
            _placementService = placementService;
            _previewRenderer = previewRenderer;
        }

        internal void Register()
        {
            BuildCellIdMap();
            _fieldUiAdapter.FieldContainer.RegisterCallback<PointerMoveEvent>(OnPointerMoved);
            _fieldUiAdapter.FieldContainer.RegisterCallback<PointerLeaveEvent>(OnPointerLeft);
        }

        internal void Unregister()
        {
            _fieldUiAdapter.FieldContainer.UnregisterCallback<PointerMoveEvent>(OnPointerMoved);
            _fieldUiAdapter.FieldContainer.UnregisterCallback<PointerLeaveEvent>(OnPointerLeft);
            _cellIdByRoot.Clear();
        }

        private void BuildCellIdMap()
        {
            _cellIdByRoot.Clear();
            const int boardSize = BattleshipEcsBoard.DefaultBoardSize;

            for (var row = 0; row < boardSize; row++)
            {
                for (var col = 0; col < boardSize; col++)
                {
                    var cellId = new CellId(row, col);

                    if (_battleshipFieldUiAdapter is { HasOwnBoard: true }
                        && _battleshipFieldUiAdapter.TryGetOwnCell(cellId, out var ownCell))
                        _cellIdByRoot[ownCell] = cellId;
                    else if (_fieldUiAdapter.TryGetCell(cellId, out var gameplayCell))
                        _cellIdByRoot[gameplayCell] = cellId;
                }
            }
        }

        private void OnPointerMoved(PointerMoveEvent evt)
        {
            if (!_placementService.CanEdit || _placementService.SelectedShipId == null)
            {
                _previewRenderer.ClearHoverPreview();
                return;
            }

            var hoveredCell = FindCellFromTarget(evt.target as VisualElement);
            
            if (hoveredCell == null)
            {
                _previewRenderer.ClearHoverPreview();
                return;
            }

            var selectedId = _placementService.SelectedShipId.Value;
            BattleshipPlacementShipState? selectedShip = null;

            foreach (var ship in _placementService.Ships)
            {
                if (ship.ShipId == selectedId)
                {
                    selectedShip = ship;
                    break;
                }
            }

            if (selectedShip == null)
                return;

            _previewRenderer.RenderHoverPreview(hoveredCell.Value, selectedShip.Value);
        }

        private void OnPointerLeft(PointerLeaveEvent evt) => _previewRenderer.ClearHoverPreview();

        private CellId? FindCellFromTarget(VisualElement? element)
        {
            while (element != null)
            {
                if (_cellIdByRoot.TryGetValue(element, out var cellId))
                    return cellId;

                element = element.parent;
            }

            return null;
        }
    }
}
