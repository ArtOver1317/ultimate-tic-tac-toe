#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using Runtime.GameModes.Wizard.Matchmaking.Config;
using Runtime.GameModes.Wizard.Matchmaking.Contracts;
using Runtime.GameModes.Wizard.Session;
using Runtime.Infrastructure.Logging;
using Runtime.Localization;
using Runtime.UI.Core;

namespace Runtime.GameModes.Wizard.Matchmaking.Runtime
{
    /// <summary>
    /// View-model for the matchmaking progress window.
    /// Owns the matchmaking FSM and exposes UI-friendly state.
    /// </summary>
    public sealed class MatchmakingViewModel : BaseViewModel
    {
        private const string _genericMatchmakingFailureCode = "matchmaking.failed";
        private const string _matchmakingFailedMessageKey = "Errors.GameWizard.MatchmakingFailed";

        private readonly ILocalizationService _localization;
        private readonly IMatchmakingService _service;
        private readonly IMatchmakingConfig _config;
        private readonly MatchmakingElapsedTimer _elapsedTimer = new();

        private readonly ReactiveProperty<MatchmakingState> _state = new(MatchmakingState.Idle);
        private readonly ReactiveProperty<int> _playersWithDifferentParams = new(0);
        private readonly ReactiveProperty<string?> _errorMessage = new(null);
        private readonly ReactiveProperty<string?> _errorMessageKey = new(null);
        private readonly ReactiveProperty<MatchmakingResult?> _result = new(null);
        private readonly ReactiveProperty<MatchmakingFailure?> _failure = new(null);

        private readonly Subject<Unit> _cancelRequested = new();
        private readonly Subject<Unit> _backRequested = new();
        private readonly Subject<Unit> _retryRequested = new();

        private MatchmakingFsm? _fsm;
        private CancellationTokenSource? _searchCts;
        private int _isWired;

        public ReadOnlyReactiveProperty<MatchmakingState> State => _state;
        public ReadOnlyReactiveProperty<TimeSpan> ElapsedTime => _elapsedTimer.ElapsedTime;
        public ReadOnlyReactiveProperty<int> PlayersWithDifferentParams => _playersWithDifferentParams;
        public ReadOnlyReactiveProperty<string?> ErrorMessage => _errorMessage;
        public ReadOnlyReactiveProperty<MatchmakingResult?> Result => _result;
        public ReadOnlyReactiveProperty<MatchmakingFailure?> Failure => _failure;

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

        public MatchmakingViewModel(ILocalizationService localization, IMatchmakingService service, IMatchmakingConfig? config = null)
        {
            _localization = localization ?? throw new ArgumentNullException(nameof(localization));
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _config = config ?? MatchmakingConfigDefaults.Instance;

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
            TryStartSearchAsync(request, ct).Forget();
        }

        public UniTask<bool> TryBeginSearchAsync(MatchmakingRequest request, CancellationToken ct)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            EnsureWired();
            return TryStartSearchAsync(request, ct);
        }

        public UniTask<bool> TryBeginSearchFromQueueEntryAsync(QueueEntry queueEntry, CancellationToken ct)
        {
            if (queueEntry == null)
                throw new ArgumentNullException(nameof(queueEntry));

            EnsureWired();
            return TryStartSearchAsync(queueEntry, ct);
        }

        public void RequestCancel()
        {
            _cancelRequested.OnNext(Unit.Default);
            RequestCancelInternalAsync().Forget();
        }

        public void RequestBack()
        {
            _backRequested.OnNext(Unit.Default);
            CancelSearchIfSearching();
        }

        public void RequestRetry() => _retryRequested.OnNext(Unit.Default);

        public void AcknowledgeTerminalModal() => _fsm?.AcknowledgeTerminalModal();

        public void NotifySessionStartFailed() => _fsm?.NotifySessionStartFailed();

        protected override void OnReset()
        {
            Volatile.Write(ref _isWired, 0);

            CancelSearch();
            _elapsedTimer.Stop(resetElapsed: true);
            DisposeFsm();
            ResetReactiveState();
        }

        protected override void OnDispose()
        {
            CancelSearch();
            _elapsedTimer.Dispose();
            DisposeFsm();

            _state.Dispose();
            _playersWithDifferentParams.Dispose();
            _errorMessage.Dispose();
            _errorMessageKey.Dispose();
            _result.Dispose();
            _failure.Dispose();

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

            var fsm = new MatchmakingFsm(_service, _config);
            _fsm = fsm;

            BindFsm(fsm);
            BindLocalizedErrorMessage();
        }

        private void BindFsm(MatchmakingFsm fsm)
        {
            AddDisposable(fsm.State.Subscribe(ApplyState));
            AddDisposable(fsm.Failure.Subscribe(ApplyFailureState));
            AddDisposable(fsm.Result.Subscribe(result => _result.Value = result));
        }

        private void BindLocalizedErrorMessage() =>
            AddDisposable(_errorMessageKey.CombineLatest(_localization.CurrentLocale,
                    static (key, _) => key)
                .Subscribe(key => _errorMessage.Value = ResolveMessageKey(key ?? string.Empty)));

        private void ApplyFailureState(MatchmakingFailure? failure)
        {
            _failure.Value = failure;
            ApplyFailure(failure);
        }

        private UniTask<bool> TryStartSearchAsync(MatchmakingRequest request, CancellationToken ct) =>
            StartSearchCoreAsync(token => _fsm!.TryStartSearchAsync(request, token), ct);

        private UniTask<bool> TryStartSearchAsync(QueueEntry queueEntry, CancellationToken ct) =>
            StartSearchCoreAsync(token => _fsm!.TryStartSearchFromQueueEntryAsync(queueEntry, token), ct);

        private async UniTask<bool> StartSearchCoreAsync(Func<CancellationToken, UniTask<bool>> startSearch, CancellationToken ct)
        {
            if (_fsm == null)
                return false;

            CancelSearch();

            var searchCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _searchCts = searchCts;

            try
            {
                var started = await startSearch(searchCts.Token);
                
                if (started)
                    return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception ex)
            {
                GameLog.Exception(ex);
                ApplyGenericStartFailure();
            }

            ReleaseCurrentSearchCts(searchCts, cancel: false);
            return false;
        }

        private void CancelSearch()
        {
            _fsm?.Cancel();

            var cts = Interlocked.Exchange(ref _searchCts, null);
            ReleaseSearchCts(cts, cancel: true);
        }

        private void CancelSearchIfSearching()
        {
            if (!IsSearchActiveState(_state.Value))
                return;

            CancelSearch();
        }

        private void ApplyState(MatchmakingState state)
        {
            _state.Value = state;

            if (IsSearchActiveState(state))
                _elapsedTimer.Start();
            else
            {
                _elapsedTimer.Stop(resetElapsed: true);
                ReleaseSearchCts(Interlocked.Exchange(ref _searchCts, null), cancel: false);
            }

            if (state is not MatchmakingState.Failed and not MatchmakingState.TerminalModal)
                _errorMessageKey.Value = null;
        }

        private void ApplyFailure(MatchmakingFailure? failure)
        {
            if (failure == null)
            {
                if (_state.Value != MatchmakingState.Failed)
                    _errorMessageKey.Value = null;

                return;
            }

            _errorMessageKey.Value = failure.MessageKey;
        }

        private void ApplyGenericStartFailure()
        {
            var failure = new MatchmakingFailure(_genericMatchmakingFailureCode, _matchmakingFailedMessageKey, isTimeout: false);
            _failure.Value = failure;
            _result.Value = null;
            _errorMessageKey.Value = failure.MessageKey;
            _state.Value = MatchmakingState.Failed;
        }

        private async UniTaskVoid RequestCancelInternalAsync()
        {
            if (_fsm == null)
                return;

            try
            {
                await _fsm.RequestCancelAsync();
            }
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

        private static bool IsSearchActiveState(MatchmakingState state) =>
            state is MatchmakingState.Searching or MatchmakingState.CancelPending;

        private void ResetReactiveState()
        {
            _state.Value = MatchmakingState.Idle;
            _playersWithDifferentParams.Value = 0;
            _errorMessage.Value = null;
            _errorMessageKey.Value = null;
            _result.Value = null;
            _failure.Value = null;
        }

        private void ReleaseCurrentSearchCts(CancellationTokenSource cts, bool cancel)
        {
            if (Interlocked.CompareExchange(ref _searchCts, null, cts) != cts)
                return;

            ReleaseSearchCts(cts, cancel);
        }

        private void ReleaseSearchCts(CancellationTokenSource? cts, bool cancel)
        {
            if (cts == null)
                return;

            try
            {
                if (cancel)
                    cts.Cancel();
            }
            finally
            {
                cts.Dispose();
            }
        }
    }
}