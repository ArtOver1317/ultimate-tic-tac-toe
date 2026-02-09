#nullable enable

using System;
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
    /// UI specifics are delegated to <see cref="IGameWizardNavigator"/>.
    /// </summary>
    public sealed partial class GameWizardCoordinator : IGameWizardCoordinator, IDisposable
    {
        private static readonly TimeSpan _abortSwitchToMainThreadTimeout = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan _matchmakingFoundAutoCloseDelay = TimeSpan.FromMilliseconds(450);
        private static readonly TimeSpan _abortCloseWindowsTimeout = TimeSpan.FromSeconds(2);

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

        private readonly IGameWizardNavigator _navigator;
        private readonly Func<IGameSession> _sessionFactory;

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
        private readonly CancellationTokenSource _lifetimeCts = new();
        private CancellationTokenSource? _wizardCts;

        private WizardIntentQueue? _intentQueue;
        private Task? _processingTask;

        private IGameSession? _session;

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
        public IGameSession Session => _session ?? throw new InvalidOperationException("Wizard is not active.");
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

        public bool TryGetSession([NotNullWhen(true)] out IGameSession? session)
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

        public GameWizardCoordinator(IGameWizardNavigator navigator, Func<IGameSession> sessionFactory)
        {
            _navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));
            _sessionFactory = sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory));
        }

        public async UniTask StartWizardAsync(CancellationToken ct)
        {
            EnsureNotDisposed();

            // Wizard is a UI flow; enforce main thread for state mutations.
            await UniTask.SwitchToMainThread(ct);

            if (!TryInitializeWizardSession(ct, out var wizardToken))
                return;

            await OpenFirstStepAsync(wizardToken);
        }

        /// <summary>
        /// Creates session, CTS, intent queue and starts the processing loop.
        /// Must be called under main thread after <see cref="UniTask.SwitchToMainThread"/>.
        /// </summary>
        private bool TryInitializeWizardSession(CancellationToken ct, out CancellationToken wizardToken)
        {
            lock (_lifecycleLock)
            {
                EnsureNotDisposed();

                if (_wizardCts != null)
                {
                    wizardToken = default;
                    return false;
                }

                var wizardCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetimeCts.Token);

                IGameSession session;
                
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
                _session = session;
                Volatile.Write(ref _isActiveFlag, 1);

                _currentError.Value = null;
                _step = WizardStep.None;

                ResetBusyState();
                Volatile.Write(ref _hasPendingOrInFlightIntentFlag, 0);
                Volatile.Write(ref _isReadyForIntentsFlag, 0);

                _intentQueue = new WizardIntentQueue();

                _processingTask = ProcessIntentsAsync(wizardCts.Token).AsTask();
                wizardToken = wizardCts.Token;
                return true;
            }
        }

        private async UniTask OpenFirstStepAsync(CancellationToken wizardToken)
        {
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
                await AbortWizardCoreAsync(AbortReason.StartCancelled, awaitProcessingTask: false);
                throw;
            }
            catch
            {
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
                GameLog.Debug($"[GameWizardCoordinator] Cancel requested.");
                AbortWizardAsync(AbortReason.UserCancel).Forget(ex => GameLog.Exception(ex));
                return true;
            }

            if (Volatile.Read(ref _isTransitioningFlag) != 0 || Volatile.Read(ref _isSubmittingFlag) != 0)
            {
                GameLog.Debug($"[GameWizardCoordinator] Intent ignored due to busy state: {intent}");
                return false;
            }

            if (Volatile.Read(ref _isReadyForIntentsFlag) == 0)
            {
                GameLog.Debug($"[GameWizardCoordinator] Intent rejected because wizard is not ready yet: {intent}");
                return false;
            }

            var queue = _intentQueue;
            
            if (queue == null)
                return false;

            if (Interlocked.CompareExchange(ref _hasPendingOrInFlightIntentFlag, 1, 0) != 0)
            {
                GameLog.Debug($"[GameWizardCoordinator] Intent rejected due to pending/in-flight intent: {intent}");
                return false;
            }

            // Do not force callers to be on main thread.
            // UI navigation is marshaled to main thread in TransitionAsync / Abort.

            // Keep queue bounded to avoid memory leaks on intent spam.
            if (!queue.TryEnqueue(intent))
            {
                Interlocked.Exchange(ref _hasPendingOrInFlightIntentFlag, 0);
                // Anti-spam policy: reject if there's already a pending non-cancel intent.
                GameLog.Debug($"[GameWizardCoordinator] Intent rejected due to pending intent: {intent}");
                return false;
            }

            return true;
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
                        transition: _navigator.ReplaceModeSelectionWithMatchSetupAsync,
                        ct: ct);

                    _step = WizardStep.MatchSetup;
                    return;

                case WizardIntent.Back:
                    if (_step != WizardStep.MatchSetup)
                        return;

                    await TransitionAsync(
                        transition: _navigator.ReplaceMatchSetupWithModeSelectionAsync,
                        ct: ct);

                    _step = WizardStep.ModeSelection;
                    return;

                case WizardIntent.Start:
                    if (_step != WizardStep.MatchSetup)
                        return;

                    await HandleStartIntentAsync(ct);
                    return;

                default:
                    throw new ArgumentOutOfRangeException(nameof(intent), intent, null);
            }
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
