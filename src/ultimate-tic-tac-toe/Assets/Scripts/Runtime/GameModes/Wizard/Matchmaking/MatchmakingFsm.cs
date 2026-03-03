#nullable enable

using System;
using System.Threading.Tasks;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using Runtime.Infrastructure.Logging;

namespace Runtime.GameModes.Wizard
{
    /// <summary>
    /// Matchmaking finite state machine.
    /// Source of truth for matchmaking state; external sync must be one-way.
    /// All reactive updates are executed on main thread.
    /// </summary>
    public sealed class MatchmakingFsm : IDisposable
    {
        private static readonly TimeSpan _defaultTimeoutValue = TimeSpan.FromSeconds(60);

        private readonly IMatchmakingService _service;
        private readonly TimeSpan _defaultTimeout;
        private readonly ReactiveProperty<MatchmakingState> _state;
        private readonly ReactiveProperty<MatchmakingFailure?> _failure;
        private readonly ReactiveProperty<MatchmakingResult?> _result;
        private readonly object _lock = new();
        private readonly IMatchmakingConfig _config;

        private CancellationTokenSource? _userCancelCts;
        private CancellationTokenSource? _timeoutCts;
        private Task? _waitTask;
        private int _searchEpoch;
        private bool _isDisposed;

        public ReadOnlyReactiveProperty<MatchmakingState> State => _state;
        public ReadOnlyReactiveProperty<MatchmakingFailure?> Failure => _failure;
        public ReadOnlyReactiveProperty<MatchmakingResult?> Result => _result;

        public MatchmakingState CurrentState => _state.CurrentValue;
        public bool IsSearching => _state.CurrentValue is MatchmakingState.Searching or MatchmakingState.CancelPending;

        public MatchmakingFsm(IMatchmakingService service)
            : this(service, _defaultTimeoutValue) { }

        public MatchmakingFsm(IMatchmakingService service, IMatchmakingConfig config)
            : this(service, config, config?.SearchTimeout ?? _defaultTimeoutValue) { }

        public MatchmakingFsm(IMatchmakingService service, TimeSpan defaultTimeout)
            : this(service, new MatchmakingConfigDefaults(), defaultTimeout) { }

        private MatchmakingFsm(IMatchmakingService service, IMatchmakingConfig config, TimeSpan defaultTimeout)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            
            if (defaultTimeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(defaultTimeout), "Timeout must be positive.");

            _defaultTimeout = defaultTimeout;
            _state = new ReactiveProperty<MatchmakingState>(MatchmakingState.Idle);
            _failure = new ReactiveProperty<MatchmakingFailure?>(null);
            _result = new ReactiveProperty<MatchmakingResult?>(null);
        }

        /// <summary>
        /// Best-effort start. If a search is already running, the call is a no-op.
        /// </summary>
        public UniTask StartSearchAsync(MatchmakingRequest request, CancellationToken ct) =>
            StartSearchAsync(request, _defaultTimeout, ct);

        /// <summary>
        /// Attempts to start a search. Returns false if a search is already running.
        /// </summary>
        public UniTask<bool> TryStartSearchAsync(MatchmakingRequest request, CancellationToken ct) =>
            TryStartSearchAsync(request, _defaultTimeout, ct);

        public UniTask<bool> TryStartSearchFromQueueEntryAsync(QueueEntry queueEntry, CancellationToken ct) =>
            TryStartSearchFromQueueEntryAsync(queueEntry, _defaultTimeout, ct);

        public async UniTask StartSearchAsync(MatchmakingRequest request, TimeSpan timeout, CancellationToken ct)
        {
            var started = await TryStartSearchAsync(request, timeout, ct);
            
            if (!started)
                return;
        }

        public async UniTask<bool> TryStartSearchAsync(MatchmakingRequest request, TimeSpan timeout, CancellationToken ct)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            
            if (timeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be positive.");

            await UniTask.SwitchToMainThread(ct);
            int epoch;
            CancellationTokenSource userCancelCts;
            CancellationTokenSource timeoutCts;
            CancellationToken userCancelToken;
            CancellationToken timeoutToken;
            CancellationTokenSource linkedCts;
            QueueEntry queueEntry;

            lock (_lock)
            {
                EnsureNotDisposed();

                if (_state.Value == MatchmakingState.TerminalModal)
                    return false;

                if (_state.Value is MatchmakingState.Searching or MatchmakingState.CancelPending)
                    return false;

                CleanupSearchResourcesUnsafe();

                epoch = Interlocked.Increment(ref _searchEpoch);
                _failure.Value = null;
                _result.Value = null;

                _userCancelCts = new CancellationTokenSource();
                _timeoutCts = new CancellationTokenSource(timeout);

                userCancelCts = _userCancelCts;
                timeoutCts = _timeoutCts;
                userCancelToken = userCancelCts.Token;
                timeoutToken = timeoutCts.Token;
            }

            linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, userCancelToken, timeoutToken);

            try
            {
                queueEntry = await _service.EnterQueueAsync(request, linkedCts.Token);
            }
            catch (OperationCanceledException)
            {
                await UniTask.SwitchToMainThread();
                await ApplySearchCancellationAsync(epoch, ct, userCancelToken, timeoutToken);
                return false;
            }
            catch (Exception ex)
            {
                await UniTask.SwitchToMainThread();
                ApplySearchError(epoch, ex);
                return false;
            }
            finally
            {
                linkedCts.Dispose();
            }

            if (queueEntry == null)
            {
                ApplySearchError(epoch, new InvalidOperationException("Matchmaking service returned null queue entry."));
                return false;
            }

            return TryActivateQueueEntrySearch(epoch, queueEntry, ct, userCancelToken, timeoutToken);
        }

        public async UniTask<bool> TryStartSearchFromQueueEntryAsync(QueueEntry queueEntry, TimeSpan timeout, CancellationToken ct)
        {
            if (queueEntry == null)
                throw new ArgumentNullException(nameof(queueEntry));

            if (timeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be positive.");

            await UniTask.SwitchToMainThread(ct);
            int epoch;
            CancellationTokenSource userCancelCts;
            CancellationTokenSource timeoutCts;
            CancellationToken userCancelToken;
            CancellationToken timeoutToken;

            lock (_lock)
            {
                EnsureNotDisposed();

                if (_state.Value == MatchmakingState.TerminalModal)
                    return false;

                if (_state.Value is MatchmakingState.Searching or MatchmakingState.CancelPending)
                    return false;

                CleanupSearchResourcesUnsafe();

                epoch = Interlocked.Increment(ref _searchEpoch);
                _failure.Value = null;
                _result.Value = null;

                _userCancelCts = new CancellationTokenSource();
                _timeoutCts = new CancellationTokenSource(timeout);

                userCancelCts = _userCancelCts;
                timeoutCts = _timeoutCts;
                userCancelToken = userCancelCts.Token;
                timeoutToken = timeoutCts.Token;
            }

            return TryActivateQueueEntrySearch(epoch, queueEntry, ct, userCancelToken, timeoutToken);
        }

        private async UniTask RunSearchAsync(
            int capturedEpoch,
            QueueEntry queueEntry,
            CancellationToken lifetimeCt,
            CancellationToken userCancelCt,
            CancellationToken timeoutCt)
        {
            CancellationTokenSource? linkedCts = null;

            try
            {
                linkedCts = CancellationTokenSource.CreateLinkedTokenSource(lifetimeCt, userCancelCt, timeoutCt);

                var result = queueEntry.IsPaired && queueEntry.ImmediateResult != null
                    ? queueEntry.ImmediateResult
                    : await _service.WaitForMatchAsync(queueEntry, linkedCts.Token);

                if (result == null)
                    throw new InvalidOperationException("Matchmaking service returned null result.");

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
            finally
            {
                linkedCts?.Dispose();
            }
        }

        private void ApplySearchSuccess(int capturedEpoch, MatchmakingResult result)
        {
            lock (_lock)
            {
                if (_isDisposed)
                    return;

                if (capturedEpoch != _searchEpoch || IsTerminalForEpoch(_state.Value))
                    return;

                _result.Value = result;
                _failure.Value = null;
                _state.Value = MatchmakingState.Found;
                CleanupSearchResourcesUnsafe();
            }
        }

        private async UniTask ApplySearchCancellationAsync(
            int capturedEpoch,
            CancellationToken externalCt,
            CancellationToken userCancelCt,
            CancellationToken timeoutCt)
        {
            MatchmakingState stateSnapshot;

            lock (_lock)
            {
                if (_isDisposed)
                    return;

                if (capturedEpoch != _searchEpoch || IsTerminalForEpoch(_state.Value))
                    return;

                stateSnapshot = _state.Value;
            }

            if (stateSnapshot == MatchmakingState.CancelPending)
            {
                var pendingOutcome = await CompleteCancelAckAsync(capturedEpoch);
                ApplyCancelOutcome(capturedEpoch, pendingOutcome);
                return;
            }

            var timeoutRequested = timeoutCt.IsCancellationRequested;
            var externalCancelled = externalCt.IsCancellationRequested;
            var userCancelled = userCancelCt.IsCancellationRequested;

            if (externalCancelled)
            {
                RequestBestEffortLeave();
                CleanupForExternalCancellation(capturedEpoch);
                return;
            }

            if (userCancelled)
            {
                var outcome = await CompleteCancelAckAsync(capturedEpoch);
                ApplyCancelOutcome(capturedEpoch, outcome);
                return;
            }

            if (timeoutRequested)
            {
                RequestBestEffortLeave();

                lock (_lock)
                {
                    if (_isDisposed)
                        return;

                    if (capturedEpoch != _searchEpoch || IsTerminalForEpoch(_state.Value))
                        return;

                    _failure.Value = MatchmakingFailure.Terminal(MatchmakingTerminalReason.SearchTimedOut);
                    _result.Value = null;
                    _state.Value = MatchmakingState.TerminalModal;
                    CleanupSearchResourcesUnsafe();
                }

                return;
            }

            ApplySearchError(capturedEpoch, new Exception("Unexpected matchmaking cancellation."));
        }

        private async UniTask<CancelAckOutcome> CompleteCancelAckAsync(int capturedEpoch)
        {
            CancellationTokenSource ackCts = new(_config.CancelAckTimeout);

            try
            {
                await _service.LeaveAsync(ackCts.Token);
                return CancelAckOutcome.Success;
            }
            catch (MatchmakingCancelAckTimeoutException)
            {
                return CancelAckOutcome.Timeout;
            }
            catch (ConnectionLostException)
            {
                return CancelAckOutcome.ConnectionLost;
            }
            catch (OperationCanceledException)
            {
                if (ackCts.IsCancellationRequested)
                    return CancelAckOutcome.Timeout;

                return CancelAckOutcome.ConnectionLost;
            }
            catch (Exception ex)
            {
                GameLog.Exception(ex);
                return CancelAckOutcome.ConnectionLost;
            }
            finally
            {
                ackCts.Dispose();
            }
        }

        private void ApplyCancelOutcome(int capturedEpoch, CancelAckOutcome outcome)
        {
            lock (_lock)
            {
                if (_isDisposed)
                    return;

                if (capturedEpoch != _searchEpoch || IsTerminalForEpoch(_state.Value))
                    return;

                _result.Value = null;

                switch (outcome)
                {
                    case CancelAckOutcome.Success:
                        _failure.Value = null;
                        _state.Value = MatchmakingState.Cancelled;
                        break;
                    case CancelAckOutcome.Timeout:
                        _failure.Value = MatchmakingFailure.Terminal(MatchmakingTerminalReason.CancelAckTimeout);
                        _state.Value = MatchmakingState.TerminalModal;
                        break;
                    default:
                        _failure.Value = MatchmakingFailure.Terminal(MatchmakingTerminalReason.ConnectionLost);
                        _state.Value = MatchmakingState.TerminalModal;
                        break;
                }

                CleanupSearchResourcesUnsafe();
            }
        }

        private void ApplySearchError(int capturedEpoch, Exception ex)
        {
            lock (_lock)
            {
                if (_isDisposed)
                    return;

                if (capturedEpoch != _searchEpoch || IsTerminalForEpoch(_state.Value))
                    return;

                _failure.Value = ex is ConnectionLostException
                    ? MatchmakingFailure.Terminal(MatchmakingTerminalReason.ConnectionLost)
                    : MatchmakingFailure.FromException(ex);
                _result.Value = null;
                _state.Value = ex is ConnectionLostException
                    ? MatchmakingState.TerminalModal
                    : MatchmakingState.Failed;
                CleanupSearchResourcesUnsafe();
            }
        }

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

                if (_state.Value == MatchmakingState.Found || _state.Value == MatchmakingState.TerminalModal)
                    return;

                if (_state.Value != MatchmakingState.Searching)
                    return;

                _state.Value = MatchmakingState.CancelPending;
                _userCancelCts?.Cancel();
                epoch = _searchEpoch;
                waitTask = _waitTask;
            }

            if (waitTask != null)
            {
                try
                {
                    await waitTask.AsUniTask();
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    GameLog.Exception(ex);
                }

                var shouldCompleteAck = false;

                lock (_lock)
                {
                    if (epoch == _searchEpoch && _state.Value == MatchmakingState.CancelPending)
                        shouldCompleteAck = true;
                }

                if (shouldCompleteAck)
                {
                    var outcome = await CompleteCancelAckAsync(epoch);
                    ApplyCancelOutcome(epoch, outcome);
                }
            }
        }

        public void NotifySessionStartFailed()
        {
            lock (_lock)
            {
                EnsureNotDisposed();

                if (_state.Value == MatchmakingState.Found || _state.Value == MatchmakingState.Searching)
                {
                    _failure.Value = MatchmakingFailure.Terminal(MatchmakingTerminalReason.SessionStartFailed);
                    _state.Value = MatchmakingState.TerminalModal;
                    CleanupSearchResourcesUnsafe();
                }
            }
        }

        public void AcknowledgeTerminalModal()
        {
            lock (_lock)
            {
                EnsureNotDisposed();

                if (_state.Value != MatchmakingState.TerminalModal)
                    return;

                _failure.Value = null;
                _result.Value = null;
                _state.Value = MatchmakingState.Idle;
            }
        }

        public void Cancel()
        {
            EnsureNotDisposed();

            if (!PlayerLoopHelper.IsMainThread)
            {
                _userCancelCts?.Cancel();
                _timeoutCts?.Cancel();
                return;
            }

            lock (_lock)
            {
                EnsureNotDisposed();

                if (_state.Value is not MatchmakingState.Searching and not MatchmakingState.CancelPending)
                    return;

                _userCancelCts?.Cancel();
                _timeoutCts?.Cancel();
            }
        }

        private void CleanupForExternalCancellation(int capturedEpoch)
        {
            lock (_lock)
            {
                if (_isDisposed)
                    return;

                if (capturedEpoch != _searchEpoch || IsTerminalForEpoch(_state.Value))
                    return;

                _failure.Value = null;
                _result.Value = null;
                _state.Value = MatchmakingState.Cancelled;
                CleanupSearchResourcesUnsafe();
            }
        }

        private static bool IsTerminalForEpoch(MatchmakingState state) =>
            state is MatchmakingState.TerminalModal or MatchmakingState.Cancelled or MatchmakingState.Failed;

        private void RequestBestEffortLeave() =>
            BestEffortLeaveAsync().Forget();

        private async UniTaskVoid BestEffortLeaveAsync()
        {
            using var leaveCts = new CancellationTokenSource(_config.CancelAckTimeout);

            try
            {
                await _service.LeaveAsync(leaveCts.Token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                GameLog.Exception(ex);
            }
        }

        private bool TryActivateQueueEntrySearch(
            int epoch,
            QueueEntry queueEntry,
            CancellationToken lifetimeCt,
            CancellationToken userCancelCt,
            CancellationToken timeoutCt)
        {
            lock (_lock)
            {
                if (_isDisposed)
                    return false;

                if (epoch != _searchEpoch)
                    return false;

                if (_state.Value == MatchmakingState.CancelPending)
                    return false;

                if (queueEntry.IsPaired && queueEntry.ImmediateResult != null)
                {
                    _result.Value = queueEntry.ImmediateResult;
                    _failure.Value = null;
                    _state.Value = MatchmakingState.Found;
                    CleanupSearchResourcesUnsafe();
                    return true;
                }

                _state.Value = MatchmakingState.Searching;
                _waitTask = RunSearchAsync(epoch, queueEntry, lifetimeCt, userCancelCt, timeoutCt).AsTask();
                return true;
            }
        }

        private void CleanupSearchResourcesUnsafe()
        {
            _userCancelCts?.Dispose();
            _userCancelCts = null;

            _timeoutCts?.Dispose();
            _timeoutCts = null;

            _waitTask = null;
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            lock (_lock)
            {
                if (_isDisposed)
                    return;

                _isDisposed = true;
                _userCancelCts?.Cancel();
                _timeoutCts?.Cancel();
                CleanupSearchResourcesUnsafe();
            }

            _state.Dispose();
            _failure.Dispose();
            _result.Dispose();
        }

        private void EnsureNotDisposed()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(MatchmakingFsm));
        }

        private enum CancelAckOutcome
        {
            Success = 0,
            Timeout = 1,
            ConnectionLost = 2,
        }
    }
}