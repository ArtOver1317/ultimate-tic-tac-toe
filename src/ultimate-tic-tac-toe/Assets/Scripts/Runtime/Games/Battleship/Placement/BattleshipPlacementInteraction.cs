#nullable enable

using System;
using System.Collections.Generic;
using R3;
using Runtime.GameModes.Wizard.Online;
using Runtime.Gameplay;
using Runtime.Gameplay.Shared;
using Runtime.Games.Battleship.Core;

namespace Runtime.Games.Battleship.Placement
{
    public sealed class BattleshipPlacementService : IDisposable
    {
        private const string _invalidLayoutErrorKey = "Errors.Battleship.Layout.Invalid";

        private readonly IBattleshipGameplaySnapshotProvider _snapshotProvider;
        private readonly IGameplayCommandSink _commandSink;
        private readonly IBattleshipPlacementValidator _validator;
        private readonly IBattleshipAutoPlacer _autoPlacer;
        private readonly IOnlineGameplaySessionContextStore _sessionContextStore;
        private readonly Subject<Unit> _changed = new();
        private readonly BattleshipPlacementDraft _draft = new();

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

            LocalPlayerSlot = ResolveLocalSlot();
            SyncFromSnapshot();
        }

        public Observable<Unit> Changed => _changed;
        public IReadOnlyList<BattleshipPlacementShipState> Ships => _draft.Ships;
        public int LocalPlayerSlot { get; private set; }
        public int? SelectedShipId => _draft.SelectedShipId;
        public string? LastErrorKey { get; private set; }

        public bool CanEdit
        {
            get
            {
                if (_disposed)
                    return false;

                var phase = _snapshotProvider.Phase;
                
                if (phase is BattleshipPhase.Battle or BattleshipPhase.Finished)
                    return false;

                return !_snapshotProvider.IsPlacementConfirmed(LocalPlayerSlot);
            }
        }

        public bool IsReadyToConfirm => 
            _draft.TryBuildLayout(out var layout) && _validator.TryValidate(layout, out _);

        public void SyncFromSnapshot()
        {
            ThrowIfDisposed();

            LocalPlayerSlot = ResolveLocalSlot();

            if (_snapshotProvider.TryGetFleetLayout(LocalPlayerSlot, out var layout)
                && layout is { IsInitialized: true, Ships: { Count: FleetLayout.ExpectedShipCount } })
            {
                _draft.ApplyLayout(layout);
                PublishChanged();
                return;
            }

            var phase = _snapshotProvider.Phase;
            
            if (phase == BattleshipPhase.Placement)
            {
                _draft.ResetToDock();
                PublishChanged();
            }
        }

        public bool TrySelectShip(int shipId)
        {
            ThrowIfDisposed();

            if (!_draft.TrySelectShip(shipId))
                return false;

            PublishChanged();
            return true;
        }

        public void ClearSelection()
        {
            ThrowIfDisposed();

            if (!_draft.ClearSelection())
                return;

            PublishChanged();
        }

        public bool TryToggleSelectedOrientation()
        {
            ThrowIfDisposed();

            if (!_draft.TryGetSelectedShip(out var ship))
                return false;

            var toggled = ship.Orientation == ShipOrientation.Horizontal
                ? ShipOrientation.Vertical
                : ShipOrientation.Horizontal;

            return ship.IsPlaced
                ? TryTogglePlacedShipOrientation(ship, toggled)
                : TryToggleDockedShipOrientation(ship, toggled);
        }

        private bool TryToggleDockedShipOrientation(BattleshipPlacementShipState ship, ShipOrientation toggled)
        {
            if (!_draft.TrySetShipOrientation(ship.ShipId, toggled))
                return false;

            ClearLastError();
            PublishChanged();
            return true;
        }

        private bool TryTogglePlacedShipOrientation(BattleshipPlacementShipState ship, ShipOrientation toggled)
        {
            if (!CanEdit || !ship.StartCell.HasValue)
                return false;

            if (_draft.TryPlaceShip(ship.ShipId, ship.StartCell.Value, toggled))
            {
                ClearLastError();
                PublishChanged();
                return true;
            }

            LastErrorKey = _invalidLayoutErrorKey;
            PublishChanged();
            return false;
        }

        public bool TryPlaceSelected(CellId startCell)
        {
            ThrowIfDisposed();

            if (!_draft.TryGetSelectedShip(out var ship))
                return false;

            if (!CanEdit)
                return false;

            if (_draft.TryPlaceShip(ship.ShipId, startCell, ship.Orientation))
            {
                ClearLastError();
                PublishChanged();
                return true;
            }

            LastErrorKey = _invalidLayoutErrorKey;
            PublishChanged();
            return false;
        }

        public bool TryRemoveSelected()
        {
            ThrowIfDisposed();

            if (!_draft.TryGetSelectedShip(out var ship))
                return false;

            if (!_draft.TryRemoveShip(ship.ShipId))
                return false;

            ClearLastError();
            PublishChanged();
            return true;
        }

        public void AutoPlace()
        {
            ThrowIfDisposed();

            if (!CanEdit)
                return;

            var seed = unchecked(Environment.TickCount * 397) ^ LocalPlayerSlot;
            var layout = _autoPlacer.Generate(seed);
            _draft.ApplyLayout(layout);
            ClearLastError();
            PublishChanged();
        }

        public bool TryConfirmReady()
        {
            ThrowIfDisposed();

            if (!CanEdit)
                return false;

            if (!TryGetReadyLayout(out var layout, out var errorKey))
            {
                LastErrorKey = string.IsNullOrWhiteSpace(errorKey)
                    ? "Errors.Battleship.Layout.Invalid"
                    : errorKey;
                
                PublishChanged();
                return false;
            }

            _commandSink.SubmitCommand(new SubmitPlacementCommand(LocalPlayerSlot, layout));
            ClearLastError();
            PublishChanged();
            return true;
        }

        public bool TryGetShipAt(CellId cellId, out int shipId)
        {
            ThrowIfDisposed();
            return _draft.TryGetShipAt(cellId, out shipId);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _changed.OnCompleted();
            _changed.Dispose();
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

        private void ClearLastError() => LastErrorKey = null;

        private void PublishChanged() => _changed.OnNext(Unit.Default);

        private bool TryGetReadyLayout(out FleetLayout layout, out string? errorKey)
        {
            errorKey = null;

            if (!_draft.TryBuildLayout(out layout))
            {
                errorKey = _invalidLayoutErrorKey;
                return false;
            }

            if (_validator.TryValidate(layout, out errorKey))
                return true;

            if (string.IsNullOrWhiteSpace(errorKey))
                errorKey = _invalidLayoutErrorKey;

            return false;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(BattleshipPlacementService));
        }
    }
}