#nullable enable

using System;
using R3;
using Runtime.Gameplay.ECS.Publishing;
using Runtime.Infrastructure.Logging;
using StripLog;

namespace Runtime.Games.Battleship.Core
{
    public sealed class BattleshipGameplayEventStream : IBattleshipGameplayEventStream, IDisposable
    {
        private readonly IMatchEventScheduler _scheduler;
        private readonly Subject<BattleshipPhaseChangedEvent> _phaseChanged = new();
        private readonly Subject<BattleshipMarksChangedEvent> _marksChanged = new();

        private bool _disposed;

        public BattleshipGameplayEventStream(IMatchEventScheduler scheduler) =>
            _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));

        public Observable<BattleshipPhaseChangedEvent> PhaseChanged => _phaseChanged;
        public Observable<BattleshipMarksChangedEvent> MarksChanged => _marksChanged;

        internal void PublishPhaseChanged(BattleshipPhaseChangedEvent evt) =>
            _scheduler.Schedule(() => SafeInvoke(_phaseChanged, evt, nameof(BattleshipPhaseChangedEvent)));

        internal void PublishPhaseChangedImmediate(BattleshipPhaseChangedEvent evt) =>
            SafeInvoke(_phaseChanged, evt, nameof(BattleshipPhaseChangedEvent));

        internal void PublishMarksChanged(BattleshipMarksChangedEvent evt) =>
            _scheduler.Schedule(() => SafeInvoke(_marksChanged, evt, nameof(BattleshipMarksChangedEvent)));

        internal void PublishMarksChanged(int viewerSlot, int secondaryViewerSlot, bool hasSecondaryViewer) =>
            _scheduler.Schedule(() => PublishMarksChangedCore(viewerSlot, secondaryViewerSlot, hasSecondaryViewer));

        internal void PublishMarksChangedImmediate(BattleshipMarksChangedEvent evt) =>
            SafeInvoke(_marksChanged, evt, nameof(BattleshipMarksChangedEvent));

        internal void PublishMarksChangedImmediate(int viewerSlot, int secondaryViewerSlot, bool hasSecondaryViewer) =>
            PublishMarksChangedCore(viewerSlot, secondaryViewerSlot, hasSecondaryViewer);

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _phaseChanged.OnCompleted();
            _marksChanged.OnCompleted();
            _phaseChanged.Dispose();
            _marksChanged.Dispose();
        }

        private void PublishMarksChangedCore(int viewerSlot, int secondaryViewerSlot, bool hasSecondaryViewer)
        {
            SafeInvoke(_marksChanged,
                new BattleshipMarksChangedEvent(viewerSlot),
                nameof(BattleshipMarksChangedEvent));

            if (hasSecondaryViewer)
            {
                SafeInvoke(_marksChanged,
                    new BattleshipMarksChangedEvent(secondaryViewerSlot),
                    nameof(BattleshipMarksChangedEvent));
            }
        }

        private static void SafeInvoke<T>(Subject<T> subject, T evt, string eventName)
        {
            try
            {
                subject.OnNext(evt);
            }
            catch (Exception ex)
            {
                Log.Error(LogTags.Infrastructure,
                    $"[BattleshipGameplayEventStream] Exception in {eventName} subscriber: {ex}");
            }
        }
    }
}