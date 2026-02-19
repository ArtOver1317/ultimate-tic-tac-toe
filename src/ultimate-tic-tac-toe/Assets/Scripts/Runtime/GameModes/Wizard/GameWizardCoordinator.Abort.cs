#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using R3;
using Runtime.Infrastructure.Logging;

namespace Runtime.GameModes.Wizard
{
    /// <summary>
    /// Abort &amp; dispose lifecycle.
    /// </summary>
    public sealed partial class GameWizardCoordinator
    {
        public async UniTask AbortWizardAsync(AbortReason reason)
        {
            EnsureNotDisposed();

            // If Abort is triggered from inside the processing loop, awaiting that loop would self-await.
            var awaitProcessingTask = !_isInProcessingLoop.Value;
            await AbortWizardCoreAsync(reason, awaitProcessingTask: awaitProcessingTask);
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;

            _lifetimeCts.Cancel();
            _lifetimeCts.Dispose();

            DisposeAfterAbortAsync().Forget(ex => GameLog.Exception(ex));
        }

        private async UniTask DisposeAfterAbortAsync()
        {
            try
            {
                // Best-effort cleanup (must not throw due to disposal ordering)
                await AbortWizardCoreAsync(AbortReason.SceneChange, awaitProcessingTask: true);
            }
            catch (Exception ex)
            {
                GameLog.Exception(ex);
            }
            finally
            {
                _isTransitioning.Dispose();
                _isSubmitting.Dispose();
                _currentError.Dispose();
                _gameLaunchRequested.OnCompleted();
                _gameLaunchRequested.Dispose();
                _wizardAborted.OnCompleted();
                _wizardAborted.Dispose();
            }
        }

        private async UniTask AbortWizardCoreAsync(AbortReason reason, bool awaitProcessingTask)
        {
            if (Interlocked.Exchange(ref _abortInProgress, 1) != 0)
                return;

            var (wizardCts, processingTask, session, shouldPublishAbort) = DetachWizardState();

            if (wizardCts == null && processingTask == null && session == null)
            {
                try
                {
                    ResetBusyState();
                }
                finally
                {
                    Interlocked.Exchange(ref _abortInProgress, 0);
                }

                return;
            }

            Volatile.Write(ref _isReadyForIntentsFlag, 0);
            Volatile.Write(ref _hasPendingOrInFlightIntentFlag, 0);

            try
            {
                GameLog.Debug($"[GameWizardCoordinator] Abort wizard. Reason={reason}");

                wizardCts?.Cancel();
                if (ShouldExitOnlineFlow(reason))
                    await _onlineSessionFlow.ExitAsync();
                await TryCloseWizardWindowsAsync();
                await TryAwaitProcessingTaskAsync(processingTask, awaitProcessingTask);
            }
            catch (Exception ex)
            {
                TrySetCurrentError(WizardError.FromException(ex));
                GameLog.Exception(ex);
            }
            finally
            {
                DisposeSessionAndCts(session, wizardCts);
                ResetBusyState();
                
                if (shouldPublishAbort)
                    PublishWizardAborted(reason);
                
                Interlocked.Exchange(ref _abortInProgress, 0);
            }
        }

        private static bool ShouldExitOnlineFlow(AbortReason reason) =>
            reason == AbortReason.UserCancel ||
            reason == AbortReason.Disconnect ||
            reason == AbortReason.Error ||
            reason == AbortReason.StartCancelled;

        private (CancellationTokenSource? wizardCts, Task? processingTask, IGameSession? session, bool shouldPublishAbort) DetachWizardState()
        {
            lock (_lifecycleLock)
            {
                var wizardCts = _wizardCts;
                var processingTask = _processingTask;
                var session = _session;

                _wizardCts = null;
                _processingTask = null;
                _intentQueue = null;
                _session = null;
                _step = WizardStep.None;
                CleanupMatchmakingBindings();
                Volatile.Write(ref _isActiveFlag, 0);

                var shouldPublishAbort = wizardCts != null || processingTask != null || session != null;
                return (wizardCts, processingTask, session, shouldPublishAbort);
            }
        }

        private async UniTask TryCloseWizardWindowsAsync()
        {
            if (!PlayerLoopHelper.IsMainThread)
            {
                var switched = await TrySwitchToMainThreadWithTimeoutAsync(_abortSwitchToMainThreadTimeout);
                
                if (!switched)
                {
                    GameLog.Warning("[GameWizardCoordinator] Failed to switch to main thread to close wizard windows (timeout/shutdown). Windows may remain open.");
                    return;
                }
            }

            using var closeCts = new CancellationTokenSource(_abortCloseWindowsTimeout);
            
            try
            {
                await _navigator.CloseAllWizardWindowsAsync(closeCts.Token);
            }
            catch (OperationCanceledException)
            {
                // Expected on timeout/shutdown.
            }
        }

        private static async UniTask TryAwaitProcessingTaskAsync(Task? processingTask, bool shouldAwait)
        {
            if (!shouldAwait || processingTask == null)
                return;

            try
            {
                await processingTask;
            }
            catch (OperationCanceledException)
            {
                // Expected due to wizard cancellation.
            }
        }

        private static void DisposeSessionAndCts(IGameSession? session, CancellationTokenSource? wizardCts)
        {
            try
            {
                session?.Dispose();
            }
            catch (Exception ex)
            {
                GameLog.Exception(ex);
            }

            wizardCts?.Dispose();
        }

        private void ResetBusyState()
        {
            SetIsTransitioning(false);
            SetIsSubmitting(false);
            FlushPendingErrorOnMainThread();
        }
    }
}
