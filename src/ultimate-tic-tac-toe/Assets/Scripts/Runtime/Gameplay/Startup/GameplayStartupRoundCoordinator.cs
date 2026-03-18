#nullable enable
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Runtime.Games.Battleship.Startup;
using Runtime.Games.TicTacToe.Ultimate.UI;
using Runtime.Infrastructure.GameStateMachine.States;
using Runtime.Infrastructure.Logging;
using StripLog;
using EcsRoundFinishedEvent = Runtime.Gameplay.Shared.RoundFinishedEvent;

namespace Runtime.Gameplay.Startup
{
    internal sealed class GameplayStartupRoundCoordinator
    {
        private readonly GameplayStartupDependencies _dependencies;
        private readonly GameplayStartupRuntimeState _state;
        private readonly GameplayStartupUiCoordinator _uiCoordinator;
        private readonly GameplayStartupRoundFinishHandler _roundFinishHandler;
        private readonly GameplayStartupRoundRestartHandler _roundRestartHandler;

        private GameplayStartupCoreServices Core => _dependencies.Core;
        private GameplayStartupUiState UiState => _state.Ui;
        private GameplayStartupMatchState MatchState => _state.Match;

        public GameplayStartupRoundCoordinator(
            GameplayStartupDependencies dependencies,
            GameplayStartupRuntimeState state,
            GameplayStartupUiCoordinator uiCoordinator,
            GameplayStartupBattleshipSessionScoreStore sessionScoreStore)
        {
            _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _uiCoordinator = uiCoordinator ?? throw new ArgumentNullException(nameof(uiCoordinator));
            var resultMapper = new GameplayStartupRoundResultMapper(dependencies, state);
            _roundFinishHandler = new GameplayStartupRoundFinishHandler(dependencies, state, uiCoordinator, resultMapper);
            
            _roundRestartHandler = new GameplayStartupRoundRestartHandler(
                dependencies,
                state,
                uiCoordinator,
                sessionScoreStore ?? throw new ArgumentNullException(nameof(sessionScoreStore)));
        }

        internal void HandleRoundFinished(EcsRoundFinishedEvent evt) => _roundFinishHandler.Handle(evt);

        internal UniTask RestartRoundAsync() => _roundRestartHandler.RestartAsync();

        internal async UniTask ExitToMenuAsync()
        {
            if (Interlocked.CompareExchange(ref MatchState.ExitToMenuRequested, 1, 0) != 0)
                return;

            try
            {
                if (MatchState.Disposed)
                    return;

                Core.WinLineRenderer.Clear();
                UiState.ResultViewModel?.Hide();
                _uiCoordinator.SetRoundFinishedVisualState(false);
                await Core.BackHandler.HandleBackAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                if (MatchState.Disposed)
                    return;

                Log.Error(LogTags.Infrastructure, $"[GameplayStartup] Error exiting to menu: {ex}");
            }
        }

        internal async UniTask HandleErrorAsync(GameplayError error, CancellationToken ct)
        {
            Log.Error(LogTags.Infrastructure, $"[GameplayStartup] {error.Code}: {error.Details}");
            await Core.StateMachine.EnterAsync<LoadMainMenuState>(ct);
        }

        internal static GameplayError MapError(Exception ex) =>
            ex is ArgumentException or InvalidOperationException
                ? GameplayError.InvalidConfig(ex.Message)
                : GameplayError.BuildFailed(ex.Message);
    }

    internal sealed class GameplayStartupRoundFinishHandler
    {
        private readonly GameplayStartupDependencies _dependencies;
        private readonly GameplayStartupRuntimeState _state;
        private readonly GameplayStartupUiCoordinator _uiCoordinator;
        private readonly GameplayStartupRoundResultMapper _resultMapper;

        private GameplayStartupCoreServices Core => _dependencies.Core;
        private GameplayStartupTimerServices Timers => _dependencies.Timers;
        private GameplayStartupBotServices Bot => _dependencies.Bot;
        private GameplayStartupOnlineServices Online => _dependencies.Online;
        private GameplayStartupUiState UiState => _state.Ui;
        private GameplayStartupOnlineState OnlineState => _state.Online;
        private GameplayStartupMatchState MatchState => _state.Match;

        public GameplayStartupRoundFinishHandler(
            GameplayStartupDependencies dependencies,
            GameplayStartupRuntimeState state,
            GameplayStartupUiCoordinator uiCoordinator,
            GameplayStartupRoundResultMapper resultMapper)
        {
            _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _uiCoordinator = uiCoordinator ?? throw new ArgumentNullException(nameof(uiCoordinator));
            _resultMapper = resultMapper ?? throw new ArgumentNullException(nameof(resultMapper));
        }

        internal void Handle(EcsRoundFinishedEvent evt)
        {
            try
            {
                if (MatchState.Disposed)
                    return;

                MarkOnlineRoundAsFinishedIfNeeded();
                UnbindRoundPresentation();

                var gameResult = _resultMapper.BuildGameResult(evt);
                Core.SeriesService.RecordResult(gameResult);

                if (MatchState.Disposed)
                    return;

                ApplyRoundFinishedPresentation(gameResult, evt);
            }
            catch (Exception ex)
            {
                if (MatchState.Disposed)
                    return;

                Log.Error(LogTags.Infrastructure, $"[GameplayStartup] Error handling round finished: {ex}");
            }
        }

        private void MarkOnlineRoundAsFinishedIfNeeded()
        {
            if (!OnlineState.IsOnlineDirectInvite)
                return;

            OnlineState.OnlineRoundFinished = true;
            OnlineState.OnlineRematchStarted = false;
            OnlineState.OnlineTerminalResultShown = false;
            Online.OnlineSessionFlow.OnRoundCompletedAsync().Forget();
        }

        private void UnbindRoundPresentation()
        {
            Core.MovesBinder.Unbind();
            Timers.MoveTimerHudBinder?.Unbind();
            Timers.BattleshipPlacementTimerHudBinder?.Unbind();
        }

        private void ApplyRoundFinishedPresentation(GameResult gameResult, EcsRoundFinishedEvent evt)
        {
            _uiCoordinator.UpdateScoreLabels();
            ApplyUltimateFinalSyncIfNeeded();
            ShowWinLineIfNeeded(gameResult, evt);

            _uiCoordinator.SetRoundFinishedVisualState(true);
            UiState.ResultViewModel?.Show(gameResult, Core.SeriesService.Score.CurrentValue);
        }

        private void ApplyUltimateFinalSyncIfNeeded()
        {
            if (UiState.FieldSpec is not { Kind: FieldKind.Ultimate })
                return;

            if (Bot.UltimateSnapshotProvider == null)
                return;

            Bot.UltimateSnapshotProvider.CopyMiniBoardsTo(UiState.UltimateMiniBoardBuffer);

            UiState.UltimateAllowedBinder?.ApplyFinalState(Bot.UltimateSnapshotProvider.CurrentAllowedMajors);
            UiState.UltimateMiniBoardStatusBinder?.ApplyFinalState(UiState.UltimateMiniBoardBuffer);
        }

        private void ShowWinLineIfNeeded(GameResult gameResult, EcsRoundFinishedEvent evt)
        {
            if (gameResult.Status != GameStatus.Win)
                return;

            if (_resultMapper.TryGetUltimateBigBoardWinLine(out var ultimateWinLine)
                && Core.FieldUiAdapter is IUltimateGameplayFieldUiAdapter ultimateUi)
            {
                Core.WinLineRenderer.ShowUltimate(ultimateWinLine, ultimateUi);
                return;
            }

            if (evt.WinLine.HasValue)
                Core.WinLineRenderer.Show(_resultMapper.MapEcsWinLine(evt.WinLine.Value));
        }
    }
}