#nullable enable
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using Runtime.GameModes.Wizard.Modes;
using Runtime.Gameplay;
using Runtime.Gameplay.Startup;
using Runtime.Games.Battleship.Core;
using Runtime.Infrastructure.Logging;
using StripLog;
using UnityEngine.UIElements;
using EcsRoundFinishedEvent = Runtime.Gameplay.Shared.RoundFinishedEvent;

namespace Runtime.Games.Battleship.Startup
{
    public sealed class BattleshipGameplayStartup : IGameplayStartup, IDisposable
    {
        private readonly GameplayStartupDependencies _dependencies;
        private readonly GameplayStartupRuntimeState _state;
        private readonly GameplayStartupUiCoordinator _uiCoordinator;
        private readonly GameplayStartupBotCoordinator _botCoordinator;
        private readonly GameplayStartupBattleshipSessionScoreStore _sessionScoreStore;
        private readonly GameplayStartupOnlineCoordinator _onlineCoordinator;
        private readonly GameplayStartupRoundCoordinator _roundCoordinator;

        private GameplayStartupCoreServices Core => _dependencies.Core;
        private GameplayStartupTimerServices Timers => _dependencies.Timers;
        private GameplayStartupBotServices Bot => _dependencies.Bot;
        private GameplayStartupOnlineServices Online => _dependencies.Online;
        private GameplayStartupBattleshipServices Battleship => _dependencies.Battleship;
        private GameplayStartupUiState UiState => _state.Ui;
        private GameplayStartupBotState BotState => _state.Bot;
        private GameplayStartupOnlineState OnlineState => _state.Online;
        private GameplayStartupMatchState MatchState => _state.Match;
        private GameplayStartupBattleshipState BattleshipState => _state.Battleship;

        private bool _cleanedUp;

        internal BattleshipGameplayStartup(
            GameplayStartupDependencies dependencies,
            GameplayStartupRuntimeState state,
            GameplayStartupUiCoordinator uiCoordinator,
            GameplayStartupBotCoordinator botCoordinator,
            GameplayStartupBattleshipSessionScoreStore sessionScoreStore,
            GameplayStartupOnlineCoordinator onlineCoordinator,
            GameplayStartupRoundCoordinator roundCoordinator)
        {
            _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _uiCoordinator = uiCoordinator ?? throw new ArgumentNullException(nameof(uiCoordinator));
            _botCoordinator = botCoordinator ?? throw new ArgumentNullException(nameof(botCoordinator));
            _sessionScoreStore = sessionScoreStore ?? throw new ArgumentNullException(nameof(sessionScoreStore));
            _onlineCoordinator = onlineCoordinator ?? throw new ArgumentNullException(nameof(onlineCoordinator));
            _roundCoordinator = roundCoordinator ?? throw new ArgumentNullException(nameof(roundCoordinator));
        }

        public async UniTask StartAsync(CancellationToken ct)
        {
            if (Core.StatisticsReporter == null)
            {
                GameLog.Warning(
                    "[GameplayStartup] PlayerStatisticsMatchReporter is not resolved. Statistics reporting is disabled for this match.");
            }

            ct.ThrowIfCancellationRequested();

            if (!Core.ConfigStore.TryConsume(out var config) || config == null)
            {
                CleanupGameplay();
                await _roundCoordinator.HandleErrorAsync(GameplayError.InvalidConfig("Launch config not found."), ct);
                return;
            }

            config = _onlineCoordinator.ApplyOnlineMatchConfigOverrideIfNeeded(config);

            if (!string.Equals(config.GameId, BattleshipStrategy.DefaultGameId, StringComparison.Ordinal))
            {
                CleanupGameplay();
                
                await _roundCoordinator.HandleErrorAsync(
                    GameplayError.InvalidConfig("Non-Battleship config must be handled by TicTacToe startup."),
                    ct);
                
                return;
            }

            MatchState.ActiveLaunchConfig = config;
            BattleshipState.IsBattleshipMatch = true;
            BattleshipState.BattleshipCurrentStartingSlot = config.StartingPlayerSlotOverride ?? -1;

            IGameplaySession? session = null;
            
            try
            {
                session = await Core.GameService.StartMatchAsync(config, ct);
                await Core.FieldPresenter.BindAsync(session.FieldRenderSpec, ct, config.GameId);
                _uiCoordinator.BindOnlinePlayerNamesStoreIfNeeded();

                UiState.FieldSpec = session.FieldRenderSpec;
                Core.SeriesService.StartSeries();
                _sessionScoreStore.RestoreIfNeeded(_dependencies, _state, config, _uiCoordinator.UpdateScoreLabels);

                Core.EcsLifecycle.StartMatch(config);
                Timers.BattleshipPlacementTimerService.SyncFromSnapshot();

                Core.MovesBinder.Bind();
                Battleship.BattleshipBoardsBinder?.Bind();
                Battleship.BattleshipPlacementUiController?.Bind();
                _uiCoordinator.SyncBattleshipTimerHudBindings(_botCoordinator.UpdateMoveTimerStateForBattleshipBot);

                _uiCoordinator.SetRoundFinishedVisualState(false);

                await _botCoordinator.TryStartBattleshipBotAsync(config, ct);

                _uiCoordinator.CreateResultViewModel();
                SubscribeToEvents();
                await _onlineCoordinator.BindOnlineMoveBridgeAsync(BeginRestartRound);
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
                var error = GameplayStartupRoundCoordinator.MapError(ex);
                Log.Error(LogTags.Infrastructure, $"[GameplayStartup] Failed to start gameplay: {ex}");
                await _roundCoordinator.HandleErrorAsync(error, ct);
            }
        }

        public void Dispose()
        {
            if (MatchState.Disposed)
                return;

            MatchState.Disposed = true;
            CleanupGameplay();
        }

        internal void HandleResultAction(ResultAction action)
        {
            switch (action)
            {
                case ResultAction.Restart:
                    if (OnlineState.OnlineTerminalResultShown)
                    {
                        _roundCoordinator.ExitToMenuAsync().Forget();
                        break;
                    }

                    if (MatchState.RestartInProgress)
                    {
                        Log.Warning(LogTags.Infrastructure, "[GameplayStartup] Restart already in progress. Ignore duplicate request.");
                        break;
                    }

                    if (OnlineState.IsOnlineDirectInvite)
                        _onlineCoordinator.RequestOnlineRematchAsync(BeginRestartRound).Forget();
                    else
                        BeginRestartRound();
                    
                    break;

                case ResultAction.Exit:
                    _roundCoordinator.ExitToMenuAsync().Forget();
                    break;
            }
        }

        private void SubscribeToEvents()
        {
            UiState.Subscriptions?.Dispose();
            UiState.Subscriptions = new CompositeDisposable();

            Core.EventStream.RoundFinished
                .Subscribe(OnRoundFinished)
                .AddTo(UiState.Subscriptions);

            _uiCoordinator.SubscribeScoreboardPlayerNames();

            if (BotState.BattleshipBotStarted && Bot.BattleshipBotDriver != null)
            {
                Bot.BattleshipBotDriver.IsThinking
                    .Subscribe(thinking =>
                    {
                        SetFieldPickingMode(
                            thinking || Bot.MatchFailSafeGateway.IsInputLocked
                                ? PickingMode.Ignore
                                : PickingMode.Position);
                    })
                    .AddTo(UiState.Subscriptions);

                Core.EventStream.CurrentPlayerChanged
                    .Subscribe(_ => _botCoordinator.UpdateMoveTimerStateForBattleshipBot())
                    .AddTo(UiState.Subscriptions);

                Battleship.BattleshipEventStream?.PhaseChanged
                    .Subscribe(_ => _botCoordinator.UpdateMoveTimerStateForBattleshipBot())
                    .AddTo(UiState.Subscriptions);

                _botCoordinator.UpdateMoveTimerStateForBattleshipBot();
            }

            UiState.ResultViewModel?.Actions
                .Subscribe(HandleResultAction)
                .AddTo(UiState.Subscriptions);

            Online.OnlineSessionFlow.Snapshot
                .Where(_onlineCoordinator.ShouldHandleOnlineOpponentDisconnectAsResult)
                .Subscribe(_onlineCoordinator.HandleOnlineOpponentDisconnectAsResult)
                .AddTo(UiState.Subscriptions);

            Online.OnlineSessionFlow.Snapshot
                .Where(_onlineCoordinator.ShouldExitToMenuByOnlineFlow)
                .Subscribe(_ => _roundCoordinator.ExitToMenuAsync().Forget())
                .AddTo(UiState.Subscriptions);

            if (BattleshipState.IsBattleshipMatch 
                && Battleship is { BattleshipEventStream: not null, BattleshipSnapshotProvider: not null })
            {
                Battleship.BattleshipEventStream.PhaseChanged
                    .Subscribe(_ => _uiCoordinator.SyncBattleshipTimerHudBindings(_botCoordinator.UpdateMoveTimerStateForBattleshipBot))
                    .AddTo(UiState.Subscriptions);

                Battleship.BattleshipEventStream.PhaseChanged
                    .Where(evt => evt.Phase == BattleshipPhase.Battle)
                    .Subscribe(_ =>
                    {
                        var activeSlot = Battleship.BattleshipSnapshotProvider.ActivePlayerSlot;
                        
                        if (activeSlot >= 0)
                            BattleshipState.BattleshipCurrentStartingSlot = activeSlot;
                    })
                    .AddTo(UiState.Subscriptions);

                _uiCoordinator.SyncBattleshipTimerHudBindings(_botCoordinator.UpdateMoveTimerStateForBattleshipBot);
            }
        }

        private void OnRoundFinished(EcsRoundFinishedEvent evt) =>
            _roundCoordinator.HandleRoundFinished(evt);

        private void BeginRestartRound() =>
            _roundCoordinator.RestartRoundAsync().Forget();

        private void SetFieldPickingMode(PickingMode pickingMode)
        {
            var container = Core.FieldUiAdapter.FieldContainer;
            
            if (container != null)
                container.pickingMode = pickingMode;
        }

        private void CleanupGameplay()
        {
            if (_cleanedUp)
                return;

            _cleanedUp = true;

            UiState.Subscriptions?.Dispose();
            UiState.Subscriptions = null;

            _uiCoordinator.UnbindOnlinePlayerNamesStoreIfNeeded();

            Bot.BattleshipBotDriver?.Dispose();

            Battleship.BattleshipPlacementUiController?.Unbind();
            Battleship.BattleshipBoardsBinder?.Unbind();
            Core.MovesBinder.Unbind();
            Timers.MoveTimerHudBinder?.Unbind();
            Timers.BattleshipPlacementTimerHudBinder?.Unbind();

            Online.BattleshipNetworkBridge.UnbindAsync().Forget();
            Online.NetworkBridge.UnbindAsync().Forget();

            Timers.MoveTimerService.Stop();
            Timers.BattleshipPlacementTimerService.Stop();
            Core.EcsLifecycle.StopMatch();
            Core.WinLineRenderer.Clear();

            UiState.ResultViewModel?.Dispose();
            UiState.ResultViewModel = null;

            _uiCoordinator.SetRoundFinishedVisualState(false);
            Core.FieldPresenter.Unbind();
        }
    }
}