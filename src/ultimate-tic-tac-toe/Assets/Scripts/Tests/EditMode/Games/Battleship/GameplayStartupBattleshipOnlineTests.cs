#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using R3;
using Runtime.GameModes.Wizard;
using Runtime.Gameplay;
using Runtime.Gameplay.ECS;
using Runtime.Games.Battleship;
using Runtime.Games.TicTacToe;
using Runtime.Games.TicTacToe.AI;
using Runtime.Games.TicTacToe.AI.Ultimate;
using Runtime.Games.TicTacToe.Moves;
using Runtime.Games.TicTacToe.Series;
using Runtime.Infrastructure.GameStateMachine;
using Runtime.PlayerStatistics;
using UnityEngine.UIElements;

namespace Tests.EditMode.Games.Battleship
{
    [TestFixture]
    [Category("Unit")]
    public sealed class GameplayStartupBattleshipOnlineTests
    {
        private sealed class FakeMoveTimerService : IMoveTimerService
        {
            private readonly ReactiveProperty<float> _remainingSeconds = new(0f);
            private readonly ReactiveProperty<bool> _isActive = new(false);

            public ReadOnlyReactiveProperty<float> RemainingSeconds => _remainingSeconds;
            public ReadOnlyReactiveProperty<bool> IsActive => _isActive;

            public void StartOrResetForPlayer(int playerSlot) { }
            public void RestoreRemainingSeconds(float remainingSeconds, int activePlayerSlot) { }
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

        [Test]
        public async Task WhenHostReceivesGuestShotSequenceWithGap_ThenRejectsSecondMove()
        {
            var context = CreateContext();
            using var sut = context.CreateSut();

            await sut.StartAsync(CancellationToken.None);

            context.IncomingMoves.OnNext(new MoveCommand(Guid.NewGuid(), "guest-user", cellIndex: 0, clientTick: 1));
            await UniTask.DelayFrame(1);

            context.IncomingMoves.OnNext(new MoveCommand(Guid.NewGuid(), "guest-user", cellIndex: 1, clientTick: 3));
            await UniTask.DelayFrame(1);

            context.MatchStateProvider.Received(1)
                .SubmitCommand(Arg.Any<IGameplayCommand>());
        }

        [Test]
        public async Task WhenHostReceivesGuestShotSequenceStrictlyIncreasingByOne_ThenAcceptsBothMoves()
        {
            var context = CreateContext();
            using var sut = context.CreateSut();

            await sut.StartAsync(CancellationToken.None);

            context.IncomingMoves.OnNext(new MoveCommand(Guid.NewGuid(), "guest-user", cellIndex: 0, clientTick: 1));
            await UniTask.DelayFrame(1);

            context.IncomingMoves.OnNext(new MoveCommand(Guid.NewGuid(), "guest-user", cellIndex: 1, clientTick: 2));
            await UniTask.DelayFrame(1);

            context.MatchStateProvider.Received(2)
                .SubmitCommand(Arg.Any<IGameplayCommand>());
        }

        [Test]
        public async Task WhenBattleshipBotTurnStarts_ThenMoveTimerHudIsHidden()
        {
            var config = new GameLaunchConfig(
                BattleshipStrategy.DefaultGameId,
                new BattleshipConfig(placementTimeLimitSeconds: 30),
                new BotOpponentConfig(BattleshipStrategy.DefaultBotDifficultyId),
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

            var moveTimerLabel = new Label();
            var fieldUiAdapter = Substitute.For<IGameplayFieldUiAdapter>();
            fieldUiAdapter.CellClicks.Returns(new Subject<CellId>());
            fieldUiAdapter.CurrentPlayerLabel.Returns(new Label());
            fieldUiAdapter.FieldContainer.Returns(new VisualElement());
            fieldUiAdapter.Player1Panel.Returns(new VisualElement());
            fieldUiAdapter.Player2Panel.Returns(new VisualElement());
            fieldUiAdapter.Player1ScoreLabel.Returns(new Label());
            fieldUiAdapter.Player2ScoreLabel.Returns(new Label());
            fieldUiAdapter.MoveTimerLabel.Returns(moveTimerLabel);

            var eventStream = Substitute.For<IGameplayEventStream>();
            eventStream.CellChanged.Returns(new Subject<CellChangedEvent>());
            eventStream.LastMoveChanged.Returns(new Subject<LastMoveChangedEvent>());
            eventStream.CurrentPlayerChanged.Returns(new Subject<CurrentPlayerChangedEvent>());
            eventStream.CommandRejected.Returns(new Subject<CommandRejectedEvent>());
            eventStream.RoundFinished.Returns(new Subject<RoundFinishedEvent>());

            var commandSink = Substitute.For<IGameplayCommandSink>();
            var snapshotProvider = Substitute.For<IGameplaySnapshotProvider>();
            snapshotProvider.GetAllCells().Returns(new List<CellSnapshot>());
            var movesBinder = new GameplayMovesBinder(fieldUiAdapter, commandSink, eventStream, snapshotProvider);

            var ecsLifecycle = Substitute.For<IMatchEcsLifecycle>();
            ecsLifecycle.IsActive.Returns(true);

            var seriesService = Substitute.For<ISeriesService>();
            seriesService.Score.Returns(new ReactiveProperty<SeriesScore>(default));

            var moveTimerService = new FakeMoveTimerService();
            moveTimerService.SetState(isActive: true, remainingSeconds: 20f);
            using var moveTimerHudViewModel = new MoveTimerHudViewModel(moveTimerService);
            using var moveTimerHudBinder = new MoveTimerHudBinder(fieldUiAdapter, moveTimerHudViewModel);

            var battleshipBotDriver = Substitute.For<IBattleshipBotDriver>();
            battleshipBotDriver.IsThinking.Returns(new ReactiveProperty<bool>(false));
            battleshipBotDriver.StartAsync(Arg.Any<GameLaunchConfig>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(UniTask.FromResult(new BotStartResult(BotStartStatus.Started)));

            var battleshipSnapshot = Substitute.For<IBattleshipGameplaySnapshotProvider>();
            battleshipSnapshot.Phase.Returns(BattleshipPhase.Battle);
            battleshipSnapshot.ActivePlayerSlot.Returns(PlayerSlotMapping.SlotO);
            battleshipSnapshot.CurrentStatus.Returns(GameStatus.InProgress);

            var battleshipEvents = Substitute.For<IBattleshipGameplayEventStream>();
            battleshipEvents.PhaseChanged.Returns(new Subject<BattleshipPhaseChangedEvent>());
            battleshipEvents.MarksChanged.Returns(new Subject<BattleshipMarksChangedEvent>());

            var statisticsReporter = CreateStatisticsReporter(configStore, eventStream);

            using var sut = new GameplayStartup(
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
                Substitute.For<IGameplayBackHandler>(),
                Substitute.For<IGameStateMachine>(),
                Substitute.For<IBotTurnDriver>(),
                Substitute.For<IBotTurnOrchestrator>(),
                Substitute.For<IMatchFailSafeGateway>(),
                matchStateProvider: Substitute.For<IMatchStateProvider>(),
                moveTimerService: moveTimerService,
                moveTimerHudBinder: moveTimerHudBinder,
                battleshipSnapshotProvider: battleshipSnapshot,
                battleshipEventStream: battleshipEvents,
                battleshipBotDriver: battleshipBotDriver,
                statisticsReporter: statisticsReporter);

            await sut.StartAsync(CancellationToken.None);

            moveTimerLabel.style.display.value.Should().Be(DisplayStyle.None);
        }

        [Test]
        public async Task WhenBattleshipOnlineStartCancelledDuringNetworkBind_ThenCleansUpBoundResources()
        {
            var config = new GameLaunchConfig(
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
            fieldUiAdapter.Player2ScoreLabel.Returns(new Label());

            var eventStream = Substitute.For<IGameplayEventStream>();
            eventStream.CellChanged.Returns(new Subject<CellChangedEvent>());
            eventStream.LastMoveChanged.Returns(new Subject<LastMoveChangedEvent>());
            eventStream.CurrentPlayerChanged.Returns(new Subject<CurrentPlayerChangedEvent>());
            eventStream.CommandRejected.Returns(new Subject<CommandRejectedEvent>());
            eventStream.RoundFinished.Returns(new Subject<RoundFinishedEvent>());

            var commandSink = Substitute.For<IGameplayCommandSink>();
            var snapshotProvider = Substitute.For<IGameplaySnapshotProvider>();
            snapshotProvider.GetAllCells().Returns(new List<CellSnapshot>());
            var movesBinder = new GameplayMovesBinder(fieldUiAdapter, commandSink, eventStream, snapshotProvider);

            var ecsLifecycle = Substitute.For<IMatchEcsLifecycle>();
            var stateMachine = Substitute.For<IGameStateMachine>();
            var seriesService = Substitute.For<ISeriesService>();
            seriesService.Score.Returns(new ReactiveProperty<SeriesScore>(default));
            var backHandler = Substitute.For<IGameplayBackHandler>();
            backHandler.HandleBackAsync(Arg.Any<CancellationToken>()).Returns(UniTask.CompletedTask);

            var botDriver = Substitute.For<IBotTurnDriver>();
            botDriver.IsBusy.Returns(new ReactiveProperty<bool>(false));
            botDriver.IsDisabled.Returns(new ReactiveProperty<bool>(false));

            var battleshipBotDriver = Substitute.For<IBattleshipBotDriver>();
            battleshipBotDriver.IsThinking.Returns(new ReactiveProperty<bool>(false));

            var ultimateBot = Substitute.For<IBotTurnOrchestrator>();
            ultimateBot.IsThinking.Returns(new ReactiveProperty<bool>(false));
            ultimateBot.MoveFailed.Returns(new Subject<BotMoveFailedEvent>());

            var matchStateProvider = Substitute.For<IMatchStateProvider>();
            matchStateProvider.ActivePlayerSlot.Returns(PlayerSlotMapping.SlotX);

            var gameplayBridge = Substitute.For<IGameplayNetworkBridge>();
            gameplayBridge.Snapshot.Returns(new ReactiveProperty<GameplayNetworkSnapshot?>(null));
            gameplayBridge.IncomingMoves.Returns(new Subject<MoveCommand>());
            gameplayBridge.IncomingRoundReadySignals.Returns(new Subject<RoundReadySignal>());
            gameplayBridge.IncomingTimeoutSignals.Returns(new Subject<OnlineTimeoutSignal>());
            gameplayBridge.BindAsync(Arg.Any<string>(), Arg.Any<bool>())
                .Returns(UniTask.FromException(new OperationCanceledException("bind canceled")));
            gameplayBridge.UnbindAsync().Returns(UniTask.CompletedTask);

            var battleshipBridge = Substitute.For<IBattleshipNetworkBridge>();
            battleshipBridge.IncomingRecoverySnapshots.Returns(new Subject<BattleshipRecoveryMessage>());
            battleshipBridge.BindAsync(Arg.Any<string>(), Arg.Any<bool>()).Returns(UniTask.CompletedTask);
            battleshipBridge.UnbindAsync().Returns(UniTask.CompletedTask);

            var moveTimerService = Substitute.For<IMoveTimerService>();
            moveTimerService.RemainingSeconds.Returns(new ReactiveProperty<float>(0f));
            moveTimerService.IsActive.Returns(new ReactiveProperty<bool>(false));

            var placementTimerService = Substitute.For<IBattleshipPlacementTimerService>();
            placementTimerService.RemainingSeconds.Returns(new ReactiveProperty<float>(0f));
            placementTimerService.IsActive.Returns(new ReactiveProperty<bool>(false));

            var placementUiController = Substitute.For<IBattleshipPlacementUiController>();

            var onlineSessionStore = new OnlineGameplaySessionContextStore();
            onlineSessionStore.SetDirectInviteSession("ABCDEF", "host-user", isHost: true);

            var statisticsReporter = CreateStatisticsReporter(configStore, eventStream);

            using var sut = new GameplayStartup(
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
                botDriver,
                ultimateBot,
                Substitute.For<IMatchFailSafeGateway>(),
                networkBridge: gameplayBridge,
                battleshipNetworkBridge: battleshipBridge,
                onlineSessionContextStore: onlineSessionStore,
                matchStateProvider: matchStateProvider,
                moveTimerService: moveTimerService,
                battleshipPlacementTimerService: placementTimerService,
                battleshipBotDriver: battleshipBotDriver,
                battleshipPlacementUiController: placementUiController,
                statisticsReporter: statisticsReporter);

            Func<Task> act = async () => await sut.StartAsync(CancellationToken.None);

            await act.Should().ThrowAsync<OperationCanceledException>();

            session.Received(1).Dispose();
            placementUiController.Received(1).Bind();
            placementUiController.Received(1).Unbind();
            placementTimerService.Received(1).SyncFromSnapshot();
            placementTimerService.Received(1).Stop();
            moveTimerService.Received(1).Stop();
            battleshipBotDriver.Received(1).Dispose();
            await gameplayBridge.Received(1).UnbindAsync();
            await battleshipBridge.Received(1).UnbindAsync();
            ecsLifecycle.Received(1).StopMatch();
            fieldPresenter.Received(1).Unbind();
        }

        private static TestContext CreateContext()
        {
            var config = new GameLaunchConfig(
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
            fieldUiAdapter.Player2ScoreLabel.Returns(new Label());

            var eventStream = Substitute.For<IGameplayEventStream>();
            eventStream.CellChanged.Returns(new Subject<CellChangedEvent>());
            eventStream.LastMoveChanged.Returns(new Subject<LastMoveChangedEvent>());
            eventStream.CurrentPlayerChanged.Returns(new Subject<CurrentPlayerChangedEvent>());
            eventStream.CommandRejected.Returns(new Subject<CommandRejectedEvent>());
            eventStream.RoundFinished.Returns(new Subject<RoundFinishedEvent>());

            var commandSink = Substitute.For<IGameplayCommandSink>();
            var snapshotProvider = Substitute.For<IGameplaySnapshotProvider>();
            snapshotProvider.GetAllCells().Returns(new List<CellSnapshot>());
            var movesBinder = new GameplayMovesBinder(fieldUiAdapter, commandSink, eventStream, snapshotProvider);

            var ecsLifecycle = Substitute.For<IMatchEcsLifecycle>();
            ecsLifecycle.IsActive.Returns(true);

            var stateMachine = Substitute.For<IGameStateMachine>();
            var seriesService = Substitute.For<ISeriesService>();
            seriesService.Score.Returns(new ReactiveProperty<SeriesScore>(default));
            var backHandler = Substitute.For<IGameplayBackHandler>();
            backHandler.HandleBackAsync(Arg.Any<CancellationToken>()).Returns(UniTask.CompletedTask);

            var botDriver = Substitute.For<IBotTurnDriver>();
            botDriver.IsBusy.Returns(new ReactiveProperty<bool>(false));
            botDriver.IsDisabled.Returns(new ReactiveProperty<bool>(false));
            var ultimateBot = Substitute.For<IBotTurnOrchestrator>();
            ultimateBot.IsThinking.Returns(new ReactiveProperty<bool>(false));
            ultimateBot.MoveFailed.Returns(new Subject<BotMoveFailedEvent>());

            var matchStateProvider = Substitute.For<IMatchStateProvider>();
            matchStateProvider.ActivePlayerSlot.Returns(PlayerSlotMapping.SlotO);
            matchStateProvider.GetAllCells().Returns(new List<CellSnapshot>());

            var marks = new BattleshipCellMark[100];
            for (var i = 0; i < marks.Length; i++)
                marks[i] = BattleshipCellMark.Unknown;
            IReadOnlyList<BattleshipCellMark> marksView = Array.AsReadOnly(marks);

            var battleshipSnapshot = Substitute.For<IBattleshipGameplaySnapshotProvider>();
            battleshipSnapshot.Phase.Returns(BattleshipPhase.Battle);
            battleshipSnapshot.ActivePlayerSlot.Returns(PlayerSlotMapping.SlotO);
            battleshipSnapshot.CurrentStatus.Returns(GameStatus.InProgress);
            battleshipSnapshot.GetOpponentMarks(PlayerSlotMapping.SlotO).Returns(marksView);

            var battleshipEvents = Substitute.For<IBattleshipGameplayEventStream>();
            battleshipEvents.PhaseChanged.Returns(new Subject<BattleshipPhaseChangedEvent>());
            battleshipEvents.MarksChanged.Returns(new Subject<BattleshipMarksChangedEvent>());

            var incomingMoves = new Subject<MoveCommand>();
            var incomingReady = new Subject<RoundReadySignal>();
            var incomingTimeout = new Subject<OnlineTimeoutSignal>();
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
            battleshipBridge.IncomingRecoverySnapshots.Returns(new Subject<BattleshipRecoveryMessage>());
            battleshipBridge.BindAsync(Arg.Any<string>(), Arg.Any<bool>()).Returns(UniTask.CompletedTask);

            var sessionStore = new OnlineGameplaySessionContextStore();
            sessionStore.SetDirectInviteSession("ABCDEF", "host-user", isHost: true);

            var statisticsReporter = CreateStatisticsReporter(configStore, eventStream);

            return new TestContext(
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
                incomingMoves);
        }

        private static PlayerStatisticsMatchReporter CreateStatisticsReporter(
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

        private sealed class TestContext
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

            public TestContext(
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
                Subject<MoveCommand> incomingMoves)
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
                IncomingMoves = incomingMoves;
            }

            public Subject<MoveCommand> IncomingMoves { get; }
            public IMatchStateProvider MatchStateProvider => _matchStateProvider;

            public GameplayStartup CreateSut() => new(
                _configStore,
                _gameService,
                _fieldPresenter,
                _fieldUiAdapter,
                _ecsLifecycle,
                _eventStream,
                _commandSink,
                _movesBinder,
                new WinLineRenderer(_fieldUiAdapter),
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
                battleshipSnapshotProvider: _battleshipSnapshot,
                battleshipEventStream: _battleshipEvents,
                statisticsReporter: _statisticsReporter);
        }
    }
}

