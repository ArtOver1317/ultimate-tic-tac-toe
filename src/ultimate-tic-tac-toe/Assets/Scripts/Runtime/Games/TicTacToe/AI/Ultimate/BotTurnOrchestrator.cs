#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using Runtime.Gameplay;
using Runtime.Gameplay.ECS;
using Runtime.Gameplay.Shared;

namespace Runtime.Games.TicTacToe.AI.Ultimate
{
    public sealed class GameplayBotMoveCommandSink : IBotMoveCommandSink
    {
        private readonly IMatchStateProvider _matchState;
        private readonly IMatchFailSafeGateway _failSafeGateway;

        public GameplayBotMoveCommandSink(IMatchStateProvider matchState, IMatchFailSafeGateway failSafeGateway)
        {
            _matchState = matchState ?? throw new ArgumentNullException(nameof(matchState));
            _failSafeGateway = failSafeGateway ?? throw new ArgumentNullException(nameof(failSafeGateway));
        }

        public bool TrySubmitMove(Moves.CellId move, BotTurnId turnId)
        {
            if (_failSafeGateway.IsInputLocked)
            {
                return false;
            }

            if (!_matchState.IsMatchActive)
            {
                return false;
            }

            if (_matchState.CommandSequence != turnId.CommandSequenceBeforeTurn)
            {
                return false;
            }

            if (_matchState.ActivePlayerSlot != turnId.ActivePlayerSlot)
            {
                return false;
            }

            var seqBefore = _matchState.CommandSequence;
            _matchState.SubmitCommand(new MakeMoveCommand(move));
            return _matchState.CommandSequence > seqBefore;
        }
    }

    public sealed class LocalMatchFailSafeGateway : IMatchFailSafeGateway
    {
        private int _isInputLocked;

        public bool IsInputLocked { get; private set; }

        public bool TryEnterAbortState(string userSafeMessageKey)
        {
            if (Interlocked.CompareExchange(ref _isInputLocked, 1, 0) != 0)
            {
                IsInputLocked = true;
                return false;
            }

            IsInputLocked = true;
            UnityEngine.Debug.LogError($"[UltimateBot] Fail-safe abort: {userSafeMessageKey}");
            return true;
        }

        public void ResetAbortState()
        {
            Interlocked.Exchange(ref _isInputLocked, 0);
            IsInputLocked = false;
        }
    }

    public sealed class BotTurnOrchestrator : IBotTurnOrchestrator
    {
        private readonly IGameplayEventStream _events;
        private readonly Runtime.Gameplay.IGameplaySnapshotProvider _snapshot;
        private readonly IUltimateBotProfileCatalog _profiles;
        private readonly IUltimateBotStateReader _stateReader;
        private readonly IUltimateBotDecisionEngine _engine;
        private readonly IBotRngSessionFactory _rngFactory;
        private readonly IBotMoveCommandSink _commandSink;
        private readonly IMatchFailSafeGateway _failSafeGateway;

        private readonly ReactiveProperty<bool> _isStarted = new(false);
        private readonly ReactiveProperty<bool> _isThinking = new(false);
        private readonly ReactiveProperty<BotTurnId?> _inFlightTurnId = new(null);
        private readonly ReactiveProperty<BotTurnId?> _lastSubmittedTurnId = new(null);
        private readonly Subject<BotMoveFailedEvent> _moveFailed = new();
        private readonly Subject<DuplicateTurnIgnoredEvent> _duplicateIgnored = new();
        private readonly Subject<BotDecisionDiagnostics> _diagnostics = new();

        private CompositeDisposable? _subscriptions;
        private CancellationTokenSource? _lifetimeCts;
        private CancellationTokenSource? _turnCts;
        private IBotRngSession? _rng;
        private UltimateBotDifficultyProfileData _profile;
        private int _botSlot;
        private bool _disabled;
        private bool _disposed;
        private readonly object _gate = new();

        public BotOrchestratorState State { get; private set; } = BotOrchestratorState.NotStarted;

        public ReadOnlyReactiveProperty<bool> IsStarted => _isStarted;
        public ReadOnlyReactiveProperty<bool> IsThinking => _isThinking;
        public ReadOnlyReactiveProperty<BotTurnId?> InFlightTurnId => _inFlightTurnId;
        public ReadOnlyReactiveProperty<BotTurnId?> LastSubmittedTurnId => _lastSubmittedTurnId;

        public Observable<BotMoveFailedEvent> MoveFailed => _moveFailed;
        public Observable<DuplicateTurnIgnoredEvent> DuplicateIgnored => _duplicateIgnored;
        public Observable<BotDecisionDiagnostics> Diagnostics => _diagnostics;

        public BotTurnOrchestrator(
            IGameplayEventStream events,
            Runtime.Gameplay.IGameplaySnapshotProvider snapshot,
            IUltimateBotProfileCatalog profiles,
            IUltimateBotStateReader stateReader,
            IUltimateBotDecisionEngine engine,
            IBotRngSessionFactory rngFactory,
            IBotMoveCommandSink commandSink,
            IMatchFailSafeGateway failSafeGateway)
        {
            _events = events ?? throw new ArgumentNullException(nameof(events));
            _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
            _stateReader = stateReader ?? throw new ArgumentNullException(nameof(stateReader));
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _rngFactory = rngFactory ?? throw new ArgumentNullException(nameof(rngFactory));
            _commandSink = commandSink ?? throw new ArgumentNullException(nameof(commandSink));
            _failSafeGateway = failSafeGateway ?? throw new ArgumentNullException(nameof(failSafeGateway));
        }

        public UniTask StartAsync(int botSlot, string difficultyId, CancellationToken ct)
        {
            ThrowIfDisposed();
            if (State != BotOrchestratorState.NotStarted)
            {
                throw new InvalidOperationException("StartAsync can be called only from NotStarted state.");
            }

            if (!_profiles.TryGet(difficultyId, out var profile))
            {
                throw new InvalidOperationException($"Ultimate bot profile '{difficultyId}' not found.");
            }

            _profile = profile;
            _botSlot = botSlot;
            var matchInstanceId = BuildMatchInstanceId(_snapshot);
            _rng = _rngFactory.Create(matchInstanceId, botSlot, profile);
            _lifetimeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            _subscriptions = new CompositeDisposable();
            _events.CurrentPlayerChanged
                .Subscribe(evt =>
                {
                    if (evt.ActivePlayerSlot == _botSlot)
                    {
                        TriggerIfBotTurnAsync(_lifetimeCts.Token).Forget();
                    }
                })
                .AddTo(_subscriptions);

            _events.RoundFinished
                .Subscribe(_ => StopCurrentTurn())
                .AddTo(_subscriptions);

            State = BotOrchestratorState.Active;
            _isStarted.Value = true;

            if (_snapshot.ActivePlayerSlot == _botSlot)
            {
                TriggerIfBotTurnAsync(_lifetimeCts.Token).Forget();
            }

            return UniTask.CompletedTask;
        }

        public void Stop()
        {
            if (State == BotOrchestratorState.Disposed)
            {
                return;
            }

            if (State == BotOrchestratorState.NotStarted)
            {
                return;
            }

            StopCurrentTurn();
            State = BotOrchestratorState.Stopped;
            _isStarted.Value = false;
        }

        public async UniTask TriggerIfBotTurnAsync(CancellationToken ct)
        {
            if (State != BotOrchestratorState.Active || _disabled)
            {
                return;
            }

            if (_snapshot.ActivePlayerSlot != _botSlot)
            {
                return;
            }

            var turnId = BotTurnId.Build(_snapshot.CommandSequence, _snapshot.ActivePlayerSlot);

            lock (_gate)
            {
                if (_lastSubmittedTurnId.Value.HasValue && _lastSubmittedTurnId.Value.Value.Equals(turnId))
                {
                    _duplicateIgnored.OnNext(new DuplicateTurnIgnoredEvent(turnId, "already_submitted"));
                    return;
                }

                if (_inFlightTurnId.Value.HasValue && _inFlightTurnId.Value.Value.Equals(turnId))
                {
                    _duplicateIgnored.OnNext(new DuplicateTurnIgnoredEvent(turnId, "already_inflight"));
                    return;
                }
            }

            StopCurrentTurn();
            var turnCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var turnToken = turnCts.Token;
            lock (_gate)
            {
                _turnCts = turnCts;
            }

            _isThinking.Value = true;
            _inFlightTurnId.Value = turnId;

            try
            {
                if (!_stateReader.TryBuildDecisionRequest(
                        _botSlot,
                        turnId,
                        _profile,
                        _rng!,
                        out var request,
                        out var failReason))
                {
                    if (failReason == BotFailureReason.NoLegalMovesInconsistentState)
                    {
                        EnterFailSafeAndDisable(
                            turnId,
                            BotFailureReason.NoLegalMovesInconsistentState,
                            "No legal moves for in-progress state.",
                            "Errors.Bot.NoLegalMoves");
                    }

                    return;
                }

                if (_profile.PreMoveDelayMs > 0)
                {
                    await UniTask.Delay(_profile.PreMoveDelayMs, cancellationToken: turnToken);
                }

                var result = await _engine.ChooseMoveAsync(request, turnToken);

                if (turnCts.IsCancellationRequested)
                {
                    return;
                }

                if (!_commandSink.TrySubmitMove(result.Move, turnId))
                {
                    if (_stateReader.TryBuildDecisionRequest(
                            _botSlot,
                            BotTurnId.Build(_snapshot.CommandSequence, _snapshot.ActivePlayerSlot),
                            _profile,
                            _rng!,
                            out var retryRequest,
                            out _))
                    {
                        var retryResult = await _engine.ChooseMoveAsync(retryRequest, turnToken);
                        if (!_commandSink.TrySubmitMove(retryResult.Move, retryRequest.TurnId))
                        {
                            var fallback = retryRequest.LegalMovesStable.Count > 0
                                ? retryRequest.LegalMovesStable[0]
                                : default;

                            if (!_commandSink.TrySubmitMove(fallback, retryRequest.TurnId))
                            {
                                EnterFailSafeAndDisable(
                                    retryRequest.TurnId,
                                    BotFailureReason.EngineError,
                                    "Submit retry policy exhausted.",
                                    "Errors.Bot.SubmitFailed");
                                return;
                            }

                            _lastSubmittedTurnId.Value = retryRequest.TurnId;
                        }
                        else
                        {
                            _lastSubmittedTurnId.Value = retryRequest.TurnId;
                        }
                    }
                    else
                    {
                        EnterFailSafeAndDisable(
                            turnId,
                            BotFailureReason.EngineError,
                            "Retry snapshot unavailable.",
                            "Errors.Bot.RetrySnapshotUnavailable");
                    }
                }
                else
                {
                    _lastSubmittedTurnId.Value = turnId;
                }

                if (_profile.EnableDiagnostics)
                {
                    _diagnostics.OnNext(new BotDecisionDiagnostics(
                        turnId,
                        result.SearchDepthReached,
                        result.IterationsCompleted,
                        Array.Empty<BotCandidateScore>(),
                        result.DegradationReason));
                }
            }
            catch (OperationCanceledException)
            {
                // Normal lifecycle path: stop/dispose/restart cancels in-flight turn.
            }
            catch (Exception ex)
            {
                EnterFailSafeAndDisable(
                    turnId,
                    BotFailureReason.EngineError,
                    $"Unhandled orchestrator error: {ex.Message}",
                    "Errors.Bot.OrchestratorException");
            }
            finally
            {
                lock (_gate)
                {
                    if (ReferenceEquals(_turnCts, turnCts))
                    {
                        _turnCts = null;
                    }
                }

                turnCts.Dispose();

                _inFlightTurnId.Value = null;
                _isThinking.Value = false;
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            try
            {
                Stop();
            }
            finally
            {
                _turnCts?.Cancel();
                _turnCts?.Dispose();
                _turnCts = null;

                _lifetimeCts?.Cancel();
                _lifetimeCts?.Dispose();
                _lifetimeCts = null;

                _subscriptions?.Dispose();
                _subscriptions = null;

                _isStarted.Dispose();
                _isThinking.Dispose();
                _inFlightTurnId.Dispose();
                _lastSubmittedTurnId.Dispose();
                _moveFailed.Dispose();
                _duplicateIgnored.Dispose();
                _diagnostics.Dispose();

                State = BotOrchestratorState.Disposed;
            }
        }

        private void StopCurrentTurn()
        {
            CancellationTokenSource? turnCts;
            lock (_gate)
            {
                turnCts = _turnCts;
                _turnCts = null;
            }

            turnCts?.Cancel();
            turnCts?.Dispose();
            _isThinking.Value = false;
            _inFlightTurnId.Value = null;
        }

        private void EnterFailSafeAndDisable(
            BotTurnId turnId,
            BotFailureReason reason,
            string message,
            string errorKey)
        {
            if (!_failSafeGateway.TryEnterAbortState(errorKey))
            {
                _disabled = true;
                return;
            }

            _disabled = true;
            StopCurrentTurn();
            _moveFailed.OnNext(new BotMoveFailedEvent(turnId, reason, message));
        }

        private void ThrowIfDisposed()
        {
            if (_disposed || State == BotOrchestratorState.Disposed)
            {
                throw new ObjectDisposedException(nameof(BotTurnOrchestrator));
            }
        }

        private static string BuildMatchInstanceId(Runtime.Gameplay.IGameplaySnapshotProvider snapshot)
        {
            var lastMove = snapshot.LastMove;
            return lastMove.HasValue
                ? $"local-{snapshot.CommandSequence}-{lastMove.Value.Major}-{lastMove.Value.Minor}"
                : $"local-{snapshot.CommandSequence}-none";
        }
    }
}
