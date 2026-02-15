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
using Runtime.Games.TicTacToe.Ultimate;
using Runtime.Games.TicTacToe.Ultimate.Rules;
using Runtime.Games.TicTacToe.Ultimate.UI;
using Runtime.Infrastructure.GameStateMachine;
using Runtime.Infrastructure.GameStateMachine.States;
using Runtime.Infrastructure.Logging;
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
        private readonly WinLineRenderer _winLineRenderer;
        private readonly ISeriesService _seriesService;
        private readonly IGameplayBackHandler _backHandler;
        private readonly IGameStateMachine _stateMachine;
        private readonly IBotTurnDriver _botDriver;
        private readonly IUltimateGameplaySnapshotProvider _ultimateSnapshotProvider;
        private readonly MiniBoardStatus[] _ultimateMiniBoardBuffer = new MiniBoardStatus[9];

        private FieldRenderSpec _fieldSpec;
        private GameResultViewModel _resultVM;
        private UltimateAllowedBinder _ultimateAllowedBinder;
        private UltimateMiniBoardStatusBinder _ultimateMiniBoardStatusBinder;
        private CompositeDisposable _subscriptions;
        private bool _restartInProgress;
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
            IUltimateGameplaySnapshotProvider ultimateSnapshotProvider = null)
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
            _ultimateSnapshotProvider = ultimateSnapshotProvider;
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
                BindUltimateUiIfNeeded();
                SetRoundFinishedVisualState(false);

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
            DisposeUltimateUiBinders();
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
            _ => Rules.GameStatus.InProgress,
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
                    if (_restartInProgress)
                    {
                        Log.Warning(LogTags.Infrastructure, "[GameplayStartup] Restart already in progress. Ignore duplicate request.");
                        break;
                    }

                    RestartRoundAsync().Forget();
                    break;
                case ResultAction.Exit:
                    ExitToMenuAsync().Forget();
                    break;
            }
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

                _movesBinder.Bind();
                _ultimateAllowedBinder?.Bind();
                _ultimateMiniBoardStatusBinder?.Bind();
            }
            catch (Exception ex)
            {
                if (_disposed)
                    return;

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
            _botDriver.Dispose();
            _movesBinder.Unbind();
            DisposeUltimateUiBinders();
            _winLineRenderer.Clear();
            _resultVM?.Dispose();
            _resultVM = null;
            _ecsLifecycle.StopMatch();
            SetRoundFinishedVisualState(false);
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
