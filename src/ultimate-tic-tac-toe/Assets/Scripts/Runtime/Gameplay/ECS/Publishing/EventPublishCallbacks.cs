using System;
using Runtime.Gameplay.Shared;
using Runtime.Infrastructure.Logging;
using StripLog;

namespace Runtime.Gameplay.ECS.Publishing
{
    internal sealed class EventPublishCallbacks
    {
        private readonly IMatchEventScheduler _scheduler;

        private Action<CellChangedEvent> _onCellChanged;
        private Action<LastMoveChangedEvent> _onLastMoveChanged;
        private Action<CurrentPlayerChangedEvent> _onCurrentPlayerChanged;
        private Action<CommandRejectedEvent> _onCommandRejected;
        private Action<RoundFinishedEvent> _onRoundFinished;

        internal EventPublishCallbacks(IMatchEventScheduler scheduler) => 
            _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));

        internal bool HasAny =>
            _onCellChanged != null ||
            _onLastMoveChanged != null ||
            _onCurrentPlayerChanged != null ||
            _onCommandRejected != null ||
            _onRoundFinished != null;

        internal void Set(
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

        internal void Clear()
        {
            _onCellChanged = null;
            _onLastMoveChanged = null;
            _onCurrentPlayerChanged = null;
            _onCommandRejected = null;
            _onRoundFinished = null;
        }

        internal void ScheduleCommandRejected(CommandRejectedEvent evt) => 
            _scheduler.Schedule(() => SafeInvoke(_onCommandRejected, evt, nameof(CommandRejectedEvent)));

        internal void ScheduleMoveApplied(MoveAppliedPublication publication) =>
            _scheduler.Schedule(() =>
            {
                SafeInvoke(_onCellChanged, publication.CellChanged, nameof(CellChangedEvent));
                SafeInvoke(_onLastMoveChanged, publication.LastMoveChanged, nameof(LastMoveChangedEvent));
                SafeInvoke(_onCurrentPlayerChanged, publication.CurrentPlayerChanged, nameof(CurrentPlayerChangedEvent));

                if (publication.RoundFinished.HasValue)
                    SafeInvoke(_onRoundFinished, publication.RoundFinished.Value, nameof(RoundFinishedEvent));
            });

        internal void ScheduleCurrentPlayerChanged(CurrentPlayerChangedEvent evt) => 
            _scheduler.Schedule(() => SafeInvoke(_onCurrentPlayerChanged, evt, nameof(CurrentPlayerChangedEvent)));

        internal void ScheduleRoundFinished(RoundFinishedEvent evt) => 
            _scheduler.Schedule(() => SafeInvoke(_onRoundFinished, evt, nameof(RoundFinishedEvent)));

        private static void SafeInvoke<T>(Action<T> callback, T evt, string eventName)
        {
            if (callback == null)
                return;

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
    }

    internal readonly struct MoveAppliedPublication
    {
        public CellChangedEvent CellChanged { get; }
        public LastMoveChangedEvent LastMoveChanged { get; }
        public CurrentPlayerChangedEvent CurrentPlayerChanged { get; }
        public RoundFinishedEvent? RoundFinished { get; }

        public MoveAppliedPublication(
            CellChangedEvent cellChanged,
            LastMoveChangedEvent lastMoveChanged,
            CurrentPlayerChangedEvent currentPlayerChanged,
            RoundFinishedEvent? roundFinished)
        {
            CellChanged = cellChanged;
            LastMoveChanged = lastMoveChanged;
            CurrentPlayerChanged = currentPlayerChanged;
            RoundFinished = roundFinished;
        }
    }
}