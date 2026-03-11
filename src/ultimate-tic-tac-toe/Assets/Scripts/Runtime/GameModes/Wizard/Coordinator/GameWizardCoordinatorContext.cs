#nullable enable

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using R3;
using Runtime.GameModes.Wizard.Configs;
using Runtime.GameModes.Wizard.Matchmaking.Contracts;
using Runtime.GameModes.Wizard.Online;
using Runtime.GameModes.Wizard.Session;

namespace Runtime.GameModes.Wizard.Coordinator
{
    internal sealed class GameWizardCoordinatorContext
    {
        internal enum WizardStep
        {
            None = 0,
            ModeSelection = 1,
            MatchSetup = 2,
            Matchmaking = 3,
        }

        private readonly object _lifecycleLock = new();
        private readonly CancellationTokenSource _lifetimeCts = new();
        private readonly CancellationToken _lifetimeToken;
        private readonly Func<IGameSession> _sessionFactory;
        private readonly GameWizardLaunchConfigResolver _launchConfigResolver = new();

        private int _hasPendingOrInFlightIntentFlag;
        private int _isReadyForIntentsFlag;
        private int _isActiveFlag;
        private int _abortInProgress;
        private int _isDisposedFlag;
        private CancellationTokenSource? _wizardCts;

        internal GameWizardCoordinatorContext(
            IGameWizardNavigator navigator,
            Func<IGameSession> sessionFactory,
            IOnlineSessionFlowService onlineSessionFlow,
            IMatchmakingService? matchmakingService)
        {
            Navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));
            OnlineSessionFlow = onlineSessionFlow ?? throw new ArgumentNullException(nameof(onlineSessionFlow));
            MatchmakingService = matchmakingService;
            _sessionFactory = sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory));
            _lifetimeToken = _lifetimeCts.Token;
            Signals = new GameWizardCoordinatorSignals();
        }

        internal IGameWizardNavigator Navigator { get; }
        internal IOnlineSessionFlowService OnlineSessionFlow { get; }
        internal IMatchmakingService? MatchmakingService { get; }
        internal GameWizardCoordinatorSignals Signals { get; }
        internal AsyncLocal<bool> IsInProcessingLoop { get; } = new();

        internal WizardStep Step { get; set; }
        internal CancellationToken LifetimeToken => _lifetimeToken;
        internal WizardIntentQueue? IntentQueue { get; private set; }
        internal Task? ProcessingTask { get; private set; }
        internal bool IsDisposed => Volatile.Read(ref _isDisposedFlag) != 0;
        internal IGameSession Session => SessionOrNull ?? throw new InvalidOperationException("Wizard is not active.");
        internal bool IsActive => Volatile.Read(ref _isActiveFlag) != 0;
        internal bool IsBusy => Signals.IsBusy;
        internal bool IsReadyForIntents => Volatile.Read(ref _isReadyForIntentsFlag) != 0;
        internal ReadOnlyReactiveProperty<bool> IsTransitioning => Signals.IsTransitioning;
        internal ReadOnlyReactiveProperty<bool> IsSubmitting => Signals.IsSubmitting;
        internal ReadOnlyReactiveProperty<WizardError?> CurrentError => Signals.CurrentError;
        internal Observable<GameLaunchConfig> GameLaunchRequested => Signals.GameLaunchRequested;
        internal Observable<AbortReason> WizardAborted => Signals.WizardAborted;

        private IGameSession? SessionOrNull { get; set; }

        internal void EnsureNotDisposed()
        {
            if (IsDisposed)
                throw new ObjectDisposedException(nameof(GameWizardCoordinator));
        }

        internal bool TryDispose()
        {
            if (Interlocked.Exchange(ref _isDisposedFlag, 1) != 0)
                return false;

            _lifetimeCts.Cancel();
            _lifetimeCts.Dispose();
            return true;
        }

        internal bool TryGetSession([NotNullWhen(true)] out IGameSession? session)
        {
            if (Volatile.Read(ref _abortInProgress) != 0)
            {
                session = null;
                return false;
            }

            lock (_lifecycleLock)
            {
                session = SessionOrNull;
                return session != null;
            }
        }

        internal bool TryInitializeWizardSession(
            CancellationToken ct,
            Func<CancellationToken, UniTask> processIntentsAsync,
            out CancellationToken wizardToken)
        {
            lock (_lifecycleLock)
            {
                EnsureNotDisposed();

                if (_wizardCts != null)
                {
                    wizardToken = CancellationToken.None;
                    return false;
                }

                var wizardCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetimeToken);

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
                SessionOrNull = session;
                Volatile.Write(ref _isActiveFlag, 1);

                PrepareForNewWizardSession();
                Step = WizardStep.None;
                ResetIntentState();

                IntentQueue = new WizardIntentQueue();
                ProcessingTask = processIntentsAsync(wizardCts.Token).AsTask();
                wizardToken = wizardCts.Token;
                return true;
            }
        }

        internal void MarkModeSelectionOpened()
        {
            lock (_lifecycleLock)
            {
                if (_wizardCts == null)
                    return;

                Step = WizardStep.ModeSelection;
                Volatile.Write(ref _isReadyForIntentsFlag, 1);
            }
        }

        internal bool TryReserveIntentSlot() =>
            Interlocked.CompareExchange(ref _hasPendingOrInFlightIntentFlag, 1, 0) == 0;

        internal void ReleaseIntentSlot() =>
            Interlocked.Exchange(ref _hasPendingOrInFlightIntentFlag, 0);

        internal bool TryBeginAbort() =>
            Interlocked.Exchange(ref _abortInProgress, 1) == 0;

        internal void EndAbort() =>
            Interlocked.Exchange(ref _abortInProgress, 0);

        internal void ResetIntentState()
        {
            Volatile.Write(ref _isReadyForIntentsFlag, 0);
            Volatile.Write(ref _hasPendingOrInFlightIntentFlag, 0);
        }

        internal (CancellationTokenSource? WizardCts, Task? ProcessingTask, IGameSession? Session, bool ShouldPublishAbort)
            DetachWizardState(Action cleanupBeforeDetach)
        {
            lock (_lifecycleLock)
            {
                var wizardCts = _wizardCts;
                var processingTask = ProcessingTask;
                var session = SessionOrNull;

                _wizardCts = null;
                ProcessingTask = null;
                IntentQueue = null;
                cleanupBeforeDetach();
                SessionOrNull = null;
                Step = WizardStep.None;
                Volatile.Write(ref _isActiveFlag, 0);

                var shouldPublishAbort = wizardCts != null || processingTask != null || session != null;
                return (wizardCts, processingTask, session, shouldPublishAbort);
            }
        }

        internal void UpdateSession(Func<GameSessionSnapshot, GameSessionSnapshot> reducer)
        {
            if (reducer == null)
                throw new ArgumentNullException(nameof(reducer));

            SessionOrNull?.Update(reducer);
        }

        internal bool TryGetSessionSnapshot(out GameSessionSnapshot snapshot)
        {
            snapshot = null!;

            if (SessionOrNull == null)
                return false;

            try
            {
                snapshot = SessionOrNull.Snapshot.CurrentValue;
                return snapshot != null;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
        }

        internal bool TryBuildLaunchConfig(out GameLaunchConfig? launchConfig, out WizardError? error)
            => _launchConfigResolver.TryBuild(SessionOrNull, out launchConfig, out error);

        internal async UniTask TransitionAsync(Func<CancellationToken, UniTask> transition, CancellationToken ct)
        {
            if (transition == null)
                throw new ArgumentNullException(nameof(transition));

            if (Signals.IsTransitioningActive)
                return;

            await UniTask.SwitchToMainThread(ct);
            Signals.FlushPendingErrorOnMainThread();
            Signals.SetIsTransitioning(true);

            try
            {
                await transition(ct);
            }
            finally
            {
                Signals.SetIsTransitioning(false);
            }
        }

        private void PrepareForNewWizardSession()
        {
            Signals.ClearCurrentErrorValue();
            Signals.ResetBusyState();
        }
    }
}