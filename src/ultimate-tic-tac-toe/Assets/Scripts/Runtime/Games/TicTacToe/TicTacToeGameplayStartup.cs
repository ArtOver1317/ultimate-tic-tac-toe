#nullable enable
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using Runtime.GameModes.Wizard.Modes;
using Runtime.Gameplay;
using Runtime.Gameplay.Startup;
using Runtime.Gameplay.Shared;
using Runtime.Games.TicTacToe.AI.Ultimate.Core;
using Runtime.Infrastructure.Logging;
using StripLog;
using UnityEngine.UIElements;
using EcsRoundFinishedEvent = Runtime.Gameplay.Shared.RoundFinishedEvent;

namespace Runtime.Games.TicTacToe
{
    public sealed class TicTacToeGameplayStartup : IGameplayStartup, IDisposable
    {
        private readonly GameplayStartupDependencies _dependencies;
        private readonly GameplayStartupRuntimeState _state;
        private readonly GameplayStartupUiCoordinator _uiCoordinator;
        private readonly GameplayStartupBotCoordinator _botCoordinator;
        private readonly GameplayStartupOnlineCoordinator _onlineCoordinator;
        private readonly GameplayStartupRoundCoordinator _roundCoordinator;

        private GameplayStartupCoreServices Core => _dependencies.Core;
        private GameplayStartupTimerServices Timers => _dependencies.Timers;
        private GameplayStartupBotServices Bot => _dependencies.Bot;
        private GameplayStartupOnlineServices Online => _dependencies.Online;
        private GameplayStartupUiState UiState => _state.Ui;
        private GameplayStartupBotState BotState => _state.Bot;
        private GameplayStartupOnlineState OnlineState => _state.Online;
        private GameplayStartupMatchState MatchState => _state.Match;
        private GameplayStartupBattleshipState BattleshipState => _state.Battleship;

        private bool _cleanedUp;

        internal TicTacToeGameplayStartup(
            GameplayStartupDependencies dependencies,
            GameplayStartupRuntimeState state,
            GameplayStartupUiCoordinator uiCoordinator,
            GameplayStartupBotCoordinator botCoordinator,
            GameplayStartupOnlineCoordinator onlineCoordinator,
            GameplayStartupRoundCoordinator roundCoordinator)
        {
            _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _uiCoordinator = uiCoordinator ?? throw new ArgumentNullException(nameof(uiCoordinator));
            _botCoordinator = botCoordinator ?? throw new ArgumentNullException(nameof(botCoordinator));
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
            
            if (string.Equals(config.GameId, BattleshipStrategy.DefaultGameId, StringComparison.Ordinal))
            {
                CleanupGameplay();
                
                await _roundCoordinator.HandleErrorAsync(
                    GameplayError.InvalidConfig("Battleship config must be handled by Battleship startup."),
                    ct);
                
                return;
            }

            MatchState.ActiveLaunchConfig = config;
            BattleshipState.IsBattleshipMatch = false;

            IGameplaySession? session = null;
            
            try
            {
                session = await Core.GameService.StartMatchAsync(config, ct);
                await Core.FieldPresenter.BindAsync(session.FieldRenderSpec, ct, config.GameId);
                _uiCoordinator.BindOnlinePlayerNamesStoreIfNeeded();

                UiState.FieldSpec = session.FieldRenderSpec;
                Core.SeriesService.StartSeries();

                Core.EcsLifecycle.StartMatch(config);

                var activePlayerSlot = Online.MatchStateProvider?.ActivePlayerSlot ?? PlayerSlotMapping.SlotX;
                Timers.MoveTimerService.StartOrResetForPlayer(activePlayerSlot);

                Core.MovesBinder.Bind();
                Timers.MoveTimerHudBinder?.Bind();

                _uiCoordinator.BindUltimateUiIfNeeded();
                _uiCoordinator.SetRoundFinishedVisualState(false);

                await _botCoordinator.TryStartBotAsync(config, ct);

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

            if (BotState.ClassicBotStarted)
            {
                Bot.BotDriver.IsBusy
                    .Subscribe(busy => SetFieldPickingMode(busy ? PickingMode.Ignore : PickingMode.Position))
                    .AddTo(UiState.Subscriptions);

                Bot.BotDriver.IsDisabled
                    .Where(disabled => disabled)
                    .Subscribe(_ =>
                    {
                        Log.Error(LogTags.Infrastructure,
                            "[GameplayStartup] Bot disabled after exhausting all retry attempts.");
                        
                        SetFieldPickingMode(PickingMode.Position);
                    })
                    .AddTo(UiState.Subscriptions);
            }

            if (BotState.UltimateBotStarted)
            {
                Bot.UltimateBotOrchestrator.IsThinking
                    .Subscribe(thinking =>
                    {
                        SetFieldPickingMode(
                            thinking || Bot.MatchFailSafeGateway.IsInputLocked
                                ? PickingMode.Ignore
                                : PickingMode.Position);
                    })
                    .AddTo(UiState.Subscriptions);

                Bot.UltimateBotOrchestrator.MoveFailed
                    .Where(evt => evt.Reason is BotFailureReason.NoLegalMovesInconsistentState or BotFailureReason.EngineError)
                    .Subscribe(evt =>
                    {
                        Log.Error(LogTags.Infrastructure,
                            $"[GameplayStartup] Ultimate bot move failed: {evt.Reason} ({evt.Message})");
                        
                        SetFieldPickingMode(
                            Bot.MatchFailSafeGateway.IsInputLocked
                                ? PickingMode.Ignore
                                : PickingMode.Position);
                    })
                    .AddTo(UiState.Subscriptions);
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

            Bot.BotDriver.Dispose();
            Bot.UltimateBotOrchestrator.Dispose();

            Core.MovesBinder.Unbind();
            Timers.MoveTimerHudBinder?.Unbind();
            _uiCoordinator.DisposeUltimateUiBinders();

            Online.NetworkBridge.UnbindAsync().Forget();

            Timers.MoveTimerService.Stop();
            Core.EcsLifecycle.StopMatch();
            Core.WinLineRenderer.Clear();

            UiState.ResultViewModel?.Dispose();
            UiState.ResultViewModel = null;

            _uiCoordinator.SetRoundFinishedVisualState(false);
            Core.FieldPresenter.Unbind();
        }
    }
}