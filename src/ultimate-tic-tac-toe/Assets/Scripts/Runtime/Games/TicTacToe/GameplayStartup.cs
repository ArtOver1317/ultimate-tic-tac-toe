using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using Runtime.GameModes.Wizard;
using Runtime.GameModes.Wizard.Configs;
using Runtime.GameModes.Wizard.Online;
using Runtime.Gameplay;
using Runtime.Gameplay.ECS;
using Runtime.Games.Battleship;
using Runtime.Games.TicTacToe.AI;
using Runtime.Games.TicTacToe.AI.Ultimate;
using Runtime.Games.TicTacToe.ECS;
using Runtime.Games.TicTacToe.Moves;
using Runtime.Games.TicTacToe.Rules;
using Runtime.Games.TicTacToe.Series;
using Runtime.Games.TicTacToe.Ultimate;
using Runtime.Games.TicTacToe.Ultimate.Rules;
using Runtime.Games.TicTacToe.Ultimate.UI;
using Runtime.Infrastructure.GameStateMachine;
using Runtime.Infrastructure.GameStateMachine.States;
using Runtime.Infrastructure.Logging;
using Runtime.Localization;
using Runtime.PlayerStatistics;
using Runtime.PlayerProfile;
using StripLog;
using UnityEngine;
using EcsRoundFinishedEvent = Runtime.Gameplay.ECS.RoundFinishedEvent;
using EcsGameStatus = Runtime.Gameplay.ECS.GameStatus;

namespace Runtime.Games.TicTacToe
{
    public sealed class GameplayStartup : IGameplayStartup, IDisposable
    {
        private static readonly TimeSpan RestartEpochWaitTimeout = TimeSpan.FromSeconds(1);
        private static readonly object BattleshipSeriesScoresGate = new();
        private static readonly Dictionary<string, SeriesScore> BattleshipSeriesScores = new(StringComparer.Ordinal);

        private readonly IGameLaunchConfigStore _configStore;
        private readonly IGameService _gameService;
        private readonly IGameplayFieldPresenter _fieldPresenter;
        private readonly IGameplayFieldUiAdapter _fieldUiAdapter;
        private readonly IMatchEcsLifecycle _ecsLifecycle;
        private readonly IGameplayEventStream _eventStream;
        private readonly IGameplayCommandSink _commandSink;
        private readonly GameplayMovesBinder _movesBinder;
        private readonly BattleshipBoardsBinder _battleshipBoardsBinder;
        private readonly MoveTimerHudBinder _moveTimerHudBinder;
        private readonly WinLineRenderer _winLineRenderer;
        private readonly ISeriesService _seriesService;
        private readonly IMatchPlayerNames _matchPlayerNames;
        private readonly IGameplayBackHandler _backHandler;
        private readonly IGameStateMachine _stateMachine;
        private readonly IBotTurnDriver _botDriver;
        private readonly IBattleshipBotDriver _battleshipBotDriver;
        private readonly IBotTurnOrchestrator _ultimateBotOrchestrator;
        private readonly IMatchFailSafeGateway _matchFailSafeGateway;
        private readonly IUltimateGameplaySnapshotProvider _ultimateSnapshotProvider;
        private readonly IGameplayNetworkBridge _networkBridge;
        private readonly IBattleshipNetworkBridge _battleshipNetworkBridge;
        private readonly IOnlineGameplaySessionContextStore _onlineSessionContextStore;
        private readonly IOnlineSessionFlowService _onlineSessionFlow;
        private readonly IOnlineSessionLauncher _onlineSessionLauncher;
        private readonly IOnlinePlayerNamesStore _onlinePlayerNamesStore;
        private readonly IMatchStateProvider _matchStateProvider;
        private readonly IMoveTimerService _moveTimerService;
        private readonly IBattleshipPlacementTimerService _battleshipPlacementTimerService;
        private readonly ILocalizationService _localization;
        private readonly PlayerStatisticsMatchReporter _statisticsReporter;
        private readonly IBattleshipPlacementUiController _battleshipPlacementUiController;
        private readonly BattleshipPlacementTimerHudBinder _battleshipPlacementTimerHudBinder;
        private readonly IBattleshipGameplaySnapshotProvider _battleshipSnapshotProvider;
        private readonly IBattleshipGameplayEventStream _battleshipEventStream;
        private readonly IBattleshipLayoutSerializer _battleshipLayoutSerializer;
        private readonly IBattleshipRecoveryStateApplier _battleshipRecoveryStateApplier;
        private readonly HostAuthoritativeMoveProcessor _hostMoveProcessor = new();
        private readonly OnlineRoundCoordinator _onlineRoundCoordinator = new();
        private readonly MiniBoardStatus[] _ultimateMiniBoardBuffer = new MiniBoardStatus[9];

        private FieldRenderSpec _fieldSpec;
        private GameResultViewModel _resultVM;
        private UltimateAllowedBinder _ultimateAllowedBinder;
        private UltimateMiniBoardStatusBinder _ultimateMiniBoardStatusBinder;
        private CompositeDisposable _subscriptions;
        private bool _restartInProgress;
        private bool _classicBotStarted;
        private bool _battleshipBotStarted;
        private bool _ultimateBotStarted;
        private bool _isOnlineDirectInvite;
        private bool _onlineIsHost;
        private bool _onlineRoundFinished;
        private bool _onlineRematchStarted;
        private bool _onlineTerminalResultShown;
        private bool _useHostAuthoritativeFilter;
        private bool _onlinePlayerNamesStoreBound;
        private bool _isBattleshipMatch;
        private int _battleshipCurrentStartingSlot = -1;
        private bool _battleshipRecoveryHeartbeatStarted;
        private int _exitToMenuRequested;
        private GameLaunchConfig _activeLaunchConfig;
        private string? _onlineLocalUserId;
        private string? _onlineRemoteUserId;
        private long _onlineAcceptedShotSequence;
        private bool _disposed;

        public GameplayStartup(
            IGameLaunchConfigStore configStore,
            IGameService gameService,
            IGameplayFieldPresenter fieldPresenter,
            IGameplayFieldUiAdapter fieldUiAdapter,
            IMatchEcsLifecycle ecsLifecycle,
            IGameplayEventStream eventStream,
            IGameplayCommandSink commandSink,
            GameplayMovesBinder movesBinder,
            WinLineRenderer winLineRenderer,
            ISeriesService seriesService,
            IGameplayBackHandler backHandler,
            IGameStateMachine stateMachine,
            IBotTurnDriver botDriver,
            IBotTurnOrchestrator ultimateBotOrchestrator,
            IMatchFailSafeGateway matchFailSafeGateway,
            IUltimateGameplaySnapshotProvider ultimateSnapshotProvider = null,
            IGameplayNetworkBridge networkBridge = null,
            IBattleshipNetworkBridge battleshipNetworkBridge = null,
            IOnlineGameplaySessionContextStore onlineSessionContextStore = null,
            IMatchStateProvider matchStateProvider = null,
            IOnlineSessionFlowService onlineSessionFlow = null,
            IOnlineSessionLauncher onlineSessionLauncher = null,
            IOnlinePlayerNamesStore onlinePlayerNamesStore = null,
            ILocalizationService localization = null,
            IMoveTimerService moveTimerService = null,
            IBattleshipPlacementTimerService battleshipPlacementTimerService = null,
            MoveTimerHudBinder moveTimerHudBinder = null,
            BattleshipPlacementTimerHudBinder battleshipPlacementTimerHudBinder = null,
            IBattleshipGameplaySnapshotProvider battleshipSnapshotProvider = null,
            IBattleshipGameplayEventStream battleshipEventStream = null,
            IBattleshipLayoutSerializer battleshipLayoutSerializer = null,
            IBattleshipRecoveryStateApplier battleshipRecoveryStateApplier = null,
            IMatchPlayerNames matchPlayerNames = null,
            PlayerStatisticsMatchReporter statisticsReporter = null,
            IBattleshipBotDriver battleshipBotDriver = null,
            IBattleshipPlacementUiController battleshipPlacementUiController = null,
            BattleshipBoardsBinder battleshipBoardsBinder = null)
        {
            _configStore = configStore ?? throw new ArgumentNullException(nameof(configStore));
            _gameService = gameService ?? throw new ArgumentNullException(nameof(gameService));
            _fieldPresenter = fieldPresenter ?? throw new ArgumentNullException(nameof(fieldPresenter));
            _fieldUiAdapter = fieldUiAdapter ?? throw new ArgumentNullException(nameof(fieldUiAdapter));
            _ecsLifecycle = ecsLifecycle ?? throw new ArgumentNullException(nameof(ecsLifecycle));
            _eventStream = eventStream ?? throw new ArgumentNullException(nameof(eventStream));
            _commandSink = commandSink ?? throw new ArgumentNullException(nameof(commandSink));
            _movesBinder = movesBinder ?? throw new ArgumentNullException(nameof(movesBinder));
            _battleshipBoardsBinder = battleshipBoardsBinder;
            _moveTimerHudBinder = moveTimerHudBinder;
            _winLineRenderer = winLineRenderer ?? throw new ArgumentNullException(nameof(winLineRenderer));
            _seriesService = seriesService ?? throw new ArgumentNullException(nameof(seriesService));
            _matchPlayerNames = matchPlayerNames;
            _backHandler = backHandler ?? throw new ArgumentNullException(nameof(backHandler));
            _stateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
            _botDriver = botDriver ?? throw new ArgumentNullException(nameof(botDriver));
            _battleshipBotDriver = battleshipBotDriver;
            _ultimateBotOrchestrator = ultimateBotOrchestrator ?? throw new ArgumentNullException(nameof(ultimateBotOrchestrator));
            _matchFailSafeGateway = matchFailSafeGateway ?? throw new ArgumentNullException(nameof(matchFailSafeGateway));
            _ultimateSnapshotProvider = ultimateSnapshotProvider;
            _networkBridge = networkBridge ?? new NoOpGameplayNetworkBridge();
            _battleshipNetworkBridge = battleshipNetworkBridge ?? NoOpBattleshipNetworkBridge.Instance;
            _onlineSessionContextStore = onlineSessionContextStore ?? new OnlineGameplaySessionContextStore();
            _matchStateProvider = matchStateProvider ?? commandSink as IMatchStateProvider;
            _onlineSessionFlow = onlineSessionFlow ?? NoOpOnlineSessionFlowService.Instance;
            _onlineSessionLauncher = onlineSessionLauncher ?? NoOpOnlineSessionLauncher.Instance;
            _onlinePlayerNamesStore = onlinePlayerNamesStore;
            _localization = localization;
            _moveTimerService = moveTimerService ?? NoOpMoveTimerService.Instance;
            _battleshipPlacementTimerService = battleshipPlacementTimerService ?? NoOpBattleshipPlacementTimerService.Instance;
            _battleshipPlacementTimerHudBinder = battleshipPlacementTimerHudBinder;
            _battleshipSnapshotProvider = battleshipSnapshotProvider ?? _matchStateProvider as IBattleshipGameplaySnapshotProvider;
            _battleshipEventStream = battleshipEventStream ?? _matchStateProvider as IBattleshipGameplayEventStream;
            _battleshipLayoutSerializer = battleshipLayoutSerializer ?? new BattleshipLayoutSerializer();
            _battleshipRecoveryStateApplier = battleshipRecoveryStateApplier ?? _matchStateProvider as IBattleshipRecoveryStateApplier;
            _statisticsReporter = statisticsReporter;
            _battleshipPlacementUiController = battleshipPlacementUiController;
        }

        public async UniTask StartAsync(CancellationToken ct)
        {
            if (_statisticsReporter == null)
            {
                GameLog.Warning("[GameplayStartup] PlayerStatisticsMatchReporter is not resolved. Statistics reporting is disabled for this match.");
            }

            ct.ThrowIfCancellationRequested();

            if (!_configStore.TryConsume(out var config) || config == null)
            {
                CleanupGameplay();
                await HandleErrorAsync(GameplayError.InvalidConfig("Launch config not found."), ct);
                return;
            }

            config = ApplyOnlineMatchConfigOverrideIfNeeded(config);
            _activeLaunchConfig = config;
            _isBattleshipMatch = string.Equals(config.GameId, BattleshipStrategy.DefaultGameId, StringComparison.Ordinal);
            _battleshipCurrentStartingSlot = config.StartingPlayerSlotOverride ?? -1;

            IGameplaySession session = null;
            try
            {
                session = await _gameService.StartMatchAsync(config, ct);
                await _fieldPresenter.BindAsync(session.FieldRenderSpec, ct, config.GameId);
                BindOnlinePlayerNamesStoreIfNeeded();

                _fieldSpec = session.FieldRenderSpec;
                _seriesService.StartSeries();
                RestoreBattleshipSessionScoreIfNeeded(config);

                _ecsLifecycle.StartMatch(config);
                var activePlayerSlot = _matchStateProvider?.ActivePlayerSlot ?? 0;
                if (_isBattleshipMatch)
                    _battleshipPlacementTimerService.SyncFromSnapshot();
                else
                    _moveTimerService.StartOrResetForPlayer(activePlayerSlot);
                _movesBinder.Bind();
                if (_isBattleshipMatch)
                {
                    _battleshipBoardsBinder?.Bind();
                    _battleshipPlacementUiController?.Bind();
                    SyncBattleshipTimerHudBindings();
                }
                else
                    _moveTimerHudBinder?.Bind();
                BindUltimateUiIfNeeded();
                SetRoundFinishedVisualState(false);

                // Start bot driver if opponent is a bot
                await TryStartBotAsync(config, ct);

                CreateResultVM();
                SubscribeToEvents();
                await BindOnlineMoveBridgeAsync(ct);
            }
            catch (OperationCanceledException)
            {
                session?.Dispose();
                CleanupGameplay();
                throw;
            }
            catch (Exception ex)
            {
                session?.Dispose();
                CleanupGameplay();
                var error = MapError(ex);
                Log.Error(LogTags.Infrastructure, $"[GameplayStartup] Failed to start gameplay: {ex}");
                await HandleErrorAsync(error, ct);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _subscriptions?.Dispose();
            _subscriptions = null;
            UnbindOnlinePlayerNamesStoreIfNeeded();
            _botDriver.Dispose();
            _battleshipBotDriver?.Dispose();
            _ultimateBotOrchestrator.Dispose();
            _battleshipPlacementUiController?.Unbind();
            _battleshipBoardsBinder?.Unbind();
            _movesBinder.Unbind();
            _moveTimerHudBinder?.Unbind();
            _battleshipPlacementTimerHudBinder?.Unbind();
            DisposeUltimateUiBinders();
            _battleshipNetworkBridge.UnbindAsync().Forget();
            _networkBridge.UnbindAsync().Forget();
            _moveTimerService.Stop();
            _battleshipPlacementTimerService.Stop();
            _ecsLifecycle.StopMatch();
            _winLineRenderer.Clear();
            _resultVM?.Dispose();
            _resultVM = null;
            _fieldPresenter.Unbind();
        }

        private sealed class NoOpMoveTimerService : IMoveTimerService
        {
            public static readonly NoOpMoveTimerService Instance = new();

            private readonly ReactiveProperty<float> _remainingSeconds = new(0f);
            private readonly ReactiveProperty<bool> _isActive = new(false);

            public ReadOnlyReactiveProperty<float> RemainingSeconds => _remainingSeconds;
            public ReadOnlyReactiveProperty<bool> IsActive => _isActive;

            public void StartOrResetForPlayer(int playerSlot) { }
            public void RestoreRemainingSeconds(float remainingSeconds, int activePlayerSlot) { }
            public void Stop() { }
            public void Freeze() { }
            public void Unfreeze() { }
            public void Dispose() { }
        }

        private sealed class NoOpBattleshipPlacementTimerService : IBattleshipPlacementTimerService
        {
            public static readonly NoOpBattleshipPlacementTimerService Instance = new();

            private readonly ReactiveProperty<float> _remainingSeconds = new(0f);
            private readonly ReactiveProperty<bool> _isActive = new(false);

            public ReadOnlyReactiveProperty<float> RemainingSeconds => _remainingSeconds;
            public ReadOnlyReactiveProperty<bool> IsActive => _isActive;

            public void SyncFromSnapshot() { }
            public void RestoreRemainingSeconds(float remainingSeconds) { }
            public void Stop() { }
            public void Freeze() { }
            public void Unfreeze() { }
            public void Dispose() { }
        }


        // -- Bot integration --

        // TODO: BvB support (ADR-10) — currently only Player vs Bot (slot 1).
        // For Bot vs Bot: need BvBOpponentConfig type, second driver creation
        // (manual new BotTurnDriver(...) or factory), and DI refactoring.
        // Self-play tool covers BvB calibration in the meantime.

        private async UniTask TryStartBotAsync(GameLaunchConfig config, CancellationToken ct)
        {
            if (config.OpponentConfig is not BotOpponentConfig botConfig)
                return;

            if (_isBattleshipMatch)
            {
                if (_battleshipBotDriver == null)
                {
                    Log.Warning(LogTags.Infrastructure, "[GameplayStartup] Battleship bot driver is not resolved.");
                    return;
                }

                var battleshipBotStart = await _battleshipBotDriver.StartAsync(config, PlayerSlotMapping.SlotO, ct);
                _battleshipBotStarted = battleshipBotStart.Status == BotStartStatus.Started;
                if (_battleshipBotStarted)
                    return;

                Log.Warning(LogTags.Infrastructure,
                    $"[GameplayStartup] Battleship bot driver not started: {battleshipBotStart.Status} — {battleshipBotStart.Error}");
                return;
            }

            if (IsUltimateConfig(config.GameConfig))
            {
                var normalizedDifficultyId = NormalizeUltimateDifficultyId(botConfig.DifficultyId);
                try
                {
                    await _ultimateBotOrchestrator.StartAsync(botSlot: 1, normalizedDifficultyId, ct);
                    _ultimateBotStarted = true;
                    return;
                }
                catch (Exception ex)
                {
                    Log.Warning(
                        LogTags.Infrastructure,
                        $"[GameplayStartup] Ultimate bot orchestrator not started: {ex.Message}");
                    return;
                }
            }

            var result = await _botDriver.StartAsync(config, botSlot: 1, botConfig.DifficultyId, ct);
            _classicBotStarted = result.Status == BotStartStatus.Started;
            if (_classicBotStarted)
                return;

            Log.Warning(LogTags.Infrastructure,
                $"[GameplayStartup] Bot driver not started: {result.Status} — {result.Error}");
        }

        private static bool IsUltimateConfig(IGameConfig gameConfig) => gameConfig switch
        {
            UltimateTicTacToeConfig => true,
            TicTacToeConfig ticTacToeConfig => ticTacToeConfig.IsUltimate,
            _ => false,
        };

        private GameLaunchConfig ApplyOnlineMatchConfigOverrideIfNeeded(GameLaunchConfig config)
        {
            var session = _onlineSessionContextStore.Snapshot;
            if (!session.IsOnlineDirectInvite || !session.MatchConfig.HasValue)
                return config;

            var payload = session.MatchConfig.Value;
            if (!string.Equals(payload.GameId, config.GameId, StringComparison.Ordinal))
                return config;

            return new GameLaunchConfig(
                config.GameId,
                payload.ToGameConfig(),
                config.OpponentConfig,
                payload.MoveTimeLimitSeconds,
                config.StartingPlayerSlotOverride);
        }

        // -- Event wiring --

        private void CreateResultVM()
        {
            _resultVM?.Dispose();

            var container = _fieldUiAdapter.FieldContainer;
            if (container == null) return;

            _resultVM = new GameResultViewModel(container, _localization);
        }

        private void SubscribeToEvents()
        {
            _subscriptions?.Dispose();
            _subscriptions = new CompositeDisposable();

            _eventStream.RoundFinished
                .Subscribe(OnRoundFinished)
                .AddTo(_subscriptions);

            SubscribeScoreboardPlayerNames();

            // Phase 4: Input blocking while bot is thinking
            if (_classicBotStarted)
            {
                _botDriver.IsBusy
                    .Subscribe(busy =>
                    {
                        var container = _fieldUiAdapter.FieldContainer;
                        if (container != null)
                            container.pickingMode = busy
                                ? UnityEngine.UIElements.PickingMode.Ignore
                                : UnityEngine.UIElements.PickingMode.Position;
                    })
                    .AddTo(_subscriptions);
            }

            if (_ultimateBotStarted)
            {
                _ultimateBotOrchestrator.IsThinking
                    .Subscribe(thinking =>
                    {
                        var container = _fieldUiAdapter.FieldContainer;
                        if (container != null)
                            container.pickingMode = thinking || _matchFailSafeGateway.IsInputLocked
                                ? UnityEngine.UIElements.PickingMode.Ignore
                                : UnityEngine.UIElements.PickingMode.Position;
                    })
                    .AddTo(_subscriptions);

                _ultimateBotOrchestrator.MoveFailed
                    .Where(evt => evt.Reason is BotFailureReason.NoLegalMovesInconsistentState or BotFailureReason.EngineError)
                    .Subscribe(evt =>
                    {
                        Log.Error(LogTags.Infrastructure,
                            $"[GameplayStartup] Ultimate bot move failed: {evt.Reason} ({evt.Message})");

                        var container = _fieldUiAdapter.FieldContainer;
                        if (container != null)
                            container.pickingMode = _matchFailSafeGateway.IsInputLocked
                            ? UnityEngine.UIElements.PickingMode.Ignore
                            : UnityEngine.UIElements.PickingMode.Position;
                    })
                    .AddTo(_subscriptions);
            }

            if (_battleshipBotStarted && _battleshipBotDriver != null)
            {
                _battleshipBotDriver.IsThinking
                    .Subscribe(thinking =>
                    {
                        var container = _fieldUiAdapter.FieldContainer;
                        if (container != null)
                            container.pickingMode = thinking || _matchFailSafeGateway.IsInputLocked
                                ? UnityEngine.UIElements.PickingMode.Ignore
                                : UnityEngine.UIElements.PickingMode.Position;
                    })
                    .AddTo(_subscriptions);

                _eventStream.CurrentPlayerChanged
                    .Subscribe(_ => UpdateMoveTimerStateForBattleshipBot())
                    .AddTo(_subscriptions);

                if (_battleshipEventStream != null)
                {
                    _battleshipEventStream.PhaseChanged
                        .Subscribe(_ => UpdateMoveTimerStateForBattleshipBot())
                        .AddTo(_subscriptions);
                }

                UpdateMoveTimerStateForBattleshipBot();
            }

            // ADR-12: Surface bot disable error to user
            if (_classicBotStarted)
            {
                _botDriver.IsDisabled
                    .Where(disabled => disabled)
                    .Subscribe(_ =>
                    {
                        Log.Error(LogTags.Infrastructure,
                            "[GameplayStartup] Bot disabled after exhausting all retry attempts.");
                        var container = _fieldUiAdapter.FieldContainer;
                        if (container != null)
                            container.pickingMode = UnityEngine.UIElements.PickingMode.Position;
                    })
                    .AddTo(_subscriptions);
            }

            if (_resultVM != null)
            {
                _resultVM.Actions
                    .Subscribe(HandleResultAction)
                    .AddTo(_subscriptions);
            }

            _onlineSessionFlow.Snapshot
                .Where(ShouldHandleOnlineOpponentDisconnectAsResult)
                .Subscribe(HandleOnlineOpponentDisconnectAsResult)
                .AddTo(_subscriptions);

            _onlineSessionFlow.Snapshot
                .Where(ShouldExitToMenuByOnlineFlow)
                .Subscribe(_ => ExitToMenuAsync().Forget())
                .AddTo(_subscriptions);

            if (_isBattleshipMatch && _battleshipEventStream != null && _battleshipSnapshotProvider != null)
            {
                _battleshipEventStream.PhaseChanged
                    .Subscribe(_ => SyncBattleshipTimerHudBindings())
                    .AddTo(_subscriptions);

                _battleshipEventStream.PhaseChanged
                    .Where(evt => evt.Phase == BattleshipPhase.Battle)
                    .Subscribe(_ =>
                    {
                        var activeSlot = _battleshipSnapshotProvider.ActivePlayerSlot;
                        if (activeSlot >= 0)
                            _battleshipCurrentStartingSlot = activeSlot;
                    })
                    .AddTo(_subscriptions);

                    SyncBattleshipTimerHudBindings();
            }
        }

        private void SubscribeScoreboardPlayerNames()
        {
            if (_matchPlayerNames == null)
                return;

            var player1NameLabel = _fieldUiAdapter.Player1NameLabel;
            var player2NameLabel = _fieldUiAdapter.Player2NameLabel;

            if (player1NameLabel == null || player2NameLabel == null)
                return;

            var xMark = PlayerMark.X.ToUiText();
            var oMark = PlayerMark.O.ToUiText();

            _matchPlayerNames.GetSlotName(PlayerSlot.Slot1)
                .Subscribe(name => player1NameLabel.text = PlayerLabelFormat.NameWithMark(name, xMark))
                .AddTo(_subscriptions);

            _matchPlayerNames.GetSlotName(PlayerSlot.Slot2)
                .Subscribe(name => player2NameLabel.text = PlayerLabelFormat.NameWithMark(name, oMark))
                .AddTo(_subscriptions);
        }

        private bool ShouldHandleOnlineOpponentDisconnectAsResult(OnlineFlowSnapshot snapshot)
        {
            if (_disposed || !_isOnlineDirectInvite || _onlineTerminalResultShown)
                return false;

            if (!IsOpponentDisconnectTerminal(snapshot))
                return false;

            return true;
        }

        private static bool IsOpponentDisconnectTerminal(OnlineFlowSnapshot snapshot) =>
            snapshot.State == OnlineFlowState.Terminated &&
            (snapshot.ErrorCode == OnlineErrorCode.OpponentLeft ||
             snapshot.ErrorCode == OnlineErrorCode.DisconnectTimeout);

        private void HandleOnlineOpponentDisconnectAsResult(OnlineFlowSnapshot snapshot)
        {
            if (_disposed || _onlineTerminalResultShown)
                return;

            _onlineTerminalResultShown = true;
            _onlineRoundFinished = true;
            _onlineRematchStarted = false;
            _isOnlineDirectInvite = false;

            _moveTimerService.Stop();
            _battleshipPlacementTimerService.Stop();
            _movesBinder.Unbind();
            _battleshipBoardsBinder?.Unbind();
            _moveTimerHudBinder?.Unbind();
            _battleshipPlacementTimerHudBinder?.Unbind();
            _winLineRenderer.Clear();

            var winner = _onlineIsHost ? PlayerMark.X : PlayerMark.O;
            var gameResult = GameResult.Timeout(winner);
            _seriesService.RecordResult(gameResult);
            PersistBattleshipSessionScoreIfNeeded();
            UpdateScoreLabels();

            SetRoundFinishedVisualState(true);
            _resultVM?.Show(gameResult, _seriesService.Score.CurrentValue, ResolveOpponentLeftResultText(snapshot.ErrorCode));
        }

        private string ResolveOpponentLeftResultText(OnlineErrorCode errorCode)
        {
            var key = errorCode == OnlineErrorCode.OpponentLeft
                ? "Errors.Online.OpponentLeft"
                : "Errors.Online.DisconnectTimeout";

            if (_localization == null)
                return key;

            return _localization.Resolve("Errors", key);
        }

        private bool ShouldExitToMenuByOnlineFlow(OnlineFlowSnapshot snapshot)
        {
            if (_disposed)
                return false;

            if (IsOpponentDisconnectTerminal(snapshot))
                return false;

            return snapshot.State == OnlineFlowState.Terminated ||
                   snapshot.State == OnlineFlowState.Failed;
        }

        private async UniTask BindOnlineMoveBridgeAsync(CancellationToken ct)
        {
            var session = _onlineSessionContextStore.Snapshot;
            if (!session.IsOnlineDirectInvite || string.IsNullOrWhiteSpace(session.LocalUserId) || _matchStateProvider == null)
                return;

            _isOnlineDirectInvite = true;
            _onlineRoundFinished = false;
            _onlineRematchStarted = false;
            _onlineIsHost = session.IsHost;
            _onlineRoundCoordinator.ResetSession();
            _onlineLocalUserId = session.LocalUserId;
            _onlineRemoteUserId = null;
            _useHostAuthoritativeFilter = session.IsHost;
            _onlineAcceptedShotSequence = 0;

            await _networkBridge.BindAsync(session.LocalUserId, session.IsHost);
            if (_isBattleshipMatch)
                await _battleshipNetworkBridge.BindAsync(session.LocalUserId, session.IsHost);

            _networkBridge.IncomingMoves
                .Subscribe(OnIncomingOnlineMove)
                .AddTo(_subscriptions);

            _networkBridge.IncomingRoundReadySignals
                .Subscribe(OnIncomingRoundReadySignal)
                .AddTo(_subscriptions);

            _networkBridge.IncomingTimeoutSignals
                .Subscribe(OnIncomingOnlineTimeoutSignal)
                .AddTo(_subscriptions);

            if (_isBattleshipMatch)
            {
                _battleshipNetworkBridge.IncomingRecoverySnapshots
                    .Subscribe(OnIncomingBattleshipRecoverySnapshot)
                    .AddTo(_subscriptions);

                if (_onlineIsHost)
                {
                    PublishBattleshipRecoverySnapshotAsync().Forget();

                    if (!_battleshipRecoveryHeartbeatStarted)
                    {
                        _battleshipRecoveryHeartbeatStarted = true;
                        RunBattleshipRecoveryHeartbeatAsync().Forget();
                    }
                }
            }
        }

        private void OnIncomingOnlineTimeoutSignal(OnlineTimeoutSignal signal)
        {
            if (_disposed || !_ecsLifecycle.IsActive || _onlineIsHost)
                return;

            if (_isBattleshipMatch)
            {
                if (_battleshipSnapshotProvider == null)
                    return;

                if (_battleshipSnapshotProvider.Phase != BattleshipPhase.Battle)
                    return;
            }

            _matchStateProvider.SubmitCommand(new TimeoutCommand(signal.LoserSlot));
        }

        private void OnIncomingOnlineMove(MoveCommand move)
        {
            if (_disposed || !_ecsLifecycle.IsActive)
                return;

            if (_useHostAuthoritativeFilter
                && !_isBattleshipMatch
                && !TryValidateIncomingHostMove(move))
            {
                return;
            }

            var minorCount = OnlineMoveIndexCodec.ResolveMinorCount(_fieldSpec);
            CellId cellId;

            try
            {
                cellId = OnlineMoveIndexCodec.ToCellId(move.CellIndex, minorCount);
            }
            catch (Exception)
            {
                return;
            }

            if (_isBattleshipMatch && _useHostAuthoritativeFilter)
            {
                if (!TryValidateIncomingBattleshipShot(move, cellId, minorCount))
                    return;
            }

            _matchStateProvider.SubmitCommand(new MakeMoveCommand(cellId));

            if (_onlineIsHost)
                ForwardAuthoritativeHostMoveAsync(move).Forget();
        }

        private async UniTaskVoid ForwardAuthoritativeHostMoveAsync(MoveCommand proposal)
        {
            if (_disposed || !_onlineIsHost || string.IsNullOrWhiteSpace(_onlineLocalUserId))
                return;

            var authoritativeMove = new MoveCommand(
                Guid.NewGuid(),
                _onlineLocalUserId,
                proposal.CellIndex,
                _isBattleshipMatch ? proposal.ClientTick : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

            try
            {
                await _networkBridge.SubmitMoveAsync(authoritativeMove);
            }
            catch (Exception ex)
            {
                Log.Error(LogTags.Infrastructure, $"[GameplayStartup] Failed to forward authoritative move: {ex.Message}");
            }
        }

        private bool TryValidateIncomingHostMove(MoveCommand move)
        {
            if (string.IsNullOrWhiteSpace(_onlineLocalUserId))
                return false;

            if (string.IsNullOrWhiteSpace(_onlineRemoteUserId))
                _onlineRemoteUserId = move.SenderUserId;

            var remoteUserId = _onlineRemoteUserId;
            if (string.IsNullOrWhiteSpace(remoteUserId))
                return false;

            if (!string.Equals(move.SenderUserId, remoteUserId, StringComparison.Ordinal))
                return false;

            var cells = _matchStateProvider.GetAllCells();
            if (cells == null || cells.Count == 0)
                return false;

            var activeUserId = _matchStateProvider.ActivePlayerSlot == 0
                ? _onlineLocalUserId
                : remoteUserId;

            var nextUserId = _matchStateProvider.ActivePlayerSlot == 0
                ? remoteUserId
                : _onlineLocalUserId;

            if (string.IsNullOrWhiteSpace(activeUserId) || string.IsNullOrWhiteSpace(nextUserId))
                return false;

            AuthoritativeMatchState state;
            try
            {
                state = BuildAuthoritativeState(cells, activeUserId, _onlineRoundFinished);
            }
            catch
            {
                return false;
            }

            var result = _hostMoveProcessor.Process(move, state, nextUserId);
            return result.Status == MoveProcessStatus.Accepted;
        }

        private bool TryValidateIncomingBattleshipShot(MoveCommand move, CellId cellId, int minorCount)
        {
            if (!_onlineIsHost || _battleshipSnapshotProvider == null)
                return false;

            if (_battleshipSnapshotProvider.Phase != BattleshipPhase.Battle)
                return false;

            if (string.IsNullOrWhiteSpace(_onlineRemoteUserId))
                _onlineRemoteUserId = move.SenderUserId;

            if (string.IsNullOrWhiteSpace(_onlineRemoteUserId)
                || !string.Equals(move.SenderUserId, _onlineRemoteUserId, StringComparison.Ordinal))
            {
                return false;
            }

            var shooterSlot = PlayerSlotMapping.SlotO;
            if (_matchStateProvider.ActivePlayerSlot != shooterSlot)
                return false;

            var cellIndex = OnlineMoveIndexCodec.ToCellIndex(cellId, minorCount);
            var marks = _battleshipSnapshotProvider.GetOpponentMarks(shooterSlot);
            if (marks == null || cellIndex < 0 || cellIndex >= marks.Count)
                return false;

            if (marks[cellIndex] != BattleshipCellMark.Unknown)
                return false;

            var sequence = move.ClientTick;
            if (sequence <= 0)
                return false;

            var observedSequence = _networkBridge.Snapshot.CurrentValue?.ShotSequence ?? _onlineAcceptedShotSequence;
            if (observedSequence < _onlineAcceptedShotSequence)
                observedSequence = _onlineAcceptedShotSequence;

            var expectedSequence = observedSequence + 1;
            if (sequence != expectedSequence)
                return false;

            _onlineAcceptedShotSequence = sequence;
            return true;
        }

        private void UpdateMoveTimerStateForBattleshipBot()
        {
            if (!_battleshipBotStarted || _battleshipSnapshotProvider == null)
                return;

            var freeze = _battleshipSnapshotProvider.Phase == BattleshipPhase.Battle
                && _battleshipSnapshotProvider.ActivePlayerSlot == PlayerSlotMapping.SlotO;

            if (freeze)
                _moveTimerService.Freeze();
            else
                _moveTimerService.Unfreeze();

            _moveTimerHudBinder?.SetVisibilityOverride(freeze ? false : null);
        }

        private void SyncBattleshipTimerHudBindings()
        {
            if (!_isBattleshipMatch)
                return;

            var phase = _battleshipSnapshotProvider?.Phase ?? BattleshipPhase.Placement;
            var usePlacementTimer = phase == BattleshipPhase.Placement || phase == BattleshipPhase.Waiting;

            // Both binders target one label, so keep only one bound at a time.
            _moveTimerHudBinder?.Unbind();
            _battleshipPlacementTimerHudBinder?.Unbind();

            if (usePlacementTimer)
            {
                _battleshipPlacementTimerHudBinder?.Bind();
                return;
            }

            _moveTimerHudBinder?.Bind();
            UpdateMoveTimerStateForBattleshipBot();
        }

        private AuthoritativeMatchState BuildAuthoritativeState(
            System.Collections.Generic.IReadOnlyList<CellSnapshot> cells,
            string activeUserId,
            bool isRoundCompleted)
        {
            var minorCount = OnlineMoveIndexCodec.ResolveMinorCount(_fieldSpec);
            var cellsCount = _fieldSpec.Kind == FieldKind.Classic
                ? _fieldSpec.OuterSize * _fieldSpec.OuterSize
                : (_fieldSpec.OuterSize * _fieldSpec.OuterSize) * (_fieldSpec.InnerSize * _fieldSpec.InnerSize);

            var state = new AuthoritativeMatchState(cellsCount, activeUserId);

            for (var i = 0; i < cells.Count; i++)
            {
                var snapshot = cells[i];
                if (snapshot.Slot < 0)
                    continue;

                var index = OnlineMoveIndexCodec.ToCellIndex(snapshot.CellId, minorCount);
                if (index < 0 || index >= cellsCount)
                    continue;

                state.MarkCellOccupied(index);
            }

            if (isRoundCompleted)
                state.Complete();

            return state;
        }

        // -- RoundFinished handling (ADR-10) --

        private void OnRoundFinished(EcsRoundFinishedEvent evt) =>
            HandleRoundFinished(evt);

        private void HandleRoundFinished(EcsRoundFinishedEvent evt)
        {
            try
            {
                if (_disposed) return;

                if (_isOnlineDirectInvite)
                {
                    _onlineRoundFinished = true;
                    _onlineRematchStarted = false;
                    _onlineTerminalResultShown = false;
                    _onlineSessionFlow.OnRoundCompletedAsync().Forget();
                }

                // 1. Unbind binder (ADR-5 order: Unbind before Stop).
                _movesBinder.Unbind();
                _moveTimerHudBinder?.Unbind();
                _battleshipPlacementTimerHudBinder?.Unbind();

                // 2. Map ECS result to OOP GameResult for downstream consumers.
                var gameResult = BuildGameResult(evt);

                // 3. Record result in series.
                _seriesService.RecordResult(gameResult);

                if (_disposed) return;

                // 4. Update scoreboard scores.
                UpdateScoreLabels();

                // 5. Final sync + show win line.
                ApplyUltimateFinalSyncIfNeeded();
                ShowWinLine(gameResult, evt);

                // 6. Show result popup.
                SetRoundFinishedVisualState(true);
                _resultVM?.Show(gameResult, _seriesService.Score.CurrentValue);
            }
            catch (Exception ex)
            {
                if (_disposed) return;
                Log.Error(LogTags.Infrastructure, $"[GameplayStartup] Error handling round finished: {ex}");
            }
        }

        private static Rules.GameStatus MapEcsStatus(EcsGameStatus ecsStatus) => ecsStatus switch
        {
            EcsGameStatus.Win => Rules.GameStatus.Win,
            EcsGameStatus.Draw => Rules.GameStatus.Draw,
            EcsGameStatus.InProgress => Rules.GameStatus.InProgress,
            EcsGameStatus.Timeout => Rules.GameStatus.Timeout,
            _ => throw new ArgumentOutOfRangeException(nameof(ecsStatus), ecsStatus, null),
        };

        private GameResult BuildGameResult(EcsRoundFinishedEvent evt)
        {
            if (_fieldSpec != null
                && _fieldSpec.Kind == FieldKind.Ultimate
                && _ultimateSnapshotProvider != null)
            {
                var match = _ultimateSnapshotProvider.CurrentMatch;
                if (match.Status == Rules.GameStatus.Win && match.BigBoardWinLine.HasValue)
                    return GameResult.Win(match.Winner, MapUltimateBigBoardWinLine(match.BigBoardWinLine.Value));

                if (match.Status == Rules.GameStatus.Win)
                    throw new InvalidOperationException("Ultimate win result must include BigBoardWinLine.");

                if (match.Status == Rules.GameStatus.Timeout)
                    return GameResult.Timeout(match.Winner);

                return match.Status == Rules.GameStatus.Draw
                    ? GameResult.Draw()
                    : GameResult.InProgress();
            }

            var winner = evt.WinnerSlot.HasValue
                ? PlayerSlotMapping.SlotToMark(evt.WinnerSlot.Value)
                : PlayerMark.None;
            var oopStatus = MapEcsStatus(evt.Status);
            WinLine? oopWinLine = null;
            if (evt.WinLine.HasValue)
                oopWinLine = MapEcsWinLine(evt.WinLine.Value);

            if (oopStatus == Rules.GameStatus.Win)
            {
                if (winner == PlayerMark.None)
                    return GameResult.Draw();

                return GameResult.Win(winner, oopWinLine ?? CreateFallbackWinLine());
            }

            return oopStatus == Rules.GameStatus.Timeout
                    ? GameResult.Timeout(winner)
                : oopStatus == Rules.GameStatus.Draw
                    ? GameResult.Draw()
                    : GameResult.InProgress();
        }

        private void ShowWinLine(GameResult gameResult, EcsRoundFinishedEvent evt)
        {
            if (gameResult.Status != Rules.GameStatus.Win)
                return;

            if (_fieldSpec != null
                && _fieldSpec.Kind == FieldKind.Ultimate
                && _ultimateSnapshotProvider != null
                && _fieldUiAdapter is IUltimateGameplayFieldUiAdapter ultimateUi)
            {
                var match = _ultimateSnapshotProvider.CurrentMatch;
                if (match.BigBoardWinLine.HasValue)
                    _winLineRenderer.ShowUltimate(match.BigBoardWinLine.Value, ultimateUi);

                return;
            }

            if (evt.WinLine.HasValue)
                _winLineRenderer.Show(MapEcsWinLine(evt.WinLine.Value));
        }

        private void ApplyUltimateFinalSyncIfNeeded()
        {
            if (_fieldSpec == null || _fieldSpec.Kind != FieldKind.Ultimate)
                return;

            if (_ultimateSnapshotProvider == null)
                return;

            _ultimateSnapshotProvider.CopyMiniBoardsTo(_ultimateMiniBoardBuffer);

            _ultimateAllowedBinder?.ApplyFinalState(_ultimateSnapshotProvider.CurrentAllowedMajors);
            _ultimateMiniBoardStatusBinder?.ApplyFinalState(_ultimateMiniBoardBuffer);
        }

        /// <summary>
        /// Derives Direction and Length from EcsWinLine's Start/End coordinates.
        /// Assumes normalized line: Start ≤ End (by row, then col) — guaranteed by ClassicRulesEngine.
        /// </summary>
        private static WinLine MapEcsWinLine(EcsWinLine ecsLine)
        {
            var rowDiff = ecsLine.End.Major - ecsLine.Start.Major;
            var colDiff = ecsLine.End.Minor - ecsLine.Start.Minor;

            WinLineDirection direction;
            if (rowDiff == 0)
                direction = WinLineDirection.Horizontal;
            else if (colDiff == 0)
                direction = WinLineDirection.Vertical;
            else if (colDiff > 0)
                direction = WinLineDirection.DiagonalMain;
            else
                direction = WinLineDirection.DiagonalAnti;

            var length = Math.Max(Math.Abs(rowDiff), Math.Abs(colDiff)) + 1;

            return new WinLine(ecsLine.Start, ecsLine.End, direction, length);
        }

        private static WinLine MapUltimateBigBoardWinLine(UltimateBigBoardWinLine line)
        {
            static CellId ToCellId(int major) => new CellId(major / 3, major % 3);

            var start = ToCellId(line.Major0);
            var end = ToCellId(line.Major2);

            var rowDiff = end.Major - start.Major;
            var colDiff = end.Minor - start.Minor;

            var direction = rowDiff == 0
                ? WinLineDirection.Horizontal
                : colDiff == 0
                    ? WinLineDirection.Vertical
                    : colDiff > 0
                        ? WinLineDirection.DiagonalMain
                        : WinLineDirection.DiagonalAnti;

            return new WinLine(start, end, direction, 3);
        }

        // -- Result actions (Restart / Exit) --

        internal void HandleResultAction(ResultAction action)
        {
            switch (action)
            {
                case ResultAction.Restart:
                    if (_onlineTerminalResultShown)
                    {
                        ExitToMenuAsync().Forget();
                        break;
                    }

                    if (_restartInProgress)
                    {
                        Log.Warning(LogTags.Infrastructure, "[GameplayStartup] Restart already in progress. Ignore duplicate request.");
                        break;
                    }

                    if (_isOnlineDirectInvite)
                        RequestOnlineRematchAsync().Forget();
                    else
                        RestartRoundAsync().Forget();
                    break;
                case ResultAction.Exit:
                    ExitToMenuAsync().Forget();
                    break;
            }
        }

        private async UniTaskVoid RequestOnlineRematchAsync()
        {
            if (_disposed || !_isOnlineDirectInvite || !_onlineRoundFinished || _onlineRematchStarted)
                return;

            if (string.IsNullOrWhiteSpace(_onlineLocalUserId))
                return;

            try
            {
                await _onlineSessionFlow.SetReadyForNextMatchAsync(true);

                var roundId = _onlineRoundCoordinator.MatchRoundId;
                await _networkBridge.SubmitRoundReadyAsync(new RoundReadySignal(
                    _onlineLocalUserId,
                    isReady: true,
                    matchRoundId: roundId,
                    clientTick: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));

                var bothReady = _onlineRoundCoordinator.SetReady(_onlineIsHost, isReady: true);
                if (bothReady)
                    StartOnlineRestartIfReady();
            }
            catch (Exception ex)
            {
                if (_disposed)
                    return;

                Log.Error(LogTags.Infrastructure, $"[GameplayStartup] Online rematch request failed: {ex}");
            }
        }

        private void OnIncomingRoundReadySignal(RoundReadySignal signal)
        {
            if (_disposed || !_isOnlineDirectInvite || !_onlineRoundFinished || _onlineRematchStarted)
                return;

            if (signal.MatchRoundId != _onlineRoundCoordinator.MatchRoundId)
                return;

            _onlineSessionFlow.OnOpponentReadyForNextMatchAsync(signal.IsReady).Forget();

            var bothReady = _onlineRoundCoordinator.SetReady(!_onlineIsHost, signal.IsReady);
            if (bothReady)
                StartOnlineRestartIfReady();
        }

        private void OnIncomingBattleshipRecoverySnapshot(BattleshipRecoveryMessage message)
        {
            if (_disposed || !_isBattleshipMatch || _onlineIsHost || !_isOnlineDirectInvite)
                return;

            if (string.Equals(message.SenderUserId, _onlineLocalUserId, StringComparison.Ordinal))
                return;

            if (message.MatchRoundId != _onlineRoundCoordinator.MatchRoundId)
                return;

            if (!TryBuildRecoveryState(message, out var recoveryState))
                return;

            if (_battleshipRecoveryStateApplier?.TryApplyRecoveryState(recoveryState) != true)
                return;

            _battleshipPlacementTimerService.RestoreRemainingSeconds(recoveryState.PlacementTimerRemainingSeconds);
            if (recoveryState.Phase == BattleshipPhase.Battle && recoveryState.ActivePlayerSlot >= 0)
                _moveTimerService.RestoreRemainingSeconds(recoveryState.MoveTimerRemainingSeconds, recoveryState.ActivePlayerSlot);

            SyncBattleshipTimerHudBindings();

            if (recoveryState.FinishStatus != EcsGameStatus.InProgress && !_onlineRoundFinished)
            {
                _onlineRoundFinished = true;
                _onlineRematchStarted = false;

                var recoveredResult = BuildRecoveredGameResult(recoveryState.FinishStatus, recoveryState.WinnerSlot);
                _seriesService.RecordResult(recoveredResult);
                PersistBattleshipSessionScoreIfNeeded();
                UpdateScoreLabels();
                SetRoundFinishedVisualState(true);
                _resultVM?.Show(recoveredResult, _seriesService.Score.CurrentValue);
            }
        }

        private async UniTaskVoid RunBattleshipRecoveryHeartbeatAsync()
        {
            try
            {
                while (!_disposed && _isOnlineDirectInvite && _isBattleshipMatch && _onlineIsHost)
                {
                    await PublishBattleshipRecoverySnapshotAsync();
                    await UniTask.Delay(TimeSpan.FromSeconds(1));
                }
            }
            catch (Exception ex)
            {
                if (_disposed)
                    return;

                Log.Warning(LogTags.Infrastructure, $"[GameplayStartup] Battleship recovery heartbeat stopped: {ex.Message}");
            }
            finally
            {
                _battleshipRecoveryHeartbeatStarted = false;
            }
        }

        private async UniTask PublishBattleshipRecoverySnapshotAsync()
        {
            if (_disposed || !_isOnlineDirectInvite || !_isBattleshipMatch || !_onlineIsHost)
                return;

            if (string.IsNullOrWhiteSpace(_onlineLocalUserId))
                return;

            if (!TryCreateBattleshipRecoveryMessage(out var message))
                return;

            try
            {
                await _battleshipNetworkBridge.SubmitRecoverySnapshotAsync(message);
            }
            catch (Exception ex)
            {
                Log.Warning(LogTags.Infrastructure, $"[GameplayStartup] Failed to publish Battleship recovery snapshot: {ex.Message}");
            }
        }

        private bool TryCreateBattleshipRecoveryMessage(out BattleshipRecoveryMessage message)
        {
            message = default;

            if (_battleshipSnapshotProvider == null || string.IsNullOrWhiteSpace(_onlineLocalUserId))
                return false;

            string player0LayoutPayload = string.Empty;
            if (_battleshipSnapshotProvider.TryGetFleetLayout(PlayerSlotMapping.SlotX, out var player0Layout))
            {
                try
                {
                    player0LayoutPayload = _battleshipLayoutSerializer.Serialize(player0Layout);
                }
                catch
                {
                    player0LayoutPayload = string.Empty;
                }
            }

            string player1LayoutPayload = string.Empty;
            if (_battleshipSnapshotProvider.TryGetFleetLayout(PlayerSlotMapping.SlotO, out var player1Layout))
            {
                try
                {
                    player1LayoutPayload = _battleshipLayoutSerializer.Serialize(player1Layout);
                }
                catch
                {
                    player1LayoutPayload = string.Empty;
                }
            }

            var player0MarksPayload = SerializeMarks(_battleshipSnapshotProvider.GetOpponentMarks(PlayerSlotMapping.SlotX));
            var player1MarksPayload = SerializeMarks(_battleshipSnapshotProvider.GetOpponentMarks(PlayerSlotMapping.SlotO));

            _battleshipSnapshotProvider.TryGetConsecutiveTimeouts(out var player0Timeouts, out var player1Timeouts);

            var placementRemainingMs = (long)Math.Round(Math.Max(0f, _battleshipPlacementTimerService.RemainingSeconds.CurrentValue) * 1000f);
            var moveRemainingMs = (long)Math.Round(Math.Max(0f, _moveTimerService.RemainingSeconds.CurrentValue) * 1000f);
            var winnerSlot = _battleshipSnapshotProvider.WinnerSlot ?? -1;

            message = new BattleshipRecoveryMessage(
                Guid.NewGuid(),
                _onlineLocalUserId,
                _onlineRoundCoordinator.MatchRoundId,
                (int)_battleshipSnapshotProvider.Phase,
                _battleshipSnapshotProvider.ActivePlayerSlot,
                placementRemainingMs,
                moveRemainingMs,
                player0Timeouts,
                player1Timeouts,
                winnerSlot,
                (int)_battleshipSnapshotProvider.CurrentStatus,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                player0LayoutPayload,
                player1LayoutPayload,
                player0MarksPayload,
                player1MarksPayload);

            return true;
        }

        private bool TryBuildRecoveryState(BattleshipRecoveryMessage message, out BattleshipRecoveryState recoveryState)
        {
            recoveryState = default;

            if (!Enum.IsDefined(typeof(BattleshipPhase), message.Phase))
                return false;

            if (!Enum.IsDefined(typeof(EcsGameStatus), message.FinishStatus))
                return false;

            FleetLayout? player0Layout = null;
            if (!string.IsNullOrWhiteSpace(message.Player0LayoutPayload))
            {
                if (!_battleshipLayoutSerializer.TryDeserialize(message.Player0LayoutPayload, out var parsedLayout))
                    return false;

                player0Layout = parsedLayout;
            }

            FleetLayout? player1Layout = null;
            if (!string.IsNullOrWhiteSpace(message.Player1LayoutPayload))
            {
                if (!_battleshipLayoutSerializer.TryDeserialize(message.Player1LayoutPayload, out var parsedLayout))
                    return false;

                player1Layout = parsedLayout;
            }

            if (!TryDeserializeMarks(message.Player0OpponentMarksPayload, out var player0Marks)
                || !TryDeserializeMarks(message.Player1OpponentMarksPayload, out var player1Marks))
            {
                return false;
            }

            recoveryState = new BattleshipRecoveryState(
                (BattleshipPhase)message.Phase,
                message.ActivePlayerSlot,
                (EcsGameStatus)message.FinishStatus,
                message.WinnerSlot >= 0 ? message.WinnerSlot : null,
                player0Layout,
                player1Layout,
                player0Marks,
                player1Marks,
                message.Player0ConsecutiveTimeouts,
                message.Player1ConsecutiveTimeouts,
                Math.Max(0f, message.PlacementTimerRemainingMs / 1000f),
                Math.Max(0f, message.MoveTimerRemainingMs / 1000f));

            return true;
        }

        private static string SerializeMarks(System.Collections.Generic.IReadOnlyList<BattleshipCellMark> marks)
        {
            if (marks == null || marks.Count == 0)
                return string.Empty;

            var chars = new char[marks.Count];
            for (var i = 0; i < marks.Count; i++)
                chars[i] = (char)('0' + (int)marks[i]);

            return new string(chars);
        }

        private static bool TryDeserializeMarks(string payload, out BattleshipCellMark[] marks)
        {
            marks = Array.Empty<BattleshipCellMark>();
            if (payload == null)
                return false;

            if (payload.Length == 0)
                return true;

            marks = new BattleshipCellMark[payload.Length];
            for (var i = 0; i < payload.Length; i++)
            {
                var value = payload[i] - '0';
                if (value < 0 || value > (int)BattleshipCellMark.Sunk)
                    return false;

                marks[i] = (BattleshipCellMark)value;
            }

            return true;
        }

        private static GameResult BuildRecoveredGameResult(EcsGameStatus status, int? winnerSlot)
        {
            var winner = winnerSlot.HasValue
                ? PlayerSlotMapping.SlotToMark(winnerSlot.Value)
                : PlayerMark.None;

            return status switch
            {
                EcsGameStatus.Win => winner != PlayerMark.None
                    ? GameResult.Win(winner, CreateFallbackWinLine())
                    : GameResult.Draw(),
                EcsGameStatus.Timeout => winner != PlayerMark.None
                    ? GameResult.Timeout(winner)
                    : GameResult.Draw(),
                EcsGameStatus.Draw => GameResult.Draw(),
                _ => GameResult.InProgress(),
            };
        }

        private static WinLine CreateFallbackWinLine() =>
            new(new CellId(0, 0), new CellId(0, 0), WinLineDirection.Horizontal, 1);

        private void StartOnlineRestartIfReady()
        {
            if (_onlineRematchStarted || _restartInProgress)
                return;

            _onlineRematchStarted = true;
            RestartRoundAsync().Forget();
        }

        private async UniTaskVoid RestartRoundAsync()
        {
            try
            {
                if (_disposed) return;

                _restartInProgress = true;

                // 1. Unbind ultimate binders before epoch switch.
                _ultimateAllowedBinder?.Unbind();
                _ultimateMiniBoardStatusBinder?.Unbind();

                // 2. Alternate starting player.
                var nextRoundStarterMark = _seriesService.NextRound();
                var startingSlot = PlayerSlotMapping.MarkToSlot(nextRoundStarterMark);

                if (_isBattleshipMatch)
                {
                    var previousStartingSlot = _battleshipCurrentStartingSlot;
                    if (previousStartingSlot < 0 && _battleshipSnapshotProvider != null)
                        previousStartingSlot = _battleshipSnapshotProvider.ActivePlayerSlot;

                    if (previousStartingSlot < 0)
                        previousStartingSlot = PlayerSlotMapping.SlotX;

                    startingSlot = previousStartingSlot == PlayerSlotMapping.SlotX
                        ? PlayerSlotMapping.SlotO
                        : PlayerSlotMapping.SlotX;
                    _battleshipCurrentStartingSlot = startingSlot;

                    PersistBattleshipSessionScoreIfNeeded();
                    await ReloadBattleshipGameplayScopeAsync(startingSlot);
                    return;
                }

                // 3. Submit restart command — SubmitCommand auto-ticks.
                var previousEpoch = _ultimateSnapshotProvider?.Epoch ?? 0UL;
                _commandSink.SubmitCommand(new RestartRoundCommand(startingSlot));

                if (_fieldSpec != null && _fieldSpec.Kind == FieldKind.Ultimate && _ultimateSnapshotProvider != null)
                {
                    var epochChanged = await WaitForEpochChangeAsync(previousEpoch, RestartEpochWaitTimeout);
                    if (!epochChanged)
                    {
                        Log.Error(LogTags.Infrastructure,
                            "[GameplayStartup] Restart timeout: epoch did not change. Keep result overlay visible for Retry/Exit.");
                        return;
                    }
                }

                if (_disposed)
                    return;

                // 4. Clear finish UI and perform cold-path rebind.
                _winLineRenderer.Clear();
                _resultVM?.Hide();
                SetRoundFinishedVisualState(false);
                _matchFailSafeGateway.ResetAbortState();

                _movesBinder.Bind();
                if (_isBattleshipMatch)
                {
                    _battleshipBoardsBinder?.Bind();
                    SyncBattleshipTimerHudBindings();
                }
                else
                    _moveTimerHudBinder?.Bind();
                _ultimateAllowedBinder?.Bind();
                _ultimateMiniBoardStatusBinder?.Bind();

                if (_isBattleshipMatch)
                    _battleshipPlacementTimerService.SyncFromSnapshot();
                else
                    _moveTimerService.StartOrResetForPlayer(startingSlot);

                _onlineRoundFinished = false;
                _onlineRematchStarted = false;
                _onlineTerminalResultShown = false;
            }
            catch (Exception ex)
            {
                if (_disposed)
                    return;

                _onlineRematchStarted = false;
                Log.Error(LogTags.Infrastructure, $"[GameplayStartup] Restart failed: {ex}");
            }
            finally
            {
                _restartInProgress = false;
            }
        }

        private async UniTask ReloadBattleshipGameplayScopeAsync(int nextStartingSlot)
        {
            if (_activeLaunchConfig == null)
                throw new InvalidOperationException("Launch config is not available for Battleship rematch.");

            var nextConfig = new GameLaunchConfig(
                _activeLaunchConfig.GameId,
                _activeLaunchConfig.GameConfig,
                _activeLaunchConfig.OpponentConfig,
                _activeLaunchConfig.MoveTimeLimitSeconds,
                nextStartingSlot);

            _activeLaunchConfig = nextConfig;

            _onlineRoundFinished = false;
            _onlineRematchStarted = false;
            _onlineTerminalResultShown = false;

            await _stateMachine.EnterAsync<LoadGameplayState, GameLaunchConfig>(nextConfig, CancellationToken.None);
        }

        private async UniTask<bool> WaitForEpochChangeAsync(ulong previousEpoch, TimeSpan timeout)
        {
            if (_ultimateSnapshotProvider == null)
                return true;

            var startTime = Time.realtimeSinceStartup;
            while (!_disposed)
            {
                if (_ultimateSnapshotProvider.Epoch != previousEpoch)
                    return true;

                if (Time.realtimeSinceStartup - startTime >= (float)timeout.TotalSeconds)
                    return false;

                await UniTask.DelayFrame(1);
            }

            return false;
        }

        private async UniTaskVoid ExitToMenuAsync()
        {
            if (System.Threading.Interlocked.CompareExchange(ref _exitToMenuRequested, 1, 0) != 0)
                return;

            try
            {
                if (_disposed) return;

                _winLineRenderer.Clear();
                _resultVM?.Hide();
                SetRoundFinishedVisualState(false);
                await _backHandler.HandleBackAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                if (_disposed) return;
                Log.Error(LogTags.Infrastructure, $"[GameplayStartup] Error exiting to menu: {ex}");
            }
        }

        // -- Scoreboard --

        private void UpdateScoreLabels()
        {
            var score = _seriesService.Score.CurrentValue;
            var p1Label = _fieldUiAdapter.Player1ScoreLabel;
            var p2Label = _fieldUiAdapter.Player2ScoreLabel;
            var drawsLabel = _fieldUiAdapter.DrawsScoreLabel;

            if (p1Label != null) p1Label.text = score.Player1Wins.ToString();
            if (p2Label != null) p2Label.text = score.Player2Wins.ToString();
            if (drawsLabel != null) drawsLabel.text = $"D:{score.Draws}";
        }

        private void SetRoundFinishedVisualState(bool finished)
        {
            var container = _fieldUiAdapter.FieldContainer;
            if (container == null)
                return;

            const string cls = "field-container--round-finished";
            if (finished)
                container.AddToClassList(cls);
            else
                container.RemoveFromClassList(cls);
        }

        private void BindUltimateUiIfNeeded()
        {
            if (_fieldSpec == null || _fieldSpec.Kind != FieldKind.Ultimate)
                return;

            if (_ultimateSnapshotProvider == null)
                return;

            if (_fieldUiAdapter is not IUltimateGameplayFieldUiAdapter ultimateUi)
                return;

            if (_eventStream is not IUltimateGameplayEventStream ultimateEvents)
                return;

            _ultimateAllowedBinder ??= new UltimateAllowedBinder(ultimateUi, ultimateEvents, _ultimateSnapshotProvider);
            _ultimateMiniBoardStatusBinder ??= new UltimateMiniBoardStatusBinder(ultimateUi, ultimateEvents, _ultimateSnapshotProvider);

            _ultimateAllowedBinder.Bind();
            _ultimateMiniBoardStatusBinder.Bind();
        }

        private void DisposeUltimateUiBinders()
        {
            _ultimateAllowedBinder?.Dispose();
            _ultimateMiniBoardStatusBinder?.Dispose();
            _ultimateAllowedBinder = null;
            _ultimateMiniBoardStatusBinder = null;
        }

        // -- Cleanup / Error --

        private void CleanupGameplay()
        {
            _subscriptions?.Dispose();
            _subscriptions = null;
            UnbindOnlinePlayerNamesStoreIfNeeded();
            _botDriver.Dispose();
            _battleshipBotDriver?.Dispose();
            _ultimateBotOrchestrator.Dispose();
            _battleshipPlacementUiController?.Unbind();
            _movesBinder.Unbind();
            _battleshipBoardsBinder?.Unbind();
            _moveTimerHudBinder?.Unbind();
            _battleshipPlacementTimerHudBinder?.Unbind();
            DisposeUltimateUiBinders();
            _battleshipNetworkBridge.UnbindAsync().Forget();
            _networkBridge.UnbindAsync().Forget();
            _moveTimerService.Stop();
            _battleshipPlacementTimerService.Stop();
            _winLineRenderer.Clear();
            _resultVM?.Dispose();
            _resultVM = null;
            _ecsLifecycle.StopMatch();
            SetRoundFinishedVisualState(false);
            _fieldPresenter.Unbind();
        }

        private void BindOnlinePlayerNamesStoreIfNeeded()
        {
            var session = _onlineSessionContextStore.Snapshot;
            if (!session.IsOnlineDirectInvite)
                return;

            if (_onlinePlayerNamesStoreBound)
                return;

            if (_onlinePlayerNamesStore == null)
                return;

            _onlineSessionLauncher.BindMatchPlayerNamesStore(_onlinePlayerNamesStore);
            _onlinePlayerNamesStoreBound = true;
        }

        private void UnbindOnlinePlayerNamesStoreIfNeeded()
        {
            if (!_onlinePlayerNamesStoreBound)
                return;

            if (_onlinePlayerNamesStore == null)
            {
                _onlinePlayerNamesStoreBound = false;
                return;
            }

            _onlineSessionLauncher.UnbindMatchPlayerNamesStore(_onlinePlayerNamesStore);
            _onlinePlayerNamesStoreBound = false;
        }

        private static string NormalizeUltimateDifficultyId(string difficultyId)
        {
            if (string.IsNullOrWhiteSpace(difficultyId))
                return "easy";

            var normalized = difficultyId.Trim();
            if (string.Equals(normalized, "medium", StringComparison.OrdinalIgnoreCase))
                return "medium";

            if (string.Equals(normalized, "normal", StringComparison.OrdinalIgnoreCase))
                return "medium";

            if (string.Equals(normalized, "hard", StringComparison.OrdinalIgnoreCase))
                return "hard";

            if (string.Equals(normalized, "easy", StringComparison.OrdinalIgnoreCase))
                return "easy";

            return normalized;
        }

        private async UniTask HandleErrorAsync(GameplayError error, CancellationToken ct)
        {
            Log.Error(LogTags.Infrastructure, $"[GameplayStartup] {error.Code}: {error.Details}");
            await _stateMachine.EnterAsync<LoadMainMenuState>(ct);
        }

        private void RestoreBattleshipSessionScoreIfNeeded(GameLaunchConfig config)
        {
            if (!_isBattleshipMatch)
                return;

            var key = BuildBattleshipSessionKey(config);
            if (string.IsNullOrWhiteSpace(key))
                return;

            if (!config.StartingPlayerSlotOverride.HasValue)
            {
                lock (BattleshipSeriesScoresGate)
                    BattleshipSeriesScores.Remove(key);
                return;
            }

            SeriesScore storedScore;
            lock (BattleshipSeriesScoresGate)
            {
                if (!BattleshipSeriesScores.TryGetValue(key, out storedScore))
                    return;
            }

            for (var i = 0; i < storedScore.Player1Wins; i++)
                _seriesService.RecordResult(GameResult.Timeout(PlayerMark.X));

            for (var i = 0; i < storedScore.Player2Wins; i++)
                _seriesService.RecordResult(GameResult.Timeout(PlayerMark.O));

            for (var i = 0; i < storedScore.Draws; i++)
                _seriesService.RecordResult(GameResult.Draw());

            for (var i = 0; i < storedScore.RoundIndex; i++)
                _seriesService.NextRound();

            UpdateScoreLabels();
        }

        private void PersistBattleshipSessionScoreIfNeeded()
        {
            if (!_isBattleshipMatch || _activeLaunchConfig == null)
                return;

            var key = BuildBattleshipSessionKey(_activeLaunchConfig);
            if (string.IsNullOrWhiteSpace(key))
                return;

            lock (BattleshipSeriesScoresGate)
                BattleshipSeriesScores[key] = _seriesService.Score.CurrentValue;
        }

        private string BuildBattleshipSessionKey(GameLaunchConfig config)
        {
            if (_onlineSessionContextStore.Snapshot.IsOnlineDirectInvite)
            {
                var sessionId = _onlineSessionContextStore.Snapshot.SessionId;
                if (!string.IsNullOrWhiteSpace(sessionId))
                    return $"online:{sessionId}";
            }

            return $"local:{config.GameId}";
        }

        private static GameplayError MapError(Exception ex) =>
            ex is ArgumentException or InvalidOperationException
                ? GameplayError.InvalidConfig(ex.Message)
                : GameplayError.BuildFailed(ex.Message);
    }
}
