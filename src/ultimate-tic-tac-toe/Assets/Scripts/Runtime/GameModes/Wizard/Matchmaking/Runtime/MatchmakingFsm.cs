#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using R3;
using Runtime.GameModes.Wizard.Matchmaking.Config;
using Runtime.GameModes.Wizard.Matchmaking.Contracts;
using Runtime.GameModes.Wizard.Session;
using Runtime.Infrastructure.Logging;

namespace Runtime.GameModes.Wizard.Matchmaking.Runtime
{
    /// <summary>
    /// Matchmaking finite state machine.
    /// Source of truth for matchmaking state; external sync must be one-way.
    /// All reactive updates are executed on main thread.
    /// </summary>
    public sealed class MatchmakingFsm : IDisposable
    {
        private static readonly TimeSpan _defaultTimeoutValue = TimeSpan.FromSeconds(60);
        private const string _nullQueueEntryErrorMessage = "Matchmaking service returned null queue entry.";
        private const string _nullResultErrorMessage = "Matchmaking service returned null result.";

        private readonly IMatchmakingService _service;
        private readonly TimeSpan _defaultTimeout;
        private readonly TimeSpan _cancelAckTimeout;
        private readonly object _lock = new();
        private readonly MatchmakingSearchRun _searchRun = new();
        private readonly MatchmakingStateStore _stateStore = new();

        private int _isDisposed;

        public ReadOnlyReactiveProperty<MatchmakingState> State { get; }
        public ReadOnlyReactiveProperty<MatchmakingFailure?> Failure { get; }
        public ReadOnlyReactiveProperty<MatchmakingResult?> Result { get; }

        public MatchmakingState CurrentState
        {
            get
            {
                lock (_lock)
                {
                    return _stateStore.CurrentState;
                }
            }
        }

        internal int SearchEpochForTests
        {
            get
            {
                lock (_lock)
                {
                    EnsureNotDisposed();
                    return _searchRun.Epoch;
                }
            }
        }

        public MatchmakingFsm(IMatchmakingService service)
            : this(service, _defaultTimeoutValue) { }

        public MatchmakingFsm(IMatchmakingService service, IMatchmakingConfig config)
            : this(service, config, ResolveDefaultTimeout(config)) { }

        public MatchmakingFsm(IMatchmakingService service, TimeSpan defaultTimeout)
            : this(service, new MatchmakingConfigDefaults(), defaultTimeout) { }

        private static TimeSpan ResolveDefaultTimeout(IMatchmakingConfig? config) =>
            config?.SearchTimeout ?? _defaultTimeoutValue;

        private MatchmakingFsm(IMatchmakingService service, IMatchmakingConfig config, TimeSpan defaultTimeout)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));

            if (config == null)
                throw new ArgumentNullException(nameof(config));

            if (defaultTimeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(defaultTimeout), "Timeout must be positive.");

            _defaultTimeout = defaultTimeout;
            _cancelAckTimeout = config.CancelAckTimeout;
            State = _stateStore.State;
            Failure = _stateStore.Failure;
            Result = _stateStore.Result;
        }

        /// <summary>
        /// Attempts to start a search. Returns false if a search is already running.
        /// </summary>
        public UniTask<bool> TryStartSearchAsync(MatchmakingRequest request, CancellationToken ct) =>
            TryStartSearchAsync(request, _defaultTimeout, ct);

        public UniTask<bool> TryStartSearchFromQueueEntryAsync(QueueEntry queueEntry, CancellationToken ct) =>
            TryStartSearchFromQueueEntryAsync(queueEntry, _defaultTimeout, ct);

        public async UniTask<bool> TryStartSearchAsync(MatchmakingRequest request, TimeSpan timeout, CancellationToken ct)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            ValidateTimeout(timeout);

            await UniTask.SwitchToMainThread(ct);

            if (!TryPrepareSearchStart(timeout, out var epoch, out var userCancelToken, out var timeoutToken))
                return false;

            var queueEntry = await TryEnterQueueAsync(request, epoch, ct, userCancelToken, timeoutToken);
            return queueEntry != null && TryActivateSearch(epoch, queueEntry, ct, userCancelToken, timeoutToken);
        }

        private async UniTask<bool> TryStartSearchFromQueueEntryAsync(QueueEntry queueEntry, TimeSpan timeout, CancellationToken ct)
        {
            if (queueEntry == null)
                throw new ArgumentNullException(nameof(queueEntry));

            ValidateTimeout(timeout);

            await UniTask.SwitchToMainThread(ct);

            return TryPrepareSearchStart(timeout, out var epoch, out var userCancelToken, out var timeoutToken) 
                   && TryActivateSearch(epoch, queueEntry, ct, userCancelToken, timeoutToken);
        }

        private static void ValidateTimeout(TimeSpan timeout)
        {
            if (timeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be positive.");
        }

        private bool TryPrepareSearchStart(
            TimeSpan timeout,
            out int epoch,
            out CancellationToken userCancelToken,
            out CancellationToken timeoutToken)
        {
            lock (_lock)
            {
                EnsureNotDisposed();

                if (IsStartBlockedUnsafe())
                {
                    epoch = 0;
                    userCancelToken = CancellationToken.None;
                    timeoutToken = CancellationToken.None;
                    return false;
                }

                CleanupSearchResourcesUnsafe();
                ResetSearchOutputsUnsafe();

                epoch = _searchRun.Begin(timeout);
                userCancelToken = _searchRun.UserCancelToken;
                timeoutToken = _searchRun.TimeoutToken;
                return true;
            }
        }

        private async UniTask<QueueEntry?> TryEnterQueueAsync(
            MatchmakingRequest request,
            int epoch,
            CancellationToken externalCt,
            CancellationToken userCancelCt,
            CancellationToken timeoutCt)
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(externalCt, userCancelCt, timeoutCt);

            try
            {
                var queueEntry = await _service.EnterQueueAsync(request, linkedCts.Token);
                
                if (queueEntry != null)
                    return queueEntry;
            }
            catch (OperationCanceledException)
            {
                await UniTask.SwitchToMainThread();
                await ApplySearchCancellationAsync(epoch, externalCt, userCancelCt, timeoutCt);
                return null;
            }
            catch (Exception ex)
            {
                await UniTask.SwitchToMainThread();
                ApplySearchError(epoch, ex);
                return null;
            }

            await UniTask.SwitchToMainThread();
            ApplySearchError(epoch, new InvalidOperationException(_nullQueueEntryErrorMessage));
            return null;
        }

        private bool IsStartBlockedUnsafe() =>
            _stateStore.IsStartBlocked;

        private void ResetSearchOutputsUnsafe() => _stateStore.ResetSearchOutputs();

        private async UniTask RunSearchAsync(
            int capturedEpoch,
            QueueEntry queueEntry,
            CancellationToken lifetimeCt,
            CancellationToken userCancelCt,
            CancellationToken timeoutCt)
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(lifetimeCt, userCancelCt, timeoutCt);

            try
            {
                var result = queueEntry is { IsPaired: true, ImmediateResult: not null }
                    ? queueEntry.ImmediateResult
                    : await _service.WaitForMatchAsync(queueEntry, linkedCts.Token);

                if (result == null)
                    throw new InvalidOperationException(_nullResultErrorMessage);

                await UniTask.SwitchToMainThread();
                ApplySearchSuccess(capturedEpoch, result);
            }
            catch (OperationCanceledException)
            {
                await UniTask.SwitchToMainThread();
                await ApplySearchCancellationAsync(capturedEpoch, lifetimeCt, userCancelCt, timeoutCt);
            }
            catch (Exception ex)
            {
                await UniTask.SwitchToMainThread();
                ApplySearchError(capturedEpoch, ex);
            }
        }

        private void ApplySearchSuccess(int capturedEpoch, MatchmakingResult result)
            => ApplySearchTransition(capturedEpoch, result, static (stateStore, matchResult) => stateStore.ApplyFound(matchResult));

        internal void ApplySearchSuccessForTests(int capturedEpoch, MatchmakingResult result) =>
            ApplySearchSuccess(capturedEpoch, result);

        private async UniTask ApplySearchCancellationAsync(
            int capturedEpoch,
            CancellationToken externalCt,
            CancellationToken userCancelCt,
            CancellationToken timeoutCt)
        {
            MatchmakingState stateSnapshot;

            lock (_lock)
            {
                if (_isDisposed != 0 || ShouldIgnoreTransitionUnsafe(capturedEpoch))
                    return;

                stateSnapshot = _stateStore.CurrentState;
            }

            if (stateSnapshot == MatchmakingState.CancelPending)
            {
                var pendingOutcome = await CompleteCancelAckAsync();
                ApplyCancelOutcome(capturedEpoch, pendingOutcome);
                return;
            }

            var timeoutRequested = timeoutCt.IsCancellationRequested;
            var externalCancelled = externalCt.IsCancellationRequested;
            var userCancelled = userCancelCt.IsCancellationRequested;

            if (externalCancelled)
            {
                RequestBestEffortLeave();
                ApplyExternalCancellation(capturedEpoch);
                return;
            }

            if (userCancelled)
            {
                var outcome = await CompleteCancelAckAsync();
                ApplyCancelOutcome(capturedEpoch, outcome);
                return;
            }

            if (timeoutRequested)
            {
                RequestBestEffortLeave();
                ApplySearchTimeout(capturedEpoch);
                return;
            }

            ApplySearchError(capturedEpoch, new Exception("Unexpected matchmaking cancellation."));
        }

        private UniTask<CancelAckOutcome> CompleteCancelAckAsync() =>
            MatchmakingLeaveProtocol.ExecuteCancelAckAsync(_service, _cancelAckTimeout);

        private void ApplyCancelOutcome(int capturedEpoch, CancelAckOutcome outcome)
            => ApplySearchTransition(capturedEpoch, outcome, static (stateStore, cancelOutcome) => stateStore.ApplyCancelOutcome(cancelOutcome));

        private void ApplySearchError(int capturedEpoch, Exception ex)
            => ApplySearchTransition(capturedEpoch, ex, static (stateStore, error) => stateStore.ApplyError(error));

        /// <summary>
        /// Cancels current search with cancel-ack protocol.
        /// </summary>
        public async UniTask RequestCancelAsync()
        {
            await UniTask.SwitchToMainThread();
            int epoch;
            Task? waitTask;

            lock (_lock)
            {
                EnsureNotDisposed();

                if (_stateStore.CurrentState is MatchmakingState.Found or MatchmakingState.TerminalModal)
                    return;

                if (_stateStore.CurrentState != MatchmakingState.Searching)
                    return;

                _stateStore.SetCancelPending();
                _searchRun.RequestUserCancel();
                epoch = _searchRun.Epoch;
                waitTask = _searchRun.WaitTask;
            }

            if (waitTask != null)
            {
                try
                {
                    await waitTask.AsUniTask();
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    GameLog.Exception(ex);
                }

                var shouldCompleteAck = false;

                lock (_lock)
                {
                    if (epoch == _searchRun.Epoch && _stateStore.CurrentState == MatchmakingState.CancelPending)
                        shouldCompleteAck = true;
                }

                if (shouldCompleteAck)
                {
                    var outcome = await CompleteCancelAckAsync();
                    ApplyCancelOutcome(epoch, outcome);
                }
            }
        }

        public void NotifySessionStartFailed()
        {
            lock (_lock)
            {
                EnsureNotDisposed();

                if (!_stateStore.TryApplySessionStartFailed())
                    return;

                CleanupSearchResourcesUnsafe();
            }
        }

        public void AcknowledgeTerminalModal()
        {
            lock (_lock)
            {
                EnsureNotDisposed();
                _stateStore.TryAcknowledgeTerminalModal();
            }
        }

        public void Cancel()
        {
            lock (_lock)
            {
                EnsureNotDisposed();

                if (PlayerLoopHelper.IsMainThread && !_stateStore.IsSearchActive)
                    return;

                _searchRun.CancelAll();
            }
        }

        private void ApplyExternalCancellation(int capturedEpoch)
            => ApplySearchTransition(capturedEpoch, static stateStore => stateStore.ApplyExternalCancellation());

        private void ApplySearchTimeout(int capturedEpoch)
            => ApplySearchTransition(capturedEpoch, static stateStore => stateStore.ApplySearchTimeout());

        private void ApplySearchTransition(int capturedEpoch, Action<MatchmakingStateStore> transition)
        {
            lock (_lock)
            {
                if (_isDisposed != 0 || ShouldIgnoreTransitionUnsafe(capturedEpoch))
                    return;

                transition(_stateStore);
                CleanupSearchResourcesUnsafe();
            }
        }

        private void ApplySearchTransition<TState>(
            int capturedEpoch,
            TState state,
            Action<MatchmakingStateStore, TState> transition)
        {
            lock (_lock)
            {
                if (_isDisposed != 0 || ShouldIgnoreTransitionUnsafe(capturedEpoch))
                    return;

                transition(_stateStore, state);
                CleanupSearchResourcesUnsafe();
            }
        }

        private bool ShouldIgnoreTransitionUnsafe(int capturedEpoch) =>
            capturedEpoch != _searchRun.Epoch || _stateStore.IsTerminalForEpoch;

        private void RequestBestEffortLeave() =>
            MatchmakingLeaveProtocol.RequestBestEffortLeaveAsync(_service, _cancelAckTimeout).Forget();

        private bool TryActivateSearch(
            int epoch,
            QueueEntry queueEntry,
            CancellationToken lifetimeCt,
            CancellationToken userCancelCt,
            CancellationToken timeoutCt)
        {
            lock (_lock)
            {
                if (_isDisposed != 0)
                    return false;

                if (epoch != _searchRun.Epoch)
                    return false;

                if (_stateStore.CurrentState == MatchmakingState.CancelPending)
                    return false;

                if (queueEntry is { IsPaired: true, ImmediateResult: not null })
                {
                    _stateStore.ApplyFound(queueEntry.ImmediateResult);
                    CleanupSearchResourcesUnsafe();
                    return true;
                }

                _stateStore.SetSearching();
                _searchRun.SetWaitTask(RunSearchAsync(epoch, queueEntry, lifetimeCt, userCancelCt, timeoutCt).AsTask());
                return true;
            }
        }

        private void CleanupSearchResourcesUnsafe()
            => _searchRun.Clear();

        public void Dispose()
        {
            if (Volatile.Read(ref _isDisposed) != 0)
                return;

            lock (_lock)
            {
                if (Volatile.Read(ref _isDisposed) != 0)
                    return;

                Volatile.Write(ref _isDisposed, 1);
                _searchRun.Dispose();
            }

            _stateStore.Dispose();
        }

        private void EnsureNotDisposed()
        {
            if (Volatile.Read(ref _isDisposed) != 0)
                throw new ObjectDisposedException(nameof(MatchmakingFsm));
        }
    }
}