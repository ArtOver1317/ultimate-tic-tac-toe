#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using Runtime.GameModes.Wizard.Configs;
using Runtime.GameModes.Wizard.Modes;
using Runtime.GameModes.Wizard.Online;
using Runtime.Gameplay;
using Runtime.Gameplay.Shared;
using Runtime.Games.Battleship.Core;
using Runtime.Games.TicTacToe.AI;
using Runtime.Games.TicTacToe.Moves;

namespace Runtime.Games.Battleship.AI
{
    public sealed class BattleshipBotDriver : IBattleshipBotDriver
    {
        private readonly IBattleshipGameplaySnapshotProvider _battleshipSnapshotProvider;
        private readonly IBattleshipGameplayEventStream _battleshipEventStream;
        private readonly IGameplayEventStream _gameplayEventStream;
        private readonly IGameplayCommandSink _commandSink;
        private readonly IBattleshipAutoPlacer _autoPlacer;
        private readonly IOnlineGameplaySessionContextStore _onlineSessionContextStore;
        private readonly BattleshipGameplaySettingsData _settings;
        private readonly BattleshipBotTargetSelector _targetSelector = new();

        private readonly ReactiveProperty<bool> _isThinking = new(false);

        private CompositeDisposable? _subscriptions;
        private CancellationTokenSource? _lifetimeCts;
        private CancellationTokenSource? _turnLoopCts;
        private Random? _random;
        private bool _turnLoopRunning;
        private bool _disposed;

        public BattleshipBotDriver(
            IBattleshipGameplaySnapshotProvider battleshipSnapshotProvider,
            IBattleshipGameplayEventStream battleshipEventStream,
            IGameplayEventStream gameplayEventStream,
            IGameplayCommandSink commandSink,
            IBattleshipAutoPlacer autoPlacer,
            IOnlineGameplaySessionContextStore onlineSessionContextStore,
            BattleshipGameplaySettings battleshipGameplaySettings)
        {
            _battleshipSnapshotProvider = battleshipSnapshotProvider ?? throw new ArgumentNullException(nameof(battleshipSnapshotProvider));
            _battleshipEventStream = battleshipEventStream ?? throw new ArgumentNullException(nameof(battleshipEventStream));
            _gameplayEventStream = gameplayEventStream ?? throw new ArgumentNullException(nameof(gameplayEventStream));
            _commandSink = commandSink ?? throw new ArgumentNullException(nameof(commandSink));
            _autoPlacer = autoPlacer ?? throw new ArgumentNullException(nameof(autoPlacer));
            _onlineSessionContextStore = onlineSessionContextStore ?? throw new ArgumentNullException(nameof(onlineSessionContextStore));
            
            _settings = battleshipGameplaySettings != null
                ? battleshipGameplaySettings.ToValidatedData()
                : BattleshipGameplaySettingsData.Default;
        }

        public ReadOnlyReactiveProperty<bool> IsThinking => _isThinking;
        public bool IsStarted { get; private set; }
        public int BotSlot { get; private set; } = PlayerSlotMapping.SlotO;

        public UniTask<BotStartResult> StartAsync(
            GameLaunchConfig config,
            int botSlot,
            CancellationToken ct)
        {
            if (_disposed)
                return UniTask.FromResult(new BotStartResult(BotStartStatus.Failed, "Driver disposed."));

            if (IsStarted)
                return UniTask.FromResult(new BotStartResult(BotStartStatus.Failed, "Already started."));

            if (config.GameConfig is not BattleshipConfig)
                return UniTask.FromResult(new BotStartResult(BotStartStatus.UnsupportedConfig, "Not a Battleship config."));

            if (config.OpponentConfig is not BotOpponentConfig)
                return UniTask.FromResult(new BotStartResult(BotStartStatus.NotEnabled));

            if (_onlineSessionContextStore.Snapshot.IsOnlineDirectInvite)
                return UniTask.FromResult(new BotStartResult(BotStartStatus.UnsupportedConfig, "Online direct invite does not use local bot driver."));

            BotSlot = botSlot;
            _random = new Random(unchecked(Environment.TickCount + botSlot * 7919));
            _lifetimeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            IsStarted = true;

            Subscribe();
            TrySubmitPlacementIfNeeded();
            TryStartTurnLoop();

            return UniTask.FromResult(new BotStartResult(BotStartStatus.Started));
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            CancelTurnLoop();
            _lifetimeCts?.Cancel();
            _lifetimeCts?.Dispose();
            _lifetimeCts = null;

            _subscriptions?.Dispose();
            _subscriptions = null;

            _isThinking.Dispose();
            IsStarted = false;
            _random = null;
        }

        private void Subscribe()
        {
            _subscriptions?.Dispose();
            _subscriptions = new CompositeDisposable();

            _battleshipEventStream.PhaseChanged
                .Subscribe(_ =>
                {
                    TrySubmitPlacementIfNeeded();
                    TryStartTurnLoop();
                })
                .AddTo(_subscriptions);

            _gameplayEventStream.CurrentPlayerChanged
                .Subscribe(_ => TryStartTurnLoop())
                .AddTo(_subscriptions);

            _gameplayEventStream.RoundFinished
                .Subscribe(_ => CancelTurnLoop())
                .AddTo(_subscriptions);
        }

        private void TrySubmitPlacementIfNeeded()
        {
            if (!IsStarted || _disposed)
                return;

            var phase = _battleshipSnapshotProvider.Phase;
            
            if (phase != BattleshipPhase.Placement && phase != BattleshipPhase.Waiting)
                return;

            if (_battleshipSnapshotProvider.IsPlacementConfirmed(BotSlot))
                return;

            var seed = _random?.Next() ?? Environment.TickCount;
            var layout = _autoPlacer.Generate(seed);
            _commandSink.SubmitCommand(new SubmitPlacementCommand(BotSlot, layout));
        }

        private void TryStartTurnLoop()
        {
            if (!CanRunTurnLoop())
                return;

            _turnLoopCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts!.Token);
            RunTurnLoopAsync(_turnLoopCts.Token).Forget();
        }

        private async UniTaskVoid RunTurnLoopAsync(CancellationToken ct)
        {
            _turnLoopRunning = true;
            SetThinkingSafe(true);

            try
            {
                while (!ct.IsCancellationRequested && CanContinueTurnLoop())
                {
                    if (!TrySubmitNextShot())
                        break;

                    await WaitAfterShotAsync(ct);
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                SetThinkingSafe(false);
                _turnLoopRunning = false;
                CancelTurnLoop();
            }
        }

        private bool TrySubmitNextShot()
        {
            if (!TryResolveNextTarget(out var targetCell))
                return false;

            _commandSink.SubmitCommand(new MakeMoveCommand(targetCell));
            return true;
        }

        private UniTask WaitAfterShotAsync(CancellationToken ct)
        {
            if (_settings.BotShotDelaySeconds > 0f)
            {
                return UniTask.Delay(
                    TimeSpan.FromSeconds(_settings.BotShotDelaySeconds),
                    DelayType.UnscaledDeltaTime,
                    PlayerLoopTiming.Update,
                    ct);
            }

            return UniTask.Yield(PlayerLoopTiming.Update, ct);
        }

        private bool CanRunTurnLoop()
        {
            if (!IsStarted || _disposed || _turnLoopRunning)
                return false;

            return CanContinueTurnLoop();
        }

        private bool CanContinueTurnLoop() =>
            IsStarted
            && !_disposed
            && _battleshipSnapshotProvider is { Phase: BattleshipPhase.Battle, CurrentStatus: GameStatus.InProgress }
            && _battleshipSnapshotProvider.ActivePlayerSlot == BotSlot;

        private bool TryResolveNextTarget(out CellId cellId)
        {
            cellId = default;
            var marks = _battleshipSnapshotProvider.GetOpponentMarks(BotSlot);
            return _targetSelector.TryChooseTarget(marks, _random, out cellId);
        }

        private void SetThinkingSafe(bool value)
        {
            if (_disposed)
                return;

            try
            {
                _isThinking.Value = value;
            }
            catch (ObjectDisposedException) { }
        }

        private void CancelTurnLoop()
        {
            _turnLoopCts?.Cancel();
            _turnLoopCts?.Dispose();
            _turnLoopCts = null;
        }
    }
}