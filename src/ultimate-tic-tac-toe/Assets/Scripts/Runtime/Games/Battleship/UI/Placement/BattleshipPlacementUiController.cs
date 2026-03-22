#nullable enable

using System;
using R3;
using Runtime.Games.Battleship.Core;
using Runtime.Games.Battleship.Placement;
using Runtime.Gameplay;
using Runtime.Localization.Contracts;
using UnityEngine.UIElements;

namespace Runtime.Games.Battleship.UI.Placement
{
    public sealed class BattleshipPlacementUiController : IBattleshipPlacementUiController
    {
        private readonly IGameplayFieldUiAdapter _fieldUiAdapter;
        private readonly BattleshipPlacementService _placementService;
        private readonly IBattleshipGameplaySnapshotProvider _snapshotProvider;
        private readonly IBattleshipGameplayEventStream _eventStream;
        private readonly BattleshipPlacementPreviewRenderer _previewRenderer;
        private readonly BattleshipPlacementPanelView _panelView;
        private readonly IBattleshipFieldUiAdapter? _battleshipFieldUiAdapter;
        private readonly BattleshipPlacementHoverHandler _hoverHandler;
        private readonly BattleshipPlacementPanelTextBinder _textBinder;

        private CompositeDisposable? _subscriptions;
        private bool _isBound;
        private bool _disposed;

        public BattleshipPlacementUiController(
            IGameplayFieldUiAdapter fieldUiAdapter,
            BattleshipPlacementService placementService,
            IBattleshipGameplaySnapshotProvider snapshotProvider,
            IBattleshipGameplayEventStream eventStream,
            ILocalizationService? localization = null,
            IBattleshipFieldUiAdapter? battleshipFieldUiAdapter = null)
        {
            _fieldUiAdapter = fieldUiAdapter ?? throw new ArgumentNullException(nameof(fieldUiAdapter));
            _placementService = placementService ?? throw new ArgumentNullException(nameof(placementService));
            _snapshotProvider = snapshotProvider ?? throw new ArgumentNullException(nameof(snapshotProvider));
            _eventStream = eventStream ?? throw new ArgumentNullException(nameof(eventStream));
            _battleshipFieldUiAdapter = battleshipFieldUiAdapter;

            _previewRenderer = new BattleshipPlacementPreviewRenderer(fieldUiAdapter, battleshipFieldUiAdapter);

            _panelView = new BattleshipPlacementPanelView(
                onAutoPlace: () => _placementService.AutoPlace(),
                onRotate: () => _placementService.TryToggleSelectedOrientation(),
                onRemove: () => _placementService.TryRemoveSelected(),
                onReady: OnReadyClicked,
                onShipSelected: shipId => _placementService.TrySelectShip(shipId));

            _hoverHandler = new BattleshipPlacementHoverHandler(
                fieldUiAdapter, battleshipFieldUiAdapter, placementService, _previewRenderer);

            _textBinder = new BattleshipPlacementPanelTextBinder(_panelView, placementService, localization);
        }

        public void Bind()
        {
            ThrowIfDisposed();

            if (_isBound)
                return;

            if (!_panelView.TryAttach(_fieldUiAdapter.FieldContainer, _placementService.Ships))
                return;

            _subscriptions = new CompositeDisposable();

            ResolvePlacementCellClicks()
                .Subscribe(OnCellClicked)
                .AddTo(_subscriptions);

            _placementService.Changed
                .Subscribe(_ => RefreshUi())
                .AddTo(_subscriptions);

            _eventStream.PhaseChanged
                .Subscribe(_ =>
                {
                    _placementService.SyncFromSnapshot();
                    RefreshUi();
                })
                .AddTo(_subscriptions);

            _hoverHandler.Register();
            _textBinder.SetTooltips();

            _placementService.SyncFromSnapshot();
            RefreshUi();
            _isBound = true;
        }

        public void Unbind()
        {
            if (!_isBound)
                return;

            _subscriptions?.Dispose();
            _subscriptions = null;
            _hoverHandler.Unregister();
            _panelView.Detach();
            _isBound = false;
            _previewRenderer.Clear();
            RestoreScoreboardVisibility();
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            Unbind();
        }

        private void OnReadyClicked()
        {
            _placementService.TryConfirmReady();
            _placementService.SyncFromSnapshot();
            RefreshUi();
        }

        private void OnCellClicked(CellId cellId)
        {
            if (!_isBound || !_placementService.CanEdit)
                return;

            if (_placementService.SelectedShipId == null)
            {
                if (_placementService.TryGetShipAt(cellId, out var shipId))
                    _placementService.TrySelectShip(shipId);

                return;
            }

            if (_placementService.TryPlaceSelected(cellId))
                return;

            if (_placementService.TryGetShipAt(cellId, out var replacementShipId))
                _placementService.TrySelectShip(replacementShipId);
        }

        private void RefreshUi()
        {
            var panel = _panelView.Root;

            if (panel == null)
                return;

            var phase = _snapshotProvider.Phase;
            var isVisible = phase == BattleshipPhase.Placement || phase == BattleshipPhase.Waiting;
            panel.style.display = isVisible ? DisplayStyle.Flex : DisplayStyle.None;

            if (!isVisible)
                return;

            var canEdit = _placementService.CanEdit;
            var hasSelection = _placementService.SelectedShipId.HasValue;

            UpdateScoreboardVisibility(isVisible);
            _textBinder.RefreshControlTexts();
            UpdateActionButtons(canEdit, hasSelection);
            UpdateShipButtons(canEdit);
            _textBinder.RefreshStatusLabel(phase, hasSelection);
            _previewRenderer.Render(_placementService.Ships, _placementService.SelectedShipId);

            if (!canEdit || !hasSelection)
                _previewRenderer.ClearHoverPreview();
        }

        private void UpdateActionButtons(bool canEdit, bool hasSelection)
        {
            _panelView.AutoButton?.SetEnabled(canEdit);
            _panelView.RotateButton?.SetEnabled(canEdit && hasSelection);
            _panelView.RemoveButton?.SetEnabled(canEdit && hasSelection);
            _panelView.ReadyButton?.SetEnabled(canEdit && _placementService.IsReadyToConfirm);
        }

        private void UpdateShipButtons(bool canEdit)
        {
            foreach (var ship in _placementService.Ships)
            {
                if (!_panelView.ShipButtons.TryGetValue(ship.ShipId, out var button))
                    continue;

                var prefix = _placementService.SelectedShipId == ship.ShipId
                    ? "\u25b6 "
                    : ship.IsPlaced
                        ? "\u2713 "
                        : string.Empty;

                button.text = prefix + $"\u00d7{(int)ship.Size}";
                button.SetEnabled(canEdit);
            }
        }

        private Observable<CellId> ResolvePlacementCellClicks() =>
            _battleshipFieldUiAdapter is { HasOwnBoard: true }
                ? _battleshipFieldUiAdapter.OwnBoardCellClicks
                : _fieldUiAdapter.CellClicks;

        private void UpdateScoreboardVisibility(bool isPlacementPhase)
        {
            var scoreboard = _fieldUiAdapter.Player1Panel?.parent;
            
            if (scoreboard != null)
                scoreboard.style.display = isPlacementPhase ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private void RestoreScoreboardVisibility()
        {
            var scoreboard = _fieldUiAdapter.Player1Panel?.parent;
            
            if (scoreboard != null)
                scoreboard.style.display = DisplayStyle.Flex;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(BattleshipPlacementUiController));
        }
    }
}
