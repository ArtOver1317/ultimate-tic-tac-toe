#nullable enable

using System;
using R3;
using Runtime.Gameplay.ECS;
using Runtime.Gameplay.ECS.Publishing;
using Runtime.Infrastructure.Logging;
using StripLog;

namespace Runtime.Games.TicTacToe.Ultimate
{
    public sealed class UltimateGameplayEventStream : IUltimateGameplayEventStream, IDisposable
    {
        private readonly IMatchEventScheduler _scheduler;
        private readonly Subject<AllowedMajorsChangedEvent> _allowedMajorsChanged = new();
        private readonly Subject<MiniBoardStatusChangedEvent> _miniBoardStatusChanged = new();

        private bool _disposed;

        public UltimateGameplayEventStream(IMatchEventScheduler scheduler) =>
            _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));

        public Observable<AllowedMajorsChangedEvent> AllowedMajorsChanged => _allowedMajorsChanged;
        public Observable<MiniBoardStatusChangedEvent> MiniBoardStatusChanged => _miniBoardStatusChanged;

        internal void PublishAllowedMajorsChanged(AllowedMajorsChangedEvent evt) =>
            _scheduler.Schedule(() => SafeInvoke(_allowedMajorsChanged, evt, nameof(AllowedMajorsChangedEvent)));

        internal void PublishMiniBoardStatusChanged(MiniBoardStatusChangedEvent evt) =>
            _scheduler.Schedule(() => SafeInvoke(_miniBoardStatusChanged, evt, nameof(MiniBoardStatusChangedEvent)));

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _allowedMajorsChanged.OnCompleted();
            _miniBoardStatusChanged.OnCompleted();
            _allowedMajorsChanged.Dispose();
            _miniBoardStatusChanged.Dispose();
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
                    $"[UltimateGameplayEventStream] Exception in {eventName} subscriber: {ex}");
            }
        }
    }
}