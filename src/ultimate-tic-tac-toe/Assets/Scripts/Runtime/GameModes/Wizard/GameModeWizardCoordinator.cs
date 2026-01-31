#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using R3;
using Runtime.Infrastructure.Logging;

namespace Runtime.GameModes.Wizard
{
    /// <summary>
    /// Phase 3: coordinator owning intent queue, busy state and session lifecycle.
    /// UI specifics are delegated to <see cref="IGameModeWizardNavigator"/>.
    /// </summary>
    public sealed class GameModeWizardCoordinator : IGameModeWizardCoordinator, IDisposable
    {
        private static readonly TimeSpan AbortSwitchToMainThreadTimeout = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan MatchmakingFoundAutoCloseDelay = TimeSpan.FromMilliseconds(450);
        private static readonly TimeSpan AbortCloseWindowsTimeout = TimeSpan.FromSeconds(2);

        // Invariant: processing loop is started via .AsTask() and does not use ConfigureAwait(false).
        // This marker is used to avoid self-await when abort is triggered from inside the processing loop.
        private readonly AsyncLocal<bool> _isInProcessingLoop = new();

        private enum WizardStep
        {
            None = 0,
            ModeSelection = 1,
            MatchSetup = 2,
            Matchmaking = 3,
        }

        private readonly IGameModeWizardNavigator _navigator;
        private readonly Func<IGameModeSession> _sessionFactory;

        private readonly ReactiveProperty<bool> _isTransitioning = new(false);
        private readonly ReactiveProperty<bool> _isSubmitting = new(false);
        private readonly ReactiveProperty<WizardError?> _currentError = new(null);
        private readonly Subject<GameLaunchConfig> _gameLaunchRequested = new();
        private readonly Subject<AbortReason> _wizardAborted = new();

        // Thread-safe flags so TryPublishIntent can check busy state without touching R3 from non-main threads.
        private int _isTransitioningFlag;
        private int _isSubmittingFlag;

        // Anti-spam gate: only one pending/in-flight non-cancel intent is allowed at a time.
        private int _hasPendingOrInFlightIntentFlag;

        // Intents are only accepted after the first wizard window is successfully opened.
        private int _isReadyForIntentsFlag;

        // If we fail off-main-thread, we store the error and flush it once we're on main thread again.
        private WizardError? _pendingError;

        private readonly object _lifecycleLock = new();
        private CancellationTokenSource _lifetimeCts = new();
        private CancellationTokenSource? _wizardCts;

        private WizardIntentQueue? _intentQueue;
        private Task? _processingTask;

        private IGameModeSession? _session;

        private MatchmakingViewModel? _matchmakingViewModel;
        private CompositeDisposable? _matchmakingSubscriptions;
        private int _matchmakingCloseInProgress;

        private int _isActiveFlag;

        private WizardStep _step;

        private int _abortInProgress;
        private bool _isDisposed;

        public ReadOnlyReactiveProperty<bool> IsTransitioning => _isTransitioning;
        public ReadOnlyReactiveProperty<bool> IsSubmitting => _isSubmitting;
        public ReadOnlyReactiveProperty<WizardError?> CurrentError => _currentError;
        public Observable<GameLaunchConfig> GameLaunchRequested => _gameLaunchRequested;
        public Observable<AbortReason> WizardAborted => _wizardAborted;
        public IGameModeSession Session => _session ?? throw new InvalidOperationException("Wizard is not active.");
        public bool IsActive => Volatile.Read(ref _isActiveFlag) != 0;

        public void ClearCurrentError()
        {
            if (_isDisposed)
                return;

            Interlocked.Exchange(ref _pendingError, null);

            if (PlayerLoopHelper.IsMainThread)
            {
                _currentError.Value = null;
                return;
            }

            ClearCurrentErrorOnMainThreadAsync().Forget();
        }

        public bool TryGetSession([NotNullWhen(true)] out IGameModeSession? session)
        {
            if (Volatile.Read(ref _abortInProgress) != 0)
            {
                session = null;
                return false;
            }

            lock (_lifecycleLock)
            {
                session = _session;
                return session != null;
            }
        }

        public GameModeWizardCoordinator(IGameModeWizardNavigator navigator, Func<IGameModeSession> sessionFactory)
        {
            _navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));
            _sessionFactory = sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory));
        }

        public async UniTask StartWizardAsync(CancellationToken ct)
        {
            EnsureNotDisposed();

            // Wizard is a UI flow; enforce main thread for state mutations.
            await UniTask.SwitchToMainThread(ct);

            CancellationToken wizardToken;

            lock (_lifecycleLock)
            {
                EnsureNotDisposed();

                if (_wizardCts != null)
                    return;

                var wizardCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetimeCts.Token);

                IGameModeSession session;
                try
                {
                    session = _sessionFactory();
                }
                catch
                {
                    wizardCts.Dispose();
                    throw;
                }

                if (session == null)
                {
                    wizardCts.Dispose();
                    throw new InvalidOperationException("Session factory returned null.");
                }

                _wizardCts = wizardCts;
                wizardToken = wizardCts.Token;
                _session = session;
                Volatile.Write(ref _isActiveFlag, 1);

                _currentError.Value = null;
                _step = WizardStep.None;

                SetIsTransitioning(false);
                SetIsSubmitting(false);
                Volatile.Write(ref _hasPendingOrInFlightIntentFlag, 0);
                Volatile.Write(ref _isReadyForIntentsFlag, 0);
                FlushPendingErrorOnMainThread();

                _intentQueue = new WizardIntentQueue();

                _processingTask = ProcessIntentsAsync(wizardToken).AsTask();
            }

            // Open the first step (Mode Selection)
            try
            {
                await _navigator.OpenModeSelectionAsync(wizardToken);

                lock (_lifecycleLock)
                {
                    if (_wizardCts != null)
                    {
                        _step = WizardStep.ModeSelection;
                        Volatile.Write(ref _isReadyForIntentsFlag, 1);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Start was cancelled - ensure we cleanup internal state.
                await AbortWizardCoreAsync(AbortReason.StartCancelled, awaitProcessingTask: false);
                throw;
            }
            catch
            {
                // If opening the very first window failed, do not leak a live session/queue.
                await AbortWizardCoreAsync(AbortReason.Error, awaitProcessingTask: false);
                throw;
            }
        }

        public bool TryPublishIntent(WizardIntent intent)
        {
            EnsureNotDisposed();

            if (intent == WizardIntent.Cancel)
            {
                // Cancel must always work and must interrupt in-flight navigation.
                // We intentionally handle it out-of-band to cancel current CTS.
                GameLog.Debug($"[GameModeWizardCoordinator] Cancel requested.");
                AbortWizardAsync(AbortReason.UserCancel).Forget(ex => GameLog.Exception(ex));
                return true;
            }

            if (Volatile.Read(ref _isTransitioningFlag) != 0 || Volatile.Read(ref _isSubmittingFlag) != 0)
            {
                GameLog.Debug($"[GameModeWizardCoordinator] Intent ignored due to busy state: {intent}");
                return false;
            }

            if (Volatile.Read(ref _isReadyForIntentsFlag) == 0)
            {
                GameLog.Debug($"[GameModeWizardCoordinator] Intent rejected because wizard is not ready yet: {intent}");
                return false;
            }

            var queue = _intentQueue;
            if (queue == null)
                return false;

            if (Interlocked.CompareExchange(ref _hasPendingOrInFlightIntentFlag, 1, 0) != 0)
            {
                GameLog.Debug($"[GameModeWizardCoordinator] Intent rejected due to pending/in-flight intent: {intent}");
                return false;
            }

            // Do not force callers to be on main thread.
            // UI navigation is marshaled to main thread in TransitionAsync / Abort.

            // Keep queue bounded to avoid memory leaks on intent spam.
            if (!queue.TryEnqueue(intent))
            {
                Interlocked.Exchange(ref _hasPendingOrInFlightIntentFlag, 0);
                // Anti-spam policy: reject if there's already a pending non-cancel intent.
                GameLog.Debug($"[GameModeWizardCoordinator] Intent rejected due to pending intent: {intent}");
                return false;
            }

            return true;
        }

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

            CancellationTokenSource? wizardCts;
            Task? processingTask;
            IGameModeSession? session;
            bool shouldPublishAbort;

            lock (_lifecycleLock)
            {
                wizardCts = _wizardCts;
                processingTask = _processingTask;
                session = _session;

                _wizardCts = null;
                _processingTask = null;
                _intentQueue = null;
                _session = null;
                _step = WizardStep.None;
                CleanupMatchmakingBindings();
                Volatile.Write(ref _isActiveFlag, 0);

                shouldPublishAbort = wizardCts != null || processingTask != null || session != null;
            }

            if (wizardCts == null && processingTask == null && session == null)
            {
                try
                {
                    SetIsTransitioning(false);
                    SetIsSubmitting(false);
                    FlushPendingErrorOnMainThread();
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
                GameLog.Debug($"[GameModeWizardCoordinator] Abort wizard. Reason={reason}");

                wizardCts?.Cancel();

                // Best-effort close:
                // - We must attempt to close even when abort is triggered off-main-thread.
                // - We also must avoid hanging forever during shutdown.
                if (!PlayerLoopHelper.IsMainThread)
                {
                    var switched = await TrySwitchToMainThreadWithTimeoutAsync(AbortSwitchToMainThreadTimeout);
                    if (!switched)
                        GameLog.Warning("[GameModeWizardCoordinator] Failed to switch to main thread to close wizard windows (timeout/shutdown). Windows may remain open.");
                }

                if (PlayerLoopHelper.IsMainThread)
                {
                    using var closeCts = new CancellationTokenSource(AbortCloseWindowsTimeout);
                    try
                    {
                        await _navigator.CloseAllWizardWindowsAsync(closeCts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        // Expected on timeout/shutdown.
                    }
                }

                if (awaitProcessingTask && processingTask != null)
                {
                    try
                    {
                        await processingTask;
                    }
                    catch (OperationCanceledException)
                    {
                        // Expected due to wizard cancellation.
                    }
                }
            }
            catch (Exception ex)
            {
                TrySetCurrentError(WizardError.FromException(ex));
                GameLog.Exception(ex);
            }
            finally
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

                // Must never hang in finally.
                SetIsTransitioning(false);
                SetIsSubmitting(false);
                FlushPendingErrorOnMainThread();
                if (shouldPublishAbort)
                    PublishWizardAborted(reason);
                Interlocked.Exchange(ref _abortInProgress, 0);
            }
        }

        private async UniTask ProcessIntentsAsync(CancellationToken ct)
        {
            var queue = _intentQueue;
            if (queue == null)
                return;

            _isInProcessingLoop.Value = true;

            try
            {
                // Enforce main thread for coordinator state changes.
                await UniTask.SwitchToMainThread(ct);
                FlushPendingErrorOnMainThread();

                while (!ct.IsCancellationRequested)
                {
                    WizardIntent intent;

                    try
                    {
                        intent = await queue.DequeueAsync(ct);
                        await UniTask.SwitchToMainThread(ct);
                        FlushPendingErrorOnMainThread();
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }

                    try
                    {
                        if ((Volatile.Read(ref _isTransitioningFlag) != 0 || Volatile.Read(ref _isSubmittingFlag) != 0) && intent != WizardIntent.Cancel)
                            continue;

                        switch (intent)
                        {
                            case WizardIntent.Continue:
                            case WizardIntent.Back:
                            case WizardIntent.Start:
                                await HandleNonCancelIntentAsync(intent, ct);
                                break;

                            case WizardIntent.Cancel:
                                // Cancel is handled out-of-band in TryPublishIntent.
                                break;

                            default:
                                throw new ArgumentOutOfRangeException(nameof(intent), intent, null);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        TrySetCurrentError(WizardError.FromException(ex));
                        GameLog.Exception(ex);

                        // Best-effort abort to avoid zombie wizard.
                        await AbortWizardCoreAsync(AbortReason.Error, awaitProcessingTask: false);
                        break;
                    }
                    finally
                    {
                        Interlocked.Exchange(ref _hasPendingOrInFlightIntentFlag, 0);
                    }
                }
            }
            finally
            {
                _isInProcessingLoop.Value = false;
            }
        }

        private async UniTask HandleNonCancelIntentAsync(WizardIntent intent, CancellationToken ct)
        {
            await UniTask.SwitchToMainThread(ct);
            FlushPendingErrorOnMainThread();

            if (Volatile.Read(ref _isTransitioningFlag) != 0 || Volatile.Read(ref _isSubmittingFlag) != 0)
                return;

            switch (intent)
            {
                case WizardIntent.Continue:
                    if (_step != WizardStep.ModeSelection)
                        return;

                    await TransitionAsync(
                        close: _navigator.CloseModeSelectionAsync,
                        open: _navigator.OpenMatchSetupAsync,
                        ct: ct);

                    _step = WizardStep.MatchSetup;
                    return;

                case WizardIntent.Back:
                    if (_step != WizardStep.MatchSetup)
                        return;

                    await TransitionAsync(
                        close: _navigator.CloseMatchSetupAsync,
                        open: _navigator.OpenModeSelectionAsync,
                        ct: ct);

                    _step = WizardStep.ModeSelection;
                    return;

                case WizardIntent.Start:
                    if (_step != WizardStep.MatchSetup)
                        return;
                    if (TryGetSessionSnapshot(out var snapshot) && snapshot.OpponentType == OpponentType.Human && snapshot.HumanOpponentKind == HumanOpponentKind.Matchmaking)
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

                    PublishGameLaunchRequested(launchConfig);

                    SetIsSubmitting(true);
                    try
                    {
                        await AbortWizardCoreAsync(AbortReason.GameStarted, awaitProcessingTask: false);
                    }
                    finally
                    {
                        SetIsSubmitting(false);
                    }
                    return;

                default:
                    throw new ArgumentOutOfRangeException(nameof(intent), intent, null);
            }
        }

        private async UniTask OpenMatchmakingAsync(GameModeSessionSnapshot snapshot, CancellationToken ct)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));

            if (string.IsNullOrWhiteSpace(snapshot.SelectedModeId) || snapshot.ModeConfig == null)
            {
                TrySetCurrentError(new WizardError(
                    code: "wizard.mode_config_required",
                    messageKey: "Errors.GameModeWizard.ModeConfigRequired",
                    isBlocking: true,
                    displayType: ErrorDisplayType.Modal));
                return;
            }

            await TransitionAsync(
                close: _navigator.CloseMatchSetupAsync,
                open: async token =>
                {
                    var viewModel = await _navigator.OpenMatchmakingAsync(token);
                    if (viewModel == null)
                        throw new InvalidOperationException("Matchmaking ViewModel is not available.");

                    BindMatchmakingViewModel(viewModel, snapshot, token);
                },
                ct: ct);

            _step = WizardStep.Matchmaking;
        }

        private void BindMatchmakingViewModel(MatchmakingViewModel viewModel, GameModeSessionSnapshot snapshot, CancellationToken ct)
        {
            CleanupMatchmakingBindings();

            _matchmakingViewModel = viewModel;
            _matchmakingSubscriptions = new CompositeDisposable();

            viewModel.CancelRequested
                .Subscribe(_ => CloseMatchmakingToSetupAsync(ct).Forget(LogForgetException))
                .AddTo(_matchmakingSubscriptions);

            viewModel.BackRequested
                .Subscribe(_ => CloseMatchmakingToSetupAsync(ct).Forget(LogForgetException))
                .AddTo(_matchmakingSubscriptions);

            viewModel.RetryRequested
                .Subscribe(_ => TryRestartMatchmaking(snapshot, ct))
                .AddTo(_matchmakingSubscriptions);

            viewModel.State
                .Subscribe(state => HandleMatchmakingStateChanged(state, ct).Forget(LogForgetException))
                .AddTo(_matchmakingSubscriptions);

            viewModel.Result
                .Subscribe(UpdateMatchmakingResult)
                .AddTo(_matchmakingSubscriptions);

            UpdateMatchmakingResult(null);
            viewModel.BeginSearch(new MatchmakingRequest(snapshot.SelectedModeId, snapshot.ModeConfig), ct);
        }

        private void TryRestartMatchmaking(GameModeSessionSnapshot snapshot, CancellationToken ct)
        {
            if (_matchmakingViewModel == null)
                return;

            if (string.IsNullOrWhiteSpace(snapshot.SelectedModeId) || snapshot.ModeConfig == null)
            {
                TrySetCurrentError(new WizardError(
                    code: "wizard.mode_config_required",
                    messageKey: "Errors.GameModeWizard.ModeConfigRequired",
                    isBlocking: true,
                    displayType: ErrorDisplayType.Modal));

                CloseMatchmakingToSetupAsync(ct).Forget(LogForgetException);
                return;
            }

            var request = new MatchmakingRequest(snapshot.SelectedModeId, snapshot.ModeConfig);
            UpdateMatchmakingResult(null);
            _matchmakingViewModel.BeginSearch(request, ct);
        }

        private async UniTask HandleMatchmakingStateChanged(MatchmakingState state, CancellationToken ct)
        {
            if (_step != WizardStep.Matchmaking)
                return;

            switch (state)
            {
                case MatchmakingState.Found:
                    await UniTask.Delay(MatchmakingFoundAutoCloseDelay, cancellationToken: ct);
                    await CloseMatchmakingAndStartAsync(ct);
                    break;

                case MatchmakingState.Cancelled:
                    await CloseMatchmakingToSetupAsync(ct);
                    break;
            }
        }

        private static void LogForgetException(Exception ex)
        {
            if (ex is OperationCanceledException)
                return;

            GameLog.Exception(ex);
        }

        private async UniTask CloseMatchmakingToSetupAsync(CancellationToken ct)
        {
            if (_step != WizardStep.Matchmaking)
                return;

            if (Interlocked.Exchange(ref _matchmakingCloseInProgress, 1) != 0)
                return;

            try
            {
                await TransitionAsync(
                    close: _navigator.CloseMatchmakingAsync,
                    open: _navigator.OpenMatchSetupAsync,
                    ct: ct);

                CleanupMatchmakingBindings();
                _step = WizardStep.MatchSetup;
            }
            finally
            {
                Interlocked.Exchange(ref _matchmakingCloseInProgress, 0);
            }
        }

        private async UniTask CloseMatchmakingAndStartAsync(CancellationToken ct)
        {
            if (_step != WizardStep.Matchmaking)
                return;

            if (Interlocked.Exchange(ref _matchmakingCloseInProgress, 1) != 0)
                return;

            try
            {
                if (!TryBuildLaunchConfig(out var launchConfig, out var error))
                {
                    if (error != null)
                        TrySetCurrentError(error);

                    await TransitionAsync(
                        close: _navigator.CloseMatchmakingAsync,
                        open: _navigator.OpenMatchSetupAsync,
                        ct: ct);

                    CleanupMatchmakingBindings();
                    _step = WizardStep.MatchSetup;
                    return;
                }

                await TransitionAsync(
                    close: _navigator.CloseMatchmakingAsync,
                    open: _ => UniTask.CompletedTask,
                    ct: ct);

                CleanupMatchmakingBindings();

                PublishGameLaunchRequested(launchConfig);

                SetIsSubmitting(true);
                try
                {
                    await AbortWizardCoreAsync(AbortReason.GameStarted, awaitProcessingTask: false);
                }
                finally
                {
                    SetIsSubmitting(false);
                }
            }
            finally
            {
                Interlocked.Exchange(ref _matchmakingCloseInProgress, 0);
            }
        }

        private bool TryBuildLaunchConfig(out GameLaunchConfig launchConfig, out WizardError? error)
        {
            launchConfig = null;
            error = null;

            var session = _session;
            if (session == null)
            {
                error = new WizardError(
                    code: "wizard.session_missing",
                    messageKey: "Errors.GameModeWizard.UnhandledException",
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

        private static WizardError CreateWizardErrorFromValidation(IReadOnlyList<ValidationError> errors)
        {
            if (errors == null || errors.Count == 0)
            {
                return new WizardError(
                    code: "wizard.validation_failed",
                    messageKey: "Errors.GameModeWizard.UnhandledException",
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
                WizardFieldNames.ModeCatalog => ErrorDisplayType.Modal,
                _ => ErrorDisplayType.Inline
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

        private void CleanupMatchmakingBindings()
        {
            _matchmakingSubscriptions?.Dispose();
            _matchmakingSubscriptions = null;
            _matchmakingViewModel = null;
            UpdateMatchmakingResult(null);
            Interlocked.Exchange(ref _matchmakingCloseInProgress, 0);
        }

        private void UpdateMatchmakingResult(MatchmakingResult? result)
        {
            if (_session == null)
                return;

            _session.Update(s =>
                s.WithMatchmakingResult(result?.MatchId, result?.OpponentId));
        }

        private bool TryGetSessionSnapshot(out GameModeSessionSnapshot snapshot)
        {
            snapshot = default!;

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
            Func<CancellationToken, UniTask> close,
            Func<CancellationToken, UniTask> open,
            CancellationToken ct)
        {
            if (close == null)
                throw new ArgumentNullException(nameof(close));
            if (open == null)
                throw new ArgumentNullException(nameof(open));

            if (Volatile.Read(ref _isTransitioningFlag) != 0)
                return;

            await UniTask.SwitchToMainThread(ct);
            FlushPendingErrorOnMainThread();
            SetIsTransitioning(true);

            try
            {
                await close(ct);
                await open(ct);
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
                throw new ObjectDisposedException(nameof(GameModeWizardCoordinator));
        }

        private sealed class WizardIntentQueue
        {
            private readonly object _lock = new();
            private WizardIntent? _pendingIntent;
            private UniTaskCompletionSource<bool>? _signal;

            public bool TryEnqueue(WizardIntent intent)
            {
                lock (_lock)
                {
                    // Anti-spam policy: we only allow a single pending non-cancel intent.
                    // This keeps memory bounded while guaranteeing that accepted intents are not silently dropped.
                    if (_pendingIntent.HasValue)
                        return false;

                    _pendingIntent = intent;
                    // Signal waiter but do NOT clear _signal here - consumer owns clearing it.
                    _signal?.TrySetResult(true);
                    return true;
                }
            }

            public async UniTask<WizardIntent> DequeueAsync(CancellationToken ct)
            {
                while (true)
                {
                    UniTask waitTask;

                    lock (_lock)
                    {
                        if (_pendingIntent.HasValue)
                        {
                            var intent = _pendingIntent.Value;
                            _pendingIntent = null;
                            return intent;
                        }

                        _signal ??= new UniTaskCompletionSource<bool>();
                        waitTask = _signal.Task;
                    }

                    await waitTask.AttachExternalCancellation(ct);

                    // Consumer owns clearing the signal after awaiting it.
                    // This prevents race where TryEnqueue clears signal before we consume the item.
                    lock (_lock)
                    {
                        _signal = null;
                    }
                }
            }
        }
    }
}
