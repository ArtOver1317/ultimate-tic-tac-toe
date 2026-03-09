#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Runtime.GameModes.Wizard.Configs;
using Runtime.GameModes.Wizard.Session;
using Runtime.Infrastructure.Logging;

namespace Runtime.GameModes.Wizard
{
    /// <summary>
    /// Launch flow, publishing, transition helper, state setters, and thread helpers.
    /// </summary>
    public sealed partial class GameWizardCoordinator
    {
        private async UniTask HandleStartIntentAsync(CancellationToken ct)
        {
            if (TryGetSessionSnapshot(out var snapshot) && 
                snapshot is { OpponentType: OpponentType.Human, HumanOpponentKind: HumanOpponentKind.Matchmaking })
            {
                await OpenMatchmakingAsync(snapshot, ct);
                return;
            }

            if (!TryBuildLaunchConfig(out var launchConfig, out var error))
            {
                if (error != null)
                    TrySetCurrentError(error);

                return;
            }

            SetIsSubmitting(true);

            if (launchConfig != null)
                PublishGameLaunchRequested(launchConfig);
        }

        public void CompleteStartAttempt(bool succeeded, WizardError? error = null)
        {
            if (_isDisposed)
                return;

            if (PlayerLoopHelper.IsMainThread)
            {
                CompleteStartAttemptOnMainThreadCoreAsync(succeeded, error).Forget(ex =>
                {
                    if (ex is OperationCanceledException || ex is ObjectDisposedException)
                        return;

                    GameLog.Exception(ex);
                });
                return;
            }

            CompleteStartAttemptOnMainThreadAsync(succeeded, error).Forget();
        }

        public void CancelStartAttempt()
        {
            if (_isDisposed)
                return;

            if (PlayerLoopHelper.IsMainThread)
            {
                SetIsSubmitting(false);
                ClearCurrentError();
                return;
            }

            CancelStartAttemptOnMainThreadAsync().Forget();
        }

        private async UniTask CompleteStartAttemptOnMainThreadCoreAsync(bool succeeded, WizardError? error)
        {
            if (!IsActive)
            {
                SetIsSubmitting(false);
                return;
            }

            if (!succeeded)
            {
                SetIsSubmitting(false);

                if (_step == WizardStep.Matchmaking || _matchmakingViewModel != null)
                {
                    _matchmakingViewModel?.NotifySessionStartFailed();
                    return;
                }

                TrySetCurrentError(error ?? WizardError.FromException(new InvalidOperationException("Start failed.")));
                return;
            }

            try
            {
                await AbortWizardCoreAsync(AbortReason.GameStarted, awaitProcessingTask: false);
            }
            finally
            {
                SetIsSubmitting(false);
            }
        }

        private async UniTaskVoid CompleteStartAttemptOnMainThreadAsync(bool succeeded, WizardError? error)
        {
            try
            {
                await UniTask.SwitchToMainThread(_lifetimeCts.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (_isDisposed)
                return;

            await CompleteStartAttemptOnMainThreadCoreAsync(succeeded, error);
        }

        private async UniTaskVoid CancelStartAttemptOnMainThreadAsync()
        {
            try
            {
                await UniTask.SwitchToMainThread(_lifetimeCts.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (_isDisposed)
                return;

            SetIsSubmitting(false);
            ClearCurrentError();
        }

        private bool TryBuildLaunchConfig(out GameLaunchConfig? launchConfig, out WizardError? error)
        {
            launchConfig = null;
            error = null;

            var session = _session;
            
            if (session == null)
            {
                error = new WizardError(
                    code: "wizard.session_missing",
                    messageKey: "Errors.GameWizard.UnhandledException",
                    isBlocking: true,
                    displayType: ErrorDisplayType.Modal);
                
                return false;
            }

            Result<GameLaunchConfig> result;

            try
            {
                result = session.BuildLaunchConfig();
            }
            catch (Exception ex)
            {
                error = WizardError.FromException(ex);
                return false;
            }

            if (result.IsFailure)
            {
                error = CreateWizardErrorFromValidation(result.Errors);
                return false;
            }

            launchConfig = result.Value;
            return true;
        }

        private static WizardError CreateWizardErrorFromValidation(IReadOnlyList<ValidationError>? errors)
        {
            if (errors == null || errors.Count == 0)
            {
                return new WizardError(
                    code: "wizard.validation_failed",
                    messageKey: "Errors.GameWizard.UnhandledException",
                    isBlocking: true,
                    displayType: ErrorDisplayType.Modal);
            }

            return CreateWizardErrorFromValidation(errors[0]);
        }

        private static WizardError CreateWizardErrorFromValidation(ValidationError error)
        {
            if (error == null)
                throw new ArgumentNullException(nameof(error));

            var displayType = error.Field switch
            {
                WizardFieldNames.Matchmaking => ErrorDisplayType.Modal,
                WizardFieldNames.GameCatalog => ErrorDisplayType.Modal,
                _ => ErrorDisplayType.Inline,
            };

            var isBlocking = displayType == ErrorDisplayType.Modal;

            return new WizardError(
                code: error.Field,
                messageKey: error.MessageKey,
                isBlocking: isBlocking,
                displayType: displayType);
        }

        private void PublishGameLaunchRequested(GameLaunchConfig config)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            if (_isDisposed)
                return;

            if (PlayerLoopHelper.IsMainThread)
            {
                _gameLaunchRequested.OnNext(config);
                return;
            }

            PublishGameLaunchRequestedOnMainThreadAsync(config).Forget();
        }

        private void PublishWizardAborted(AbortReason reason)
        {
            if (_isDisposed)
                return;

            if (PlayerLoopHelper.IsMainThread)
            {
                _wizardAborted.OnNext(reason);
                return;
            }

            PublishWizardAbortedOnMainThreadAsync(reason).Forget();
        }

        private async UniTaskVoid PublishGameLaunchRequestedOnMainThreadAsync(GameLaunchConfig config)
        {
            try
            {
                await UniTask.SwitchToMainThread(CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (_isDisposed)
                return;

            _gameLaunchRequested.OnNext(config);
        }

        private async UniTaskVoid PublishWizardAbortedOnMainThreadAsync(AbortReason reason)
        {
            try
            {
                await UniTask.SwitchToMainThread(CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (_isDisposed)
                return;

            _wizardAborted.OnNext(reason);
        }

        private bool TryGetSessionSnapshot(out GameSessionSnapshot snapshot)
        {
            snapshot = null!;

            if (_session == null)
                return false;

            try
            {
                snapshot = _session.Snapshot.CurrentValue;
                return snapshot != null;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
        }

        private async UniTask TransitionAsync(
            Func<CancellationToken, UniTask> transition,
            CancellationToken ct)
        {
            if (transition == null)
                throw new ArgumentNullException(nameof(transition));

            if (Volatile.Read(ref _isTransitioningFlag) != 0)
                return;

            await UniTask.SwitchToMainThread(ct);
            FlushPendingErrorOnMainThread();
            SetIsTransitioning(true);

            try
            {
                await transition(ct);
            }
            finally
            {
                SetIsTransitioning(false);
            }
        }

        private void SetIsTransitioning(bool value)
        {
            Volatile.Write(ref _isTransitioningFlag, value ? 1 : 0);

            if (PlayerLoopHelper.IsMainThread && !_isDisposed)
                _isTransitioning.Value = value;
        }

        private void SetIsSubmitting(bool value)
        {
            Volatile.Write(ref _isSubmittingFlag, value ? 1 : 0);

            if (PlayerLoopHelper.IsMainThread && !_isDisposed)
                _isSubmitting.Value = value;
        }

        private void TrySetCurrentError(WizardError error)
        {
            if (error == null)
                throw new ArgumentNullException(nameof(error));

            if (_isDisposed)
                return;

            if (PlayerLoopHelper.IsMainThread)
            {
                _currentError.Value = error;
                return;
            }

            Interlocked.Exchange(ref _pendingError, error);
        }

        private void FlushPendingErrorOnMainThread()
        {
            if (!PlayerLoopHelper.IsMainThread || _isDisposed)
                return;

            var pending = Interlocked.Exchange(ref _pendingError, null);
            
            if (pending != null)
                _currentError.Value = pending;
        }

        private async UniTaskVoid ClearCurrentErrorOnMainThreadAsync()
        {
            try
            {
                await UniTask.SwitchToMainThread(_lifetimeCts.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (_isDisposed)
                return;

            Interlocked.Exchange(ref _pendingError, null);

            _currentError.Value = null;
            TryHandleMatchmakingTerminalModalAcknowledge();
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

        private void EnsureNotDisposed()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(GameWizardCoordinator));
        }
    }
}
