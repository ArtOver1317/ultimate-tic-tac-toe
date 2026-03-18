#nullable enable
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Runtime.GameModes.Wizard.Configs;
using Runtime.GameModes.Wizard.Modes;
using Runtime.Games.Battleship.Core;
using Runtime.Gameplay.Shared;
using Runtime.Infrastructure.Logging;
using StripLog;

namespace Runtime.Gameplay.Startup
{
    internal sealed class GameplayStartupBotCoordinator
    {
        private readonly GameplayStartupDependencies _dependencies;
        private readonly GameplayStartupRuntimeState _state;

        private GameplayStartupBotServices Bot => _dependencies.Bot;
        private GameplayStartupTimerServices Timers => _dependencies.Timers;
        private GameplayStartupBattleshipServices Battleship => _dependencies.Battleship;
        private GameplayStartupBotState BotState => _state.Bot;

        public GameplayStartupBotCoordinator(GameplayStartupDependencies dependencies, GameplayStartupRuntimeState state)
        {
            _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
            _state = state ?? throw new ArgumentNullException(nameof(state));
        }

        internal async UniTask TryStartBattleshipBotAsync(GameLaunchConfig config, CancellationToken ct)
        {
            if (config.OpponentConfig is not BotOpponentConfig)
                return;

            if (Bot.BattleshipBotDriver == null)
            {
                Log.Warning(LogTags.Infrastructure, "[GameplayStartup] Battleship bot driver is not resolved.");
                return;
            }

            var battleshipBotStart = await Bot.BattleshipBotDriver.StartAsync(config, PlayerSlotMapping.SlotO, ct);
            BotState.BattleshipBotStarted = battleshipBotStart.Status == BotStartStatus.Started;

            if (BotState.BattleshipBotStarted)
                return;

            Log.Warning(
                LogTags.Infrastructure,
                $"[GameplayStartup] Battleship bot driver not started: {battleshipBotStart.Status} — {battleshipBotStart.Error}");
        }

        internal async UniTask TryStartBotAsync(GameLaunchConfig config, CancellationToken ct)
        {
            if (config.OpponentConfig is not BotOpponentConfig botConfig)
                return;

            if (IsUltimateConfig(config.GameConfig))
            {
                var normalizedDifficultyId = NormalizeUltimateDifficultyId(botConfig.DifficultyId);

                try
                {
                    await Bot.UltimateBotOrchestrator.StartAsync(botSlot: 1, normalizedDifficultyId, ct);
                    BotState.UltimateBotStarted = true;
                    return;
                }
                catch (Exception ex)
                {
                    Log.Warning(LogTags.Infrastructure, $"[GameplayStartup] Ultimate bot orchestrator not started: {ex.Message}");
                    return;
                }
            }

            var result = await Bot.BotDriver.StartAsync(config, botSlot: 1, botConfig.DifficultyId, ct);
            BotState.ClassicBotStarted = result.Status == BotStartStatus.Started;
            
            if (BotState.ClassicBotStarted)
                return;

            Log.Warning(LogTags.Infrastructure, $"[GameplayStartup] Bot driver not started: {result.Status} — {result.Error}");
        }

        internal void UpdateMoveTimerStateForBattleshipBot()
        {
            if (!BotState.BattleshipBotStarted || Battleship.BattleshipSnapshotProvider == null)
                return;

            var freeze = Battleship.BattleshipSnapshotProvider.Phase == BattleshipPhase.Battle
                         && Battleship.BattleshipSnapshotProvider.ActivePlayerSlot == PlayerSlotMapping.SlotO;

            if (freeze)
                Timers.MoveTimerService.Freeze();
            else
                Timers.MoveTimerService.Unfreeze();

            Timers.MoveTimerHudBinder?.SetVisibilityOverride(freeze ? false : null);
        }

        private static bool IsUltimateConfig(IGameConfig gameConfig) => gameConfig switch
        {
            UltimateTicTacToeConfig => true,
            TicTacToeConfig ticTacToeConfig => ticTacToeConfig.IsUltimate,
            _ => false,
        };

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

            return string.Equals(normalized, "easy", StringComparison.OrdinalIgnoreCase) ? "easy" : normalized;
        }
    }
}