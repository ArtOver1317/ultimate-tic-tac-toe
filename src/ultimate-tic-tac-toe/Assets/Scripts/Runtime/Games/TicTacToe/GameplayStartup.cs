using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using Runtime.GameModes.Wizard;
using Runtime.Gameplay;
using Runtime.Gameplay.ECS;
using Runtime.Games.TicTacToe.AI;
using Runtime.Games.TicTacToe.ECS;
using Runtime.Games.TicTacToe.Moves;
using Runtime.Games.TicTacToe.Rules;
using Runtime.Games.TicTacToe.Series;
using Runtime.Infrastructure.GameStateMachine;
using Runtime.Infrastructure.GameStateMachine.States;
using Runtime.Infrastructure.Logging;
using StripLog;
using EcsRoundFinishedEvent = Runtime.Gameplay.ECS.RoundFinishedEvent;
using EcsGameStatus = Runtime.Gameplay.ECS.GameStatus;

namespace Runtime.Games.TicTacToe
{
    public sealed class GameplayStartup : IGameplayStartup, IDisposable
    {
        private readonly IGameLaunchConfigStore _configStore;
        private readonly IGameService _gameService;
        private readonly IGameplayFieldPresenter _fieldPresenter;
        private readonly IGameplayFieldUiAdapter _fieldUiAdapter;
        private readonly IMatchEcsLifecycle _ecsLifecycle;
        private readonly IGameplayEventStream _eventStream;
        private readonly IGameplayCommandSink _commandSink;
        private readonly GameplayMovesBinder _movesBinder;
        private readonly WinLineRenderer _winLineRenderer;
        private readonly ISeriesService _seriesService;
        private readonly IGameplayBackHandler _backHandler;
        private readonly IGameStateMachine _stateMachine;
        private readonly IBotTurnDriver _botDriver;

        private FieldRenderSpec _fieldSpec;
        private GameResultViewModel _resultVM;
        private CompositeDisposable _subscriptions;
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
            IBotTurnDriver botDriver)
        {
            _configStore = configStore ?? throw new ArgumentNullException(nameof(configStore));
            _gameService = gameService ?? throw new ArgumentNullException(nameof(gameService));
            _fieldPresenter = fieldPresenter ?? throw new ArgumentNullException(nameof(fieldPresenter));
            _fieldUiAdapter = fieldUiAdapter ?? throw new ArgumentNullException(nameof(fieldUiAdapter));
            _ecsLifecycle = ecsLifecycle ?? throw new ArgumentNullException(nameof(ecsLifecycle));
            _eventStream = eventStream ?? throw new ArgumentNullException(nameof(eventStream));
            _commandSink = commandSink ?? throw new ArgumentNullException(nameof(commandSink));
            _movesBinder = movesBinder ?? throw new ArgumentNullException(nameof(movesBinder));
            _winLineRenderer = winLineRenderer ?? throw new ArgumentNullException(nameof(winLineRenderer));
            _seriesService = seriesService ?? throw new ArgumentNullException(nameof(seriesService));
            _backHandler = backHandler ?? throw new ArgumentNullException(nameof(backHandler));
            _stateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
            _botDriver = botDriver ?? throw new ArgumentNullException(nameof(botDriver));
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

            IGameplaySession session = null;
            try
            {
                session = await _gameService.StartMatchAsync(config, ct);
                await _fieldPresenter.BindAsync(session.FieldRenderSpec, ct);

                _fieldSpec = session.FieldRenderSpec;
                _seriesService.StartSeries();

                _ecsLifecycle.StartMatch(config);
                _movesBinder.Bind();

                // Start bot driver if opponent is a bot
                await TryStartBotDriverAsync(config, ct);

                CreateResultVM();
                SubscribeToEvents();
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
            _botDriver.Dispose();
            _movesBinder.Unbind();
            _ecsLifecycle.StopMatch();
            _winLineRenderer.Clear();
            _resultVM?.Dispose();
            _resultVM = null;
            _fieldPresenter.Unbind();
        }

        // -- Bot integration --

        // TODO: BvB support (ADR-10) — currently only Player vs Bot (slot 1).
        // For Bot vs Bot: need BvBOpponentConfig type, second driver creation
        // (manual new BotTurnDriver(...) or factory), and DI refactoring.
        // Self-play tool covers BvB calibration in the meantime.

        private async UniTask TryStartBotDriverAsync(GameLaunchConfig config, CancellationToken ct)
        {
            if (config.OpponentConfig is not BotOpponentConfig botConfig)
                return;

            var result = await _botDriver.StartAsync(config, botSlot: 1, botConfig.DifficultyId, ct);
            if (result.Status != BotStartStatus.Started)
            {
                Log.Warning(LogTags.Infrastructure,
                    $"[GameplayStartup] Bot driver not started: {result.Status} — {result.Error}");
            }
        }

        // -- Event wiring --

        private void CreateResultVM()
        {
            _resultVM?.Dispose();

            var container = _fieldUiAdapter.FieldContainer;
            if (container == null) return;

            _resultVM = new GameResultViewModel(container);
        }

        private void SubscribeToEvents()
        {
            _subscriptions?.Dispose();
            _subscriptions = new CompositeDisposable();

            _eventStream.RoundFinished
                .Subscribe(OnRoundFinished)
                .AddTo(_subscriptions);

            // Phase 4: Input blocking while bot is thinking
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

            // ADR-12: Surface bot disable error to user
            _botDriver.IsDisabled
                .Where(disabled => disabled)
                .Subscribe(_ =>
                {
                    Log.Error(LogTags.Infrastructure,
                        "[GameplayStartup] Bot disabled after exhausting all retry attempts.");
                    // Re-enable input so human can exit the match
                    var container = _fieldUiAdapter.FieldContainer;
                    if (container != null)
                        container.pickingMode = UnityEngine.UIElements.PickingMode.Position;
                })
                .AddTo(_subscriptions);

            if (_resultVM != null)
            {
                _resultVM.Actions
                    .Subscribe(OnResultAction)
                    .AddTo(_subscriptions);
            }
        }

        // -- RoundFinished handling (ADR-10) --

        private void OnRoundFinished(EcsRoundFinishedEvent evt) =>
            HandleRoundFinished(evt);

        private void HandleRoundFinished(EcsRoundFinishedEvent evt)
        {
            try
            {
                if (_disposed) return;

                // 1. Unbind binder (ADR-5 order: Unbind before Stop).
                _movesBinder.Unbind();

                // 2. Map ECS result to OOP GameResult for downstream consumers.
                var winner = evt.WinnerSlot.HasValue
                    ? TicTacToeEcsRegistrar.SlotToMark(evt.WinnerSlot.Value)
                    : PlayerMark.None;
                var oopStatus = MapEcsStatus(evt.Status);
                WinLine? oopWinLine = null;
                if (evt.WinLine.HasValue)
                {
                    oopWinLine = MapEcsWinLine(evt.WinLine.Value);
                }
                var gameResult = oopStatus == Rules.GameStatus.Win
                    ? GameResult.Win(winner, oopWinLine!.Value)
                    : oopStatus == Rules.GameStatus.Draw
                        ? GameResult.Draw()
                        : GameResult.InProgress();

                // 3. Record result in series.
                _seriesService.RecordResult(gameResult);

                if (_disposed) return;

                // 4. Update scoreboard scores.
                UpdateScoreLabels();

                // 5. Show win line if applicable.
                if (evt.WinLine.HasValue)
                    _winLineRenderer.Show(oopWinLine!.Value);

                // 6. Show result popup.
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
            _ => Rules.GameStatus.InProgress,
        };

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

        // -- Result actions (Restart / Exit) --

        private void OnResultAction(ResultAction action)
        {
            switch (action)
            {
                case ResultAction.Restart:
                    RestartRound();
                    break;
                case ResultAction.Exit:
                    ExitToMenuAsync().Forget();
                    break;
            }
        }

        private void RestartRound()
        {
            if (_disposed) return;

            // 1. Clear win line.
            _winLineRenderer.Clear();

            // 2. Hide popup.
            _resultVM?.Hide();

            // 3. Alternate starting player.
            var startingPlayer = _seriesService.NextRound();
            var startingSlot = TicTacToeEcsRegistrar.MarkToSlot(startingPlayer);

            // 4. Submit restart command — SubmitCommand auto-ticks, board is cleared synchronously.
            _commandSink.SubmitCommand(new RestartRoundCommand(startingSlot));

            // 5. Cold-path render of cleared field + subscribe.
            _movesBinder.Bind();
        }

        private async UniTaskVoid ExitToMenuAsync()
        {
            try
            {
                if (_disposed) return;

                _winLineRenderer.Clear();
                _resultVM?.Hide();
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

            if (p1Label != null) p1Label.text = score.Player1Wins.ToString();
            if (p2Label != null) p2Label.text = score.Player2Wins.ToString();
        }

        // -- Cleanup / Error --

        private void CleanupGameplay()
        {
            _subscriptions?.Dispose();
            _subscriptions = null;
            _botDriver.Dispose();
            _movesBinder.Unbind();
            _winLineRenderer.Clear();
            _resultVM?.Dispose();
            _resultVM = null;
            _ecsLifecycle.StopMatch();
            _fieldPresenter.Unbind();
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
