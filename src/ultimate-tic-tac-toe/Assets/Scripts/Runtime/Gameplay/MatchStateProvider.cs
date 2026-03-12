#nullable enable

using System;
using System.Collections.Generic;
using R3;
using Runtime.Gameplay.ECS.Components;
using Runtime.Gameplay.ECS.Lifecycle;
using Runtime.Gameplay.ECS.Pipeline;
using Runtime.Gameplay.ECS.Publishing;
using Runtime.Gameplay.Shared;
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
    /// Creates shared R3 Subjects, wires them into <see cref="EventPublishSystem"/> callbacks,
    /// and exposes cross-game and Ultimate snapshot reads from ECS state.
    /// </summary>
    public sealed class MatchStateProvider : IMatchStateProvider
        , ICurrentPlayerChangedPublisher
        , IUltimateGameplaySnapshotProvider
    {
        private const int UltimateMiniBoardCount = 9;

        private readonly CommandQueue _commandQueue;
        private readonly MatchEcsLifecycleService _lifecycle;
        private readonly EventPublishSystem _eventPublishSystem;
        private readonly UltimateGameplayEventStream? _ultimateEventStream;

        // R3 Subjects — hot-path event streams
        private readonly Subject<CellChangedEvent> _cellChanged = new();
        private readonly Subject<LastMoveChangedEvent> _lastMoveChanged = new();
        private readonly Subject<CurrentPlayerChangedEvent> _currentPlayerChanged = new();
        private readonly Subject<CommandRejectedEvent> _commandRejected = new();
        private readonly Subject<RoundFinishedEvent> _roundFinished = new();

        private bool _disposed;

        // IGameplayEventStream
        public Observable<CellChangedEvent> CellChanged => _cellChanged;
        public Observable<LastMoveChangedEvent> LastMoveChanged => _lastMoveChanged;
        public Observable<CurrentPlayerChangedEvent> CurrentPlayerChanged => _currentPlayerChanged;
        public Observable<CommandRejectedEvent> CommandRejected => _commandRejected;
        public Observable<RoundFinishedEvent> RoundFinished => _roundFinished;

        // IMatchStateProvider
        public bool IsMatchActive => _lifecycle.IsActive;

        public MatchStateProvider(
            CommandQueue commandQueue,
            MatchEcsLifecycleService lifecycle,
            EventPublishSystem eventPublishSystem,
            UltimateGameplayEventStream? ultimateEventStream = null)
        {
            _commandQueue = commandQueue ?? throw new ArgumentNullException(nameof(commandQueue));
            _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));

            _eventPublishSystem = eventPublishSystem ?? throw new ArgumentNullException(nameof(eventPublishSystem));
            _ultimateEventStream = ultimateEventStream;

            // Wire Subject.OnNext as event callbacks — breaks circular DI dependency
            eventPublishSystem.SetCallbacks(
                evt => _cellChanged.OnNext(evt),
                evt => _lastMoveChanged.OnNext(evt),
                evt => _currentPlayerChanged.OnNext(evt),
                evt => _commandRejected.OnNext(evt),
                evt => _roundFinished.OnNext(evt));
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

        public long CommandSequence => TryReadComponent<CommandSequenceComponent>(out var sequence)
            ? sequence.Value
            : -1;

        public int ActivePlayerSlot => TryReadComponent<PlayersComponent>(out var players)
            ? players.ActivePlayerSlot
            : 0;

        public GameStatus CurrentStatus => TryReadComponent<MatchStatusComponent>(out var status)
            ? status.Status
            : GameStatus.InProgress;

        public int? WinnerSlot => TryReadComponent<MatchStatusComponent>(out var status)
            ? status.WinnerSlot
            : null;

        public CellId? LastMove
        {
            get
            {
                if (!TryReadComponent<LastMoveComponent>(out var lastMove))
                    return null;

                return lastMove.HasValue ? lastMove.CellId : null;
            }
        }

        public ulong Epoch => TryReadComponent<UltimateEpochComponent>(out var epoch)
            ? epoch.Value
            : 0UL;

        public AllowedMajors CurrentAllowedMajors => TryReadComponent<UltimateAllowedMajorsComponent>(out var allowedMajors)
            ? allowedMajors.Value
            : AllowedMajors.None;

        public UltimateMatchResult CurrentMatch
        {
            get
            {
                if (!TryReadComponent<MatchStatusComponent>(out var ecsStatus))
                    return new UltimateMatchResult(Games.TicTacToe.Rules.GameStatus.InProgress, PlayerMark.None, null);

                var status = ecsStatus.Status switch
                {
                    GameStatus.Win => Games.TicTacToe.Rules.GameStatus.Win,
                    GameStatus.Draw => Games.TicTacToe.Rules.GameStatus.Draw,
                    GameStatus.InProgress => Games.TicTacToe.Rules.GameStatus.InProgress,
                    GameStatus.Timeout => Games.TicTacToe.Rules.GameStatus.Timeout,
                    _ => throw new ArgumentOutOfRangeException(nameof(ecsStatus.Status), ecsStatus.Status, null),
                };

                if (status != Games.TicTacToe.Rules.GameStatus.Win)
                    return new UltimateMatchResult(status, PlayerMark.None, null);

                var winner = ecsStatus.WinnerSlot.HasValue
                    ? PlayerSlotMapping.SlotToMark(ecsStatus.WinnerSlot.Value)
                    : PlayerMark.None;

                UltimateBigBoardWinLine? winLine = null;
                
                if (TryReadComponent<UltimateBigBoardWinLineComponent>(out var winLineComponent) && winLineComponent.HasValue)
                    winLine = winLineComponent.Value;

                return new UltimateMatchResult(status, winner, winLine);
            }
        }

        public void CopyMiniBoardsTo(Span<MiniBoardStatus> destination)
        {
            if (destination.Length < UltimateMiniBoardCount)
                throw new ArgumentException($"destination must be >= {UltimateMiniBoardCount}", nameof(destination));

            for (var i = 0; i < UltimateMiniBoardCount; i++)
            {
                destination[i] = MiniBoardStatus.InProgress;
            }

            if (!TryReadComponent<UltimateMiniBoardsComponent>(out var miniBoards) || miniBoards.Statuses == null)
                return;

            var source = miniBoards.Statuses;
            var count = source.Length < UltimateMiniBoardCount ? source.Length : UltimateMiniBoardCount;
            
            for (var i = 0; i < count; i++)
            {
                destination[i] = source[i];
            }
        }

        public void Dispose()
        {
            if (_disposed) 
                return;
            
            _disposed = true;

            _eventPublishSystem.ClearCallbacks();

            _cellChanged.OnCompleted();
            _lastMoveChanged.OnCompleted();
            _currentPlayerChanged.OnCompleted();
            _commandRejected.OnCompleted();
            _roundFinished.OnCompleted();

            _cellChanged.Dispose();
            _lastMoveChanged.Dispose();
            _currentPlayerChanged.Dispose();
            _commandRejected.Dispose();
            _roundFinished.Dispose();
            _ultimateEventStream?.Dispose();
        }

        void ICurrentPlayerChangedPublisher.PublishCurrentPlayerChangedImmediate(int activePlayerSlot) => 
            _currentPlayerChanged.OnNext(new CurrentPlayerChangedEvent(activePlayerSlot));

        private bool TryReadComponent<T>(out T value)
            where T : struct, IComponent
        {
            value = default;

            if (!_lifecycle.IsActive)
                return false;

            var stash = _lifecycle.World.GetStash<T>();
            
            if (!stash.Has(_lifecycle.MatchEntity))
                return false;

            value = stash.Get(_lifecycle.MatchEntity);
            return true;
        }
    }
}
