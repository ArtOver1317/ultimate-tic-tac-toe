#nullable enable

using System;
using System.Collections.Generic;
using R3;
using Runtime.GameModes.Wizard;
using Runtime.GameModes.Wizard.Online;
using Runtime.Gameplay.ECS;
using Runtime.Games.TicTacToe.Moves;
using Runtime.Infrastructure.Logging;
using UnityEngine.UIElements;

namespace Runtime.Games.Battleship
{
    public interface IBattleshipFieldUiAdapter
    {
        Observable<CellId> OwnBoardCellClicks { get; }

        bool HasOwnBoard { get; }

        bool TryGetOwnCell(CellId id, out VisualElement cellRoot);

        bool TryGetOwnCellView(CellId id, out VisualElement cellRoot, out Label markLabel);
    }

    public sealed class BattleshipBoardsBinder : IDisposable
    {
        private const int DefaultBoardSize = 10;

        private const string ShipClass = "battleship-mark--ship";
        private const string MissClass = "battleship-mark--miss";
        private const string HitClass = "battleship-mark--hit";
        private const string SunkClass = "battleship-mark--sunk";

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

            var localSlot = ResolveLocalSlot();
            var opponentMarks = _snapshotProvider.GetOpponentMarks(localSlot) ?? Array.Empty<BattleshipCellMark>();
            var ownMarks = _snapshotProvider.GetOwnMarks(localSlot) ?? Array.Empty<BattleshipCellMark>();

            var cellCount = Math.Max(opponentMarks.Count, ownMarks.Count);
            if (cellCount <= 0)
                return;

            var boardSize = ResolveBoardSize(cellCount);
            var shipOccupancy = BuildShipOccupancy(localSlot, cellCount, boardSize);

            RenderOpponentBoard(opponentMarks, cellCount, boardSize);
            RenderOwnBoard(ownMarks, shipOccupancy, cellCount, boardSize);
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

                if (!_battleshipFieldUiAdapter.TryGetOwnCellView(cellId, out _, out var markLabel) || markLabel == null)
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
            {
                return occupancy;
            }

            for (var shipIndex = 0; shipIndex < layout.Ships.Count; shipIndex++)
            {
                var ship = layout.Ships[shipIndex];
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

            return occupancy;
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
                return DefaultBoardSize;

            var root = (int)Math.Sqrt(cellCount);
            return root * root == cellCount
                ? root
                : DefaultBoardSize;
        }

        private static CellId ToCellId(int index, int boardSize)
        {
            var row = index / boardSize;
            var col = index % boardSize;
            return new CellId(row, col);
        }

        private static BattleshipCellMark GetMarkOrUnknown(IReadOnlyList<BattleshipCellMark> marks, int index)
        {
            if (marks == null || index < 0 || index >= marks.Count)
                return BattleshipCellMark.Unknown;

            return marks[index];
        }

        private static (string text, string? cssClass) ResolveOpponentMark(BattleshipCellMark mark) =>
            mark switch
            {
                BattleshipCellMark.Miss => ("o", MissClass),
                BattleshipCellMark.Hit => ("X", HitClass),
                BattleshipCellMark.Sunk => ("X", SunkClass),
                _ => (string.Empty, null),
            };

        private static (string text, string? cssClass) ResolveOwnMark(BattleshipCellMark mark, bool hasShip) =>
            mark switch
            {
                BattleshipCellMark.Miss => ("o", MissClass),
                BattleshipCellMark.Hit => ("X", HitClass),
                BattleshipCellMark.Sunk => ("X", SunkClass),
                _ when hasShip => ("S", ShipClass),
                _ => (string.Empty, null),
            };

        private static void ApplyMark(Label markLabel, string text, string? cssClass)
        {
            markLabel.text = text;
            markLabel.RemoveFromClassList("mark-label--x");
            markLabel.RemoveFromClassList("mark-label--o");
            markLabel.RemoveFromClassList(ShipClass);
            markLabel.RemoveFromClassList(MissClass);
            markLabel.RemoveFromClassList(HitClass);
            markLabel.RemoveFromClassList(SunkClass);

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
