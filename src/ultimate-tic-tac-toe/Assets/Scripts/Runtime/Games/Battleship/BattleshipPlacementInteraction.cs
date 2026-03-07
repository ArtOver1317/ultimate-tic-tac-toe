#nullable enable

using System;
using System.Collections.Generic;
using R3;
using Runtime.GameModes.Wizard;
using Runtime.Gameplay;
using Runtime.Gameplay.ECS;
using Runtime.Localization;
using Runtime.Games.TicTacToe.Moves;
using UnityEngine.UIElements;

namespace Runtime.Games.Battleship
{
    public readonly struct BattleshipPlacementShipState
    {
        public int ShipId { get; }
        public ShipSize Size { get; }
        public ShipOrientation Orientation { get; }
        public CellId? StartCell { get; }
        public bool IsPlaced => StartCell.HasValue;

        public BattleshipPlacementShipState(int shipId, ShipSize size, ShipOrientation orientation, CellId? startCell)
        {
            ShipId = shipId;
            Size = size;
            Orientation = orientation;
            StartCell = startCell;
        }
    }

    public interface IBattleshipPlacementService : IDisposable
    {
        Observable<Unit> Changed { get; }
        IReadOnlyList<BattleshipPlacementShipState> Ships { get; }
        int LocalPlayerSlot { get; }
        int? SelectedShipId { get; }
        bool CanEdit { get; }
        bool IsReadyToConfirm { get; }
        string? LastErrorKey { get; }

        void SyncFromSnapshot();
        bool TrySelectShip(int shipId);
        void ClearSelection();
        bool TryToggleSelectedOrientation();
        bool TryPlaceSelected(CellId startCell);
        bool TryRemoveSelected();
        void AutoPlace();
        bool TryConfirmReady();
        bool TryGetShipAt(CellId cellId, out int shipId);
    }

    public sealed class BattleshipPlacementService : IBattleshipPlacementService
    {
        private const int BoardSize = 10;

        private static readonly ShipSize[] FleetOrder =
        {
            ShipSize.Four,
            ShipSize.Three,
            ShipSize.Three,
            ShipSize.Two,
            ShipSize.Two,
            ShipSize.Two,
            ShipSize.One,
            ShipSize.One,
            ShipSize.One,
            ShipSize.One,
        };

        private readonly IBattleshipGameplaySnapshotProvider _snapshotProvider;
        private readonly IGameplayCommandSink _commandSink;
        private readonly IBattleshipPlacementValidator _validator;
        private readonly IBattleshipAutoPlacer _autoPlacer;
        private readonly IOnlineGameplaySessionContextStore _sessionContextStore;
        private readonly Subject<Unit> _changed = new();
        private readonly BattleshipPlacementShipState[] _ships = new BattleshipPlacementShipState[FleetLayout.ExpectedShipCount];

        private int _localPlayerSlot;
        private int? _selectedShipId;
        private string? _lastErrorKey;
        private bool _disposed;

        public BattleshipPlacementService(
            IBattleshipGameplaySnapshotProvider snapshotProvider,
            IGameplayCommandSink commandSink,
            IBattleshipPlacementValidator validator,
            IBattleshipAutoPlacer autoPlacer,
            IOnlineGameplaySessionContextStore sessionContextStore)
        {
            _snapshotProvider = snapshotProvider ?? throw new ArgumentNullException(nameof(snapshotProvider));
            _commandSink = commandSink ?? throw new ArgumentNullException(nameof(commandSink));
            _validator = validator ?? throw new ArgumentNullException(nameof(validator));
            _autoPlacer = autoPlacer ?? throw new ArgumentNullException(nameof(autoPlacer));
            _sessionContextStore = sessionContextStore ?? throw new ArgumentNullException(nameof(sessionContextStore));

            _localPlayerSlot = ResolveLocalSlot();
            ResetToDock();
            SyncFromSnapshot();
        }

        public Observable<Unit> Changed => _changed;
        public IReadOnlyList<BattleshipPlacementShipState> Ships => _ships;
        public int LocalPlayerSlot => _localPlayerSlot;
        public int? SelectedShipId => _selectedShipId;
        public string? LastErrorKey => _lastErrorKey;

        public bool CanEdit
        {
            get
            {
                if (_disposed)
                    return false;

                var phase = _snapshotProvider.Phase;
                if (phase == BattleshipPhase.Battle || phase == BattleshipPhase.Finished)
                    return false;

                return !_snapshotProvider.IsPlacementConfirmed(_localPlayerSlot);
            }
        }

        public bool IsReadyToConfirm
        {
            get
            {
                if (!TryBuildLayout(out var layout))
                    return false;

                return _validator.TryValidate(layout, out _);
            }
        }

        public void SyncFromSnapshot()
        {
            ThrowIfDisposed();

            _localPlayerSlot = ResolveLocalSlot();

            if (_snapshotProvider.TryGetFleetLayout(_localPlayerSlot, out var layout)
                && layout.IsInitialized
                && layout.Ships != null
                && layout.Ships.Count == FleetLayout.ExpectedShipCount)
            {
                ApplyLayout(layout);
                PublishChanged();
                return;
            }

            var phase = _snapshotProvider.Phase;
            if (phase == BattleshipPhase.Placement)
            {
                ResetToDock();
                PublishChanged();
            }
        }

        public bool TrySelectShip(int shipId)
        {
            ThrowIfDisposed();

            if (!IsValidShipId(shipId))
                return false;

            _selectedShipId = shipId;
            PublishChanged();
            return true;
        }

        public void ClearSelection()
        {
            ThrowIfDisposed();

            if (_selectedShipId == null)
                return;

            _selectedShipId = null;
            PublishChanged();
        }

        public bool TryToggleSelectedOrientation()
        {
            ThrowIfDisposed();

            if (_selectedShipId == null)
                return false;

            var shipId = _selectedShipId.Value;
            ref var ship = ref _ships[shipId];
            var toggled = ship.Orientation == ShipOrientation.Horizontal
                ? ShipOrientation.Vertical
                : ShipOrientation.Horizontal;

            if (!ship.IsPlaced)
            {
                ship = new BattleshipPlacementShipState(ship.ShipId, ship.Size, toggled, null);
                ClearLastError();
                PublishChanged();
                return true;
            }

            var startCell = ship.StartCell;
            if (!startCell.HasValue)
                return false;

            return TryPlaceShip(shipId, startCell.Value, toggled);
        }

        public bool TryPlaceSelected(CellId startCell)
        {
            ThrowIfDisposed();

            if (_selectedShipId == null)
                return false;

            var selected = _ships[_selectedShipId.Value];
            return TryPlaceShip(selected.ShipId, startCell, selected.Orientation);
        }

        public bool TryRemoveSelected()
        {
            ThrowIfDisposed();

            if (_selectedShipId == null)
                return false;

            var shipId = _selectedShipId.Value;
            ref var ship = ref _ships[shipId];

            if (!ship.IsPlaced)
                return false;

            ship = new BattleshipPlacementShipState(ship.ShipId, ship.Size, ship.Orientation, null);
            ClearLastError();
            PublishChanged();
            return true;
        }

        public void AutoPlace()
        {
            ThrowIfDisposed();

            if (!CanEdit)
                return;

            var seed = unchecked(Environment.TickCount * 397) ^ _localPlayerSlot;
            var layout = _autoPlacer.Generate(seed);
            ApplyLayout(layout);
            ClearLastError();
            PublishChanged();
        }

        public bool TryConfirmReady()
        {
            ThrowIfDisposed();

            if (!CanEdit)
                return false;

            if (!TryBuildLayout(out var layout))
            {
                _lastErrorKey = "Errors.Battleship.Layout.Invalid";
                PublishChanged();
                return false;
            }

            if (!_validator.TryValidate(layout, out var errorKey))
            {
                _lastErrorKey = string.IsNullOrWhiteSpace(errorKey)
                    ? "Errors.Battleship.Layout.Invalid"
                    : errorKey;
                PublishChanged();
                return false;
            }

            _commandSink.SubmitCommand(new SubmitPlacementCommand(_localPlayerSlot, layout));
            ClearLastError();
            PublishChanged();
            return true;
        }

        public bool TryGetShipAt(CellId cellId, out int shipId)
        {
            ThrowIfDisposed();

            shipId = -1;
            for (var i = 0; i < _ships.Length; i++)
            {
                var ship = _ships[i];
                if (!ship.IsPlaced)
                    continue;

                if (ContainsCell(ship, cellId))
                {
                    shipId = ship.ShipId;
                    return true;
                }
            }

            return false;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _changed.OnCompleted();
            _changed.Dispose();
        }

        private bool TryPlaceShip(int shipId, CellId startCell, ShipOrientation orientation)
        {
            if (!CanEdit || !IsValidShipId(shipId))
                return false;

            var existing = _ships[shipId];
            var candidate = new BattleshipPlacementShipState(existing.ShipId, existing.Size, orientation, startCell);

            if (!CanPlace(candidate, shipId))
            {
                _lastErrorKey = "Errors.Battleship.Layout.Invalid";
                PublishChanged();
                return false;
            }

            _ships[shipId] = candidate;
            _selectedShipId = shipId;
            ClearLastError();
            PublishChanged();
            return true;
        }

        private int ResolveLocalSlot()
        {
            var snapshot = _sessionContextStore.Snapshot;
            if (!snapshot.IsOnlineDirectInvite)
                return PlayerSlotMapping.SlotX;

            return snapshot.IsHost
                ? PlayerSlotMapping.SlotX
                : PlayerSlotMapping.SlotO;
        }

        private static bool IsValidShipId(int shipId) => shipId >= 0 && shipId < FleetLayout.ExpectedShipCount;

        private void ResetToDock()
        {
            for (var i = 0; i < _ships.Length; i++)
            {
                _ships[i] = new BattleshipPlacementShipState(
                    shipId: i,
                    size: FleetOrder[i],
                    orientation: ShipOrientation.Horizontal,
                    startCell: null);
            }

            _selectedShipId = null;
            _lastErrorKey = null;
        }

        private void ApplyLayout(in FleetLayout layout)
        {
            if (!layout.IsInitialized || layout.Ships == null || layout.Ships.Count != FleetLayout.ExpectedShipCount)
                return;

            for (var i = 0; i < layout.Ships.Count && i < _ships.Length; i++)
            {
                var placement = layout.Ships[i];
                _ships[i] = new BattleshipPlacementShipState(
                    shipId: i,
                    size: placement.Size,
                    orientation: placement.Orientation,
                    startCell: placement.StartCell);
            }

            _selectedShipId = null;
            _lastErrorKey = null;
        }

        private bool TryBuildLayout(out FleetLayout layout)
        {
            layout = default;

            for (var i = 0; i < _ships.Length; i++)
            {
                if (!_ships[i].StartCell.HasValue)
                    return false;
            }

            var placements = new ShipPlacement[_ships.Length];
            for (var i = 0; i < _ships.Length; i++)
            {
                var ship = _ships[i];
                placements[i] = new ShipPlacement(ship.Size, ship.Orientation, ship.StartCell!.Value);
            }

            layout = new FleetLayout(Array.AsReadOnly(placements));
            return true;
        }

        private bool CanPlace(in BattleshipPlacementShipState candidate, int movingShipId)
        {
            var occupancy = new bool[BoardSize * BoardSize];

            for (var i = 0; i < _ships.Length; i++)
            {
                if (i == movingShipId)
                    continue;

                var ship = _ships[i];
                if (!ship.IsPlaced)
                    continue;

                if (!TryApplyShipToBoard(ship, occupancy, validateNeighbors: false))
                    return false;
            }

            return TryApplyShipToBoard(candidate, occupancy, validateNeighbors: true);
        }

        private static bool TryApplyShipToBoard(
            in BattleshipPlacementShipState ship,
            bool[] occupancy,
            bool validateNeighbors)
        {
            if (!ship.StartCell.HasValue)
                return false;

            var startCell = ship.StartCell.Value;
            var length = (int)ship.Size;
            if (length <= 0)
                return false;

            for (var segment = 0; segment < length; segment++)
            {
                var row = startCell.Major + (ship.Orientation == ShipOrientation.Vertical ? segment : 0);
                var col = startCell.Minor + (ship.Orientation == ShipOrientation.Horizontal ? segment : 0);
                if (row < 0 || row >= BoardSize || col < 0 || col >= BoardSize)
                    return false;

                var index = row * BoardSize + col;
                if (occupancy[index])
                    return false;

                if (!validateNeighbors)
                    continue;

                for (var neighborRow = row - 1; neighborRow <= row + 1; neighborRow++)
                {
                    if (neighborRow < 0 || neighborRow >= BoardSize)
                        continue;

                    for (var neighborCol = col - 1; neighborCol <= col + 1; neighborCol++)
                    {
                        if (neighborCol < 0 || neighborCol >= BoardSize)
                            continue;

                        var neighborIndex = neighborRow * BoardSize + neighborCol;
                        if (occupancy[neighborIndex])
                            return false;
                    }
                }
            }

            for (var segment = 0; segment < length; segment++)
            {
                var row = startCell.Major + (ship.Orientation == ShipOrientation.Vertical ? segment : 0);
                var col = startCell.Minor + (ship.Orientation == ShipOrientation.Horizontal ? segment : 0);
                occupancy[row * BoardSize + col] = true;
            }

            return true;
        }

        private static bool ContainsCell(in BattleshipPlacementShipState ship, CellId cellId)
        {
            if (!ship.StartCell.HasValue)
                return false;

            var start = ship.StartCell.Value;
            var length = (int)ship.Size;

            for (var segment = 0; segment < length; segment++)
            {
                var row = start.Major + (ship.Orientation == ShipOrientation.Vertical ? segment : 0);
                var col = start.Minor + (ship.Orientation == ShipOrientation.Horizontal ? segment : 0);
                if (row == cellId.Major && col == cellId.Minor)
                    return true;
            }

            return false;
        }

        private void ClearLastError() => _lastErrorKey = null;

        private void PublishChanged() => _changed.OnNext(Unit.Default);

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(BattleshipPlacementService));
        }
    }

    public interface IBattleshipPlacementUiController : IDisposable
    {
        void Bind();
        void Unbind();
    }

    public sealed class BattleshipPlacementUiController : IBattleshipPlacementUiController
    {
        private const string PanelName = "BattleshipPlacementPanel";
        private const string StatusLabelName = "BattleshipPlacementStatusLabel";
        private const string ShipButtonPrefix = "ShipButton_";
        private const string AutoButtonKey = "Game.Battleship.Placement.AutoButton";
        private const string RotateButtonKey = "Game.Battleship.Placement.RotateButton";
        private const string RemoveButtonKey = "Game.Battleship.Placement.RemoveButton";
        private const string ReadyButtonKey = "Game.Battleship.Placement.ReadyButton";
        private const string WaitingStatusKey = "Game.Battleship.Placement.Status.WaitingOpponent";
        private const string UnavailableStatusKey = "Game.Battleship.Placement.Status.Unavailable";
        private const string PlaceAllShipsStatusKey = "Game.Battleship.Placement.Status.PlaceAllShips";
        private const string ConfirmReadyStatusKey = "Game.Battleship.Placement.Status.ConfirmReady";

        private const string AutoButtonFallback = "Auto place";
        private const string RotateButtonFallback = "Rotate";
        private const string RemoveButtonFallback = "Remove";
        private const string ReadyButtonFallback = "Ready";
        private const string WaitingStatusFallback = "Placement submitted. Waiting for opponent.";
        private const string UnavailableStatusFallback = "Placement is unavailable.";
        private const string PlaceAllShipsStatusFallback = "Place all ships.";
        private const string ConfirmReadyStatusFallback = "Press Ready to confirm placement.";

        private readonly IGameplayFieldUiAdapter _fieldUiAdapter;
        private readonly IBattleshipFieldUiAdapter? _battleshipFieldUiAdapter;
        private readonly IBattleshipPlacementService _placementService;
        private readonly IBattleshipGameplaySnapshotProvider _snapshotProvider;
        private readonly IBattleshipGameplayEventStream _eventStream;
        private readonly ILocalizationService? _localization;

        private readonly Dictionary<int, Button> _shipButtons = new();

        private CompositeDisposable? _subscriptions;
        private VisualElement? _panel;
        private Label? _statusLabel;
        private Button? _rotateButton;
        private Button? _removeButton;
        private Button? _autoButton;
        private Button? _readyButton;
        private bool _isBound;
        private bool _disposed;

        public BattleshipPlacementUiController(
            IGameplayFieldUiAdapter fieldUiAdapter,
            IBattleshipPlacementService placementService,
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
        }

        public void Bind()
        {
            ThrowIfDisposed();

            if (_isBound)
                return;

            if (!EnsurePanel())
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
            _shipButtons.Clear();

            if (_panel != null)
            {
                _panel.RemoveFromHierarchy();
                _panel = null;
            }

            _statusLabel = null;
            _rotateButton = null;
            _removeButton = null;
            _autoButton = null;
            _readyButton = null;
            _isBound = false;
            ClearPreview();
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            Unbind();
        }

        private bool EnsurePanel()
        {
            var fieldContainer = _fieldUiAdapter.FieldContainer;
            var fieldRoot = fieldContainer?.parent;
            if (fieldContainer == null || fieldRoot == null)
                return false;

            var existing = fieldRoot.Q<VisualElement>(PanelName);
            if (existing != null)
                existing.RemoveFromHierarchy();

            _panel = new VisualElement { name = PanelName };
            _panel.style.flexDirection = FlexDirection.Column;
            _panel.style.marginBottom = 8f;

            var controlsRow = new VisualElement();
            controlsRow.style.flexDirection = FlexDirection.Row;
            controlsRow.style.marginBottom = 6f;
            controlsRow.style.flexWrap = Wrap.Wrap;

            _autoButton = new Button(() => _placementService.AutoPlace());
            _rotateButton = new Button(() =>
            {
                _placementService.TryToggleSelectedOrientation();
            });
            _removeButton = new Button(() =>
            {
                _placementService.TryRemoveSelected();
            });
            _readyButton = new Button(OnReadyClicked);
            RefreshControlTexts();

            controlsRow.Add(_autoButton);
            controlsRow.Add(_rotateButton);
            controlsRow.Add(_removeButton);
            controlsRow.Add(_readyButton);

            _panel.Add(controlsRow);

            var shipsRow = new VisualElement { name = "ShipButtonsRow" };
            shipsRow.style.flexDirection = FlexDirection.Row;
            shipsRow.style.marginBottom = 6f;
            shipsRow.style.flexWrap = Wrap.Wrap;
            _panel.Add(shipsRow);

            for (var i = 0; i < _placementService.Ships.Count; i++)
            {
                var shipId = _placementService.Ships[i].ShipId;
                var button = new Button(() =>
                {
                    _placementService.TrySelectShip(shipId);
                })
                {
                    name = ShipButtonPrefix + shipId,
                };
                button.style.marginRight = 4f;
                button.style.marginBottom = 4f;

                _shipButtons[shipId] = button;
                shipsRow.Add(button);
            }

            _statusLabel = new Label { name = StatusLabelName };
            _panel.Add(_statusLabel);

            var insertIndex = fieldRoot.IndexOf(fieldContainer);
            if (insertIndex < 0)
                fieldRoot.Add(_panel);
            else
                fieldRoot.Insert(insertIndex, _panel);

            return true;
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
            if (_panel == null)
                return;

            var phase = _snapshotProvider.Phase;
            var isVisible = phase == BattleshipPhase.Placement || phase == BattleshipPhase.Waiting;
            _panel.style.display = isVisible ? DisplayStyle.Flex : DisplayStyle.None;

            if (!isVisible)
                return;

            var canEdit = _placementService.CanEdit;
            var hasSelection = _placementService.SelectedShipId.HasValue;

            RefreshControlTexts();

            _autoButton?.SetEnabled(canEdit);
            _rotateButton?.SetEnabled(canEdit && hasSelection);
            _removeButton?.SetEnabled(canEdit && hasSelection);
            _readyButton?.SetEnabled(canEdit && _placementService.IsReadyToConfirm);

            foreach (var ship in _placementService.Ships)
            {
                if (!_shipButtons.TryGetValue(ship.ShipId, out var button))
                    continue;

                var prefix = _placementService.SelectedShipId == ship.ShipId
                    ? "▶ "
                    : ship.IsPlaced
                        ? "✓ "
                        : string.Empty;

                button.text = prefix + ShipLabel(ship.Size, ship.ShipId);
                button.SetEnabled(canEdit);
            }

            if (_statusLabel != null)
                _statusLabel.text = ResolveStatusText(phase);

            RenderPreview();
        }

        private string ResolveStatusText(BattleshipPhase phase)
        {
            if (!_placementService.CanEdit)
            {
                return phase == BattleshipPhase.Waiting
                    ? ResolvePlacementText(WaitingStatusKey, WaitingStatusFallback)
                    : ResolvePlacementText(UnavailableStatusKey, UnavailableStatusFallback);
            }

            if (!string.IsNullOrWhiteSpace(_placementService.LastErrorKey))
                return ResolvePlacementText(_placementService.LastErrorKey!, _placementService.LastErrorKey!);

            if (!_placementService.IsReadyToConfirm)
                return ResolvePlacementText(PlaceAllShipsStatusKey, PlaceAllShipsStatusFallback);

            return ResolvePlacementText(ConfirmReadyStatusKey, ConfirmReadyStatusFallback);
        }

        private void RefreshControlTexts()
        {
            if (_autoButton != null)
                _autoButton.text = ResolvePlacementText(AutoButtonKey, AutoButtonFallback);

            if (_rotateButton != null)
                _rotateButton.text = ResolvePlacementText(RotateButtonKey, RotateButtonFallback);

            if (_removeButton != null)
                _removeButton.text = ResolvePlacementText(RemoveButtonKey, RemoveButtonFallback);

            if (_readyButton != null)
                _readyButton.text = ResolvePlacementText(ReadyButtonKey, ReadyButtonFallback);
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

        private void RenderPreview()
        {
            ClearPreview();

            foreach (var ship in _placementService.Ships)
            {
                if (!ship.IsPlaced || !ship.StartCell.HasValue)
                    continue;

                var startCell = ship.StartCell.Value;
                var length = (int)ship.Size;

                for (var segment = 0; segment < length; segment++)
                {
                    var row = startCell.Major + (ship.Orientation == ShipOrientation.Vertical ? segment : 0);
                    var col = startCell.Minor + (ship.Orientation == ShipOrientation.Horizontal ? segment : 0);
                    if (row < 0 || row >= 10 || col < 0 || col >= 10)
                        continue;

                    var cellId = new CellId(row, col);
                    if (TryGetPlacementCellView(cellId, out var markLabel) && markLabel != null)
                        markLabel.text = "■";
                }
            }
        }

        private void ClearPreview()
        {
            for (var row = 0; row < 10; row++)
            {
                for (var col = 0; col < 10; col++)
                {
                    var cellId = new CellId(row, col);
                    if (TryGetPlacementCellView(cellId, out var markLabel) && markLabel != null)
                        markLabel.text = string.Empty;
                }
            }
        }

        private Observable<CellId> ResolvePlacementCellClicks()
        {
            if (_battleshipFieldUiAdapter != null && _battleshipFieldUiAdapter.HasOwnBoard)
                return _battleshipFieldUiAdapter.OwnBoardCellClicks;

            return _fieldUiAdapter.CellClicks;
        }

        private bool TryGetPlacementCellView(CellId cellId, out Label? markLabel)
        {
            markLabel = null;

            if (_battleshipFieldUiAdapter != null
                && _battleshipFieldUiAdapter.HasOwnBoard
                && _battleshipFieldUiAdapter.TryGetOwnCellView(cellId, out _, out var ownMarkLabel)
                && ownMarkLabel != null)
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
