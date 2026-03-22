#nullable enable

using System;
using System.Collections.Generic;
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

        private readonly IGameplayFieldUiAdapter _fieldUiAdapter;
        private readonly IBattleshipFieldUiAdapter _battleshipFieldUiAdapter;
        private readonly IBattleshipGameplaySnapshotProvider _snapshotProvider;
        private readonly IBattleshipGameplayEventStream _eventStream;
        private readonly IOnlineGameplaySessionContextStore _sessionContextStore;

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

            RenderOpponentBoard(state.OpponentMarks, state.CellCount, state.BoardSize);
            RenderOwnBoard(state.OwnMarks, state.ShipOccupancy, state.CellCount, state.BoardSize);
        }

        private void UpdateOpponentBoardVisibility()
        {
            var phase = _snapshotProvider.Phase;
            var isPlacing = phase == BattleshipPhase.Placement || phase == BattleshipPhase.Waiting;
            var opponentBoard = _fieldUiAdapter.FieldContainer?.Q<VisualElement>(className: "battleship-board--opponent");
            
            if (opponentBoard != null)
                opponentBoard.style.display = isPlacing ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private void UpdateTurnIndicator()
        {
            var phase = _snapshotProvider.Phase;
            var isBattle = phase == BattleshipPhase.Battle;
            var localSlot = ResolveLocalSlot();
            var isMyTurn = isBattle && _snapshotProvider.ActivePlayerSlot == localSlot;

            var opponentBoard = _fieldUiAdapter.FieldContainer?.Q<VisualElement>(className: "battleship-board--opponent");
            var ownBoard = _fieldUiAdapter.FieldContainer?.Q<VisualElement>(className: "battleship-board--own");

            opponentBoard?.EnableInClassList("battleship-board--active-attack", isMyTurn);
            ownBoard?.EnableInClassList("battleship-board--under-attack", isBattle && !isMyTurn);
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

            var boardSize = ResolveBoardSize(cellCount);
            var shipOccupancy = BuildShipOccupancy(localSlot, cellCount, boardSize);
            state = new BoardRenderState(opponentMarks, ownMarks, shipOccupancy, cellCount, boardSize);
            return true;
        }

        private void RenderOpponentBoard(IReadOnlyList<BattleshipCellMark> marks, int cellCount, int boardSize)
        {
            for (var index = 0; index < cellCount; index++)
            {
                var mark = GetMarkOrUnknown(marks, index);
                var cellId = ToCellId(index, boardSize);

                if (!_fieldUiAdapter.TryGetCellView(cellId, out var cellRoot, out var markLabel) || markLabel == null)
                    continue;

                var (text, cssClass) = BattleshipBoardCellRenderer.ResolveOpponentMark(mark);
                BattleshipBoardCellRenderer.ApplyMark(markLabel, text, cssClass);
                BattleshipBoardCellRenderer.ApplyOpponentCellClass(cellRoot, mark);
            }
        }

        private void RenderOwnBoard(
            IReadOnlyList<BattleshipCellMark> marks,
            bool[] shipOccupancy,
            int cellCount,
            int boardSize)
        {
            for (var index = 0; index < cellCount; index++)
            {
                var mark = GetMarkOrUnknown(marks, index);
                var hasShip = index < shipOccupancy.Length && shipOccupancy[index];
                var cellId = ToCellId(index, boardSize);

                if (!_battleshipFieldUiAdapter.TryGetOwnCellView(cellId, out var cellRoot, out var markLabel))
                    continue;

                var (text, markCssClass) = BattleshipBoardCellRenderer.ResolveOwnMark(mark, hasShip);
                BattleshipBoardCellRenderer.ApplyMark(markLabel, text, markCssClass);
                BattleshipBoardCellRenderer.ApplyOwnCellClass(cellRoot, mark, hasShip);
            }
        }

        private bool[] BuildShipOccupancy(int localSlot, int cellCount, int boardSize)
        {
            var occupancy = new bool[cellCount];

            if (!_snapshotProvider.TryGetFleetLayout(localSlot, out var layout)
                || !layout.IsInitialized
                || layout.Ships == null)
                return occupancy;

            for (var shipIndex = 0; shipIndex < layout.Ships.Count; shipIndex++)
            {
                MarkShipOccupancy(layout.Ships[shipIndex], occupancy, boardSize);
            }

            return occupancy;
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

        private int ResolveLocalSlot()
        {
            var snapshot = _sessionContextStore.Snapshot;
            
            if (!snapshot.IsOnlineDirectInvite)
                return PlayerSlotMapping.SlotX;

            return snapshot.IsHost
                ? PlayerSlotMapping.SlotX
                : PlayerSlotMapping.SlotO;
        }

        private static int ResolveBoardSize(int cellCount)
        {
            if (cellCount <= 0)
                return BattleshipEcsBoard.DefaultBoardSize;

            var root = (int)Math.Sqrt(cellCount);
            
            return root * root == cellCount
                ? root
                : BattleshipEcsBoard.DefaultBoardSize;
        }

        private static CellId ToCellId(int index, int boardSize)
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

        private void ClearPlacementClasses()
        {
            const int boardSize = BattleshipEcsBoard.DefaultBoardSize;

            for (var row = 0; row < boardSize; row++)
            {
                for (var col = 0; col < boardSize; col++)
                {
                    var cellId = new CellId(row, col);

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