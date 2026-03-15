#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using Runtime.Gameplay;
using Runtime.Games.TicTacToe.AI.Ultimate.Core;

namespace Runtime.Games.TicTacToe.AI.Ultimate.Execution
{
    public sealed class BotTurnOrchestrator : IBotTurnOrchestrator
    {
        private readonly IGameplayEventStream _events;
        private readonly IGameplaySnapshotProvider _snapshot;
        private readonly IUltimateBotProfileCatalog _profiles;
        private readonly IBotRngSessionFactory _rngFactory;
        private readonly IMatchFailSafeGateway _failSafeGateway;
        private readonly BotTurnExecutionRunner _executionRunner;
        private readonly BotTurnExecutionState _turnState = new();

        private readonly ReactiveProperty<bool> _isStarted = new(false);
        private readonly Subject<BotMoveFailedEvent> _moveFailed = new();
        private readonly Subject<DuplicateTurnIgnoredEvent> _duplicateIgnored = new();
        private readonly Subject<BotDecisionDiagnostics> _diagnostics = new();

        private CompositeDisposable? _subscriptions;
        private CancellationTokenSource? _lifetimeCts;
        private IBotRngSession? _rng;
        private UltimateBotDifficultyProfileData _profile;
        private int _botSlot;
        private bool _disabled;
        private bool _disposed;

        public BotOrchestratorState State { get; private set; } = BotOrchestratorState.NotStarted;

        public ReadOnlyReactiveProperty<bool> IsStarted => _isStarted;
        public ReadOnlyReactiveProperty<bool> IsThinking => _turnState.IsThinking;
        public ReadOnlyReactiveProperty<BotTurnId?> InFlightTurnId => _turnState.InFlightTurnId;
        public ReadOnlyReactiveProperty<BotTurnId?> LastSubmittedTurnId => _turnState.LastSubmittedTurnId;

        public Observable<BotMoveFailedEvent> MoveFailed => _moveFailed;
        public Observable<DuplicateTurnIgnoredEvent> DuplicateIgnored => _duplicateIgnored;
        public Observable<BotDecisionDiagnostics> Diagnostics => _diagnostics;

        public BotTurnOrchestrator(
            IGameplayEventStream events,
            IGameplaySnapshotProvider snapshot,
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
            _rngFactory = rngFactory ?? throw new ArgumentNullException(nameof(rngFactory));
            _failSafeGateway = failSafeGateway ?? throw new ArgumentNullException(nameof(failSafeGateway));

            _executionRunner = new BotTurnExecutionRunner(
                _snapshot,
                stateReader ?? throw new ArgumentNullException(nameof(stateReader)),
                engine ?? throw new ArgumentNullException(nameof(engine)),
                commandSink ?? throw new ArgumentNullException(nameof(commandSink)));
        }

        public UniTask StartAsync(int botSlot, string difficultyId, CancellationToken ct)
        {
            ThrowIfDisposed();

            if (State != BotOrchestratorState.NotStarted)
                throw new InvalidOperationException("StartAsync can be called only from NotStarted state.");

            if (!_profiles.TryGet(difficultyId, out var profile))
                throw new InvalidOperationException($"Ultimate bot profile '{difficultyId}' not found.");

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
                        TriggerIfBotTurnAsync(_lifetimeCts.Token).Forget();
                })
                .AddTo(_subscriptions);

            _events.RoundFinished
                .Subscribe(_ => _turnState.StopCurrentTurn())
                .AddTo(_subscriptions);

            State = BotOrchestratorState.Active;
            _isStarted.Value = true;

            if (_snapshot.ActivePlayerSlot == _botSlot)
                TriggerIfBotTurnAsync(_lifetimeCts.Token).Forget();

            return UniTask.CompletedTask;
        }

        public void Stop()
        {
            if (State == BotOrchestratorState.Disposed) return;

            if (State == BotOrchestratorState.NotStarted)
                return;

            _turnState.StopCurrentTurn();
            State = BotOrchestratorState.Stopped;
            _isStarted.Value = false;
        }

        public async UniTask TriggerIfBotTurnAsync(CancellationToken ct)
        {
            if (!TryPrepareTurnExecution(ct, out var turnId, out var turnCts))
                return;

            try
            {
                await ExecutePreparedTurnAsync(turnId, turnCts.Token);
            }
            catch (OperationCanceledException)
            {
                // Normal lifecycle path: stop/dispose/restart cancels in-flight turn.
            }
            catch (Exception ex)
            {
                EnterFailSafeAndDisable(
                    BotFailureReason.EngineError,
                    $"Unhandled orchestrator error: {ex.Message}",
                    "Errors.Bot.OrchestratorException");
            }
            finally
            {
                FinalizeTurnExecution(turnCts);
            }
        }

        private bool TryPrepareTurnExecution(
            CancellationToken ct,
            out BotTurnId turnId,
            out CancellationTokenSource turnCts)
        {
            turnId = default;
            turnCts = null!;

            if (!CanExecuteTurnNow())
                return false;

            turnId = BotTurnId.Build(_snapshot.CommandSequence, _snapshot.ActivePlayerSlot);

            if (!_turnState.TryBeginTurn(turnId, ct, out turnCts))
            {
                _duplicateIgnored.OnNext(default);
                return false;
            }

            return true;
        }

        private bool CanExecuteTurnNow()
        {
            if (State != BotOrchestratorState.Active || _disabled)
                return false;

            return _snapshot.ActivePlayerSlot == _botSlot;
        }

        private async UniTask ExecutePreparedTurnAsync(BotTurnId turnId, CancellationToken turnToken)
        {
            var execution = await _executionRunner.ExecuteAsync(
                _botSlot,
                turnId,
                _profile,
                _rng!,
                turnToken);

            ApplyExecutionResult(execution);
        }

        private void ApplyExecutionResult(BotTurnExecutionResult execution)
        {
            if (execution.HasFailure)
            {
                var failure = execution.Failure!.Value;
                EnterFailSafeAndDisable(failure.Reason, failure.Message, failure.ErrorKey);
            }

            if (execution.HasSubmittedTurnId)
                _turnState.MarkSubmitted(execution.SubmittedTurnId!.Value);

            if (execution.Diagnostics.HasValue)
                _diagnostics.OnNext(execution.Diagnostics.Value);
        }

        private void FinalizeTurnExecution(CancellationTokenSource turnCts) => _turnState.FinalizeTurn(turnCts);

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            try
            {
                Stop();
            }
            finally
            {
                _lifetimeCts?.Cancel();
                _lifetimeCts?.Dispose();
                _lifetimeCts = null;

                _subscriptions?.Dispose();
                _subscriptions = null;

                _isStarted.Dispose();
                _turnState.Dispose();
                _moveFailed.Dispose();
                _duplicateIgnored.Dispose();
                _diagnostics.Dispose();

                State = BotOrchestratorState.Disposed;
            }
        }

        private void EnterFailSafeAndDisable(BotFailureReason reason, string message, string errorKey)
        {
            if (!_failSafeGateway.TryEnterAbortState(errorKey))
            {
                _disabled = true;
                return;
            }

            _disabled = true;
            _turnState.StopCurrentTurn();
            _moveFailed.OnNext(new BotMoveFailedEvent(reason, message));
        }

        private void ThrowIfDisposed()
        {
            if (_disposed || State == BotOrchestratorState.Disposed)
                throw new ObjectDisposedException(nameof(BotTurnOrchestrator));
        }

        private static string BuildMatchInstanceId(IGameplaySnapshotProvider snapshot)
        {
            var lastMove = snapshot.LastMove;

            return lastMove.HasValue
                ? $"local-{snapshot.CommandSequence}-{lastMove.Value.Major}-{lastMove.Value.Minor}"
                : $"local-{snapshot.CommandSequence}-none";
        }
    }
}
