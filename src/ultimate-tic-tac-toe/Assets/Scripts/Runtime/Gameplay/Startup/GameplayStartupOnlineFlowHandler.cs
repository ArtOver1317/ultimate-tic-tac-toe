#nullable enable
using System;
using Cysharp.Threading.Tasks;
using Runtime.GameModes.Wizard.Online;
using Runtime.Games.Battleship.Startup;
using Runtime.Infrastructure.Logging;
using StripLog;

namespace Runtime.Gameplay.Startup
{
    internal sealed class GameplayStartupOnlineFlowHandler
    {
        private readonly GameplayStartupDependencies _dependencies;
        private readonly GameplayStartupRuntimeState _state;
        private readonly GameplayStartupUiCoordinator _uiCoordinator;
        private readonly GameplayStartupBattleshipSessionScoreStore _sessionScoreStore;

        private GameplayStartupCoreServices Core => _dependencies.Core;
        private GameplayStartupTimerServices Timers => _dependencies.Timers;
        private GameplayStartupOnlineServices Online => _dependencies.Online;
        private GameplayStartupBattleshipServices Battleship => _dependencies.Battleship;
        private GameplayStartupUiState UiState => _state.Ui;
        private GameplayStartupOnlineState OnlineState => _state.Online;
        private GameplayStartupMatchState MatchState => _state.Match;

        public GameplayStartupOnlineFlowHandler(
            GameplayStartupDependencies dependencies,
            GameplayStartupRuntimeState state,
            GameplayStartupUiCoordinator uiCoordinator,
            GameplayStartupBattleshipSessionScoreStore sessionScoreStore)
        {
            _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _uiCoordinator = uiCoordinator ?? throw new ArgumentNullException(nameof(uiCoordinator));
            _sessionScoreStore = sessionScoreStore ?? throw new ArgumentNullException(nameof(sessionScoreStore));
        }

        internal async UniTask RequestOnlineRematchAsync(Action beginRestartRound)
        {
            if (!CanRequestOnlineRematch() || string.IsNullOrWhiteSpace(OnlineState.OnlineLocalUserId))
                return;

            try
            {
                await SubmitLocalRoundReadyAsync(OnlineState.OnlineLocalUserId);

                if (MarkLocalReady())
                    TryStartOnlineRestart(beginRestartRound);
            }
            catch (Exception ex)
            {
                if (MatchState.Disposed)
                    return;

                Log.Error(LogTags.Infrastructure, $"[GameplayStartup] Online rematch request failed: {ex}");
            }
        }

        internal bool ShouldHandleOnlineOpponentDisconnectAsResult(OnlineFlowSnapshot snapshot)
        {
            if (MatchState.Disposed || !OnlineState.IsOnlineDirectInvite || OnlineState.OnlineTerminalResultShown)
                return false;

            return IsOpponentDisconnectTerminal(snapshot);
        }

        internal void HandleOnlineOpponentDisconnectAsResult(OnlineFlowSnapshot snapshot)
        {
            if (MatchState.Disposed || OnlineState.OnlineTerminalResultShown)
                return;

            ApplyTerminalDisconnectState();
            StopOnlineGameplayPresentation();

            var gameResult = BuildOpponentDisconnectResult();
            RecordOpponentDisconnectResult(gameResult);
            ShowOpponentDisconnectResult(snapshot, gameResult);
        }

        internal bool ShouldExitToMenuByOnlineFlow(OnlineFlowSnapshot snapshot)
        {
            if (MatchState.Disposed)
                return false;

            if (IsOpponentDisconnectTerminal(snapshot))
                return false;

            return snapshot.State == OnlineFlowState.Terminated || snapshot.State == OnlineFlowState.Failed;
        }

        internal void HandleIncomingRoundReadySignal(RoundReadySignal signal, Action beginRestartRound)
        {
            if (!CanHandleIncomingRoundReadySignal(signal))
                return;

            Online.OnlineSessionFlow.OnOpponentReadyForNextMatchAsync(signal.IsReady).Forget();

            var bothReady = Online.OnlineRoundCoordinator.SetReady(!OnlineState.OnlineIsHost, signal.IsReady);
            
            if (bothReady)
                TryStartOnlineRestart(beginRestartRound);
        }

        private bool CanRequestOnlineRematch() =>
            !MatchState.Disposed
            && OnlineState is { IsOnlineDirectInvite: true, OnlineRoundFinished: true, OnlineRematchStarted: false };

        private async UniTask SubmitLocalRoundReadyAsync(string localUserId)
        {
            await Online.OnlineSessionFlow.SetReadyForNextMatchAsync(true);

            var roundId = Online.OnlineRoundCoordinator.MatchRoundId;
            
            await Online.NetworkBridge.SubmitRoundReadyAsync(new RoundReadySignal(
                localUserId,
                isReady: true,
                matchRoundId: roundId,
                clientTick: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
        }

        private bool MarkLocalReady() =>
            Online.OnlineRoundCoordinator.SetReady(OnlineState.OnlineIsHost, isReady: true);

        private void ApplyTerminalDisconnectState()
        {
            OnlineState.OnlineTerminalResultShown = true;
            OnlineState.OnlineRoundFinished = true;
            OnlineState.OnlineRematchStarted = false;
            OnlineState.IsOnlineDirectInvite = false;
        }

        private void StopOnlineGameplayPresentation()
        {
            Timers.MoveTimerService.Stop();
            Timers.BattleshipPlacementTimerService.Stop();
            Core.MovesBinder.Unbind();
            Battleship.BattleshipBoardsBinder?.Unbind();
            Timers.MoveTimerHudBinder?.Unbind();
            Timers.BattleshipPlacementTimerHudBinder?.Unbind();
            Core.WinLineRenderer.Clear();
        }

        private GameResult BuildOpponentDisconnectResult()
        {
            var winner = OnlineState.OnlineIsHost ? PlayerMark.X : PlayerMark.O;
            return GameResult.Timeout(winner);
        }

        private void RecordOpponentDisconnectResult(GameResult gameResult)
        {
            Core.SeriesService.RecordResult(gameResult);
            _sessionScoreStore.PersistIfNeeded(_dependencies, _state);
            _uiCoordinator.UpdateScoreLabels();
            _uiCoordinator.SetRoundFinishedVisualState(true);
        }

        private void ShowOpponentDisconnectResult(OnlineFlowSnapshot snapshot, GameResult gameResult) =>
            UiState.ResultViewModel?.Show(
                gameResult,
                Core.SeriesService.Score.CurrentValue,
                _uiCoordinator.ResolveOpponentLeftResultText(snapshot.ErrorCode));

        private bool CanHandleIncomingRoundReadySignal(RoundReadySignal signal)
        {
            if (MatchState.Disposed || !OnlineState.IsOnlineDirectInvite || !OnlineState.OnlineRoundFinished || OnlineState.OnlineRematchStarted)
                return false;

            return signal.MatchRoundId == Online.OnlineRoundCoordinator.MatchRoundId;
        }

        private void TryStartOnlineRestart(Action beginRestartRound)
        {
            if (OnlineState.OnlineRematchStarted || MatchState.RestartInProgress)
                return;

            OnlineState.OnlineRematchStarted = true;
            beginRestartRound();
        }

        private static bool IsOpponentDisconnectTerminal(OnlineFlowSnapshot snapshot) =>
            snapshot is { State: OnlineFlowState.Terminated, ErrorCode: OnlineErrorCode.OpponentLeft or OnlineErrorCode.DisconnectTimeout };
    }
}