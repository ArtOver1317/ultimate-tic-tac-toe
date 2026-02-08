#nullable enable

using System;
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
        private static readonly TimeSpan _defaultTimeoutValue = TimeSpan.FromSeconds(30);

        private readonly IMatchmakingService _service;
        private readonly TimeSpan _defaultTimeout;
        private readonly ReactiveProperty<MatchmakingState> _state;
        private readonly ReactiveProperty<MatchmakingFailure?> _failure;
        private readonly ReactiveProperty<MatchmakingResult?> _result;
        private readonly object _lock = new();

        private CancellationTokenSource? _searchCts;
        private bool _isDisposed;

        public ReadOnlyReactiveProperty<MatchmakingState> State => _state;
        public ReadOnlyReactiveProperty<MatchmakingFailure?> Failure => _failure;
        public ReadOnlyReactiveProperty<MatchmakingResult?> Result => _result;

        public MatchmakingState CurrentState => _state.CurrentValue;
        public bool IsSearching => _state.CurrentValue == MatchmakingState.Searching;

        public MatchmakingFsm(IMatchmakingService service)
            : this(service, _defaultTimeoutValue) { }

        public MatchmakingFsm(IMatchmakingService service, TimeSpan defaultTimeout)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            
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

            CancellationTokenSource localCts;

            lock (_lock)
            {
                EnsureNotDisposed();

                if (_searchCts != null)
                    return false;

                _failure.Value = null;
                _result.Value = null;
                _state.Value = MatchmakingState.Searching;

                _searchCts = new CancellationTokenSource();
                localCts = _searchCts;
            }

            CancellationTokenSource? timeoutCts = null;
            CancellationTokenSource? linkedCts = null;

            try
            {
                timeoutCts = new CancellationTokenSource(timeout);
                linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, localCts.Token, timeoutCts.Token);

                var result = await _service.FindMatchAsync(request, linkedCts.Token);
                
                if (result == null)
                    throw new InvalidOperationException("Matchmaking service returned null result.");

                await UniTask.SwitchToMainThread();

                lock (_lock)
                {
                    if (!ReferenceEquals(_searchCts, localCts))
                        return true;

                    _result.Value = result;
                    _failure.Value = null;
                    _state.Value = MatchmakingState.Found;
                    _searchCts = null;
                }
            }
            catch (OperationCanceledException ex)
            {
                await UniTask.SwitchToMainThread();

                lock (_lock)
                {
                    if (!ReferenceEquals(_searchCts, localCts))
                        return true;

                    var timeoutRequested = timeoutCts?.IsCancellationRequested ?? false;
                    var externalCancelled = ct.IsCancellationRequested || localCts.IsCancellationRequested;
                    var isTimeout = timeoutRequested && !externalCancelled;

                    if (isTimeout)
                    {
                        _failure.Value = MatchmakingFailure.Timeout();
                        _state.Value = MatchmakingState.Failed;
                    }
                    else if (externalCancelled)
                    {
                        _failure.Value = null;
                        _state.Value = MatchmakingState.Cancelled;
                    }
                    else
                    {
                        _failure.Value = MatchmakingFailure.FromException(new Exception("Unexpected matchmaking cancellation.", ex));
                        _state.Value = MatchmakingState.Failed;
                    }

                    _result.Value = null;
                    _searchCts = null;
                }
            }
            catch (Exception ex)
            {
                await UniTask.SwitchToMainThread();

                lock (_lock)
                {
                    if (!ReferenceEquals(_searchCts, localCts))
                        return true;

                    _failure.Value = MatchmakingFailure.FromException(ex);
                    _result.Value = null;
                    _state.Value = MatchmakingState.Failed;
                    _searchCts = null;
                }
            }
            finally
            {
                linkedCts?.Dispose();
                timeoutCts?.Dispose();
                localCts.Dispose();
            }

            return true;
        }

        /// <summary>
        /// Cancels current search. Must update reactive state on main thread.
        /// </summary>
        public void Cancel()
        {
            if (!PlayerLoopHelper.IsMainThread)
            {
                CancelFromAnyThread();
                return;
            }

            CancelInternal();
        }

        private void CancelFromAnyThread()
        {
            CancellationTokenSource? cts;

            lock (_lock)
            {
                EnsureNotDisposed();

                cts = _searchCts;
                
                if (cts == null)
                    return;

                _searchCts = null;
            }

            cts.Cancel();
            CancelOnMainThread().Forget();
        }

        private async UniTaskVoid CancelOnMainThread()
        {
            try
            {
                await UniTask.SwitchToMainThread();
                CancelInternalIfStillSearching();
            }
            catch (Exception ex)
            {
                GameLog.Exception(ex);
            }
        }

        private void CancelInternal()
        {
            CancellationTokenSource? cts;

            lock (_lock)
            {
                EnsureNotDisposed();

                cts = _searchCts;
                
                if (cts == null)
                    return;

                _failure.Value = null;
                _result.Value = null;
                _state.Value = MatchmakingState.Cancelled;
                _searchCts = null;
            }

            cts.Cancel();
        }

        private void CancelInternalIfStillSearching()
        {
            lock (_lock)
            {
                if (_searchCts != null)
                    return;

                if (_state.Value != MatchmakingState.Searching)
                    return;

                _failure.Value = null;
                _result.Value = null;
                _state.Value = MatchmakingState.Cancelled;
            }
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            CancellationTokenSource? cts;

            lock (_lock)
            {
                if (_isDisposed)
                    return;

                _isDisposed = true;
                cts = _searchCts;
                _searchCts = null;
            }

            if (cts != null)
            {
                cts.Cancel();
                cts.Dispose();
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
    }
}