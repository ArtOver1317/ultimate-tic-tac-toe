#nullable enable

using System;
using System.Collections.Generic;
using R3;
using Runtime.Gameplay.ECS;
using Runtime.Games.Battleship;
using Runtime.Games.Battleship.ECS;
using Runtime.Games.TicTacToe.ECS;
using Runtime.Games.TicTacToe.Moves;
using Runtime.Games.TicTacToe.Ultimate;
using Runtime.Games.TicTacToe.Ultimate.Rules;
using Scellecs.Morpeh;
using CellId = Runtime.Games.TicTacToe.Moves.CellId;

namespace Runtime.Gameplay
{
    /// <summary>
    /// Bridges ECS World and UI/ViewModel via ISP interfaces (ADR-4).
    /// Creates R3 Subjects, wires them into <see cref="EventPublishSystem"/> callbacks.
    /// Reads ECS state for snapshot queries.
    /// </summary>
    public sealed class MatchStateProvider : IMatchStateProvider
        , IUltimateGameplayEventStream
        , IUltimateGameplaySnapshotProvider
        , IBattleshipGameplayEventStream
        , IBattleshipGameplaySnapshotProvider
        , IBattleshipRecoveryStateApplier
    {
        private readonly CommandQueue _commandQueue;
        private readonly MatchEcsLifecycleService _lifecycle;
        private readonly EventPublishSystem _eventPublishSystem;

        // R3 Subjects — hot-path event streams
        private readonly Subject<CellChangedEvent> _cellChanged = new();
        private readonly Subject<LastMoveChangedEvent> _lastMoveChanged = new();
        private readonly Subject<CurrentPlayerChangedEvent> _currentPlayerChanged = new();
        private readonly Subject<CommandRejectedEvent> _commandRejected = new();
        private readonly Subject<RoundFinishedEvent> _roundFinished = new();
        private readonly Subject<AllowedMajorsChangedEvent> _allowedMajorsChanged = new();
        private readonly Subject<MiniBoardStatusChangedEvent> _miniBoardStatusChanged = new();
        private readonly Subject<BattleshipPhaseChangedEvent> _battleshipPhaseChanged = new();
        private readonly Subject<BattleshipMarksChangedEvent> _battleshipMarksChanged = new();

        private bool _disposed;
        private static readonly BattleshipCellMark[] UnknownBattleshipMarks = CreateUnknownMarks(BattleshipEcsBoard.DefaultBoardSize);

        // IGameplayEventStream
        public Observable<CellChangedEvent> CellChanged => _cellChanged;
        public Observable<LastMoveChangedEvent> LastMoveChanged => _lastMoveChanged;
        public Observable<CurrentPlayerChangedEvent> CurrentPlayerChanged => _currentPlayerChanged;
        public Observable<CommandRejectedEvent> CommandRejected => _commandRejected;
        public Observable<RoundFinishedEvent> RoundFinished => _roundFinished;
        public Observable<AllowedMajorsChangedEvent> AllowedMajorsChanged => _allowedMajorsChanged;
        public Observable<MiniBoardStatusChangedEvent> MiniBoardStatusChanged => _miniBoardStatusChanged;
        public Observable<BattleshipPhaseChangedEvent> PhaseChanged => _battleshipPhaseChanged;
        public Observable<BattleshipMarksChangedEvent> MarksChanged => _battleshipMarksChanged;

        // IMatchStateProvider
        public bool IsMatchActive => _lifecycle.IsActive;

        public MatchStateProvider(
            CommandQueue commandQueue,
            MatchEcsLifecycleService lifecycle,
            EventPublishSystem eventPublishSystem)
        {
            _commandQueue = commandQueue ?? throw new ArgumentNullException(nameof(commandQueue));
            _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));

            if (eventPublishSystem == null)
                throw new ArgumentNullException(nameof(eventPublishSystem));

            _eventPublishSystem = eventPublishSystem;

            // Wire Subject.OnNext as event callbacks — breaks circular DI dependency
            eventPublishSystem.SetCallbacks(
                evt => _cellChanged.OnNext(evt),
                evt => _lastMoveChanged.OnNext(evt),
                evt => _currentPlayerChanged.OnNext(evt),
                evt => _commandRejected.OnNext(evt),
                evt => _roundFinished.OnNext(evt),
                evt => _allowedMajorsChanged.OnNext(evt),
                evt => _miniBoardStatusChanged.OnNext(evt),
                evt => _battleshipPhaseChanged.OnNext(evt),
                evt => _battleshipMarksChanged.OnNext(evt));
        }

        // IGameplayCommandSink

        public void SubmitCommand(IGameplayCommand command)
        {
            if (command == null)
                throw new ArgumentNullException(nameof(command));

            if (!_lifecycle.IsActive)
            {
                // ADR-2: command sink exists only during active match; reject immediately
                _commandRejected.OnNext(new CommandRejectedEvent(
                    command.CommandType,
                    new CommandRejection(GameplayRejectionReason.MatchNotActive)));
                return;
            }

            _commandQueue.Enqueue(command);

            // CONTRACT: SubmitCommand is the single tick driver for turn-based play.
            // Callers must NOT call Tick() after SubmitCommand — it is handled here.
            // DeferredEventScheduler ensures events fire next frame (ADR-5 re-entrancy safety).
            _lifecycle.Tick();
        }

        // IGameplaySnapshotProvider

        public int GetCellSlot(CellId cellId)
        {
            if (!_lifecycle.IsActive || _lifecycle.ActiveRegistrar == null)
                return -1;

            return _lifecycle.ActiveRegistrar.GetCellSlot(_lifecycle.World, _lifecycle.MatchEntity, cellId);
        }

        public IReadOnlyList<CellSnapshot> GetAllCells()
        {
            if (!_lifecycle.IsActive || _lifecycle.ActiveRegistrar == null)
                return Array.Empty<CellSnapshot>();

            return _lifecycle.ActiveRegistrar.GetAllCells(_lifecycle.World, _lifecycle.MatchEntity);
        }

        public long CommandSequence
        {
            get
            {
                if (!_lifecycle.IsActive)
                    return -1;

                var world = _lifecycle.World;
                var entity = _lifecycle.MatchEntity;

                var seqStash = world.GetStash<CommandSequenceComponent>();
                if (!seqStash.Has(entity))
                    return -1;

                return seqStash.Get(entity).Value;
            }
        }

        public int ActivePlayerSlot
        {
            get
            {
                if (!_lifecycle.IsActive)
                    return 0;

                var world = _lifecycle.World;
                var entity = _lifecycle.MatchEntity;

                var playersStash = world.GetStash<PlayersComponent>();
                if (!playersStash.Has(entity))
                    return 0;

                return playersStash.Get(entity).ActivePlayerSlot;
            }
        }

        public GameStatus CurrentStatus
        {
            get
            {
                if (!_lifecycle.IsActive)
                    return GameStatus.InProgress;

                var world = _lifecycle.World;
                var entity = _lifecycle.MatchEntity;

                var statusStash = world.GetStash<MatchStatusComponent>();
                if (!statusStash.Has(entity))
                    return GameStatus.InProgress;

                return statusStash.Get(entity).Status;
            }
        }

        public int? WinnerSlot
        {
            get
            {
                if (!_lifecycle.IsActive)
                    return null;

                var world = _lifecycle.World;
                var entity = _lifecycle.MatchEntity;

                var statusStash = world.GetStash<MatchStatusComponent>();
                if (!statusStash.Has(entity))
                    return null;

                return statusStash.Get(entity).WinnerSlot;
            }
        }

        public CellId? LastMove
        {
            get
            {
                if (!_lifecycle.IsActive)
                    return null;

                var world = _lifecycle.World;
                var entity = _lifecycle.MatchEntity;

                var lastMoveStash = world.GetStash<LastMoveComponent>();
                if (!lastMoveStash.Has(entity))
                    return null;

                ref var component = ref lastMoveStash.Get(entity);
                return component.HasValue ? component.CellId : null;
            }
        }

        public BattleshipPhase Phase
        {
            get
            {
                if (!_lifecycle.IsActive)
                    return BattleshipPhase.Placement;

                var world = _lifecycle.World;
                var entity = _lifecycle.MatchEntity;
                var stateStash = world.GetStash<BattleshipStateComponent>();
                if (!stateStash.Has(entity))
                    return BattleshipPhase.Placement;

                return stateStash.Get(entity).Phase;
            }
        }

        public IReadOnlyList<BattleshipCellMark> GetOpponentMarks(int viewerSlot)
        {
            if (!TryGetBattleshipState(out _, out _, out var state, out var players))
                return UnknownBattleshipMarks;

            if (!BattleshipEcsBoard.TryResolvePlayerIndex(players, viewerSlot, out var viewerIndex))
                return CreateUnknownMarks(state.BoardSize);

            var targetIndex = viewerIndex == 0 ? 1 : 0;
            var shots = viewerIndex == 0 ? state.Player0Shots : state.Player1Shots;
            var targetShips = targetIndex == 0 ? state.Player0Ships : state.Player1Ships;
            var targetFleet = targetIndex == 0 ? state.Player0Fleet : state.Player1Fleet;
            return BuildMarks(state.BoardSize, shots, targetShips, targetFleet);
        }

        public bool IsPlacementConfirmed(int playerSlot)
        {
            if (!TryGetBattleshipState(out _, out _, out var state, out var players))
                return false;

            if (!BattleshipEcsBoard.TryResolvePlayerIndex(players, playerSlot, out var playerIndex))
                return false;

            return playerIndex == 0
                ? state.Player0Placed
                : playerIndex == 1 && state.Player1Placed;
        }

        public bool TryGetFleetLayout(int playerSlot, out FleetLayout layout)
        {
            layout = default;

            if (!TryGetBattleshipState(out _, out _, out var state, out var players))
                return false;

            if (!BattleshipEcsBoard.TryResolvePlayerIndex(players, playerSlot, out var playerIndex))
                return false;

            var fleet = playerIndex == 0 ? state.Player0Fleet : state.Player1Fleet;
            if (fleet == null || fleet.Length == 0)
                return false;

            var copy = new ShipPlacement[fleet.Length];
            Array.Copy(fleet, copy, fleet.Length);

            layout = new FleetLayout(Array.AsReadOnly(copy));
            return true;
        }

        public bool TryGetConsecutiveTimeouts(out int player0ConsecutiveTimeouts, out int player1ConsecutiveTimeouts)
        {
            player0ConsecutiveTimeouts = 0;
            player1ConsecutiveTimeouts = 0;

            if (!TryGetBattleshipState(out _, out _, out var state, out _))
                return false;

            player0ConsecutiveTimeouts = state.Player0ConsecutiveTimeouts;
            player1ConsecutiveTimeouts = state.Player1ConsecutiveTimeouts;
            return true;
        }

        public IReadOnlyList<BattleshipCellMark> GetOwnMarks(int viewerSlot)
        {
            if (!TryGetBattleshipState(out _, out _, out var state, out var players))
                return UnknownBattleshipMarks;

            if (!BattleshipEcsBoard.TryResolvePlayerIndex(players, viewerSlot, out var viewerIndex))
                return CreateUnknownMarks(state.BoardSize);

            var opponentIndex = viewerIndex == 0 ? 1 : 0;
            var shotsReceived = opponentIndex == 0 ? state.Player0Shots : state.Player1Shots;
            var ownShips = viewerIndex == 0 ? state.Player0Ships : state.Player1Ships;
            var ownFleet = viewerIndex == 0 ? state.Player0Fleet : state.Player1Fleet;
            return BuildMarks(state.BoardSize, shotsReceived, ownShips, ownFleet);
        }

        public ulong Epoch
        {
            get
            {
                if (!_lifecycle.IsActive)
                    return 0UL;

                var world = _lifecycle.World;
                var entity = _lifecycle.MatchEntity;
                var epochStash = world.GetStash<UltimateEpochComponent>();
                if (!epochStash.Has(entity))
                    return 0UL;

                return epochStash.Get(entity).Value;
            }
        }

        public AllowedMajors CurrentAllowedMajors
        {
            get
            {
                if (!_lifecycle.IsActive)
                    return AllowedMajors.None;

                var world = _lifecycle.World;
                var entity = _lifecycle.MatchEntity;
                var allowedStash = world.GetStash<UltimateAllowedMajorsComponent>();
                if (!allowedStash.Has(entity))
                    return AllowedMajors.None;

                return allowedStash.Get(entity).Value;
            }
        }

        public UltimateMatchResult CurrentMatch
        {
            get
            {
                if (!_lifecycle.IsActive)
                    return new UltimateMatchResult(Runtime.Games.TicTacToe.Rules.GameStatus.InProgress, PlayerMark.None, null);

                var world = _lifecycle.World;
                var entity = _lifecycle.MatchEntity;

                var statusStash = world.GetStash<MatchStatusComponent>();
                if (!statusStash.Has(entity))
                    return new UltimateMatchResult(Runtime.Games.TicTacToe.Rules.GameStatus.InProgress, PlayerMark.None, null);

                var ecsStatus = statusStash.Get(entity);
                var winLineStash = world.GetStash<UltimateBigBoardWinLineComponent>();

                var status = ecsStatus.Status switch
                {
                    Runtime.Gameplay.ECS.GameStatus.Win => Runtime.Games.TicTacToe.Rules.GameStatus.Win,
                    Runtime.Gameplay.ECS.GameStatus.Draw => Runtime.Games.TicTacToe.Rules.GameStatus.Draw,
                    Runtime.Gameplay.ECS.GameStatus.InProgress => Runtime.Games.TicTacToe.Rules.GameStatus.InProgress,
                    Runtime.Gameplay.ECS.GameStatus.Timeout => Runtime.Games.TicTacToe.Rules.GameStatus.Timeout,
                    _ => throw new ArgumentOutOfRangeException(nameof(ecsStatus.Status), ecsStatus.Status, null),
                };

                if (status != Runtime.Games.TicTacToe.Rules.GameStatus.Win)
                    return new UltimateMatchResult(status, PlayerMark.None, null);

                var winner = ecsStatus.WinnerSlot.HasValue
                    ? PlayerSlotMapping.SlotToMark(ecsStatus.WinnerSlot.Value)
                    : PlayerMark.None;

                UltimateBigBoardWinLine? winLine = null;
                if (winLineStash.Has(entity) && winLineStash.Get(entity).HasValue)
                    winLine = winLineStash.Get(entity).Value;

                return new UltimateMatchResult(status, winner, winLine);
            }
        }

        public void CopyMiniBoardsTo(Span<MiniBoardStatus> destination)
        {
            if (destination.Length < 9)
                throw new ArgumentException("destination must be >= 9", nameof(destination));

            for (var i = 0; i < 9; i++)
                destination[i] = MiniBoardStatus.InProgress;

            if (!_lifecycle.IsActive)
                return;

            var world = _lifecycle.World;
            var entity = _lifecycle.MatchEntity;
            var miniBoardsStash = world.GetStash<UltimateMiniBoardsComponent>();
            if (!miniBoardsStash.Has(entity) || miniBoardsStash.Get(entity).Statuses == null)
                return;

            var source = miniBoardsStash.Get(entity).Statuses;
            var count = source.Length < 9 ? source.Length : 9;
            for (var i = 0; i < count; i++)
                destination[i] = source[i];
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _eventPublishSystem.ClearCallbacks();

            _cellChanged.OnCompleted();
            _lastMoveChanged.OnCompleted();
            _currentPlayerChanged.OnCompleted();
            _commandRejected.OnCompleted();
            _roundFinished.OnCompleted();
            _allowedMajorsChanged.OnCompleted();
            _miniBoardStatusChanged.OnCompleted();
            _battleshipPhaseChanged.OnCompleted();
            _battleshipMarksChanged.OnCompleted();

            _cellChanged.Dispose();
            _lastMoveChanged.Dispose();
            _currentPlayerChanged.Dispose();
            _commandRejected.Dispose();
            _roundFinished.Dispose();
            _allowedMajorsChanged.Dispose();
            _miniBoardStatusChanged.Dispose();
            _battleshipPhaseChanged.Dispose();
            _battleshipMarksChanged.Dispose();
        }

        public bool TryApplyRecoveryState(in BattleshipRecoveryState state)
        {
            if (!TryGetBattleshipState(out var world, out var entity, out _, out _))
                return false;

            var stateStash = world.GetStash<BattleshipStateComponent>();
            var playersStash = world.GetStash<PlayersComponent>();
            var statusStash = world.GetStash<MatchStatusComponent>();

            if (!stateStash.Has(entity) || !playersStash.Has(entity) || !statusStash.Has(entity))
                return false;

            ref var battleshipState = ref stateStash.Get(entity);
            ref var players = ref playersStash.Get(entity);
            ref var matchStatus = ref statusStash.Get(entity);

            var boardSize = battleshipState.BoardSize > 0 ? battleshipState.BoardSize : BattleshipEcsBoard.DefaultBoardSize;
            var cellCount = boardSize * boardSize;

            battleshipState.Player0Shots = BuildShotsFromMarks(state.Player0OpponentMarks, cellCount);
            battleshipState.Player1Shots = BuildShotsFromMarks(state.Player1OpponentMarks, cellCount);

            if (state.Player0Layout.HasValue)
            {
                ApplyFleetState(
                    boardSize,
                    state.Player0Layout.Value,
                    battleshipState.Player1Shots,
                    out battleshipState.Player0Fleet,
                    out battleshipState.Player0Ships,
                    out battleshipState.Player0RemainingDecks);

                battleshipState.Player0Placed = true;
            }

            if (state.Player1Layout.HasValue)
            {
                ApplyFleetState(
                    boardSize,
                    state.Player1Layout.Value,
                    battleshipState.Player0Shots,
                    out battleshipState.Player1Fleet,
                    out battleshipState.Player1Ships,
                    out battleshipState.Player1RemainingDecks);

                battleshipState.Player1Placed = true;
            }

            battleshipState.Phase = state.Phase;
            battleshipState.Player0ConsecutiveTimeouts = state.Player0ConsecutiveTimeouts;
            battleshipState.Player1ConsecutiveTimeouts = state.Player1ConsecutiveTimeouts;

            players.ActivePlayerSlot = state.ActivePlayerSlot;

            matchStatus.Status = state.FinishStatus;
            matchStatus.WinnerSlot = state.WinnerSlot;
            matchStatus.WinLine = null;

            _battleshipPhaseChanged.OnNext(new BattleshipPhaseChangedEvent(state.Phase));
            _battleshipMarksChanged.OnNext(new BattleshipMarksChangedEvent(PlayerSlotMapping.SlotX));
            _battleshipMarksChanged.OnNext(new BattleshipMarksChangedEvent(PlayerSlotMapping.SlotO));

            if (state.ActivePlayerSlot >= 0)
                _currentPlayerChanged.OnNext(new CurrentPlayerChangedEvent(state.ActivePlayerSlot));

            return true;
        }

        private bool TryGetBattleshipState(
            out Scellecs.Morpeh.World world,
            out Scellecs.Morpeh.Entity entity,
            out BattleshipStateComponent state,
            out PlayersComponent players)
        {
            world = null!;
            entity = default;
            state = default;
            players = default;

            if (!_lifecycle.IsActive)
                return false;

            world = _lifecycle.World;
            entity = _lifecycle.MatchEntity;

            var stateStash = world.GetStash<BattleshipStateComponent>();
            var playersStash = world.GetStash<PlayersComponent>();
            if (!stateStash.Has(entity) || !playersStash.Has(entity))
                return false;

            state = stateStash.Get(entity);
            players = playersStash.Get(entity);
            return true;
        }

        private static BattleshipCellMark[] BuildMarks(
            int boardSize,
            bool[]? shots,
            bool[]? ships,
            ShipPlacement[]? fleet)
        {
            var cellCount = boardSize * boardSize;
            var result = new BattleshipCellMark[cellCount];

            if (shots == null || ships == null || shots.Length < cellCount || ships.Length < cellCount)
                return result;

            for (var index = 0; index < cellCount; index++)
            {
                if (!shots[index])
                    continue;

                result[index] = ships[index]
                    ? BattleshipCellMark.Hit
                    : BattleshipCellMark.Miss;
            }

            if (fleet == null)
                return result;

            for (var shipIndex = 0; shipIndex < fleet.Length; shipIndex++)
            {
                var ship = fleet[shipIndex];
                var deckCount = (int)ship.Size;
                if (deckCount <= 0)
                    continue;

                var sunk = true;
                for (var deck = 0; deck < deckCount; deck++)
                {
                    var major = ship.StartCell.Major + (ship.Orientation == ShipOrientation.Vertical ? deck : 0);
                    var minor = ship.StartCell.Minor + (ship.Orientation == ShipOrientation.Horizontal ? deck : 0);
                    var cellId = new CellId(major, minor);
                    if (!BattleshipEcsBoard.IsInBounds(boardSize, cellId))
                    {
                        sunk = false;
                        break;
                    }

                    var index = BattleshipEcsBoard.ToIndex(boardSize, cellId);
                    if (!shots[index])
                    {
                        sunk = false;
                        break;
                    }
                }

                if (!sunk)
                    continue;

                for (var deck = 0; deck < deckCount; deck++)
                {
                    var major = ship.StartCell.Major + (ship.Orientation == ShipOrientation.Vertical ? deck : 0);
                    var minor = ship.StartCell.Minor + (ship.Orientation == ShipOrientation.Horizontal ? deck : 0);
                    var cellId = new CellId(major, minor);
                    if (!BattleshipEcsBoard.IsInBounds(boardSize, cellId))
                        continue;

                    var index = BattleshipEcsBoard.ToIndex(boardSize, cellId);
                    if (shots[index] && ships[index])
                        result[index] = BattleshipCellMark.Sunk;
                }
            }

            return result;
        }

        private static BattleshipCellMark[] CreateUnknownMarks(int boardSize)
        {
            var size = boardSize <= 0 ? BattleshipEcsBoard.DefaultBoardSize : boardSize;
            return new BattleshipCellMark[size * size];
        }

        private static bool[] BuildShotsFromMarks(IReadOnlyList<BattleshipCellMark> marks, int cellCount)
        {
            var shots = new bool[cellCount];
            if (marks == null)
                return shots;

            var count = marks.Count < cellCount ? marks.Count : cellCount;
            for (var i = 0; i < count; i++)
            {
                if (marks[i] != BattleshipCellMark.Unknown)
                    shots[i] = true;
            }

            return shots;
        }

        private static void ApplyFleetState(
            int boardSize,
            in FleetLayout layout,
            bool[]? shotsReceived,
            out ShipPlacement[] fleet,
            out bool[] ships,
            out int remainingDecks)
        {
            fleet = ToFleetArray(layout);
            ships = new bool[boardSize * boardSize];

            remainingDecks = 0;
            for (var i = 0; i < fleet.Length; i++)
            {
                var ship = fleet[i];
                var deckCount = (int)ship.Size;
                for (var deck = 0; deck < deckCount; deck++)
                {
                    var major = ship.StartCell.Major + (ship.Orientation == ShipOrientation.Vertical ? deck : 0);
                    var minor = ship.StartCell.Minor + (ship.Orientation == ShipOrientation.Horizontal ? deck : 0);
                    var cellId = new CellId(major, minor);
                    if (!BattleshipEcsBoard.IsInBounds(boardSize, cellId))
                        continue;

                    var index = BattleshipEcsBoard.ToIndex(boardSize, cellId);
                    ships[index] = true;
                    var hit = shotsReceived != null && index < shotsReceived.Length && shotsReceived[index];
                    if (!hit)
                        remainingDecks++;
                }
            }
        }

        private static ShipPlacement[] ToFleetArray(in FleetLayout layout)
        {
            if (!layout.IsInitialized || layout.Ships == null || layout.Ships.Count == 0)
                return Array.Empty<ShipPlacement>();

            var fleet = new ShipPlacement[layout.Ships.Count];
            for (var i = 0; i < layout.Ships.Count; i++)
                fleet[i] = layout.Ships[i];

            return fleet;
        }
    }
}
