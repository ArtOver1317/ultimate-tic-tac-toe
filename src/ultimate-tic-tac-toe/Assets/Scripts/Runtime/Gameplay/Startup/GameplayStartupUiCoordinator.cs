#nullable enable
using System;
using R3;
using Runtime.GameModes.Wizard.Online;
using Runtime.Games.Battleship.Core;
using Runtime.Localization;
using Runtime.PlayerProfile;
using Runtime.Games.TicTacToe.Ultimate.UI;
using Runtime.Localization.Types;

namespace Runtime.Gameplay.Startup
{
    internal sealed class GameplayStartupUiCoordinator
    {
        private readonly GameplayStartupDependencies _dependencies;
        private readonly GameplayStartupRuntimeState _state;

        private GameplayStartupCoreServices Core => _dependencies.Core;
        private GameplayStartupTimerServices Timers => _dependencies.Timers;
        private GameplayStartupBotServices Bot => _dependencies.Bot;
        private GameplayStartupOnlineServices Online => _dependencies.Online;
        private GameplayStartupBattleshipServices Battleship => _dependencies.Battleship;
        private GameplayStartupUiState UiState => _state.Ui;
        private GameplayStartupBattleshipState BattleshipState => _state.Battleship;

        public GameplayStartupUiCoordinator(GameplayStartupDependencies dependencies, GameplayStartupRuntimeState state)
        {
            _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
            _state = state ?? throw new ArgumentNullException(nameof(state));
        }

        internal void CreateResultViewModel()
        {
            UiState.ResultViewModel?.Dispose();

            var container = Core.FieldUiAdapter.FieldContainer;
            
            if (container == null)
                return;

            UiState.ResultViewModel = new GameResultViewModel(container, Core.Localization);
        }

        internal void SubscribeScoreboardPlayerNames()
        {
            if (Core.MatchPlayerNames == null || UiState.Subscriptions == null)
                return;

            var player1NameLabel = Core.FieldUiAdapter.Player1NameLabel;
            var player2NameLabel = Core.FieldUiAdapter.Player2NameLabel;

            if (player1NameLabel == null || player2NameLabel == null)
                return;

            var xMark = PlayerMark.X.ToUiText();
            var oMark = PlayerMark.O.ToUiText();

            Core.MatchPlayerNames.GetSlotName(PlayerSlot.Slot1)
                .Subscribe(name => player1NameLabel.text = PlayerLabelFormat.NameWithMark(name, xMark))
                .AddTo(UiState.Subscriptions);

            Core.MatchPlayerNames.GetSlotName(PlayerSlot.Slot2)
                .Subscribe(name => player2NameLabel.text = PlayerLabelFormat.NameWithMark(name, oMark))
                .AddTo(UiState.Subscriptions);
        }

        internal void UpdateScoreLabels()
        {
            var score = Core.SeriesService.Score.CurrentValue;
            var p1Label = Core.FieldUiAdapter.Player1ScoreLabel;
            var p2Label = Core.FieldUiAdapter.Player2ScoreLabel;
            var drawsLabel = Core.FieldUiAdapter.DrawsScoreLabel;

            if (p1Label != null)
                p1Label.text = score.Player1Wins.ToString();

            if (p2Label != null)
                p2Label.text = score.Player2Wins.ToString();

            if (drawsLabel != null)
                drawsLabel.text = $"D:{score.Draws}";
        }

        internal void SetRoundFinishedVisualState(bool finished)
        {
            var container = Core.FieldUiAdapter.FieldContainer;
            
            if (container == null)
                return;

            const string cls = "field-container--round-finished";
            
            if (finished)
                container.AddToClassList(cls);
            else
                container.RemoveFromClassList(cls);
        }

        internal void BindUltimateUiIfNeeded()
        {
            if (UiState.FieldSpec == null || UiState.FieldSpec.Kind != FieldKind.Ultimate)
                return;

            if (Bot.UltimateSnapshotProvider == null)
                return;

            if (Core.FieldUiAdapter is not IUltimateGameplayFieldUiAdapter ultimateUi)
                return;

            if (Bot.UltimateEventStream == null)
                return;

            UiState.UltimateAllowedBinder ??= new UltimateAllowedBinder(
                ultimateUi,
                Bot.UltimateEventStream,
                Bot.UltimateSnapshotProvider);

            UiState.UltimateMiniBoardStatusBinder ??= new UltimateMiniBoardStatusBinder(
                ultimateUi,
                Bot.UltimateEventStream,
                Bot.UltimateSnapshotProvider);

            UiState.UltimateAllowedBinder.Bind();
            UiState.UltimateMiniBoardStatusBinder.Bind();
        }

        internal void DisposeUltimateUiBinders()
        {
            UiState.UltimateAllowedBinder?.Dispose();
            UiState.UltimateMiniBoardStatusBinder?.Dispose();
            UiState.UltimateAllowedBinder = null;
            UiState.UltimateMiniBoardStatusBinder = null;
        }

        internal void BindOnlinePlayerNamesStoreIfNeeded()
        {
            var session = Online.OnlineSessionContextStore.Snapshot;
            
            if (!session.IsOnlineDirectInvite || UiState.OnlinePlayerNamesStoreBound || Online.OnlinePlayerNamesStore == null)
                return;

            Online.OnlineSessionLauncher.BindMatchPlayerNamesStore(Online.OnlinePlayerNamesStore);
            UiState.OnlinePlayerNamesStoreBound = true;
        }

        internal void UnbindOnlinePlayerNamesStoreIfNeeded()
        {
            if (!UiState.OnlinePlayerNamesStoreBound)
                return;

            if (Online.OnlinePlayerNamesStore != null)
                Online.OnlineSessionLauncher.UnbindMatchPlayerNamesStore(Online.OnlinePlayerNamesStore);

            UiState.OnlinePlayerNamesStoreBound = false;
        }

        internal void SyncBattleshipTimerHudBindings(Action updateMoveTimerStateForBattleshipBot)
        {
            if (!BattleshipState.IsBattleshipMatch)
                return;

            var phase = Battleship.BattleshipSnapshotProvider?.Phase ?? BattleshipPhase.Placement;
            var usePlacementTimer = phase == BattleshipPhase.Placement || phase == BattleshipPhase.Waiting;

            Timers.MoveTimerHudBinder?.Unbind();
            Timers.BattleshipPlacementTimerHudBinder?.Unbind();

            if (usePlacementTimer)
            {
                Timers.BattleshipPlacementTimerHudBinder?.Bind();
                return;
            }

            Timers.MoveTimerHudBinder?.Bind();
            updateMoveTimerStateForBattleshipBot();
        }

        internal string ResolveOpponentLeftResultText(OnlineErrorCode errorCode)
        {
            var key = errorCode == OnlineErrorCode.OpponentLeft
                ? "Errors.Online.OpponentLeft"
                : "Errors.Online.DisconnectTimeout";

            return Core.Localization == null ? key : Core.Localization.Resolve(TextTableId.Errors, key);
        }
    }
}