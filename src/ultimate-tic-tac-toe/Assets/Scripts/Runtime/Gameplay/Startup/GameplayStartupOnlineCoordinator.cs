#nullable enable
using System;
using Cysharp.Threading.Tasks;
using R3;
using Runtime.GameModes.Wizard.Configs;
using Runtime.GameModes.Wizard.Online;
using Runtime.Games.Battleship.Startup;

namespace Runtime.Gameplay.Startup
{
    internal sealed class GameplayStartupOnlineCoordinator
    {
        private readonly GameplayStartupDependencies _dependencies;
        private readonly GameplayStartupRuntimeState _state;
        private readonly GameplayStartupBattleshipRecoveryCoordinator _recoveryCoordinator;
        private readonly GameplayStartupOnlineFlowHandler _flowHandler;
        private readonly GameplayStartupOnlineMoveHandler _moveHandler;

        private GameplayStartupOnlineServices Online => _dependencies.Online;
        private GameplayStartupUiState UiState => _state.Ui;
        private GameplayStartupOnlineState OnlineState => _state.Online;
        private GameplayStartupBattleshipState BattleshipState => _state.Battleship;

        public GameplayStartupOnlineCoordinator(
            GameplayStartupDependencies dependencies,
            GameplayStartupRuntimeState state,
            GameplayStartupUiCoordinator uiCoordinator,
            GameplayStartupBattleshipRecoveryCoordinator recoveryCoordinator,
            GameplayStartupBattleshipSessionScoreStore sessionScoreStore)
        {
            _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _recoveryCoordinator = recoveryCoordinator ?? throw new ArgumentNullException(nameof(recoveryCoordinator));
            
            _flowHandler = new GameplayStartupOnlineFlowHandler(
                dependencies,
                state,
                uiCoordinator ?? throw new ArgumentNullException(nameof(uiCoordinator)),
                sessionScoreStore ?? throw new ArgumentNullException(nameof(sessionScoreStore)));
            
            _moveHandler = new GameplayStartupOnlineMoveHandler(dependencies, state);
        }

        internal GameLaunchConfig ApplyOnlineMatchConfigOverrideIfNeeded(GameLaunchConfig config)
        {
            var session = Online.OnlineSessionContextStore.Snapshot;
            
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

        internal async UniTask BindOnlineMoveBridgeAsync(Action beginRestartRound)
        {
            if (!TryGetBindableOnlineSession(out var session))
                return;

            ApplyOnlineSessionState(session);
            await BindOnlineBridgesAsync(session);
            SubscribeOnlineSignals(beginRestartRound);
            await InitializeBattleshipRecoveryAsync();
        }

        internal UniTask RequestOnlineRematchAsync(Action beginRestartRound) =>
            _flowHandler.RequestOnlineRematchAsync(beginRestartRound);

        internal bool ShouldHandleOnlineOpponentDisconnectAsResult(OnlineFlowSnapshot snapshot) =>
            _flowHandler.ShouldHandleOnlineOpponentDisconnectAsResult(snapshot);

        internal void HandleOnlineOpponentDisconnectAsResult(OnlineFlowSnapshot snapshot) =>
            _flowHandler.HandleOnlineOpponentDisconnectAsResult(snapshot);

        internal bool ShouldExitToMenuByOnlineFlow(OnlineFlowSnapshot snapshot) =>
            _flowHandler.ShouldExitToMenuByOnlineFlow(snapshot);

        private bool TryGetBindableOnlineSession(out OnlineGameplaySessionSnapshot session)
        {
            session = Online.OnlineSessionContextStore.Snapshot;
            
            return session.IsOnlineDirectInvite
                   && !string.IsNullOrWhiteSpace(session.LocalUserId)
                   && Online.MatchStateProvider != null;
        }

        private void ApplyOnlineSessionState(OnlineGameplaySessionSnapshot session)
        {
            OnlineState.IsOnlineDirectInvite = true;
            OnlineState.OnlineRoundFinished = false;
            OnlineState.OnlineRematchStarted = false;
            OnlineState.OnlineIsHost = session.IsHost;
            Online.OnlineRoundCoordinator.ResetSession();
            OnlineState.OnlineLocalUserId = session.LocalUserId;
            OnlineState.OnlineRemoteUserId = null;
            OnlineState.UseHostAuthoritativeFilter = session.IsHost;
            OnlineState.OnlineAcceptedShotSequence = 0;
        }

        private async UniTask BindOnlineBridgesAsync(OnlineGameplaySessionSnapshot session)
        {
            await Online.NetworkBridge.BindAsync(session.LocalUserId!, session.IsHost);

            if (BattleshipState.IsBattleshipMatch)
                await Online.BattleshipNetworkBridge.BindAsync(session.LocalUserId!, session.IsHost);
        }

        private void SubscribeOnlineSignals(Action beginRestartRound)
        {
            Online.NetworkBridge.IncomingMoves
                .Subscribe(_moveHandler.HandleIncomingOnlineMove)
                .AddTo(UiState.Subscriptions!);

            Online.NetworkBridge.IncomingRoundReadySignals
                .Subscribe(signal => _flowHandler.HandleIncomingRoundReadySignal(signal, beginRestartRound))
                .AddTo(UiState.Subscriptions!);

            Online.NetworkBridge.IncomingTimeoutSignals
                .Subscribe(_moveHandler.HandleIncomingOnlineTimeoutSignal)
                .AddTo(UiState.Subscriptions!);

            if (!BattleshipState.IsBattleshipMatch)
                return;

            Online.BattleshipNetworkBridge.IncomingRecoverySnapshots
                .Subscribe(_recoveryCoordinator.OnIncomingBattleshipRecoverySnapshot)
                .AddTo(UiState.Subscriptions!);
        }

        private async UniTask InitializeBattleshipRecoveryAsync()
        {
            if (!BattleshipState.IsBattleshipMatch || !OnlineState.OnlineIsHost)
                return;

            await _recoveryCoordinator.PublishBattleshipRecoverySnapshotAsync();
            _recoveryCoordinator.TryStartHeartbeatIfNeeded();
        }
    }
}