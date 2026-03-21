#nullable enable

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using R3;
using Runtime.GameModes.Wizard.Configs;
using Runtime.GameModes.Wizard.Modes;
using Runtime.GameModes.Wizard.Online;
using Runtime.Gameplay;
using Runtime.Gameplay.ECS.Lifecycle;
using Runtime.Gameplay.Shared;
using Runtime.Games.Battleship.AI;
using Runtime.Games.Battleship.Core;
using Runtime.Games.Battleship.Networking;
using Runtime.Games.Battleship.Placement;
using Runtime.Games.Battleship.UI.Placement;
using Runtime.Games.TicTacToe.AI.Core;
using Runtime.Games.TicTacToe.AI.Ultimate.Core;
using Runtime.Games.TicTacToe.Series;
using Runtime.Infrastructure.GameStateMachine;
using Runtime.Infrastructure.GameStateMachine.States;
using Tests.EditMode.Games.Battleship.Fakes;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using EcsGameStatus = Runtime.Gameplay.Shared.EcsGameStatus;

namespace Tests.EditMode.Games.Battleship.Startup
{
    [TestFixture]
    [Category("Unit")]
    public sealed class GameplayStartupBattleshipGuardTests
    {
        [Test]
        public async Task WhenConfigIsNotBattleship_ThenReturnsToMainMenuWithoutStartingMatch()
        {
            using var context = GameplayStartupBattleshipTestFactory.CreateContext(
                launchConfig: new GameLaunchConfig(
                    "classic",
                    new TicTacToeConfig(3),
                    new LocalHumanConfig(),
                    moveTimeLimitSeconds: 30));

            context.StateMachine
                .EnterAsync<LoadMainMenuState>(Arg.Any<CancellationToken>())
                .Returns(UniTask.CompletedTask);

            LogAssert.Expect(
                UnityEngine.LogType.Error,
                new Regex(@"(\[Error\]\s*)?\[Infrastructure\] \[GameplayStartup\] INVALID_CONFIG: Non-Battleship config must be handled by TicTacToe startup\.\s*$"));

            using var sut = context.CreateSut();

            Func<Task> act = async () => await sut.StartAsync(CancellationToken.None);

            await act.Should().NotThrowAsync();
            await context.StateMachine.Received(1).EnterAsync<LoadMainMenuState>(Arg.Any<CancellationToken>());
            context.MatchStateProvider.DidNotReceive().SubmitCommand(Arg.Any<IGameplayCommand>());
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
            battleshipSnapshot.CurrentStatus.Returns(EcsGameStatus.InProgress);

            var battleshipEvents = Substitute.For<IBattleshipGameplayEventStream>();
            battleshipEvents.PhaseChanged.Returns(new Subject<BattleshipPhaseChangedEvent>());
            battleshipEvents.MarksChanged.Returns(new Subject<BattleshipMarksChangedEvent>());

            var movesBinder = GameplayStartupBattleshipTestFactory.CreateBattleshipMovesBinder(
                fieldUiAdapter,
                commandSink,
                eventStream,
                snapshotProvider,
                battleshipSnapshot);

            var statisticsReporter = GameplayStartupBattleshipTestFactory.CreateStatisticsReporter(configStore, eventStream);

            using var sut = GameplayStartupBattleshipTestFactory.CreateStartup(
                configStore,
                gameService,
                fieldPresenter,
                fieldUiAdapter,
                ecsLifecycle,
                eventStream,
                commandSink,
                movesBinder,
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
            var battleshipMovesSnapshot = Substitute.For<IBattleshipGameplaySnapshotProvider>();
            var emptyMarks = Array.AsReadOnly(Array.Empty<BattleshipCellMark>());
            battleshipMovesSnapshot.Phase.Returns(BattleshipPhase.Battle);
            battleshipMovesSnapshot.GetOpponentMarks(Arg.Any<int>()).Returns(emptyMarks);
          
            var movesBinder = GameplayStartupBattleshipTestFactory.CreateBattleshipMovesBinder(
                fieldUiAdapter,
                commandSink,
                eventStream,
                snapshotProvider,
                battleshipMovesSnapshot);

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

            var statisticsReporter = GameplayStartupBattleshipTestFactory.CreateStatisticsReporter(configStore, eventStream);

            using var sut = GameplayStartupBattleshipTestFactory.CreateStartup(
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
    }
}