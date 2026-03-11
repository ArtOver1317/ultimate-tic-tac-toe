#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using Runtime.GameModes.Wizard.Configs;

namespace Runtime.GameModes.Wizard.Coordinator
{
    internal sealed class GameWizardCoordinatorSignals
    {
        private readonly ReactiveProperty<bool> _isTransitioning = new(false);
        private readonly ReactiveProperty<bool> _isSubmitting = new(false);
        private readonly ReactiveProperty<WizardError?> _currentError = new(null);
        private readonly Subject<GameLaunchConfig> _gameLaunchRequested = new();
        private readonly Subject<AbortReason> _wizardAborted = new();

        private int _isDisposedFlag;
        private int _isTransitioningFlag;
        private int _isSubmittingFlag;
        private WizardError? _pendingError;

        private bool IsDisposed => Volatile.Read(ref _isDisposedFlag) != 0;

        internal bool IsBusy =>
            Volatile.Read(ref _isTransitioningFlag) != 0 ||
            Volatile.Read(ref _isSubmittingFlag) != 0;

        internal bool IsTransitioningActive =>
            Volatile.Read(ref _isTransitioningFlag) != 0;

        internal ReadOnlyReactiveProperty<bool> IsTransitioning => _isTransitioning;
        internal ReadOnlyReactiveProperty<bool> IsSubmitting => _isSubmitting;
        internal ReadOnlyReactiveProperty<WizardError?> CurrentError => _currentError;
        internal Observable<GameLaunchConfig> GameLaunchRequested => _gameLaunchRequested;
        internal Observable<AbortReason> WizardAborted => _wizardAborted;

        internal void SetIsTransitioning(bool value)
        {
            Volatile.Write(ref _isTransitioningFlag, value ? 1 : 0);

            if (PlayerLoopHelper.IsMainThread && !IsDisposed)
                _isTransitioning.Value = value;
        }

        internal void SetIsSubmitting(bool value)
        {
            Volatile.Write(ref _isSubmittingFlag, value ? 1 : 0);

            if (PlayerLoopHelper.IsMainThread && !IsDisposed)
                _isSubmitting.Value = value;
        }

        internal void TrySetCurrentError(WizardError error)
        {
            if (error == null)
                throw new ArgumentNullException(nameof(error));

            if (IsDisposed)
                return;

            if (PlayerLoopHelper.IsMainThread)
            {
                _currentError.Value = error;
                return;
            }

            Interlocked.Exchange(ref _pendingError, error);
        }

        internal void FlushPendingErrorOnMainThread()
        {
            if (!PlayerLoopHelper.IsMainThread || IsDisposed)
                return;

            var pending = Interlocked.Exchange(ref _pendingError, null);
            
            if (pending != null)
                _currentError.Value = pending;
        }

        internal void ClearCurrentError(Action onCleared)
        {
            if (onCleared == null)
                throw new ArgumentNullException(nameof(onCleared));

            if (IsDisposed)
                return;

            ClearPendingError();

            if (PlayerLoopHelper.IsMainThread)
            {
                ClearCurrentErrorValue();
                onCleared();
                return;
            }

            ClearCurrentErrorOnMainThreadAsync(onCleared).Forget();
        }

        internal void ClearPendingError() =>
            Interlocked.Exchange(ref _pendingError, null);

        internal void ClearCurrentErrorValue()
        {
            if (IsDisposed)
                return;

            ClearPendingError();
            _currentError.Value = null;
        }

        internal void PublishGameLaunchRequested(GameLaunchConfig config)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            if (IsDisposed)
                return;

            if (PlayerLoopHelper.IsMainThread)
            {
                _gameLaunchRequested.OnNext(config);
                return;
            }

            PublishGameLaunchRequestedOnMainThreadAsync(config).Forget();
        }

        internal void PublishWizardAborted(AbortReason reason)
        {
            if (IsDisposed)
                return;

            if (PlayerLoopHelper.IsMainThread)
            {
                _wizardAborted.OnNext(reason);
                return;
            }

            PublishWizardAbortedOnMainThreadAsync(reason).Forget();
        }

        internal void ResetBusyState()
        {
            SetIsTransitioning(false);
            SetIsSubmitting(false);
            FlushPendingErrorOnMainThread();
        }

        internal void Dispose()
        {
            if (Interlocked.Exchange(ref _isDisposedFlag, 1) != 0)
                return;

            _isTransitioning.Dispose();
            _isSubmitting.Dispose();
            _currentError.Dispose();
            _gameLaunchRequested.OnCompleted();
            _gameLaunchRequested.Dispose();
            _wizardAborted.OnCompleted();
            _wizardAborted.Dispose();
        }

        private async UniTask ClearCurrentErrorOnMainThreadAsync(Action onCleared)
        {
            try
            {
                await UniTask.SwitchToMainThread(CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (IsDisposed)
                return;

            ClearCurrentErrorValue();
            onCleared();
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

            if (IsDisposed)
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

            if (IsDisposed)
                return;

            _wizardAborted.OnNext(reason);
        }
    }
}