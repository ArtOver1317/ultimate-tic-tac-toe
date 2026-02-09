#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using Runtime.Infrastructure.Logging;
using Runtime.Localization;
using Runtime.UI.Core;

namespace Runtime.GameModes.Wizard
{
    /// <summary>
    /// View-model for the matchmaking progress window.
    /// Owns the matchmaking FSM and exposes UI-friendly state.
    /// </summary>
    public sealed class MatchmakingViewModel : BaseViewModel
    {
        private readonly ILocalizationService _localization;
        private readonly IMatchmakingService _service;

        private readonly ReactiveProperty<MatchmakingState> _stateFallback = new(MatchmakingState.Idle);
        private readonly ReactiveProperty<TimeSpan> _elapsedTime = new(TimeSpan.Zero);
        private readonly ReactiveProperty<int> _playersWithDifferentParams = new(0);
        private readonly ReactiveProperty<string?> _errorMessage = new(null);
        private readonly ReactiveProperty<string?> _errorMessageKey = new(null);
        private readonly ReactiveProperty<MatchmakingResult?> _result = new(null);

        private readonly Subject<Unit> _cancelRequested = new();
        private readonly Subject<Unit> _backRequested = new();
        private readonly Subject<Unit> _retryRequested = new();

        private MatchmakingFsm? _fsm;
        private CancellationTokenSource? _searchCts;
        private CancellationTokenSource? _timerCts;
        private DateTime _searchStartUtc;
        private int _isWired;

        public ReadOnlyReactiveProperty<MatchmakingState> State => _stateFallback;
        public ReadOnlyReactiveProperty<TimeSpan> ElapsedTime => _elapsedTime;
        public ReadOnlyReactiveProperty<int> PlayersWithDifferentParams => _playersWithDifferentParams;
        public ReadOnlyReactiveProperty<string?> ErrorMessage => _errorMessage;
        public ReadOnlyReactiveProperty<MatchmakingResult?> Result => _result;

        public Observable<Unit> CancelRequested => _cancelRequested;
        public Observable<Unit> BackRequested => _backRequested;
        public Observable<Unit> RetryRequested => _retryRequested;

        public Observable<string> TitleText { get; }
        public Observable<string> SearchingPrefixText { get; }
        public Observable<string> FoundText { get; }
        public Observable<string> FailedText { get; }
        public Observable<string> CancelledText { get; }
        public Observable<string> CancelButtonText { get; }
        public Observable<string> RetryButtonText { get; }
        public Observable<string> BackButtonText { get; }
        public Observable<string> HintText { get; }

        public MatchmakingViewModel(ILocalizationService localization, IMatchmakingService service)
        {
            _localization = localization ?? throw new ArgumentNullException(nameof(localization));
            _service = service ?? throw new ArgumentNullException(nameof(service));

            var table = new TextTableId("GameWizard");
            TitleText = _localization.Observe(table, new TextKey("GameWizard.Matchmaking.Title"));
            SearchingPrefixText = _localization.Observe(table, new TextKey("GameWizard.Matchmaking.SearchingFor"));
            FoundText = _localization.Observe(table, new TextKey("GameWizard.Matchmaking.Found"));
            FailedText = _localization.Observe(table, new TextKey("GameWizard.Matchmaking.Failed"));
            CancelledText = _localization.Observe(table, new TextKey("GameWizard.Matchmaking.Cancelled"));
            CancelButtonText = _localization.Observe(table, new TextKey("GameWizard.Matchmaking.Cancel"));
            RetryButtonText = _localization.Observe(table, new TextKey("GameWizard.Matchmaking.Retry"));
            BackButtonText = _localization.Observe(table, new TextKey("GameWizard.Matchmaking.Back"));

            var hintArgs = _playersWithDifferentParams
                .Select(count => new Dictionary<string, object> { { "count", count } } as IReadOnlyDictionary<string, object>);

            HintText = _localization.Observe(table, new TextKey("GameWizard.Matchmaking.Hint"), hintArgs);
        }

        public override void Initialize()
        {
            base.Initialize();
            EnsureWired();
        }

        public void BeginSearch(MatchmakingRequest request, CancellationToken ct)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            EnsureWired();
            StartSearchAsync(request, ct).Forget();
        }

        public void RequestCancel()
        {
            _cancelRequested.OnNext(Unit.Default);
            CancelSearch();
        }

        public void RequestBack()
        {
            _backRequested.OnNext(Unit.Default);
            CancelSearchIfSearching();
        }

        public void RequestRetry() => _retryRequested.OnNext(Unit.Default);

        protected override void OnReset()
        {
            Volatile.Write(ref _isWired, 0);

            CancelSearch();
            StopTimer(resetElapsed: true);

            _stateFallback.Value = MatchmakingState.Idle;
            _elapsedTime.Value = TimeSpan.Zero;
            _playersWithDifferentParams.Value = 0;
            _errorMessage.Value = null;
            _errorMessageKey.Value = null;
            _result.Value = null;

            DisposeFsm();
        }

        protected override void OnDispose()
        {
            CancelSearch();
            StopTimer(resetElapsed: true);
            DisposeFsm();

            _stateFallback.Dispose();
            _elapsedTime.Dispose();
            _playersWithDifferentParams.Dispose();
            _errorMessage.Dispose();
            _errorMessageKey.Dispose();
            _result.Dispose();

            _cancelRequested.OnCompleted();
            _cancelRequested.Dispose();
            _backRequested.OnCompleted();
            _backRequested.Dispose();
            _retryRequested.OnCompleted();
            _retryRequested.Dispose();
        }

        private void EnsureWired()
        {
            if (IsDisposed)
                return;

            if (Interlocked.Exchange(ref _isWired, 1) != 0)
                return;

            _fsm = new MatchmakingFsm(_service);

            AddDisposable(_fsm.State.Subscribe(ApplyState));
            AddDisposable(_fsm.Failure.Subscribe(ApplyFailure));
            AddDisposable(_fsm.Result.Subscribe(result => _result.Value = result));

            AddDisposable(Observable.CombineLatest(
                    _errorMessageKey,
                    _localization.CurrentLocale,
                    static (key, _) => key)
                .Subscribe(key => _errorMessage.Value = ResolveMessageKey(key ?? string.Empty)));
        }

        private async UniTask StartSearchAsync(MatchmakingRequest request, CancellationToken ct)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            if (_fsm == null)
                return;

            CancelSearch();

            var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _searchCts = cts;

            try
            {
                await _fsm.TryStartSearchAsync(request, cts.Token);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                GameLog.Exception(ex);
                _errorMessageKey.Value = "Errors.GameWizard.MatchmakingFailed";
                _stateFallback.Value = MatchmakingState.Failed;
            }
            finally
            {
                try
                {
                    cts.Dispose();
                }
                finally
                {
                    if (ReferenceEquals(_searchCts, cts))
                        _searchCts = null;
                }
            }
        }

        private void CancelSearch()
        {
            _fsm?.Cancel();

            if (_searchCts == null)
                return;

            var cts = Interlocked.Exchange(ref _searchCts, null);
            
            if (cts == null)
                return;

            try
            {
                cts.Cancel();
            }
            finally
            {
                cts.Dispose();
            }
        }

        private void CancelSearchIfSearching()
        {
            if (_stateFallback.Value != MatchmakingState.Searching)
                return;

            CancelSearch();
        }

        private void ApplyState(MatchmakingState state)
        {
            _stateFallback.Value = state;

            if (state == MatchmakingState.Searching)
                StartTimer();
            else
                StopTimer(resetElapsed: true);

            if (state != MatchmakingState.Failed)
                _errorMessageKey.Value = null;
        }

        private void ApplyFailure(MatchmakingFailure? failure)
        {
            if (failure == null)
            {
                if (_stateFallback.Value != MatchmakingState.Failed)
                    _errorMessageKey.Value = null;
                
                return;
            }

            _errorMessageKey.Value = failure.MessageKey;
        }

        private void StartTimer()
        {
            if (_timerCts != null)
                return;

            _searchStartUtc = DateTime.UtcNow;

            var cts = new CancellationTokenSource();
            _timerCts = cts;

            RunTimerAsync(cts.Token).Forget();
        }

        private void StopTimer(bool resetElapsed)
        {
            if (resetElapsed)
                _elapsedTime.Value = TimeSpan.Zero;

            var cts = Interlocked.Exchange(ref _timerCts, null);
            
            if (cts == null)
                return;

            try
            {
                cts.Cancel();
            }
            finally
            {
                cts.Dispose();
            }
        }

        private async UniTaskVoid RunTimerAsync(CancellationToken ct)
        {
            try
            {
                await UniTask.SwitchToMainThread(ct);

                while (!ct.IsCancellationRequested)
                {
                    _elapsedTime.Value = DateTime.UtcNow - _searchStartUtc;
                    await UniTask.Delay(TimeSpan.FromSeconds(1), cancellationToken: ct);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                GameLog.Exception(ex);
            }
        }

        private void DisposeFsm()
        {
            if (_fsm == null)
                return;

            _fsm.Dispose();
            _fsm = null;
        }

        private string ResolveMessageKey(string messageKey)
        {
            if (string.IsNullOrWhiteSpace(messageKey))
                return string.Empty;

            var dotIndex = messageKey.IndexOf('.', StringComparison.Ordinal);
            
            if (dotIndex <= 0)
                return messageKey;

            var tableName = messageKey[..dotIndex];
            return _localization.Resolve(new TextTableId(tableName), new TextKey(messageKey));
        }
    }
}