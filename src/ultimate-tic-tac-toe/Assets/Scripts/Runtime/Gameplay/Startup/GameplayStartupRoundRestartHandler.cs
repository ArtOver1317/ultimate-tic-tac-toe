#nullable enable
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Runtime.GameModes.Wizard.Configs;
using Runtime.Gameplay.Shared;
using Runtime.Games.Battleship.Startup;
using Runtime.Infrastructure.GameStateMachine.States;
using Runtime.Infrastructure.Logging;
using StripLog;
using UnityEngine;

namespace Runtime.Gameplay.Startup
{
    internal sealed class GameplayStartupRoundRestartHandler
    {
        private static readonly TimeSpan _restartEpochWaitTimeout = TimeSpan.FromSeconds(1);

        private readonly GameplayStartupDependencies _dependencies;
        private readonly GameplayStartupRuntimeState _state;
        private readonly GameplayStartupUiCoordinator _uiCoordinator;
        private readonly GameplayStartupBattleshipSessionScoreStore _sessionScoreStore;

        private GameplayStartupCoreServices Core => _dependencies.Core;
        private GameplayStartupTimerServices Timers => _dependencies.Timers;
        private GameplayStartupBotServices Bot => _dependencies.Bot;
        private GameplayStartupBattleshipServices Battleship => _dependencies.Battleship;
        private GameplayStartupUiState UiState => _state.Ui;
        private GameplayStartupOnlineState OnlineState => _state.Online;
        private GameplayStartupMatchState MatchState => _state.Match;
        private GameplayStartupBattleshipState BattleshipState => _state.Battleship;

        public GameplayStartupRoundRestartHandler(
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

        internal async UniTask RestartAsync()
        {
            try
            {
                if (MatchState.Disposed)
                    return;

                MatchState.RestartInProgress = true;
                UnbindRestartSensitiveUi();

                var nextRoundStarterMark = Core.SeriesService.NextRound();
                var startingSlot = PlayerSlotMapping.MarkToSlot(nextRoundStarterMark);

                if (BattleshipState.IsBattleshipMatch)
                {
                    await RestartBattleshipRoundAsync();
                    return;
                }

                await RestartStandardRoundAsync(startingSlot);
            }
            catch (Exception ex)
            {
                if (MatchState.Disposed)
                    return;

                OnlineState.OnlineRematchStarted = false;
                Log.Error(LogTags.Infrastructure, $"[GameplayStartup] Restart failed: {ex}");
            }
            finally
            {
                MatchState.RestartInProgress = false;
            }
        }

        private void UnbindRestartSensitiveUi()
        {
            UiState.UltimateAllowedBinder?.Unbind();
            UiState.UltimateMiniBoardStatusBinder?.Unbind();
        }

        private async UniTask RestartBattleshipRoundAsync()
        {
            var startingSlot = ResolveNextBattleshipStartingSlot();
            BattleshipState.BattleshipCurrentStartingSlot = startingSlot;

            _sessionScoreStore.PersistIfNeeded(_dependencies, _state);
            await ReloadBattleshipGameplayScopeAsync(startingSlot);
        }

        private int ResolveNextBattleshipStartingSlot()
        {
            var previousStartingSlot = BattleshipState.BattleshipCurrentStartingSlot;

            if (previousStartingSlot < 0 && Battleship.BattleshipSnapshotProvider != null)
                previousStartingSlot = Battleship.BattleshipSnapshotProvider.ActivePlayerSlot;

            if (previousStartingSlot < 0)
                previousStartingSlot = PlayerSlotMapping.SlotX;

            return previousStartingSlot == PlayerSlotMapping.SlotX
                ? PlayerSlotMapping.SlotO
                : PlayerSlotMapping.SlotX;
        }

        private async UniTask RestartStandardRoundAsync(int startingSlot)
        {
            var previousEpoch = Bot.UltimateSnapshotProvider?.Epoch ?? 0UL;
            Core.CommandSink.SubmitCommand(new RestartRoundCommand(startingSlot));

            if (!await WaitForUltimateEpochIfNeeded(previousEpoch))
                return;

            if (MatchState.Disposed)
                return;

            ResetRoundPresentation(startingSlot);
            ResetOnlineRoundState();
        }

        private async UniTask<bool> WaitForUltimateEpochIfNeeded(ulong previousEpoch)
        {
            if (UiState.FieldSpec is not { Kind: FieldKind.Ultimate } || Bot.UltimateSnapshotProvider == null)
                return true;

            var epochChanged = await WaitForEpochChangeAsync(previousEpoch, _restartEpochWaitTimeout);
            
            if (epochChanged)
                return true;

            Log.Error(LogTags.Infrastructure,
                "[GameplayStartup] Restart timeout: epoch did not change. Keep result overlay visible for Retry/Exit.");
            
            return false;
        }

        private void ResetRoundPresentation(int startingSlot)
        {
            Core.WinLineRenderer.Clear();
            UiState.ResultViewModel?.Hide();
            _uiCoordinator.SetRoundFinishedVisualState(false);
            Bot.MatchFailSafeGateway.ResetAbortState();

            Core.MovesBinder.Bind();
            Timers.MoveTimerHudBinder?.Bind();
            UiState.UltimateAllowedBinder?.Bind();
            UiState.UltimateMiniBoardStatusBinder?.Bind();
            Timers.MoveTimerService.StartOrResetForPlayer(startingSlot);
        }

        private void ResetOnlineRoundState()
        {
            OnlineState.OnlineRoundFinished = false;
            OnlineState.OnlineRematchStarted = false;
            OnlineState.OnlineTerminalResultShown = false;
        }

        private async UniTask ReloadBattleshipGameplayScopeAsync(int nextStartingSlot)
        {
            if (MatchState.ActiveLaunchConfig == null)
                throw new InvalidOperationException("Launch config is not available for Battleship rematch.");

            var nextConfig = new GameLaunchConfig(
                MatchState.ActiveLaunchConfig.GameId,
                MatchState.ActiveLaunchConfig.GameConfig,
                MatchState.ActiveLaunchConfig.OpponentConfig,
                MatchState.ActiveLaunchConfig.MoveTimeLimitSeconds,
                nextStartingSlot);

            MatchState.ActiveLaunchConfig = nextConfig;
            ResetOnlineRoundState();

            await Core.StateMachine.EnterAsync<LoadGameplayState, GameLaunchConfig>(nextConfig, CancellationToken.None);
        }

        private async UniTask<bool> WaitForEpochChangeAsync(ulong previousEpoch, TimeSpan timeout)
        {
            if (Bot.UltimateSnapshotProvider == null)
                return true;

            var startTime = Time.realtimeSinceStartup;

            while (!MatchState.Disposed)
            {
                if (Bot.UltimateSnapshotProvider.Epoch != previousEpoch)
                    return true;

                if (Time.realtimeSinceStartup - startTime >= (float)timeout.TotalSeconds)
                    return false;

                await UniTask.DelayFrame(1);
            }

            return false;
        }
    }
}