#nullable enable
using System;
using Cysharp.Threading.Tasks;
using Runtime.Gameplay;
using Runtime.Gameplay.Startup;
using Runtime.Gameplay.Shared;
using Runtime.Games.Battleship.Core;
using Runtime.Games.Battleship.Networking;
using Runtime.Infrastructure.Logging;
using StripLog;
using EcsGameStatus = Runtime.Gameplay.Shared.EcsGameStatus;

namespace Runtime.Games.Battleship.Startup
{
    internal sealed class GameplayStartupBattleshipRecoveryCoordinator
    {
        private readonly GameplayStartupDependencies _dependencies;
        private readonly GameplayStartupRuntimeState _state;
        private readonly GameplayStartupUiCoordinator _uiCoordinator;
        private readonly GameplayStartupBotCoordinator _botCoordinator;
        private readonly GameplayStartupBattleshipSessionScoreStore _sessionScoreStore;

        private GameplayStartupCoreServices Core => _dependencies.Core;
        private GameplayStartupTimerServices Timers => _dependencies.Timers;
        private GameplayStartupOnlineServices Online => _dependencies.Online;
        private GameplayStartupBattleshipServices Battleship => _dependencies.Battleship;
        private GameplayStartupUiState UiState => _state.Ui;
        private GameplayStartupOnlineState OnlineState => _state.Online;
        private GameplayStartupMatchState MatchState => _state.Match;
        private GameplayStartupBattleshipState BattleshipState => _state.Battleship;

        public GameplayStartupBattleshipRecoveryCoordinator(
            GameplayStartupDependencies dependencies,
            GameplayStartupRuntimeState state,
            GameplayStartupUiCoordinator uiCoordinator,
            GameplayStartupBotCoordinator botCoordinator,
            GameplayStartupBattleshipSessionScoreStore sessionScoreStore)
        {
            _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _uiCoordinator = uiCoordinator ?? throw new ArgumentNullException(nameof(uiCoordinator));
            _botCoordinator = botCoordinator ?? throw new ArgumentNullException(nameof(botCoordinator));
            _sessionScoreStore = sessionScoreStore ?? throw new ArgumentNullException(nameof(sessionScoreStore));
        }

        internal void OnIncomingBattleshipRecoverySnapshot(BattleshipRecoveryMessage message)
        {
            if (MatchState.Disposed || !BattleshipState.IsBattleshipMatch || OnlineState.OnlineIsHost || !OnlineState.IsOnlineDirectInvite)
                return;

            if (string.Equals(message.SenderUserId, OnlineState.OnlineLocalUserId, StringComparison.Ordinal))
                return;

            if (message.MatchRoundId != Online.OnlineRoundCoordinator.MatchRoundId)
                return;

            if (!TryBuildRecoveryState(message, out var recoveryState))
                return;

            if (Battleship.BattleshipRecoveryStateApplier?.TryApplyRecoveryState(recoveryState) != true)
                return;

            Timers.BattleshipPlacementTimerService.RestoreRemainingSeconds(recoveryState.PlacementTimerRemainingSeconds);
            
            if (recoveryState is { Phase: BattleshipPhase.Battle, ActivePlayerSlot: >= 0 })
                Timers.MoveTimerService.RestoreRemainingSeconds(recoveryState.MoveTimerRemainingSeconds, recoveryState.ActivePlayerSlot);

            _uiCoordinator.SyncBattleshipTimerHudBindings(_botCoordinator.UpdateMoveTimerStateForBattleshipBot);

            if (recoveryState.FinishStatus != EcsGameStatus.InProgress && !OnlineState.OnlineRoundFinished)
            {
                OnlineState.OnlineRoundFinished = true;
                OnlineState.OnlineRematchStarted = false;

                var recoveredResult = BuildRecoveredGameResult(recoveryState.FinishStatus, recoveryState.WinnerSlot);
                Core.SeriesService.RecordResult(recoveredResult);
                _sessionScoreStore.PersistIfNeeded(_dependencies, _state);
                _uiCoordinator.UpdateScoreLabels();
                _uiCoordinator.SetRoundFinishedVisualState(true);
                UiState.ResultViewModel?.Show(recoveredResult, Core.SeriesService.Score.CurrentValue);
            }
        }

        internal void TryStartHeartbeatIfNeeded()
        {
            if (BattleshipState.BattleshipRecoveryHeartbeatStarted)
                return;

            BattleshipState.BattleshipRecoveryHeartbeatStarted = true;
            RunBattleshipRecoveryHeartbeatAsync().Forget();
        }

        internal async UniTask PublishBattleshipRecoverySnapshotAsync()
        {
            if (MatchState.Disposed || !OnlineState.IsOnlineDirectInvite || !BattleshipState.IsBattleshipMatch || !OnlineState.OnlineIsHost)
                return;

            if (string.IsNullOrWhiteSpace(OnlineState.OnlineLocalUserId))
                return;

            if (!TryCreateBattleshipRecoveryMessage(out var message))
                return;

            try
            {
                await Online.BattleshipNetworkBridge.SubmitRecoverySnapshotAsync(message);
            }
            catch (Exception ex)
            {
                Log.Warning(LogTags.Infrastructure, $"[GameplayStartup] Failed to publish Battleship recovery snapshot: {ex.Message}");
            }
        }

        private async UniTaskVoid RunBattleshipRecoveryHeartbeatAsync()
        {
            try
            {
                while (!MatchState.Disposed && OnlineState.IsOnlineDirectInvite && BattleshipState.IsBattleshipMatch && OnlineState.OnlineIsHost)
                {
                    await PublishBattleshipRecoverySnapshotAsync();
                    await UniTask.Delay(TimeSpan.FromSeconds(1));
                }
            }
            catch (Exception ex)
            {
                if (MatchState.Disposed)
                    return;

                Log.Warning(LogTags.Infrastructure, $"[GameplayStartup] Battleship recovery heartbeat stopped: {ex.Message}");
            }
            finally
            {
                BattleshipState.BattleshipRecoveryHeartbeatStarted = false;
            }
        }

        private bool TryCreateBattleshipRecoveryMessage(out BattleshipRecoveryMessage message)
        {
            message = default;

            if (Battleship.BattleshipSnapshotProvider == null || string.IsNullOrWhiteSpace(OnlineState.OnlineLocalUserId))
                return false;

            var player0LayoutPayload = string.Empty;
            
            if (Battleship.BattleshipSnapshotProvider.TryGetFleetLayout(PlayerSlotMapping.SlotX, out var player0Layout))
            {
                try
                {
                    player0LayoutPayload = Battleship.BattleshipLayoutSerializer.Serialize(player0Layout);
                }
                catch
                {
                    player0LayoutPayload = string.Empty;
                }
            }

            var player1LayoutPayload = string.Empty;
            
            if (Battleship.BattleshipSnapshotProvider.TryGetFleetLayout(PlayerSlotMapping.SlotO, out var player1Layout))
            {
                try
                {
                    player1LayoutPayload = Battleship.BattleshipLayoutSerializer.Serialize(player1Layout);
                }
                catch
                {
                    player1LayoutPayload = string.Empty;
                }
            }

            var player0MarksPayload = SerializeMarks(Battleship.BattleshipSnapshotProvider.GetOpponentMarks(PlayerSlotMapping.SlotX));
            var player1MarksPayload = SerializeMarks(Battleship.BattleshipSnapshotProvider.GetOpponentMarks(PlayerSlotMapping.SlotO));

            Battleship.BattleshipSnapshotProvider.TryGetConsecutiveTimeouts(out var player0Timeouts, out var player1Timeouts);

            var placementRemainingMs = (long)Math.Round(Math.Max(0f, Timers.BattleshipPlacementTimerService.RemainingSeconds.CurrentValue) * 1000f);
            var moveRemainingMs = (long)Math.Round(Math.Max(0f, Timers.MoveTimerService.RemainingSeconds.CurrentValue) * 1000f);
            var winnerSlot = Battleship.BattleshipSnapshotProvider.WinnerSlot ?? -1;

            message = new BattleshipRecoveryMessage(
                Guid.NewGuid(),
                OnlineState.OnlineLocalUserId,
                Online.OnlineRoundCoordinator.MatchRoundId,
                (int)Battleship.BattleshipSnapshotProvider.Phase,
                Battleship.BattleshipSnapshotProvider.ActivePlayerSlot,
                placementRemainingMs,
                moveRemainingMs,
                player0Timeouts,
                player1Timeouts,
                winnerSlot,
                (int)Battleship.BattleshipSnapshotProvider.CurrentStatus,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                player0LayoutPayload,
                player1LayoutPayload,
                player0MarksPayload,
                player1MarksPayload);

            return true;
        }

        private bool TryBuildRecoveryState(BattleshipRecoveryMessage message, out BattleshipRecoveryState recoveryState)
        {
            recoveryState = default;

            if (!Enum.IsDefined(typeof(BattleshipPhase), message.Phase))
                return false;

            if (!Enum.IsDefined(typeof(EcsGameStatus), message.FinishStatus))
                return false;

            FleetLayout? player0Layout = null;
            
            if (!string.IsNullOrWhiteSpace(message.Player0LayoutPayload))
            {
                if (!Battleship.BattleshipLayoutSerializer.TryDeserialize(message.Player0LayoutPayload, out var parsedLayout))
                    return false;

                player0Layout = parsedLayout;
            }

            FleetLayout? player1Layout = null;
            
            if (!string.IsNullOrWhiteSpace(message.Player1LayoutPayload))
            {
                if (!Battleship.BattleshipLayoutSerializer.TryDeserialize(message.Player1LayoutPayload, out var parsedLayout))
                    return false;

                player1Layout = parsedLayout;
            }

            if (!TryDeserializeMarks(message.Player0OpponentMarksPayload, out var player0Marks)
                || !TryDeserializeMarks(message.Player1OpponentMarksPayload, out var player1Marks))
                return false;

            recoveryState = new BattleshipRecoveryState(
                (BattleshipPhase)message.Phase,
                message.ActivePlayerSlot,
                (EcsGameStatus)message.FinishStatus,
                message.WinnerSlot >= 0 ? message.WinnerSlot : null,
                player0Layout,
                player1Layout,
                player0Marks,
                player1Marks,
                message.Player0ConsecutiveTimeouts,
                message.Player1ConsecutiveTimeouts,
                Math.Max(0f, message.PlacementTimerRemainingMs / 1000f),
                Math.Max(0f, message.MoveTimerRemainingMs / 1000f));

            return true;
        }

        private static string SerializeMarks(System.Collections.Generic.IReadOnlyList<BattleshipCellMark> marks)
        {
            if (marks.Count == 0)
                return string.Empty;

            var chars = new char[marks.Count];
            
            for (var i = 0; i < marks.Count; i++)
            {
                chars[i] = (char)('0' + (int)marks[i]);
            }

            return new string(chars);
        }

        private static bool TryDeserializeMarks(string payload, out BattleshipCellMark[] marks)
        {
            marks = Array.Empty<BattleshipCellMark>();

            if (payload.Length == 0)
                return true;

            marks = new BattleshipCellMark[payload.Length];
            
            for (var i = 0; i < payload.Length; i++)
            {
                var value = payload[i] - '0';
                
                if (value is < 0 or > (int)BattleshipCellMark.Sunk)
                    return false;

                marks[i] = (BattleshipCellMark)value;
            }

            return true;
        }

        private static GameResult BuildRecoveredGameResult(EcsGameStatus status, int? winnerSlot)
        {
            var winner = winnerSlot.HasValue
                ? PlayerSlotMapping.SlotToMark(winnerSlot.Value)
                : PlayerMark.None;

            return status switch
            {
                EcsGameStatus.Win => winner != PlayerMark.None
                    ? GameResult.Win(winner, CreateFallbackWinLine())
                    : GameResult.Draw(),
                EcsGameStatus.Timeout => winner != PlayerMark.None
                    ? GameResult.Timeout(winner)
                    : GameResult.Draw(),
                EcsGameStatus.Draw => GameResult.Draw(),
                _ => GameResult.InProgress(),
            };
        }

        private static WinLine CreateFallbackWinLine() =>
            new(new CellId(0, 0), new CellId(0, 0), WinLineDirection.Horizontal, 1);
    }
}