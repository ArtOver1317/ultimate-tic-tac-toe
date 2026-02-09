using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using Runtime.GameModes.Wizard;
using Runtime.Games.TicTacToe.Moves;
using Runtime.Games.TicTacToe.Rules;
using Runtime.Games.TicTacToe.Series;
using Runtime.Infrastructure.GameStateMachine;
using Runtime.Infrastructure.GameStateMachine.States;
using Runtime.Infrastructure.Logging;
using StripLog;

using Runtime.Gameplay;

namespace Runtime.Games.TicTacToe
{
    public sealed class GameplayStartup : IGameplayStartup, IDisposable
    {
        private readonly IGameLaunchConfigStore _configStore;
        private readonly IGameService _gameService;
        private readonly IGameplayFieldPresenter _fieldPresenter;
        private readonly IGameplayFieldUiAdapter _fieldUiAdapter;
        private readonly ILocalMovesService _localMoves;
        private readonly GameplayMovesBinder _movesBinder;
        private readonly GameplayRulesHandler _rulesHandler;
        private readonly WinLineRenderer _winLineRenderer;
        private readonly ISeriesService _seriesService;
        private readonly IGameplayBackHandler _backHandler;
        private readonly IGameStateMachine _stateMachine;

        private FieldRenderSpec _fieldSpec;
        private GameResultViewModel _resultVM;
        private CompositeDisposable _subscriptions;
        private bool _disposed;

        public GameplayStartup(
            IGameLaunchConfigStore configStore,
            IGameService gameService,
            IGameplayFieldPresenter fieldPresenter,
            IGameplayFieldUiAdapter fieldUiAdapter,
            ILocalMovesService localMoves,
            GameplayMovesBinder movesBinder,
            GameplayRulesHandler rulesHandler,
            WinLineRenderer winLineRenderer,
            ISeriesService seriesService,
            IGameplayBackHandler backHandler,
            IGameStateMachine stateMachine)
        {
            _configStore = configStore ?? throw new ArgumentNullException(nameof(configStore));
            _gameService = gameService ?? throw new ArgumentNullException(nameof(gameService));
            _fieldPresenter = fieldPresenter ?? throw new ArgumentNullException(nameof(fieldPresenter));
            _fieldUiAdapter = fieldUiAdapter ?? throw new ArgumentNullException(nameof(fieldUiAdapter));
            _localMoves = localMoves ?? throw new ArgumentNullException(nameof(localMoves));
            _movesBinder = movesBinder ?? throw new ArgumentNullException(nameof(movesBinder));
            _rulesHandler = rulesHandler ?? throw new ArgumentNullException(nameof(rulesHandler));
            _winLineRenderer = winLineRenderer ?? throw new ArgumentNullException(nameof(winLineRenderer));
            _seriesService = seriesService ?? throw new ArgumentNullException(nameof(seriesService));
            _backHandler = backHandler ?? throw new ArgumentNullException(nameof(backHandler));
            _stateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
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

                var movesConfig = LocalMovesConfigMapper.FromLaunchConfig(config, session.FieldRenderSpec);
                _localMoves.Start(movesConfig);
                _movesBinder.Bind();

                _rulesHandler.Bind(session.FieldRenderSpec.OuterSize);

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
            _rulesHandler.Unbind();
            _movesBinder.Unbind();
            _localMoves.Stop();
            _winLineRenderer.Clear();
            _resultVM?.Dispose();
            _resultVM = null;
            _fieldPresenter.Unbind();
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

            _rulesHandler.RoundFinished
                .Subscribe(OnRoundFinished)
                .AddTo(_subscriptions);

            if (_resultVM != null)
            {
                _resultVM.Actions
                    .Subscribe(OnResultAction)
                    .AddTo(_subscriptions);
            }
        }

        // -- RoundFinished handling (ADR-10) --

        private void OnRoundFinished(RoundFinishedEvent evt)
        {
            HandleRoundFinished(evt);
        }

        private void HandleRoundFinished(RoundFinishedEvent evt)
        {
            try
            {
                if (_disposed) return;

                // Next-frame deferral is now in GameplayRulesHandler (ADR-4).
                // RoundFinished already arrives on the next frame.

                // 1. Unbind binder (ADR-5 order: Unbind before Stop).
                _movesBinder.Unbind();

                // 2. Stop moves -- block input at data level.
                _localMoves.Stop();

                // 3. Record result in series.
                _seriesService.RecordResult(evt.Result);

                if (_disposed) return;

                // 4. Update scoreboard scores.
                UpdateScoreLabels();

                // 5. Show win line if applicable.
                if (evt.Result.Status == GameStatus.Win && evt.Result.WinLine.HasValue)
                    _winLineRenderer.Show(evt.Result.WinLine.Value);

                // 6. Show result popup.
                _resultVM?.Show(evt.Result, _seriesService.Score.CurrentValue);
            }
            catch (Exception ex)
            {
                if (_disposed) return;
                Log.Error(LogTags.Infrastructure, $"[GameplayStartup] Error handling round finished: {ex}");
            }
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

        /// <summary>
        /// ADR-10: RestartRound sequence.
        /// </summary>
        private void RestartRound()
        {
            if (_disposed) return;

            // 1. Clear win line.
            _winLineRenderer.Clear();

            // 2. Hide popup.
            _resultVM?.Hide();

            // 3. Unbind rules handler.
            _rulesHandler.Unbind();

            // 4. Alternate starting player.
            var startingPlayer = _seriesService.NextRound();

            // 5. Restart moves with new starting player.
            var newConfig = new LocalMovesConfig(_fieldSpec, startingPlayer);
            _localMoves.Start(newConfig);

            // 6. Cold-path render of empty field + subscribe.
            _movesBinder.Bind();

            // 7. Fresh mirror-board + subscribe to CellChanged.
            _rulesHandler.Bind(_fieldSpec.OuterSize);
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
            _rulesHandler.Unbind();
            _subscriptions?.Dispose();
            _subscriptions = null;
            _movesBinder.Unbind();
            _winLineRenderer.Clear();
            _resultVM?.Dispose();
            _resultVM = null;
            _localMoves.Stop();
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
