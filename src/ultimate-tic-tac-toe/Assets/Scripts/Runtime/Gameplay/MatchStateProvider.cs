#nullable enable

using System;
using System.Collections.Generic;
using R3;
using Runtime.Gameplay.ECS;
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

        private bool _disposed;

        // IGameplayEventStream
        public Observable<CellChangedEvent> CellChanged => _cellChanged;
        public Observable<LastMoveChangedEvent> LastMoveChanged => _lastMoveChanged;
        public Observable<CurrentPlayerChangedEvent> CurrentPlayerChanged => _currentPlayerChanged;
        public Observable<CommandRejectedEvent> CommandRejected => _commandRejected;
        public Observable<RoundFinishedEvent> RoundFinished => _roundFinished;
        public Observable<AllowedMajorsChangedEvent> AllowedMajorsChanged => _allowedMajorsChanged;
        public Observable<MiniBoardStatusChangedEvent> MiniBoardStatusChanged => _miniBoardStatusChanged;

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
                evt => _miniBoardStatusChanged.OnNext(evt));
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

            _cellChanged.Dispose();
            _lastMoveChanged.Dispose();
            _currentPlayerChanged.Dispose();
            _commandRejected.Dispose();
            _roundFinished.Dispose();
            _allowedMajorsChanged.Dispose();
            _miniBoardStatusChanged.Dispose();
        }
    }
}
