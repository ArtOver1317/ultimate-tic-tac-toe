using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Runtime.Infrastructure.Logging;
using StripLog;

namespace Runtime.Gameplay.ECS
{
    /// <summary>
    /// Defers event publishing to the next frame for re-entrancy safety (ADR-5).
    /// Used in runtime. For tests, use <see cref="SynchronousEventScheduler"/>.
    /// Implements <see cref="IDisposable"/> — VContainer auto-disposes scoped instances,
    /// which cancels pending scheduled actions on scope teardown.
    /// </summary>
    public sealed class DeferredEventScheduler : IMatchEventScheduler, IDisposable
    {
        private readonly CancellationTokenSource _cts = new();

        public void Schedule(Action publishAction)
        {
            if (publishAction == null) return;
            ScheduleAsync(publishAction, _cts.Token).Forget();
        }

        private static async UniTaskVoid ScheduleAsync(Action publishAction, CancellationToken ct)
        {
            try
            {
                await UniTask.NextFrame(ct);
                publishAction.Invoke();
            }
            catch (OperationCanceledException)
            {
                // Expected on scope teardown — silently discard.
            }
            catch (Exception ex)
            {
                Log.Error(LogTags.Infrastructure,
                    $"[DeferredEventScheduler] Exception in deferred publish: {ex}");
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _cts.Dispose();
        }
    }
}
