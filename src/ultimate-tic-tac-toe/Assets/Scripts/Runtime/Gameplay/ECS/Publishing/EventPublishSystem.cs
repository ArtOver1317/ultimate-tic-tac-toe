using System;
using Runtime.Gameplay.ECS.Components;
using Runtime.Gameplay.Shared;
using Runtime.Infrastructure.Logging;
using Scellecs.Morpeh;
using StripLog;

namespace Runtime.Gameplay.ECS.Publishing
{
    /// <summary>
    /// Last system in the pipeline. Reads one-shot event components from the match entity
    /// and publishes them through <see cref="IMatchEventScheduler"/>.
    /// Publishes only cross-game events. Game-specific events are published by
    /// game-specific post-publish systems registered by the active registrar.
    /// Deterministic event order per section 3 of the design doc:
    /// 1. CellChanged, 2. LastMoveChanged, 3. CurrentPlayerChanged, 4. RoundFinished.
    /// On rejection: CommandRejected only.
    /// </summary>
    public sealed class EventPublishSystem : ISystem
    {
        public World World { get; set; }

        private readonly EventPublishCallbacks _callbacks;

        private Filter _matchFilter;
        private Stash<MoveAppliedOneShot> _moveAppliedStash;
        private Stash<MoveRejectedOneShot> _moveRejectedStash;
        private Stash<RoundFinishedOneShot> _roundFinishedStash;
        private Stash<PlayersComponent> _playersStash;
        private Stash<LastMoveComponent> _lastMoveStash;
        private Stash<RoundRestartedOneShot> _roundRestartedStash;

        public EventPublishSystem(IMatchEventScheduler scheduler) => _callbacks = new EventPublishCallbacks(scheduler);

        /// <summary>
        /// Wires event callbacks. Called by <see cref="Runtime.Gameplay.MatchStateProvider"/> after construction
        /// to break the circular dependency (lifecycle → eventPublishSystem → stateProvider → lifecycle).
        /// </summary>
        internal void SetCallbacks(
            Action<CellChangedEvent> onCellChanged,
            Action<LastMoveChangedEvent> onLastMoveChanged,
            Action<CurrentPlayerChangedEvent> onCurrentPlayerChanged,
            Action<CommandRejectedEvent> onCommandRejected,
            Action<RoundFinishedEvent> onRoundFinished) =>
            _callbacks.Set(
                onCellChanged,
                onLastMoveChanged,
                onCurrentPlayerChanged,
                onCommandRejected,
                onRoundFinished);

        internal void ClearCallbacks() => _callbacks.Clear();

        /// <summary>
        /// Returns true if any event callbacks are currently registered.
        /// Useful for diagnostics and lifecycle verification.
        /// </summary>
        internal bool HasCallbacks => _callbacks.HasAny;

        public void OnAwake()
        {
            _matchFilter = World.Filter.With<MatchTag>().Build();
            _moveAppliedStash = World.GetStash<MoveAppliedOneShot>();
            _moveRejectedStash = World.GetStash<MoveRejectedOneShot>();
            _roundFinishedStash = World.GetStash<RoundFinishedOneShot>();
            _roundRestartedStash = World.GetStash<RoundRestartedOneShot>();
            _playersStash = World.GetStash<PlayersComponent>();
            _lastMoveStash = World.GetStash<LastMoveComponent>();
        }

        public void OnUpdate(float deltaTime)
        {
            if (_matchFilter.IsEmpty())
                return;

            var matchEntity = _matchFilter.First();

            // Handle rejection (mutually exclusive with applied)
            if (TryPublishRejected(matchEntity))
                return;

            // Handle successful move — deterministic order (section 3)
            if (TryPublishAppliedMove(matchEntity))
                return;

            // Handle round restart without move application — publish CurrentPlayerChanged so bot driver reacts.
            if (TryPublishRoundRestart(matchEntity))
                return;

            // Handle terminal events without move application (for example timeout command).
            TryPublishTerminalRound(matchEntity);
        }

        private bool TryPublishRejected(Entity matchEntity)
        {
            if (!_moveRejectedStash.Has(matchEntity))
                return false;

            ref var rejected = ref _moveRejectedStash.Get(matchEntity);
            var evt = new CommandRejectedEvent(rejected.CommandType, rejected.Rejection);
            _moveRejectedStash.Remove(matchEntity);
            _callbacks.ScheduleCommandRejected(evt);
            return true;
        }

        private bool TryPublishAppliedMove(Entity matchEntity)
        {
            if (!_moveAppliedStash.Has(matchEntity))
                return false;

            // Move can co-exist with RoundRestartedOneShot (for example miss in Battleship).
            // In this case move events must be published in the same tick; consume restart marker here.
            if (_roundRestartedStash.Has(matchEntity))
                _roundRestartedStash.Remove(matchEntity);

            if (!TryCreateMoveAppliedPublication(matchEntity, out var publication))
            {
                _moveAppliedStash.Remove(matchEntity);
                return true;
            }

            _moveAppliedStash.Remove(matchEntity);
            _callbacks.ScheduleMoveApplied(publication);
            return true;
        }

        private bool TryCreateMoveAppliedPublication(Entity matchEntity, out MoveAppliedPublication publication)
        {
            publication = default;

            if (!_playersStash.Has(matchEntity))
            {
                Log.Error(LogTags.Infrastructure,
                    "[EventPublishSystem] PlayersComponent missing on match entity. " +
                    "Game-specific registrar must initialize it.");
                
                return false;
            }

            ref var applied = ref _moveAppliedStash.Get(matchEntity);
            ref var lastMove = ref _lastMoveStash.Get(matchEntity);
            ref var players = ref _playersStash.Get(matchEntity);

            var roundFinishedEvent = TryTakeRoundFinishedEvent(matchEntity, out var roundFinished)
                ? roundFinished
                : (RoundFinishedEvent?)null;

            publication = new MoveAppliedPublication(
                new CellChangedEvent(applied.CellId, applied.PlayerSlot),
                new LastMoveChangedEvent(lastMove.HasValue ? lastMove.CellId : null),
                new CurrentPlayerChangedEvent(players.ActivePlayerSlot),
                roundFinishedEvent);

            return true;
        }

        private bool TryPublishRoundRestart(Entity matchEntity)
        {
            if (!_roundRestartedStash.Has(matchEntity))
                return false;

            _roundRestartedStash.Remove(matchEntity);

            if (_playersStash.Has(matchEntity))
            {
                ref var players = ref _playersStash.Get(matchEntity);
                _callbacks.ScheduleCurrentPlayerChanged(new CurrentPlayerChangedEvent(players.ActivePlayerSlot));
            }

            return true;
        }

        private void TryPublishTerminalRound(Entity matchEntity)
        {
            if (!TryTakeRoundFinishedEvent(matchEntity, out var roundFinishedEvent))
                return;

            _callbacks.ScheduleRoundFinished(roundFinishedEvent);
        }

        private bool TryTakeRoundFinishedEvent(Entity matchEntity, out RoundFinishedEvent evt)
        {
            if (!_roundFinishedStash.Has(matchEntity))
            {
                evt = default;
                return false;
            }

            ref var finished = ref _roundFinishedStash.Get(matchEntity);
            evt = new RoundFinishedEvent(finished.Status, finished.WinnerSlot, finished.WinLine);
            _roundFinishedStash.Remove(matchEntity);
            return true;
        }

        public void Dispose()
        {
            // Callbacks are managed by MatchStateProvider and outlive individual matches.
            // Do NOT null them here — Morpeh calls Dispose when World is disposed,
            // but the same EventPublishSystem instance is reused for the next match.
        }
    }
}