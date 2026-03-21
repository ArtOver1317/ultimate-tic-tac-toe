#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NSubstitute;
using NUnit.Framework;
using R3;
using Runtime.GameModes.Wizard.Configs;
using Runtime.GameModes.Wizard.Modes;
using Runtime.GameModes.Wizard.Online;
using Runtime.Gameplay;
using Runtime.Gameplay.ECS.Lifecycle;
using Runtime.Gameplay.Shared;
using Runtime.Gameplay.Startup;
using Runtime.Games.Battleship.AI;
using Runtime.Games.Battleship.Core;
using Runtime.Games.Battleship.Networking;
using Runtime.Games.Battleship.Placement;
using Runtime.Games.Battleship.Startup;
using Runtime.Games.Battleship.UI.Board;
using Runtime.Games.Battleship.UI.Placement;
using Runtime.Games.TicTacToe;
using Runtime.Games.TicTacToe.AI.Core;
using Runtime.Games.TicTacToe.AI.Ultimate.Core;
using Runtime.Games.TicTacToe.Series;
using Runtime.Infrastructure.GameStateMachine;
using Runtime.PlayerStatistics;
using UnityEngine.UIElements;
using EcsGameStatus = Runtime.Gameplay.Shared.EcsGameStatus;

namespace Tests.EditMode.Games.Battleship.Fakes
{
    internal sealed class FakeMoveTimerService : IMoveTimerService
    {
        private readonly ReactiveProperty<float> _remainingSeconds = new(0f);
        private readonly ReactiveProperty<bool> _isActive = new(false);

        public int RestoreCallCount { get; private set; }
        public float LastRestoreRemainingSeconds { get; private set; }
        public int LastRestoreActivePlayerSlot { get; private set; } = -1;

        public ReadOnlyReactiveProperty<float> RemainingSeconds => _remainingSeconds;
        public ReadOnlyReactiveProperty<bool> IsActive => _isActive;

        public void StartOrResetForPlayer(int playerSlot) { }

        public void RestoreRemainingSeconds(float remainingSeconds, int activePlayerSlot)
        {
            RestoreCallCount++;
            LastRestoreRemainingSeconds = remainingSeconds;
            LastRestoreActivePlayerSlot = activePlayerSlot;
            _remainingSeconds.Value = remainingSeconds;
            _isActive.Value = true;
        }

        public void Stop() => _isActive.Value = false;

        public void Freeze() { }

        public void Unfreeze() { }

        public void Dispose()
        {
            _remainingSeconds.Dispose();
            _isActive.Dispose();
        }

        public void SetState(bool isActive, float remainingSeconds)
        {
            _isActive.Value = isActive;
            _remainingSeconds.Value = remainingSeconds;
        }
    }

    internal sealed class FakePlacementTimerService : IBattleshipPlacementTimerService
    {
        private readonly ReactiveProperty<float> _remainingSeconds = new(0f);
        private readonly ReactiveProperty<bool> _isActive = new(false);

        public int RestoreCallCount { get; private set; }
        public float LastRestoreRemainingSeconds { get; private set; }

        public ReadOnlyReactiveProperty<float> RemainingSeconds => _remainingSeconds;
        public ReadOnlyReactiveProperty<bool> IsActive => _isActive;

        public void SyncFromSnapshot() { }

        public void RestoreRemainingSeconds(float remainingSeconds)
        {
            RestoreCallCount++;
            LastRestoreRemainingSeconds = remainingSeconds;
            _remainingSeconds.Value = remainingSeconds;
            _isActive.Value = true;
        }

        public void Stop() => _isActive.Value = false;

        public void Freeze() { }

        public void Unfreeze() { }

        public void Dispose()
        {
            _remainingSeconds.Dispose();
            _isActive.Dispose();
        }
    }

    internal sealed class CapturingRecoveryStateApplier : IBattleshipRecoveryStateApplier
    {
        public bool ShouldApply { get; set; } = true;
        public int CallCount { get; private set; }
        public BattleshipRecoveryState? LastState { get; private set; }

        public bool TryApplyRecoveryState(in BattleshipRecoveryState state)
        {
            CallCount++;
            LastState = state;
            return ShouldApply;
        }
    }

    internal static class GameplayStartupBattleshipTestFactory
    {
        public static BattleshipRecoveryMessage CreateRecoveryMessage(
            string senderUserId,
            int matchRoundId,
            BattleshipPhase phase,
            int activePlayerSlot,
            long placementTimerRemainingMs,
            long moveTimerRemainingMs)
        {
            var serializer = new BattleshipLayoutSerializer();
            var validator = new BattleshipPlacementValidator();
            var autoPlacer = new BattleshipAutoPlacer(validator);
            var layoutPayload = serializer.Serialize(autoPlacer.Generate(24680));
            var marksPayload = new string('0', 100);

            return new BattleshipRecoveryMessage(
                Guid.NewGuid(),
                senderUserId,
                matchRoundId,
                (int)phase,
                activePlayerSlot,
                placementTimerRemainingMs,
                moveTimerRemainingMs,
                player0ConsecutiveTimeouts: 1,
                player1ConsecutiveTimeouts: 0,
                winnerSlot: -1,
                finishStatus: (int)EcsGameStatus.InProgress,
                clientTick: 321,
                player0LayoutPayload: layoutPayload,
                player1LayoutPayload: layoutPayload,
                player0OpponentMarksPayload: marksPayload,
                player1OpponentMarksPayload: marksPayload);
        }

        public static async Task WaitUntilAsync(Func<bool> condition, int maxFrames = 20)
        {
            for (var frame = 0; frame < maxFrames; frame++)
            {
                if (condition())
                    return;

                await UniTask.DelayFrame(1);
            }

            Assert.Fail("Expected condition to become true within the allotted frames.");
        }

        public static GameplayStartupBattleshipTestContext CreateContext(
            bool isHost = true,
            int activePlayerSlot = PlayerSlotMapping.SlotO,
            GameLaunchConfig? launchConfig = null,
            ISeriesService? seriesService = null,
            FakeMoveTimerService? moveTimerService = null,
            FakePlacementTimerService? placementTimerService = null,
            CapturingRecoveryStateApplier? recoveryStateApplier = null)
        {
            var config = launchConfig ?? new GameLaunchConfig(
                BattleshipStrategy.DefaultGameId,
                new BattleshipConfig(placementTimeLimitSeconds: 30),
                new LocalHumanConfig(),
                moveTimeLimitSeconds: 30);

            var configStore = Substitute.For<IGameLaunchConfigStore>();
            
            configStore.TryConsume(out Arg.Any<GameLaunchConfig>()).Returns(callInfo =>
            {
                callInfo[0] = config;
                return true;
            });
           
            configStore.TryPeek(out Arg.Any<GameLaunchConfig>()).Returns(callInfo =>
            {
                callInfo[0] = config;
                return true;
            });

            var gameService = Substitute.For<IGameService>();
            var session = Substitute.For<IGameplaySession>();
            session.FieldRenderSpec.Returns(FieldRenderSpec.Classic(10));
           
            gameService.StartMatchAsync(Arg.Any<GameLaunchConfig>(), Arg.Any<CancellationToken>())
                .Returns(UniTask.FromResult(session));

            var fieldPresenter = Substitute.For<IGameplayFieldPresenter>();
           
            fieldPresenter.BindAsync(Arg.Any<FieldRenderSpec>(), Arg.Any<CancellationToken>(), Arg.Any<string>())
                .Returns(UniTask.CompletedTask);

            var fieldContainer = new VisualElement();
            var fieldUiAdapter = Substitute.For<IGameplayFieldUiAdapter>();
            fieldUiAdapter.CellClicks.Returns(new Subject<CellId>());
            fieldUiAdapter.CurrentPlayerLabel.Returns(new Label());
            fieldUiAdapter.FieldContainer.Returns(fieldContainer);
            fieldUiAdapter.Player1Panel.Returns(new VisualElement());
            fieldUiAdapter.Player2Panel.Returns(new VisualElement());
            fieldUiAdapter.Player1ScoreLabel.Returns(new Label());
            fieldUiAdapter.Player1NameLabel.Returns(new Label());
            fieldUiAdapter.Player2ScoreLabel.Returns(new Label());
            fieldUiAdapter.Player2NameLabel.Returns(new Label());
            fieldUiAdapter.DrawsScoreLabel.Returns(new Label());
            fieldUiAdapter.MoveTimerLabel.Returns(new Label());

            var eventStream = Substitute.For<IGameplayEventStream>();
            var cellChanged = new Subject<CellChangedEvent>();
            var lastMoveChanged = new Subject<LastMoveChangedEvent>();
            var currentPlayerChanged = new Subject<CurrentPlayerChangedEvent>();
            var commandRejected = new Subject<CommandRejectedEvent>();
            var roundFinished = new Subject<RoundFinishedEvent>();
            eventStream.CellChanged.Returns(cellChanged);
            eventStream.LastMoveChanged.Returns(lastMoveChanged);
            eventStream.CurrentPlayerChanged.Returns(currentPlayerChanged);
            eventStream.CommandRejected.Returns(commandRejected);
            eventStream.RoundFinished.Returns(roundFinished);

            var commandSink = Substitute.For<IGameplayCommandSink>();
            var snapshotProvider = Substitute.For<IGameplaySnapshotProvider>();
            snapshotProvider.GetAllCells().Returns(new List<CellSnapshot>());

            var ecsLifecycle = Substitute.For<IMatchEcsLifecycle>();
            ecsLifecycle.IsActive.Returns(true);

            var stateMachine = Substitute.For<IGameStateMachine>();
           
            if (seriesService == null)
            {
                var seriesServiceSubstitute = Substitute.For<ISeriesService>();
                seriesServiceSubstitute.Score.Returns(new ReactiveProperty<SeriesScore>(default));
                seriesService = seriesServiceSubstitute;
            }

            var backHandler = Substitute.For<IGameplayBackHandler>();
            backHandler.HandleBackAsync(Arg.Any<CancellationToken>()).Returns(UniTask.CompletedTask);

            var botDriver = Substitute.For<IBotTurnDriver>();
            botDriver.IsBusy.Returns(new ReactiveProperty<bool>(false));
            botDriver.IsDisabled.Returns(new ReactiveProperty<bool>(false));

            var ultimateBot = Substitute.For<IBotTurnOrchestrator>();
            ultimateBot.IsThinking.Returns(new ReactiveProperty<bool>(false));
            ultimateBot.MoveFailed.Returns(new Subject<BotMoveFailedEvent>());

            var matchStateProvider = Substitute.For<IMatchStateProvider>();
            matchStateProvider.ActivePlayerSlot.Returns(activePlayerSlot);
            matchStateProvider.GetAllCells().Returns(new List<CellSnapshot>());

            var marks = new BattleshipCellMark[100];
           
            for (var i = 0; i < marks.Length; i++)
            {
                marks[i] = BattleshipCellMark.Unknown;
            }

            IReadOnlyList<BattleshipCellMark> marksView = Array.AsReadOnly(marks);

            var battleshipSnapshot = Substitute.For<IBattleshipGameplaySnapshotProvider>();
            battleshipSnapshot.Phase.Returns(BattleshipPhase.Battle);
            battleshipSnapshot.ActivePlayerSlot.Returns(activePlayerSlot);
            battleshipSnapshot.CurrentStatus.Returns(EcsGameStatus.InProgress);
            battleshipSnapshot.GetOpponentMarks(Arg.Any<int>()).Returns(marksView);

            var battleshipEvents = Substitute.For<IBattleshipGameplayEventStream>();
            battleshipEvents.PhaseChanged.Returns(new Subject<BattleshipPhaseChangedEvent>());
            battleshipEvents.MarksChanged.Returns(new Subject<BattleshipMarksChangedEvent>());

            var incomingMoves = new Subject<MoveCommand>();
            var incomingReady = new Subject<RoundReadySignal>();
            var incomingTimeout = new Subject<OnlineTimeoutSignal>();
            var incomingRecovery = new Subject<BattleshipRecoveryMessage>();
            var snapshot = new ReactiveProperty<GameplayNetworkSnapshot?>(null);
            long authoritativeTick = 0;

            var gameplayBridge = Substitute.For<IGameplayNetworkBridge>();
            gameplayBridge.Snapshot.Returns(snapshot);
            gameplayBridge.IncomingMoves.Returns(incomingMoves);
            gameplayBridge.IncomingRoundReadySignals.Returns(incomingReady);
            gameplayBridge.IncomingTimeoutSignals.Returns(incomingTimeout);
            gameplayBridge.BindAsync(Arg.Any<string>(), Arg.Any<bool>()).Returns(UniTask.CompletedTask);
          
            gameplayBridge.SubmitMoveAsync(Arg.Any<MoveCommand>()).Returns(callInfo =>
            {
                var command = callInfo.Arg<MoveCommand>();
                authoritativeTick++;
             
                snapshot.Value = new GameplayNetworkSnapshot(
                    matchRoundId: 1,
                    isCompleted: false,
                    winnerUserId: null,
                    authoritativeTick: authoritativeTick,
                    countdownTargetTick: command.ClientTick,
                    shotSequence: command.ClientTick);
            
                return UniTask.CompletedTask;
            });
           
            gameplayBridge.SubmitRoundReadyAsync(Arg.Any<RoundReadySignal>()).Returns(UniTask.CompletedTask);
            gameplayBridge.SubmitTimeoutAsync(Arg.Any<OnlineTimeoutSignal>()).Returns(UniTask.CompletedTask);

            var battleshipBridge = Substitute.For<IBattleshipNetworkBridge>();
            battleshipBridge.IncomingRecoverySnapshots.Returns(incomingRecovery);
            battleshipBridge.BindAsync(Arg.Any<string>(), Arg.Any<bool>()).Returns(UniTask.CompletedTask);
            battleshipBridge.SubmitRecoverySnapshotAsync(Arg.Any<BattleshipRecoveryMessage>()).Returns(UniTask.CompletedTask);

            var sessionStore = new OnlineGameplaySessionContextStore();
            sessionStore.SetDirectInviteSession("ABCDEF", isHost ? "host-user" : "guest-user", isHost);

            var movesBinder = CreateBattleshipMovesBinder(
                fieldUiAdapter,
                commandSink,
                eventStream,
                snapshotProvider,
                battleshipSnapshot,
                sessionStore);

            moveTimerService ??= new FakeMoveTimerService();
            placementTimerService ??= new FakePlacementTimerService();
            recoveryStateApplier ??= new CapturingRecoveryStateApplier();

            var statisticsReporter = CreateStatisticsReporter(configStore, eventStream);

            return new GameplayStartupBattleshipTestContext(
                configStore,
                gameService,
                fieldPresenter,
                fieldUiAdapter,
                ecsLifecycle,
                eventStream,
                commandSink,
                movesBinder,
                seriesService,
                backHandler,
                stateMachine,
                botDriver,
                ultimateBot,
                matchStateProvider,
                battleshipSnapshot,
                battleshipEvents,
                gameplayBridge,
                battleshipBridge,
                sessionStore,
                statisticsReporter,
                incomingMoves,
                incomingReady,
                roundFinished,
                fieldContainer,
                incomingRecovery,
                moveTimerService,
                placementTimerService,
                recoveryStateApplier);
        }

        public static PlayerStatisticsMatchReporter CreateStatisticsReporter(
            IGameLaunchConfigStore configStore,
            IGameplayEventStream eventStream)
        {
            var outcomeResolver = Substitute.For<IMatchOutcomeResolver>();
            var statisticsService = Substitute.For<IPlayerStatisticsService>();
            var contextStore = Substitute.For<IOnlineGameplaySessionContextStore>();
            contextStore.Snapshot.Returns(OnlineGameplaySessionSnapshot.Empty());

            return new PlayerStatisticsMatchReporter(
                configStore,
                eventStream,
                outcomeResolver,
                statisticsService,
                contextStore,
                new MatchKeyMapper());
        }

        public static BattleshipGameplayStartup CreateStartup(
            IGameLaunchConfigStore configStore,
            IGameService gameService,
            IGameplayFieldPresenter fieldPresenter,
            IGameplayFieldUiAdapter fieldUiAdapter,
            IMatchEcsLifecycle ecsLifecycle,
            IGameplayEventStream eventStream,
            IGameplayCommandSink commandSink,
            GameplayMovesBinder movesBinder,
            ISeriesService seriesService,
            IGameplayBackHandler backHandler,
            IGameStateMachine stateMachine,
            IBotTurnDriver botDriver,
            IBotTurnOrchestrator ultimateBotOrchestrator,
            IMatchFailSafeGateway matchFailSafeGateway,
            IGameplayNetworkBridge? networkBridge = null,
            IBattleshipNetworkBridge? battleshipNetworkBridge = null,
            IOnlineGameplaySessionContextStore? onlineSessionContextStore = null,
            IMatchStateProvider? matchStateProvider = null,
            IOnlineSessionFlowService? onlineSessionFlow = null,
            IMoveTimerService? moveTimerService = null,
            IBattleshipPlacementTimerService? battleshipPlacementTimerService = null,
            MoveTimerHudBinder? moveTimerHudBinder = null,
            BattleshipPlacementTimerHudBinder? battleshipPlacementTimerHudBinder = null,
            IBattleshipGameplaySnapshotProvider? battleshipSnapshotProvider = null,
            IBattleshipGameplayEventStream? battleshipEventStream = null,
            IBattleshipRecoveryStateApplier? battleshipRecoveryStateApplier = null,
            PlayerStatisticsMatchReporter? statisticsReporter = null,
            IBattleshipBotDriver? battleshipBotDriver = null,
            IBattleshipPlacementUiController? battleshipPlacementUiController = null,
            BattleshipBoardsBinder? battleshipBoardsBinder = null)
        {
            var resolvedMatchStateProvider = matchStateProvider ?? commandSink as IMatchStateProvider;
          
            var core = new GameplayStartupCoreServices(
                configStore,
                gameService,
                fieldPresenter,
                fieldUiAdapter,
                ecsLifecycle,
                eventStream,
                commandSink,
                movesBinder,
                new WinLineRenderer(fieldUiAdapter),
                seriesService,
                backHandler,
                stateMachine,
                statisticsReporter: statisticsReporter);
            
            var timers = new GameplayStartupTimerServices(
                moveTimerService ?? new FakeMoveTimerService(),
                battleshipPlacementTimerService ?? new FakePlacementTimerService(),
                moveTimerHudBinder,
                battleshipPlacementTimerHudBinder);
            
            var bot = new GameplayStartupBotServices(
                botDriver,
                battleshipBotDriver,
                ultimateBotOrchestrator,
                matchFailSafeGateway);
           
            var online = new GameplayStartupOnlineServices(
                networkBridge ?? new NoOpGameplayNetworkBridge(),
                battleshipNetworkBridge ?? NoOpBattleshipNetworkBridge.Instance,
                onlineSessionContextStore ?? new OnlineGameplaySessionContextStore(),
                onlineSessionFlow ?? NoOpOnlineSessionFlowService.Instance,
                NoOpOnlineSessionLauncher.Instance,
                matchStateProvider: resolvedMatchStateProvider);
            
            var battleship = new GameplayStartupBattleshipServices(
                new BattleshipLayoutSerializer(),
                battleshipBoardsBinder,
                battleshipPlacementUiController,
                battleshipSnapshotProvider ?? resolvedMatchStateProvider as IBattleshipGameplaySnapshotProvider,
                battleshipEventStream,
                battleshipRecoveryStateApplier ?? resolvedMatchStateProvider as IBattleshipRecoveryStateApplier);
            
            var dependencies = new GameplayStartupDependencies(core, timers, bot, online, battleship);
            var state = new GameplayStartupRuntimeState();
            var uiCoordinator = new GameplayStartupUiCoordinator(dependencies, state);
            var botCoordinator = new GameplayStartupBotCoordinator(dependencies, state);
            var sessionScoreStore = new GameplayStartupBattleshipSessionScoreStore();
          
            var recoveryCoordinator = new GameplayStartupBattleshipRecoveryCoordinator(
                dependencies,
                state,
                uiCoordinator,
                botCoordinator,
                sessionScoreStore);
            
            var onlineCoordinator = new GameplayStartupOnlineCoordinator(
                dependencies,
                state,
                uiCoordinator,
                recoveryCoordinator,
                sessionScoreStore);
           
            var roundCoordinator = new GameplayStartupRoundCoordinator(
                dependencies,
                state,
                uiCoordinator,
                sessionScoreStore);

            return new BattleshipGameplayStartup(
                dependencies,
                state,
                uiCoordinator,
                botCoordinator,
                sessionScoreStore,
                onlineCoordinator,
                roundCoordinator);
        }

        public static GameplayMovesBinder CreateBattleshipMovesBinder(
            IGameplayFieldUiAdapter fieldUiAdapter,
            IGameplayCommandSink commandSink,
            IGameplayEventStream eventStream,
            IGameplaySnapshotProvider snapshotProvider,
            IBattleshipGameplaySnapshotProvider battleshipSnapshotProvider,
            IOnlineGameplaySessionContextStore? sessionStore = null)
        {
            sessionStore ??= new OnlineGameplaySessionContextStore();

            return new GameplayMovesBinder(
                fieldUiAdapter,
                commandSink,
                eventStream,
                snapshotProvider,
                new BattleshipGameplayMovesModeBehavior(battleshipSnapshotProvider, sessionStore));
        }
    }

    internal sealed class GameplayStartupBattleshipTestContext : IDisposable
    {
        private readonly IGameLaunchConfigStore _configStore;
        private readonly IGameService _gameService;
        private readonly IGameplayFieldPresenter _fieldPresenter;
        private readonly IGameplayFieldUiAdapter _fieldUiAdapter;
        private readonly IMatchEcsLifecycle _ecsLifecycle;
        private readonly IGameplayEventStream _eventStream;
        private readonly IGameplayCommandSink _commandSink;
        private readonly GameplayMovesBinder _movesBinder;
        private readonly ISeriesService _seriesService;
        private readonly IGameplayBackHandler _backHandler;
        private readonly IGameStateMachine _stateMachine;
        private readonly IBotTurnDriver _botDriver;
        private readonly IBotTurnOrchestrator _ultimateBot;
        private readonly IMatchStateProvider _matchStateProvider;
        private readonly IBattleshipGameplaySnapshotProvider _battleshipSnapshot;
        private readonly IBattleshipGameplayEventStream _battleshipEvents;
        private readonly IGameplayNetworkBridge _gameplayBridge;
        private readonly IBattleshipNetworkBridge _battleshipBridge;
        private readonly OnlineGameplaySessionContextStore _sessionStore;
        private readonly PlayerStatisticsMatchReporter _statisticsReporter;
        private readonly FakeMoveTimerService _moveTimerService;
        private readonly FakePlacementTimerService _placementTimerService;
        private readonly CapturingRecoveryStateApplier _recoveryStateApplier;

        public GameplayStartupBattleshipTestContext(
            IGameLaunchConfigStore configStore,
            IGameService gameService,
            IGameplayFieldPresenter fieldPresenter,
            IGameplayFieldUiAdapter fieldUiAdapter,
            IMatchEcsLifecycle ecsLifecycle,
            IGameplayEventStream eventStream,
            IGameplayCommandSink commandSink,
            GameplayMovesBinder movesBinder,
            ISeriesService seriesService,
            IGameplayBackHandler backHandler,
            IGameStateMachine stateMachine,
            IBotTurnDriver botDriver,
            IBotTurnOrchestrator ultimateBot,
            IMatchStateProvider matchStateProvider,
            IBattleshipGameplaySnapshotProvider battleshipSnapshot,
            IBattleshipGameplayEventStream battleshipEvents,
            IGameplayNetworkBridge gameplayBridge,
            IBattleshipNetworkBridge battleshipBridge,
            OnlineGameplaySessionContextStore sessionStore,
            PlayerStatisticsMatchReporter statisticsReporter,
            Subject<MoveCommand> incomingMoves,
            Subject<RoundReadySignal> incomingReadySignals,
            Subject<RoundFinishedEvent> roundFinishedEvents,
            VisualElement fieldContainer,
            Subject<BattleshipRecoveryMessage> incomingRecoverySnapshots,
            FakeMoveTimerService moveTimerService,
            FakePlacementTimerService placementTimerService,
            CapturingRecoveryStateApplier recoveryStateApplier)
        {
            _configStore = configStore;
            _gameService = gameService;
            _fieldPresenter = fieldPresenter;
            _fieldUiAdapter = fieldUiAdapter;
            _ecsLifecycle = ecsLifecycle;
            _eventStream = eventStream;
            _commandSink = commandSink;
            _movesBinder = movesBinder;
            _seriesService = seriesService;
            _backHandler = backHandler;
            _stateMachine = stateMachine;
            _botDriver = botDriver;
            _ultimateBot = ultimateBot;
            _matchStateProvider = matchStateProvider;
            _battleshipSnapshot = battleshipSnapshot;
            _battleshipEvents = battleshipEvents;
            _gameplayBridge = gameplayBridge;
            _battleshipBridge = battleshipBridge;
            _sessionStore = sessionStore;
            _statisticsReporter = statisticsReporter;
            _moveTimerService = moveTimerService;
            _placementTimerService = placementTimerService;
            _recoveryStateApplier = recoveryStateApplier;
            IncomingMoves = incomingMoves;
            IncomingReadySignals = incomingReadySignals;
            RoundFinishedEvents = roundFinishedEvents;
            FieldContainer = fieldContainer;
            IncomingRecoverySnapshots = incomingRecoverySnapshots;
        }

        public Subject<MoveCommand> IncomingMoves { get; }
        public Subject<RoundReadySignal> IncomingReadySignals { get; }
        public Subject<RoundFinishedEvent> RoundFinishedEvents { get; }
        public VisualElement FieldContainer { get; }
        public Subject<BattleshipRecoveryMessage> IncomingRecoverySnapshots { get; }
        public IMatchStateProvider MatchStateProvider => _matchStateProvider;
        public IGameplayNetworkBridge GameplayBridge => _gameplayBridge;
        public IBattleshipNetworkBridge BattleshipBridge => _battleshipBridge;
        public IGameStateMachine StateMachine => _stateMachine;
        public FakeMoveTimerService MoveTimerService => _moveTimerService;
        public FakePlacementTimerService PlacementTimerService => _placementTimerService;
        public CapturingRecoveryStateApplier RecoveryStateApplier => _recoveryStateApplier;

        public void Dispose()
        {
            IncomingMoves.Dispose();
            IncomingReadySignals.Dispose();
            RoundFinishedEvents.Dispose();
            IncomingRecoverySnapshots.Dispose();
        }

        public BattleshipGameplayStartup CreateSut() => GameplayStartupBattleshipTestFactory.CreateStartup(
            _configStore,
            _gameService,
            _fieldPresenter,
            _fieldUiAdapter,
            _ecsLifecycle,
            _eventStream,
            _commandSink,
            _movesBinder,
            _seriesService,
            _backHandler,
            _stateMachine,
            _botDriver,
            _ultimateBot,
            Substitute.For<IMatchFailSafeGateway>(),
            networkBridge: _gameplayBridge,
            battleshipNetworkBridge: _battleshipBridge,
            onlineSessionContextStore: _sessionStore,
            matchStateProvider: _matchStateProvider,
            moveTimerService: _moveTimerService,
            battleshipPlacementTimerService: _placementTimerService,
            battleshipSnapshotProvider: _battleshipSnapshot,
            battleshipEventStream: _battleshipEvents,
            battleshipRecoveryStateApplier: _recoveryStateApplier,
            statisticsReporter: _statisticsReporter);
    }
}