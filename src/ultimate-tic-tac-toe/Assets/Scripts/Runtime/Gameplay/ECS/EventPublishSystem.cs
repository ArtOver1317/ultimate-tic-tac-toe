using System;
using Runtime.Infrastructure.Logging;
using Scellecs.Morpeh;
using StripLog;

namespace Runtime.Gameplay.ECS
{
    /// <summary>
    /// Last system in the pipeline. Reads one-shot event components from the match entity
    /// and publishes them through <see cref="IMatchEventScheduler"/>.
    /// Deterministic event order per section 3 of the design doc:
    /// 1. CellChanged, 2. LastMoveChanged, 3. CurrentPlayerChanged, 4. RoundFinished
    /// On rejection: CommandRejected only.
    /// </summary>
    public sealed class EventPublishSystem : ISystem
    {
        public World World { get; set; }

        private readonly IMatchEventScheduler _scheduler;

        private Action<CellChangedEvent> _onCellChanged;
        private Action<LastMoveChangedEvent> _onLastMoveChanged;
        private Action<CurrentPlayerChangedEvent> _onCurrentPlayerChanged;
        private Action<CommandRejectedEvent> _onCommandRejected;
        private Action<RoundFinishedEvent> _onRoundFinished;

        private Filter _matchFilter;
        private Stash<MoveAppliedOneShot> _moveAppliedStash;
        private Stash<MoveRejectedOneShot> _moveRejectedStash;
        private Stash<RoundFinishedOneShot> _roundFinishedStash;
        private Stash<PlayersComponent> _playersStash;
        private Stash<LastMoveComponent> _lastMoveStash;

        public EventPublishSystem(IMatchEventScheduler scheduler)
        {
            _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        }

        /// <summary>
        /// Wires event callbacks. Called by <see cref="Runtime.Gameplay.MatchStateProvider"/> after construction
        /// to break the circular dependency (lifecycle → eventPublishSystem → stateProvider → lifecycle).
        /// </summary>
        internal void SetCallbacks(
            Action<CellChangedEvent> onCellChanged,
            Action<LastMoveChangedEvent> onLastMoveChanged,
            Action<CurrentPlayerChangedEvent> onCurrentPlayerChanged,
            Action<CommandRejectedEvent> onCommandRejected,
            Action<RoundFinishedEvent> onRoundFinished)
        {
            _onCellChanged = onCellChanged;
            _onLastMoveChanged = onLastMoveChanged;
            _onCurrentPlayerChanged = onCurrentPlayerChanged;
            _onCommandRejected = onCommandRejected;
            _onRoundFinished = onRoundFinished;
        }

        internal void ClearCallbacks()
        {
            _onCellChanged = null;
            _onLastMoveChanged = null;
            _onCurrentPlayerChanged = null;
            _onCommandRejected = null;
            _onRoundFinished = null;
        }

        /// <summary>
        /// Returns true if any event callbacks are currently registered.
        /// Useful for diagnostics and lifecycle verification.
        /// </summary>
        internal bool HasCallbacks =>
            _onCellChanged != null ||
            _onLastMoveChanged != null ||
            _onCurrentPlayerChanged != null ||
            _onCommandRejected != null ||
            _onRoundFinished != null;

        public void OnAwake()
        {
            _matchFilter = World.Filter.With<MatchTag>().Build();
            _moveAppliedStash = World.GetStash<MoveAppliedOneShot>();
            _moveRejectedStash = World.GetStash<MoveRejectedOneShot>();
            _roundFinishedStash = World.GetStash<RoundFinishedOneShot>();
            _playersStash = World.GetStash<PlayersComponent>();
            _lastMoveStash = World.GetStash<LastMoveComponent>();
        }

        public void OnUpdate(float deltaTime)
        {
            if (_matchFilter.IsEmpty())
                return;

            var matchEntity = _matchFilter.First();

            // Handle rejection (mutually exclusive with applied)
            if (_moveRejectedStash.Has(matchEntity))
            {
                ref var rejected = ref _moveRejectedStash.Get(matchEntity);
                var evt = new CommandRejectedEvent(rejected.CommandType, rejected.Rejection);
                _moveRejectedStash.Remove(matchEntity);

                _scheduler.Schedule(() => SafeInvoke(_onCommandRejected, evt, nameof(CommandRejectedEvent)));
                return;
            }

            // Handle successful move — deterministic order (section 3)
            if (_moveAppliedStash.Has(matchEntity))
            {
                ref var applied = ref _moveAppliedStash.Get(matchEntity);
                var cellEvt = new CellChangedEvent(applied.CellId, applied.PlayerSlot);

                ref var lastMove = ref _lastMoveStash.Get(matchEntity);
                var lastMoveEvt = new LastMoveChangedEvent(lastMove.HasValue ? lastMove.CellId : null);

                // PlayersComponent must be initialized by game-specific registrar
                if (!_playersStash.Has(matchEntity))
                {
                    Log.Error(LogTags.Infrastructure,
                        "[EventPublishSystem] PlayersComponent missing on match entity. " +
                        "Game-specific registrar must initialize it.");
                    _moveAppliedStash.Remove(matchEntity);
                    return;
                }

                ref var players = ref _playersStash.Get(matchEntity);
                var playerEvt = new CurrentPlayerChangedEvent(players.ActivePlayerSlot);

                RoundFinishedEvent? roundEvt = null;
                if (_roundFinishedStash.Has(matchEntity))
                {
                    ref var finished = ref _roundFinishedStash.Get(matchEntity);
                    roundEvt = new RoundFinishedEvent(finished.Status, finished.WinnerSlot, finished.WinLine);
                    _roundFinishedStash.Remove(matchEntity);
                }

                _moveAppliedStash.Remove(matchEntity);

                // Each callback in its own try/catch to guarantee deterministic delivery order
                // (section 3 of design doc). An exception in one subscriber must NOT prevent
                // remaining events from being delivered.
                _scheduler.Schedule(() =>
                {
                    SafeInvoke(_onCellChanged, cellEvt, nameof(CellChangedEvent));
                    SafeInvoke(_onLastMoveChanged, lastMoveEvt, nameof(LastMoveChangedEvent));
                    SafeInvoke(_onCurrentPlayerChanged, playerEvt, nameof(CurrentPlayerChangedEvent));
                    if (roundEvt.HasValue)
                        SafeInvoke(_onRoundFinished, roundEvt.Value, nameof(RoundFinishedEvent));
                });
            }
        }

        private static void SafeInvoke<T>(Action<T> callback, T evt, string eventName)
        {
            if (callback == null) return;
            try
            {
                callback.Invoke(evt);
            }
            catch (Exception ex)
            {
                Log.Error(LogTags.Infrastructure,
                    $"[EventPublishSystem] Exception in {eventName} subscriber: {ex}");
            }
        }

        public void Dispose()
        {
            // Callbacks are managed by MatchStateProvider and outlive individual matches.
            // Do NOT null them here — Morpeh calls Dispose when World is disposed,
            // but the same EventPublishSystem instance is reused for the next match.
        }
    }
}
