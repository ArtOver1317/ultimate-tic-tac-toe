#nullable enable

using System;
using System.Collections.Generic;
using R3;
using Runtime.GameModes.Wizard.Online;
using Runtime.Gameplay.Shared;
using Runtime.Games.Battleship.Core;
using Runtime.Games.Battleship.ECS.Core;
using Runtime.Games.TicTacToe.Moves;
using Runtime.Infrastructure.Logging;
using UnityEngine.UIElements;

namespace Runtime.Games.Battleship.UI.Board
{
    public sealed class BattleshipBoardsBinder : IDisposable
    {
        private const string _shipClass = "battleship-mark--ship";
        private const string _missClass = "battleship-mark--miss";
        private const string _hitClass = "battleship-mark--hit";
        private const string _sunkClass = "battleship-mark--sunk";

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

            if (!TryBuildRenderState(out var state))
                return;

            RenderOpponentBoard(state.OpponentMarks, state.CellCount, state.BoardSize);
            RenderOwnBoard(state.OwnMarks, state.ShipOccupancy, state.CellCount, state.BoardSize);
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

                if (!_fieldUiAdapter.TryGetCellView(cellId, out _, out var markLabel) || markLabel == null)
                    continue;

                var (text, cssClass) = ResolveOpponentMark(mark);
                ApplyMark(markLabel, text, cssClass);
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

                if (!_battleshipFieldUiAdapter.TryGetOwnCellView(cellId, out _, out var markLabel))
                    continue;

                var (text, cssClass) = ResolveOwnMark(mark, hasShip);
                ApplyMark(markLabel, text, cssClass);
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

                var index = (row * boardSize) + col;
                
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

        private static (string text, string? cssClass) ResolveOpponentMark(BattleshipCellMark mark) =>
            mark switch
            {
                BattleshipCellMark.Miss => ("o", MissClass: _missClass),
                BattleshipCellMark.Hit => ("X", HitClass: _hitClass),
                BattleshipCellMark.Sunk => ("X", SunkClass: _sunkClass),
                _ => (string.Empty, null),
            };

        private static (string text, string? cssClass) ResolveOwnMark(BattleshipCellMark mark, bool hasShip) =>
            mark switch
            {
                BattleshipCellMark.Miss => ("o", MissClass: _missClass),
                BattleshipCellMark.Hit => ("X", HitClass: _hitClass),
                BattleshipCellMark.Sunk => ("X", SunkClass: _sunkClass),
                _ when hasShip => ("S", ShipClass: _shipClass),
                _ => (string.Empty, null),
            };

        private static void ApplyMark(Label markLabel, string text, string? cssClass)
        {
            markLabel.text = text;
            markLabel.RemoveFromClassList("mark-label--x");
            markLabel.RemoveFromClassList("mark-label--o");
            markLabel.RemoveFromClassList(_shipClass);
            markLabel.RemoveFromClassList(_missClass);
            markLabel.RemoveFromClassList(_hitClass);
            markLabel.RemoveFromClassList(_sunkClass);

            if (!string.IsNullOrEmpty(cssClass))
                markLabel.AddToClassList(cssClass);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(BattleshipBoardsBinder));
        }
    }
}
