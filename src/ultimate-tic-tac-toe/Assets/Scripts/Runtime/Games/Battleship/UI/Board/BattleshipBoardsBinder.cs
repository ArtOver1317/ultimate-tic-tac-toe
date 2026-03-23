#nullable enable

using System;
using R3;
using Runtime.GameModes.Wizard.Online;
using Runtime.Gameplay.Shared;
using Runtime.Games.Battleship.Core;
using Runtime.Games.Battleship.ECS.Core;
using Runtime.Gameplay;
using Runtime.Infrastructure.Logging;
using UnityEngine.UIElements;

namespace Runtime.Games.Battleship.UI.Board
{
    public sealed class BattleshipBoardsBinder : IDisposable
    {
        private const string _placedClass = "placement-ship--placed";
        private const string _selectedClass = "placement-ship--selected";

        private const string _opponentBoardClass = "battleship-board--opponent";
        private const string _ownBoardClass = "battleship-board--own";
        private const string _activeAttackClass = "battleship-board--active-attack";
        private const string _underAttackClass = "battleship-board--under-attack";

        private readonly IGameplayFieldUiAdapter _fieldUiAdapter;
        private readonly IBattleshipFieldUiAdapter _battleshipFieldUiAdapter;
        private readonly IBattleshipGameplaySnapshotProvider _snapshotProvider;
        private readonly IBattleshipGameplayEventStream _eventStream;
        private readonly IOnlineGameplaySessionContextStore _sessionContextStore;
        private readonly BattleshipBoardsRenderer _renderer;

        private CompositeDisposable? _subscriptions;
        private bool _isBound;
        private bool _disposed;

        public BattleshipBoardsBinder(
            IGameplayFieldUiAdapter fieldUiAdapter,
            IBattleshipFieldUiAdapter battleshipFieldUiAdapter,
            IBattleshipGameplaySnapshotProvider snapshotProvider,
            IBattleshipGameplayEventStream eventStream,
            IOnlineGameplaySessionContextStore sessionContextStore)
        {
            _fieldUiAdapter = fieldUiAdapter ?? throw new ArgumentNullException(nameof(fieldUiAdapter));
            _battleshipFieldUiAdapter = battleshipFieldUiAdapter ?? throw new ArgumentNullException(nameof(battleshipFieldUiAdapter));
            _snapshotProvider = snapshotProvider ?? throw new ArgumentNullException(nameof(snapshotProvider));
            _eventStream = eventStream ?? throw new ArgumentNullException(nameof(eventStream));
            _sessionContextStore = sessionContextStore ?? throw new ArgumentNullException(nameof(sessionContextStore));
            _renderer = new BattleshipBoardsRenderer(fieldUiAdapter, battleshipFieldUiAdapter);
        }

        public void Bind()
        {
            ThrowIfDisposed();

            if (_isBound)
                return;

            if (!_battleshipFieldUiAdapter.HasOwnBoard)
            {
                GameLog.Warning("[BattleshipBoardsBinder] Battleship dual-board UI is unavailable.");
                return;
            }

            _subscriptions = new CompositeDisposable();

            _eventStream.MarksChanged
                .Subscribe(_ => RefreshBoards())
                .AddTo(_subscriptions);

            _eventStream.PhaseChanged
                .Subscribe(_ => RefreshBoards())
                .AddTo(_subscriptions);

            _isBound = true;
            ClearPlacementClasses();
            RefreshBoards();
        }

        public void Unbind()
        {
            if (!_isBound)
                return;

            _subscriptions?.Dispose();
            _subscriptions = null;
            _renderer.Reset();
            _isBound = false;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            Unbind();
        }

        private void RefreshBoards()
        {
            if (!_isBound)
                return;

            UpdateOpponentBoardVisibility();
            UpdateTurnIndicator();

            if (!TryBuildRenderState(out var state))
                return;

            _renderer.RenderOpponentBoard(state.OpponentMarks, state.CellCount, state.BoardSize);
            _renderer.RenderOwnBoard(state.OwnMarks, state.ShipOccupancy, state.CellCount, state.BoardSize);
        }

        private void UpdateOpponentBoardVisibility()
        {
            var phase = _snapshotProvider.Phase;
            var isPlacing = phase == BattleshipPhase.Placement || phase == BattleshipPhase.Waiting;
            var opponentBoard = _fieldUiAdapter.FieldContainer?.Q<VisualElement>(className: _opponentBoardClass);

            if (opponentBoard != null)
                opponentBoard.style.display = isPlacing ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private void UpdateTurnIndicator()
        {
            var phase = _snapshotProvider.Phase;
            var isBattle = phase == BattleshipPhase.Battle;
            var localSlot = ResolveLocalSlot();
            var isMyTurn = isBattle && _snapshotProvider.ActivePlayerSlot == localSlot;

            var opponentBoard = _fieldUiAdapter.FieldContainer?.Q<VisualElement>(className: _opponentBoardClass);
            var ownBoard = _fieldUiAdapter.FieldContainer?.Q<VisualElement>(className: _ownBoardClass);

            opponentBoard?.EnableInClassList(_activeAttackClass, isMyTurn);
            ownBoard?.EnableInClassList(_underAttackClass, isBattle && !isMyTurn);
        }

        private bool TryBuildRenderState(out BoardRenderState state)
        {
            var localSlot = ResolveLocalSlot();
            var opponentMarks = _snapshotProvider.GetOpponentMarks(localSlot);
            var ownMarks = _snapshotProvider.GetOwnMarks(localSlot);

            var cellCount = Math.Max(opponentMarks.Count, ownMarks.Count);

            if (cellCount <= 0)
            {
                state = default;
                return false;
            }

            var boardSize = BattleshipBoardsRenderer.ResolveBoardSize(cellCount);
            var shipOccupancy = _renderer.BuildShipOccupancy(localSlot, cellCount, boardSize, _snapshotProvider);
            state = new BoardRenderState(opponentMarks, ownMarks, shipOccupancy, cellCount, boardSize);
            return true;
        }

        private int ResolveLocalSlot()
        {
            var snapshot = _sessionContextStore.Snapshot;

            if (!snapshot.IsOnlineDirectInvite)
                return PlayerSlotMapping.SlotX;

            return snapshot.IsHost ? PlayerSlotMapping.SlotX : PlayerSlotMapping.SlotO;
        }

        private void ClearPlacementClasses()
        {
            const int boardSize = BattleshipEcsBoard.DefaultBoardSize;

            for (var row = 0; row < boardSize; row++)
            {
                for (var col = 0; col < boardSize; col++)
                {
                    var cellId = BattleshipBoardsRenderer.ToCellId(row * boardSize + col, boardSize);

                    if (_battleshipFieldUiAdapter.TryGetOwnCell(cellId, out var ownCell))
                    {
                        ownCell.RemoveFromClassList(_placedClass);
                        ownCell.RemoveFromClassList(_selectedClass);
                    }

                    if (_fieldUiAdapter.TryGetCell(cellId, out var cell))
                    {
                        cell.RemoveFromClassList(_placedClass);
                        cell.RemoveFromClassList(_selectedClass);
                    }
                }
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(BattleshipBoardsBinder));
        }
    }
}