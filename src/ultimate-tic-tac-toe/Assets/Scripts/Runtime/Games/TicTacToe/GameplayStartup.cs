using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using Runtime.GameModes.Wizard;
using Runtime.Gameplay;
using Runtime.Gameplay.ECS;
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

        private readonly IGameLaunchConfigStore _configStore;
        private readonly IGameService _gameService;
        private readonly IGameplayFieldPresenter _fieldPresenter;
        private readonly IGameplayFieldUiAdapter _fieldUiAdapter;
        private readonly IMatchEcsLifecycle _ecsLifecycle;
        private readonly IGameplayEventStream _eventStream;
        private readonly IGameplayCommandSink _commandSink;
        private readonly GameplayMovesBinder _movesBinder;
        private readonly MoveTimerHudBinder _moveTimerHudBinder;
        private readonly WinLineRenderer _winLineRenderer;
        private readonly ISeriesService _seriesService;
        private readonly IMatchPlayerNames _matchPlayerNames;
        private readonly IGameplayBackHandler _backHandler;
        private readonly IGameStateMachine _stateMachine;
        private readonly IBotTurnDriver _botDriver;
        private readonly IBotTurnOrchestrator _ultimateBotOrchestrator;
        private readonly IMatchFailSafeGateway _matchFailSafeGateway;
        private readonly IUltimateGameplaySnapshotProvider _ultimateSnapshotProvider;
        private readonly IGameplayNetworkBridge _networkBridge;
        private readonly IOnlineGameplaySessionContextStore _onlineSessionContextStore;
        private readonly IOnlineSessionFlowService _onlineSessionFlow;
        private readonly IOnlineSessionLauncher _onlineSessionLauncher;
        private readonly IOnlinePlayerNamesStore _onlinePlayerNamesStore;
        private readonly IMatchStateProvider _matchStateProvider;
        private readonly IMoveTimerService _moveTimerService;
        private readonly ILocalizationService _localization;
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
        private bool _ultimateBotStarted;
        private bool _isOnlineDirectInvite;
        private bool _onlineIsHost;
        private bool _onlineRoundFinished;
        private bool _onlineRematchStarted;
        private bool _onlineTerminalResultShown;
        private bool _useHostAuthoritativeFilter;
        private bool _onlinePlayerNamesStoreBound;
        private int _exitToMenuRequested;
        private string? _onlineLocalUserId;
        private string? _onlineRemoteUserId;
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
            IOnlineGameplaySessionContextStore onlineSessionContextStore = null,
            IMatchStateProvider matchStateProvider = null,
            IOnlineSessionFlowService onlineSessionFlow = null,
            IOnlineSessionLauncher onlineSessionLauncher = null,
            IOnlinePlayerNamesStore onlinePlayerNamesStore = null,
            ILocalizationService localization = null,
            IMoveTimerService moveTimerService = null,
            MoveTimerHudBinder moveTimerHudBinder = null,
            IMatchPlayerNames matchPlayerNames = null)
        {
            _configStore = configStore ?? throw new ArgumentNullException(nameof(configStore));
            _gameService = gameService ?? throw new ArgumentNullException(nameof(gameService));
            _fieldPresenter = fieldPresenter ?? throw new ArgumentNullException(nameof(fieldPresenter));
            _fieldUiAdapter = fieldUiAdapter ?? throw new ArgumentNullException(nameof(fieldUiAdapter));
            _ecsLifecycle = ecsLifecycle ?? throw new ArgumentNullException(nameof(ecsLifecycle));
            _eventStream = eventStream ?? throw new ArgumentNullException(nameof(eventStream));
            _commandSink = commandSink ?? throw new ArgumentNullException(nameof(commandSink));
            _movesBinder = movesBinder ?? throw new ArgumentNullException(nameof(movesBinder));
            _moveTimerHudBinder = moveTimerHudBinder;
            _winLineRenderer = winLineRenderer ?? throw new ArgumentNullException(nameof(winLineRenderer));
            _seriesService = seriesService ?? throw new ArgumentNullException(nameof(seriesService));
            _matchPlayerNames = matchPlayerNames;
            _backHandler = backHandler ?? throw new ArgumentNullException(nameof(backHandler));
            _stateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
            _botDriver = botDriver ?? throw new ArgumentNullException(nameof(botDriver));
            _ultimateBotOrchestrator = ultimateBotOrchestrator ?? throw new ArgumentNullException(nameof(ultimateBotOrchestrator));
            _matchFailSafeGateway = matchFailSafeGateway ?? throw new ArgumentNullException(nameof(matchFailSafeGateway));
            _ultimateSnapshotProvider = ultimateSnapshotProvider;
            _networkBridge = networkBridge ?? new NoOpGameplayNetworkBridge();
            _onlineSessionContextStore = onlineSessionContextStore ?? new OnlineGameplaySessionContextStore();
            _matchStateProvider = matchStateProvider ?? commandSink as IMatchStateProvider;
            _onlineSessionFlow = onlineSessionFlow ?? NoOpOnlineSessionFlowService.Instance;
            _onlineSessionLauncher = onlineSessionLauncher ?? NoOpOnlineSessionLauncher.Instance;
            _onlinePlayerNamesStore = onlinePlayerNamesStore;
            _localization = localization;
            _moveTimerService = moveTimerService ?? NoOpMoveTimerService.Instance;
        }

        public async UniTask StartAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            if (!_configStore.TryConsume(out var config) || config == null)
            {
                CleanupGameplay();
                await HandleErrorAsync(GameplayError.InvalidConfig("Launch config not found."), ct);
                return;
            }

            config = ApplyOnlineMatchConfigOverrideIfNeeded(config);

            IGameplaySession session = null;
            try
            {
                session = await _gameService.StartMatchAsync(config, ct);
                await _fieldPresenter.BindAsync(session.FieldRenderSpec, ct);
                BindOnlinePlayerNamesStoreIfNeeded();

                _fieldSpec = session.FieldRenderSpec;
                _seriesService.StartSeries();

                _ecsLifecycle.StartMatch(config);
                var activePlayerSlot = _matchStateProvider?.ActivePlayerSlot ?? 0;
                _moveTimerService.StartOrResetForPlayer(activePlayerSlot);
                _movesBinder.Bind();
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
            _ultimateBotOrchestrator.Dispose();
            _movesBinder.Unbind();
            _moveTimerHudBinder?.Unbind();
            DisposeUltimateUiBinders();
            _networkBridge.UnbindAsync().Forget();
            _moveTimerService.Stop();
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

            return new GameLaunchConfig(config.GameId, payload.ToGameConfig(), config.OpponentConfig, payload.MoveTimeLimitSeconds);
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
                    .Subscribe(OnResultAction)
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
            _movesBinder.Unbind();
            _moveTimerHudBinder?.Unbind();
            _winLineRenderer.Clear();

            var winner = _onlineIsHost ? PlayerMark.X : PlayerMark.O;
            var gameResult = GameResult.Timeout(winner);
            _seriesService.RecordResult(gameResult);
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

            await _networkBridge.BindAsync(session.LocalUserId, session.IsHost);

            _networkBridge.IncomingMoves
                .Subscribe(OnIncomingOnlineMove)
                .AddTo(_subscriptions);

            _networkBridge.IncomingRoundReadySignals
                .Subscribe(OnIncomingRoundReadySignal)
                .AddTo(_subscriptions);

            _networkBridge.IncomingTimeoutSignals
                .Subscribe(OnIncomingOnlineTimeoutSignal)
                .AddTo(_subscriptions);
        }

        private void OnIncomingOnlineTimeoutSignal(OnlineTimeoutSignal signal)
        {
            if (_disposed || !_ecsLifecycle.IsActive || _onlineIsHost)
                return;

            _matchStateProvider.SubmitCommand(new TimeoutCommand(signal.LoserSlot));
        }

        private void OnIncomingOnlineMove(MoveCommand move)
        {
            if (_disposed || !_ecsLifecycle.IsActive)
                return;

            if (_useHostAuthoritativeFilter && !TryValidateIncomingHostMove(move))
                return;

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
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

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

            return oopStatus == Rules.GameStatus.Win
                ? GameResult.Win(winner, oopWinLine!.Value)
                : oopStatus == Rules.GameStatus.Timeout
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

        private void OnResultAction(ResultAction action)
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
                var startingPlayer = _seriesService.NextRound();
                var startingSlot = PlayerSlotMapping.MarkToSlot(startingPlayer);

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
                _moveTimerHudBinder?.Bind();
                _ultimateAllowedBinder?.Bind();
                _ultimateMiniBoardStatusBinder?.Bind();

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
            _ultimateBotOrchestrator.Dispose();
            _movesBinder.Unbind();
            _moveTimerHudBinder?.Unbind();
            DisposeUltimateUiBinders();
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

        private static GameplayError MapError(Exception ex) =>
            ex is ArgumentException or InvalidOperationException
                ? GameplayError.InvalidConfig(ex.Message)
                : GameplayError.BuildFailed(ex.Message);
    }
}
