#nullable enable

using System;
using R3;
using Runtime.GameModes.Wizard;
using Runtime.Games.Battleship.Core;
using Runtime.Games.Battleship.Placement;
using Runtime.Gameplay;
using Runtime.Localization;
using Runtime.Localization.Contracts;
using UnityEngine.UIElements;

namespace Runtime.Games.Battleship.UI.Placement
{
    public sealed class BattleshipPlacementUiController : IBattleshipPlacementUiController
    {
        private const string _autoButtonKey = "Game.Battleship.Placement.AutoButton";
        private const string _rotateButtonKey = "Game.Battleship.Placement.RotateButton";
        private const string _removeButtonKey = "Game.Battleship.Placement.RemoveButton";
        private const string _readyButtonKey = "Game.Battleship.Placement.ReadyButton";
        private const string _waitingStatusKey = "Game.Battleship.Placement.Status.WaitingOpponent";
        private const string _unavailableStatusKey = "Game.Battleship.Placement.Status.Unavailable";
        private const string _placeAllShipsStatusKey = "Game.Battleship.Placement.Status.PlaceAllShips";
        private const string _confirmReadyStatusKey = "Game.Battleship.Placement.Status.ConfirmReady";

        private const string _autoButtonFallback = "Auto place";
        private const string _rotateButtonFallback = "Rotate";
        private const string _removeButtonFallback = "Remove";
        private const string _readyButtonFallback = "Ready";
        private const string _waitingStatusFallback = "Placement submitted. Waiting for opponent.";
        private const string _unavailableStatusFallback = "Placement is unavailable.";
        private const string _placeAllShipsStatusFallback = "Place all ships.";
        private const string _confirmReadyStatusFallback = "Press Ready to confirm placement.";

        private readonly IGameplayFieldUiAdapter _fieldUiAdapter;
        private readonly IBattleshipFieldUiAdapter? _battleshipFieldUiAdapter;
        private readonly BattleshipPlacementService _placementService;
        private readonly IBattleshipGameplaySnapshotProvider _snapshotProvider;
        private readonly IBattleshipGameplayEventStream _eventStream;
        private readonly ILocalizationService? _localization;
        private readonly BattleshipPlacementPreviewRenderer _previewRenderer;
        private readonly BattleshipPlacementPanelView _panelView;

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
            _battleshipFieldUiAdapter = battleshipFieldUiAdapter;
            _placementService = placementService ?? throw new ArgumentNullException(nameof(placementService));
            _snapshotProvider = snapshotProvider ?? throw new ArgumentNullException(nameof(snapshotProvider));
            _eventStream = eventStream ?? throw new ArgumentNullException(nameof(eventStream));
            _localization = localization;
            _previewRenderer = new BattleshipPlacementPreviewRenderer(_fieldUiAdapter, _battleshipFieldUiAdapter);
            
            _panelView = new BattleshipPlacementPanelView(
                onAutoPlace: () => _placementService.AutoPlace(),
                onRotate: () => _placementService.TryToggleSelectedOrientation(),
                onRemove: () => _placementService.TryRemoveSelected(),
                onReady: OnReadyClicked,
                onShipSelected: shipId => _placementService.TrySelectShip(shipId));
        }

        public void Bind()
        {
            ThrowIfDisposed();

            if (_isBound)
                return;

            if (!TryAttachPanel())
                return;

            _subscriptions = new CompositeDisposable();
            SubscribeToCellClicks();
            SubscribeToPlacementChanges();
            SubscribeToPhaseChanges();
            InitializeUi();
            _isBound = true;
        }

        private bool TryAttachPanel() =>
            _panelView.TryAttach(_fieldUiAdapter.FieldContainer, _placementService.Ships);

        private void SubscribeToCellClicks() =>
            ResolvePlacementCellClicks()
                .Subscribe(OnCellClicked)
                .AddTo(_subscriptions!);

        private void SubscribeToPlacementChanges() =>
            _placementService.Changed
                .Subscribe(_ => RefreshUi())
                .AddTo(_subscriptions!);

        private void SubscribeToPhaseChanges() =>
            _eventStream.PhaseChanged
                .Subscribe(_ =>
                {
                    _placementService.SyncFromSnapshot();
                    RefreshUi();
                })
                .AddTo(_subscriptions!);

        private void InitializeUi()
        {
            _placementService.SyncFromSnapshot();
            RefreshUi();
        }

        public void Unbind()
        {
            if (!_isBound)
                return;

            _subscriptions?.Dispose();
            _subscriptions = null;
            _panelView.Detach();
            _isBound = false;
            _previewRenderer.Clear();
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

            RefreshControlTexts();

            UpdateActionButtons(canEdit, hasSelection);
            UpdateShipButtons(canEdit);
            UpdateStatusLabel(phase);
            _previewRenderer.Render(_placementService.Ships);
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
                    ? "▶ "
                    : ship.IsPlaced
                        ? "✓ "
                        : string.Empty;

                button.text = prefix + ShipLabel(ship.Size, ship.ShipId);
                button.SetEnabled(canEdit);
            }
        }

        private void UpdateStatusLabel(BattleshipPhase phase)
        {
            if (_panelView.StatusLabel != null)
                _panelView.StatusLabel.text = ResolveStatusText(phase);
        }

        private string ResolveStatusText(BattleshipPhase phase)
        {
            if (!_placementService.CanEdit)
            {
                return phase == BattleshipPhase.Waiting
                    ? ResolvePlacementText(_waitingStatusKey, _waitingStatusFallback)
                    : ResolvePlacementText(_unavailableStatusKey, _unavailableStatusFallback);
            }

            if (!string.IsNullOrWhiteSpace(_placementService.LastErrorKey))
                return ResolvePlacementText(_placementService.LastErrorKey!, _placementService.LastErrorKey!);

            return !_placementService.IsReadyToConfirm 
                ? ResolvePlacementText(_placeAllShipsStatusKey, _placeAllShipsStatusFallback) 
                : ResolvePlacementText(_confirmReadyStatusKey, _confirmReadyStatusFallback);
        }

        private void RefreshControlTexts()
        {
            if (_panelView.AutoButton != null)
                _panelView.AutoButton.text = ResolvePlacementText(_autoButtonKey, _autoButtonFallback);

            if (_panelView.RotateButton != null)
                _panelView.RotateButton.text = ResolvePlacementText(_rotateButtonKey, _rotateButtonFallback);

            if (_panelView.RemoveButton != null)
                _panelView.RemoveButton.text = ResolvePlacementText(_removeButtonKey, _removeButtonFallback);

            if (_panelView.ReadyButton != null)
                _panelView.ReadyButton.text = ResolvePlacementText(_readyButtonKey, _readyButtonFallback);
        }

        private string ResolvePlacementText(string key, string fallback)
        {
            if (_localization == null)
                return fallback;

            var resolved = WizardErrorMessageResolver.Resolve(_localization, key);
            
            if (string.IsNullOrWhiteSpace(resolved) || string.Equals(resolved, key, StringComparison.Ordinal))
                return fallback;

            return resolved;
        }

        private Observable<CellId> ResolvePlacementCellClicks() => 
            _battleshipFieldUiAdapter is { HasOwnBoard: true } ? _battleshipFieldUiAdapter.OwnBoardCellClicks : _fieldUiAdapter.CellClicks;

        private static string ShipLabel(ShipSize size, int shipId)
        {
            var sizeText = ((int)size).ToString();
            return sizeText + "#" + (shipId + 1);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(BattleshipPlacementUiController));
        }
    }
}