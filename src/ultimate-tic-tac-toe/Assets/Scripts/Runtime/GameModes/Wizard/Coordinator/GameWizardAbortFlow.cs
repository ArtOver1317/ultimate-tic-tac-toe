#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Runtime.GameModes.Wizard.Session;
using Runtime.Infrastructure.Logging;

namespace Runtime.GameModes.Wizard.Coordinator
{
    /// <summary>
    /// Abort and dispose lifecycle for the wizard session.
    /// </summary>
    internal sealed class GameWizardAbortFlow
    {
        private static readonly TimeSpan _abortSwitchToMainThreadTimeout = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan _abortCloseWindowsTimeout = TimeSpan.FromSeconds(2);

        private readonly GameWizardCoordinatorContext _context;
        private readonly GameWizardMatchmakingFlow _matchmakingFlow;

        internal GameWizardAbortFlow(
            GameWizardCoordinatorContext context,
            GameWizardMatchmakingFlow matchmakingFlow)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _matchmakingFlow = matchmakingFlow ?? throw new ArgumentNullException(nameof(matchmakingFlow));
        }

        internal async UniTask AbortAsync(AbortReason reason, bool awaitProcessingTask)
        {
            if (!_context.TryBeginAbort())
                return;

            var detached = _context.DetachWizardState(_matchmakingFlow.CleanupBindings);
            
            if (detached.WizardCts == null && detached.ProcessingTask == null && detached.Session == null)
            {
                try
                {
                    _context.Signals.ResetBusyState();
                }
                finally
                {
                    _context.EndAbort();
                }

                return;
            }

            _context.ResetIntentState();

            try
            {
                GameLog.Debug($"[GameWizardCoordinator] Abort wizard. Reason={reason}");

                detached.WizardCts?.Cancel();

                if (ShouldExitOnlineFlow(reason))
                    await _context.OnlineSessionFlow.ExitAsync();

                await TryCloseWizardWindowsAsync();
                await TryAwaitProcessingTaskAsync(detached.ProcessingTask, awaitProcessingTask);
            }
            catch (Exception ex)
            {
                _context.Signals.TrySetCurrentError(WizardError.FromException(ex));
                GameLog.Exception(ex);
            }
            finally
            {
                DisposeSessionAndCts(detached.Session, detached.WizardCts);
                _context.Signals.ResetBusyState();

                if (detached.ShouldPublishAbort)
                    _context.Signals.PublishWizardAborted(reason);

                _context.EndAbort();
            }
        }

        internal void Dispose()
        {
            if (!_context.TryDispose())
                return;

            DisposeAfterAbortAsync().Forget(GameLog.Exception);
        }

        private async UniTask DisposeAfterAbortAsync()
        {
            try
            {
                await AbortAsync(AbortReason.SceneChange, awaitProcessingTask: true);
            }
            catch (Exception ex)
            {
                GameLog.Exception(ex);
            }
            finally
            {
                _context.Signals.Dispose();
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
                await _context.Navigator.CloseAllWizardWindowsAsync(closeCts.Token);
            }
            catch (OperationCanceledException)
            {
                // Expected on timeout/shutdown.
            }
        }

        private static bool ShouldExitOnlineFlow(AbortReason reason) =>
            reason == AbortReason.UserCancel ||
            reason == AbortReason.Disconnect ||
            reason == AbortReason.Error ||
            reason == AbortReason.StartCancelled;

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

        private static async UniTask<bool> TrySwitchToMainThreadWithTimeoutAsync(TimeSpan timeout)
        {
            if (PlayerLoopHelper.IsMainThread)
                return true;

            using var timeoutCts = new CancellationTokenSource(timeout);

            try
            {
                await UniTask.SwitchToMainThread(timeoutCts.Token);
                return PlayerLoopHelper.IsMainThread;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }
    }
}