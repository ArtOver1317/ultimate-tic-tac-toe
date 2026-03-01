using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using Runtime.Gameplay;
using Runtime.Gameplay.ECS;
using Runtime.Games.TicTacToe;
using Runtime.Games.TicTacToe.Moves;
using Runtime.Games.TicTacToe.AI;
using Runtime.Games.TicTacToe.AI.Ultimate;
using Runtime.Games.TicTacToe.Rules;
using Runtime.Games.TicTacToe.Series;
using Runtime.GameModes.Wizard;
using Runtime.Infrastructure.GameStateMachine;
using Runtime.Infrastructure.GameStateMachine.States;
using Runtime.PlayerStatistics;
using CellId = Runtime.Games.TicTacToe.Moves.CellId;
using UnityEngine;
using UnityEngine.TestTools;
using R3;
using UnityEngine.UIElements;

namespace Tests.EditMode.Games.TicTacToe
{
    [TestFixture]
    [Category("Unit")]
    public class GameplayStartupTests
    {
        private IGameLaunchConfigStore _configStore;
        private IGameService _gameService;
        private IGameplayFieldPresenter _fieldPresenter;
        private IGameplayFieldUiAdapter _fieldUiAdapter;
        private IGameStateMachine _stateMachine;
        private IMatchEcsLifecycle _ecsLifecycle;
        private IGameplayEventStream _eventStream;
        private IGameplayCommandSink _commandSink;
        private IGameplaySnapshotProvider _snapshotProvider;
        private GameplayMovesBinder _movesBinder;
        private WinLineRenderer _winLineRenderer;
        private ISeriesService _seriesService;
        private IGameplayBackHandler _backHandler;
        private IBotTurnDriver _botDriver;
        private IBotTurnOrchestrator _ultimateBotOrchestrator;
        private ReactiveProperty<bool> _botBusy;
        private ReactiveProperty<bool> _botDisabled;
        private VisualElement _fieldContainer;
        private GameplayStartup _sut;
        private GameLaunchConfig _config;
        private IGameplaySession _session;

        [SetUp]
        public void SetUp()
        {
            _configStore = Substitute.For<IGameLaunchConfigStore>();
            _gameService = Substitute.For<IGameService>();
            _fieldPresenter = Substitute.For<IGameplayFieldPresenter>();
            _stateMachine = Substitute.For<IGameStateMachine>();

            _ecsLifecycle = Substitute.For<IMatchEcsLifecycle>();
            _commandSink = Substitute.For<IGameplayCommandSink>();
            _snapshotProvider = Substitute.For<IGameplaySnapshotProvider>();
            _snapshotProvider.GetAllCells().Returns(new List<CellSnapshot>());

            _eventStream = Substitute.For<IGameplayEventStream>();
            _eventStream.CellChanged.Returns(new Subject<CellChangedEvent>());
            _eventStream.LastMoveChanged.Returns(new Subject<LastMoveChangedEvent>());
            _eventStream.CurrentPlayerChanged.Returns(new Subject<CurrentPlayerChangedEvent>());
            _eventStream.CommandRejected.Returns(new Subject<CommandRejectedEvent>());
            _eventStream.RoundFinished.Returns(new Subject<RoundFinishedEvent>());

            _fieldContainer = new VisualElement();
            _fieldUiAdapter = Substitute.For<IGameplayFieldUiAdapter>();
            _fieldUiAdapter.CellClicks.Returns(new Subject<CellId>());
            _fieldUiAdapter.CurrentPlayerLabel.Returns(new Label());
            _fieldUiAdapter.FieldContainer.Returns(_fieldContainer);
            _fieldUiAdapter.Player1Panel.Returns(new VisualElement());
            _fieldUiAdapter.Player2Panel.Returns(new VisualElement());
            _fieldUiAdapter.Player1ScoreLabel.Returns(new Label());
            _fieldUiAdapter.Player2ScoreLabel.Returns(new Label());
            _movesBinder = new GameplayMovesBinder(_fieldUiAdapter, _commandSink, _eventStream, _snapshotProvider);

            _winLineRenderer = new WinLineRenderer(_fieldUiAdapter);
            _seriesService = Substitute.For<ISeriesService>();
            _seriesService.Score.Returns(new ReactiveProperty<SeriesScore>(default));
            _backHandler = Substitute.For<IGameplayBackHandler>();
            _backHandler.HandleBackAsync(Arg.Any<CancellationToken>()).Returns(UniTask.CompletedTask);

            _config = new GameLaunchConfig("classic", new TicTacToeConfig(3), new LocalHumanConfig());
            _session = Substitute.For<IGameplaySession>();
            _session.FieldRenderSpec.Returns(FieldRenderSpec.Classic(3));

            _configStore.TryConsume(out Arg.Any<GameLaunchConfig>()).Returns(callInfo =>
            {
                callInfo[0] = _config;
                return true;
            });
            _configStore.TryPeek(out Arg.Any<GameLaunchConfig>()).Returns(callInfo =>
            {
                callInfo[0] = _config;
                return true;
            });

            _gameService.StartMatchAsync(Arg.Any<GameLaunchConfig>(), Arg.Any<CancellationToken>())
                .Returns(UniTask.FromResult(_session));

            _fieldPresenter.BindAsync(Arg.Any<FieldRenderSpec>(), Arg.Any<CancellationToken>())
                .Returns(UniTask.CompletedTask);

            _stateMachine.EnterAsync<LoadMainMenuState>(Arg.Any<CancellationToken>())
                .Returns(UniTask.CompletedTask);

            _botDriver = Substitute.For<IBotTurnDriver>();
            _botBusy = new ReactiveProperty<bool>(false);
            _botDisabled = new ReactiveProperty<bool>(false);
            _botDriver.IsBusy.Returns(_botBusy);
            _botDriver.IsDisabled.Returns(_botDisabled);

            _ultimateBotOrchestrator = Substitute.For<IBotTurnOrchestrator>();
            _ultimateBotOrchestrator.IsThinking.Returns(new ReactiveProperty<bool>(false));
            _ultimateBotOrchestrator.MoveFailed.Returns(new Subject<BotMoveFailedEvent>());
            var matchFailSafeGateway = Substitute.For<IMatchFailSafeGateway>();
            var statisticsReporter = CreateStatisticsReporter(_configStore, _eventStream);

            _sut = new GameplayStartup(_configStore, _gameService, _fieldPresenter, _fieldUiAdapter, _ecsLifecycle, _eventStream, _commandSink, _movesBinder, _winLineRenderer, _seriesService, _backHandler, _stateMachine, _botDriver, _ultimateBotOrchestrator, matchFailSafeGateway, statisticsReporter: statisticsReporter);
        }

        [TearDown]
        public void TearDown() => _sut = null;

        [Test]
        public async Task WhenLaunchConfigMissing_ThenUnbindDisposesAndReturnsToMainMenu()
        {
            // Arrange
            _configStore.TryConsume(out Arg.Any<GameLaunchConfig>()).Returns(false);

            // Act
            Func<Task> act = async () => await _sut.StartAsync(CancellationToken.None);

            // Assert
            await RunAllowingFailingLogsAsync(act,
                new Regex(
                    @"(\[Error\]\s*)?\[Infrastructure\] \[GameplayStartup\] INVALID_CONFIG: Launch config not found\.\s*$",
                    RegexOptions.CultureInvariant));
            _fieldPresenter.Received(1).Unbind();
            _ecsLifecycle.Received(1).StopMatch();
            await _stateMachine.Received(1).EnterAsync<LoadMainMenuState>(Arg.Any<CancellationToken>());
            await _gameService.DidNotReceive().StartMatchAsync(Arg.Any<GameLaunchConfig>(), Arg.Any<CancellationToken>());
            await _fieldPresenter.DidNotReceive().BindAsync(Arg.Any<FieldRenderSpec>(), Arg.Any<CancellationToken>());
        }

        [Test]
        public async Task WhenTryConsumeReturnsTrueButConfigIsNull_ThenUnbindDisposesAndReturnsToMainMenu()
        {
            // Arrange
            _configStore.TryConsume(out Arg.Any<GameLaunchConfig>()).Returns(callInfo =>
            {
                callInfo[0] = null;
                return true;
            });

            // Act
            Func<Task> act = async () => await _sut.StartAsync(CancellationToken.None);

            // Assert
            await RunAllowingFailingLogsAsync(act,
                new Regex(
                    @"(\[Error\]\s*)?\[Infrastructure\] \[GameplayStartup\] INVALID_CONFIG: Launch config not found\.\s*$",
                    RegexOptions.CultureInvariant));
            _fieldPresenter.Received(1).Unbind();
            _ecsLifecycle.Received(1).StopMatch();
            await _stateMachine.Received(1).EnterAsync<LoadMainMenuState>(Arg.Any<CancellationToken>());
            await _gameService.DidNotReceive().StartMatchAsync(Arg.Any<GameLaunchConfig>(), Arg.Any<CancellationToken>());
            await _fieldPresenter.DidNotReceive().BindAsync(Arg.Any<FieldRenderSpec>(), Arg.Any<CancellationToken>());
        }

        [Test]
        public async Task WhenLaunchConfigIsValid_ThenStartsMatchBindsAndDoesNotReturnToMainMenu()
        {
            // Arrange
            // SetUp already configures valid config, session and successful Bind.

            // Act
            Func<Task> act = async () => await _sut.StartAsync(CancellationToken.None);

            // Assert
            await act.Should().NotThrowAsync();
            await _gameService.Received(1).StartMatchAsync(Arg.Any<GameLaunchConfig>(), Arg.Any<CancellationToken>());
            await _fieldPresenter.Received(1).BindAsync(Arg.Any<FieldRenderSpec>(), Arg.Any<CancellationToken>());
            _ecsLifecycle.Received(1).StartMatch(Arg.Any<GameLaunchConfig>());

            _fieldPresenter.DidNotReceive().Unbind();
            _ecsLifecycle.DidNotReceive().StopMatch();
            await _stateMachine.DidNotReceive().EnterAsync<LoadMainMenuState>(Arg.Any<CancellationToken>());
        }

        [Test]
        public async Task WhenStartMatchThrowsInvalidOperationException_ThenUnbindDisposesAndReturnsToMainMenu()
        {
            // Arrange
            _gameService.StartMatchAsync(Arg.Any<GameLaunchConfig>(), Arg.Any<CancellationToken>())
                .Returns(UniTask.FromException<IGameplaySession>(new InvalidOperationException("invalid")));

            // Act
            Func<Task> act = async () => await _sut.StartAsync(CancellationToken.None);

            // Assert
            await RunAllowingFailingLogsAsync(act,
                new Regex(
                    @"(\[Error\]\s*)?\[Infrastructure\] \[GameplayStartup\] Failed to start gameplay:.*",
                    RegexOptions.CultureInvariant | RegexOptions.Singleline),
                new Regex(
                    @"(\[Error\]\s*)?\[Infrastructure\] \[GameplayStartup\] INVALID_CONFIG: invalid\s*$",
                    RegexOptions.CultureInvariant));
            _fieldPresenter.Received(1).Unbind();
            _ecsLifecycle.Received(1).StopMatch();
            await _stateMachine.Received(1).EnterAsync<LoadMainMenuState>(Arg.Any<CancellationToken>());
            await _fieldPresenter.DidNotReceive().BindAsync(Arg.Any<FieldRenderSpec>(), Arg.Any<CancellationToken>());
        }

        [Test]
        public async Task WhenStartMatchThrowsArgumentOutOfRangeException_ThenUnbindDisposesAndReturnsToMainMenu()
        {
            // Arrange
            _gameService.StartMatchAsync(Arg.Any<GameLaunchConfig>(), Arg.Any<CancellationToken>())
                .Returns(UniTask.FromException<IGameplaySession>(new ArgumentOutOfRangeException("boardSize")));

            // Act
            Func<Task> act = async () => await _sut.StartAsync(CancellationToken.None);

            // Assert
            await RunAllowingFailingLogsAsync(act,
                new Regex(
                    @"(\[Error\]\s*)?\[Infrastructure\] \[GameplayStartup\] Failed to start gameplay:.*",
                    RegexOptions.CultureInvariant | RegexOptions.Singleline),
                new Regex(
                    @"(\[Error\]\s*)?\[Infrastructure\] \[GameplayStartup\] INVALID_CONFIG:[\s\S]*boardSize\s*$",
                    RegexOptions.CultureInvariant));
            _fieldPresenter.Received(1).Unbind();
            _ecsLifecycle.Received(1).StopMatch();
            await _stateMachine.Received(1).EnterAsync<LoadMainMenuState>(Arg.Any<CancellationToken>());
            await _fieldPresenter.DidNotReceive().BindAsync(Arg.Any<FieldRenderSpec>(), Arg.Any<CancellationToken>());
        }

        [Test]
        public async Task WhenStartMatchThrowsUnexpectedException_ThenUnbindDisposesAndReturnsToMainMenu()
        {
            // Arrange
            _gameService.StartMatchAsync(Arg.Any<GameLaunchConfig>(), Arg.Any<CancellationToken>())
                .Returns(UniTask.FromException<IGameplaySession>(new Exception("boom")));

            // Act
            Func<Task> act = async () => await _sut.StartAsync(CancellationToken.None);

            // Assert
            await RunAllowingFailingLogsAsync(act,
                new Regex(
                    @"(\[Error\]\s*)?\[Infrastructure\] \[GameplayStartup\] Failed to start gameplay:.*",
                    RegexOptions.CultureInvariant | RegexOptions.Singleline),
                new Regex(
                    @"(\[Error\]\s*)?\[Infrastructure\] \[GameplayStartup\] BUILD_FAILED: boom\s*$",
                    RegexOptions.CultureInvariant));
            _fieldPresenter.Received(1).Unbind();
            _ecsLifecycle.Received(1).StopMatch();
            await _stateMachine.Received(1).EnterAsync<LoadMainMenuState>(Arg.Any<CancellationToken>());
            await _fieldPresenter.DidNotReceive().BindAsync(Arg.Any<FieldRenderSpec>(), Arg.Any<CancellationToken>());
        }

        [Test]
        public async Task WhenBindThrowsException_ThenUnbindDisposesAndReturnsToMainMenu()
        {
            // Arrange
            _fieldPresenter.BindAsync(Arg.Any<FieldRenderSpec>(), Arg.Any<CancellationToken>())
                .Returns(UniTask.FromException(new Exception("bind failed")));

            // Act
            Func<Task> act = async () => await _sut.StartAsync(CancellationToken.None);

            // Assert
            await RunAllowingFailingLogsAsync(act,
                new Regex(
                    @"(\[Error\]\s*)?\[Infrastructure\] \[GameplayStartup\] Failed to start gameplay:.*",
                    RegexOptions.CultureInvariant | RegexOptions.Singleline),
                new Regex(
                    @"(\[Error\]\s*)?\[Infrastructure\] \[GameplayStartup\] BUILD_FAILED: bind failed\s*$",
                    RegexOptions.CultureInvariant));
            _fieldPresenter.Received(1).Unbind();
            _ecsLifecycle.Received(1).StopMatch();
            await _stateMachine.Received(1).EnterAsync<LoadMainMenuState>(Arg.Any<CancellationToken>());
            await _gameService.Received(1).StartMatchAsync(Arg.Any<GameLaunchConfig>(), Arg.Any<CancellationToken>());
            await _fieldPresenter.Received(1).BindAsync(Arg.Any<FieldRenderSpec>(), Arg.Any<CancellationToken>());
        }

        [Test]
        public async Task WhenCancelledDuringStartMatchAsync_ThenUnbindDisposesAndRethrowsOperationCanceledException()
        {
            // Arrange
            using var cts = new CancellationTokenSource();
            _gameService.StartMatchAsync(Arg.Any<GameLaunchConfig>(), Arg.Any<CancellationToken>())
                .Returns(UniTask.FromException<IGameplaySession>(new OperationCanceledException(cts.Token)));

            // Act
            Func<Task> act = async () => await _sut.StartAsync(cts.Token);

            // Assert
            await act.Should().ThrowAsync<OperationCanceledException>();
            _fieldPresenter.Received(1).Unbind();
            _ecsLifecycle.Received(1).StopMatch();
            await _stateMachine.DidNotReceive().EnterAsync<LoadMainMenuState>(Arg.Any<CancellationToken>());
            await _fieldPresenter.DidNotReceive().BindAsync(Arg.Any<FieldRenderSpec>(), Arg.Any<CancellationToken>());
        }

        [Test]
        public async Task WhenCancelledDuringBindAsync_ThenUnbindDisposesAndRethrowsOperationCanceledException()
        {
            // Arrange
            using var cts = new CancellationTokenSource();
            _fieldPresenter.BindAsync(Arg.Any<FieldRenderSpec>(), Arg.Any<CancellationToken>())
                .Returns(UniTask.FromException(new OperationCanceledException(cts.Token)));

            // Act
            Func<Task> act = async () => await _sut.StartAsync(cts.Token);

            // Assert
            await act.Should().ThrowAsync<OperationCanceledException>();
            _fieldPresenter.Received(1).Unbind();
            _ecsLifecycle.Received(1).StopMatch();
            await _stateMachine.DidNotReceive().EnterAsync<LoadMainMenuState>(Arg.Any<CancellationToken>());
            await _gameService.Received(1).StartMatchAsync(Arg.Any<GameLaunchConfig>(), Arg.Any<CancellationToken>());
            await _fieldPresenter.Received(1).BindAsync(Arg.Any<FieldRenderSpec>(), Arg.Any<CancellationToken>());
        }

        [Test]
        public async Task WhenUltimateBotMatchStarted_ThenStartsUltimateOrchestratorAndNotClassicDriver()
        {
            _config = new GameLaunchConfig(
                "ultimate",
                UltimateTicTacToeConfig.Instance,
                new BotOpponentConfig("Normal"));

            await _sut.StartAsync(CancellationToken.None);

            await _ultimateBotOrchestrator.Received(1)
                .StartAsync(1, "medium", Arg.Any<CancellationToken>());
            await _botDriver.DidNotReceive()
                .StartAsync(Arg.Any<GameLaunchConfig>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        }

        [Test]
        public async Task WhenClassicBotMatchStarted_ThenStartsClassicDriverAndNotUltimateOrchestrator()
        {
            _config = new GameLaunchConfig(
                "classic",
                new TicTacToeConfig(boardSize: 3, isUltimate: false),
                new BotOpponentConfig("Easy"));

            _botDriver.StartAsync(Arg.Any<GameLaunchConfig>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(UniTask.FromResult(new BotStartResult(BotStartStatus.Started)));

            await _sut.StartAsync(CancellationToken.None);

            await _botDriver.Received(1)
                .StartAsync(Arg.Any<GameLaunchConfig>(), 1, "Easy", Arg.Any<CancellationToken>());
            await _ultimateBotOrchestrator.DidNotReceive()
                .StartAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        }

        [Test]
        public async Task WhenOnlineMatchConfigPayloadExists_ThenUsesHostBoardSizeForStartMatch()
        {
            var onlineSessionStore = new OnlineGameplaySessionContextStore();
            onlineSessionStore.SetDirectInviteSession("ABCDEF", "guest-user", isHost: false);
            onlineSessionStore.SetMatchConfig(new OnlineMatchConfigPayload("classic", boardSize: 5, isUltimate: false, moveTimeLimitSeconds: 20));

            var networkBridge = Substitute.For<IGameplayNetworkBridge>();
            networkBridge.Snapshot.Returns(new ReactiveProperty<GameplayNetworkSnapshot?>(null));
            networkBridge.IncomingMoves.Returns(new Subject<MoveCommand>());
            networkBridge.IncomingRoundReadySignals.Returns(new Subject<RoundReadySignal>());
            networkBridge.IncomingTimeoutSignals.Returns(new Subject<OnlineTimeoutSignal>());
            networkBridge.BindAsync(Arg.Any<string>(), Arg.Any<bool>()).Returns(UniTask.CompletedTask);

            var onlineFlow = new TestOnlineSessionFlow();
            var matchStateProvider = Substitute.For<IMatchStateProvider>();

            var sut = new GameplayStartup(
                _configStore,
                _gameService,
                _fieldPresenter,
                _fieldUiAdapter,
                _ecsLifecycle,
                _eventStream,
                _commandSink,
                _movesBinder,
                _winLineRenderer,
                _seriesService,
                _backHandler,
                _stateMachine,
                _botDriver,
                _ultimateBotOrchestrator,
                Substitute.For<IMatchFailSafeGateway>(),
                ultimateSnapshotProvider: null,
                networkBridge: networkBridge,
                onlineSessionContextStore: onlineSessionStore,
                matchStateProvider: matchStateProvider,
                onlineSessionFlow: onlineFlow,
                statisticsReporter: CreateStatisticsReporter(_configStore, _eventStream));

            await sut.StartAsync(CancellationToken.None);

            await _gameService.Received(1).StartMatchAsync(
                Arg.Is<GameLaunchConfig>(cfg =>
                    cfg.GameConfig != null &&
                    cfg.GameConfig.GetType() == typeof(TicTacToeConfig) &&
                    ((TicTacToeConfig)cfg.GameConfig).BoardSize == 5 &&
                    !((TicTacToeConfig)cfg.GameConfig).IsUltimate &&
                    cfg.MoveTimeLimitSeconds == 20),
                Arg.Any<CancellationToken>());

            sut.Dispose();
        }

        [Test]
        public async Task WhenOnlineFlowTerminatedByOpponentLeftDuringOnlineMatch_ThenShowsResultAndRecordsLocalWin()
        {
            var onlineFlow = new TestOnlineSessionFlow();
            var onlineSessionStore = new OnlineGameplaySessionContextStore();
            onlineSessionStore.SetDirectInviteSession("ABCDEF", "local-user", isHost: false);

            var matchStateProvider = Substitute.For<IMatchStateProvider>();
            var networkBridge = Substitute.For<IGameplayNetworkBridge>();
            networkBridge.Snapshot.Returns(new ReactiveProperty<GameplayNetworkSnapshot?>(null));
            networkBridge.IncomingMoves.Returns(new Subject<MoveCommand>());
            networkBridge.IncomingRoundReadySignals.Returns(new Subject<RoundReadySignal>());
            networkBridge.IncomingTimeoutSignals.Returns(new Subject<OnlineTimeoutSignal>());
            networkBridge.BindAsync(Arg.Any<string>(), Arg.Any<bool>()).Returns(UniTask.CompletedTask);

            _backHandler.ClearReceivedCalls();

            var sut = new GameplayStartup(
                _configStore,
                _gameService,
                _fieldPresenter,
                _fieldUiAdapter,
                _ecsLifecycle,
                _eventStream,
                _commandSink,
                _movesBinder,
                _winLineRenderer,
                _seriesService,
                _backHandler,
                _stateMachine,
                _botDriver,
                _ultimateBotOrchestrator,
                Substitute.For<IMatchFailSafeGateway>(),
                ultimateSnapshotProvider: null,
                networkBridge: networkBridge,
                onlineSessionContextStore: onlineSessionStore,
                matchStateProvider: matchStateProvider,
                onlineSessionFlow: onlineFlow,
                statisticsReporter: CreateStatisticsReporter(_configStore, _eventStream));

            await sut.StartAsync(CancellationToken.None);

            onlineFlow.Emit(OnlineFlowState.Terminated, OnlineErrorCode.OpponentLeft);
            await UniTask.DelayFrame(1);

            _seriesService.Received(1)
                .RecordResult(Arg.Is<GameResult>(result =>
                    result.Status == Runtime.Games.TicTacToe.Rules.GameStatus.Timeout &&
                    result.Winner == PlayerMark.O));

            _fieldContainer.ClassListContains("field-container--round-finished").Should().BeTrue();
            await _backHandler.DidNotReceive().HandleBackAsync(Arg.Any<CancellationToken>());

            sut.Dispose();
        }

        [Test]
        public async Task WhenIncomingOnlineTimeoutSignalOnGuest_ThenSubmitsTimeoutCommandToMatchStateProvider()
        {
            _ecsLifecycle.IsActive.Returns(true);

            var timeoutSignals = new Subject<OnlineTimeoutSignal>();
            var onlineSessionStore = new OnlineGameplaySessionContextStore();
            onlineSessionStore.SetDirectInviteSession("ABCDEF", "guest-user", isHost: false);

            var matchStateProvider = Substitute.For<IMatchStateProvider>();
            var networkBridge = Substitute.For<IGameplayNetworkBridge>();
            networkBridge.Snapshot.Returns(new ReactiveProperty<GameplayNetworkSnapshot?>(null));
            networkBridge.IncomingMoves.Returns(new Subject<MoveCommand>());
            networkBridge.IncomingRoundReadySignals.Returns(new Subject<RoundReadySignal>());
            networkBridge.IncomingTimeoutSignals.Returns(timeoutSignals);
            networkBridge.BindAsync(Arg.Any<string>(), Arg.Any<bool>()).Returns(UniTask.CompletedTask);

            var sut = new GameplayStartup(
                _configStore,
                _gameService,
                _fieldPresenter,
                _fieldUiAdapter,
                _ecsLifecycle,
                _eventStream,
                _commandSink,
                _movesBinder,
                _winLineRenderer,
                _seriesService,
                _backHandler,
                _stateMachine,
                _botDriver,
                _ultimateBotOrchestrator,
                Substitute.For<IMatchFailSafeGateway>(),
                ultimateSnapshotProvider: null,
                networkBridge: networkBridge,
                onlineSessionContextStore: onlineSessionStore,
                matchStateProvider: matchStateProvider,
                onlineSessionFlow: new TestOnlineSessionFlow(),
                statisticsReporter: CreateStatisticsReporter(_configStore, _eventStream));

            await sut.StartAsync(CancellationToken.None);

            timeoutSignals.OnNext(new OnlineTimeoutSignal("host-user", loserSlot: 1, clientTick: 123));
            await UniTask.DelayFrame(1);

            matchStateProvider.Received(1)
                .SubmitCommand(Arg.Is<IGameplayCommand>(command => command.GetType() == typeof(TimeoutCommand) && ((TimeoutCommand)command).LoserSlot == 1));

            sut.Dispose();
            timeoutSignals.Dispose();
        }

        [Test]
        public async Task WhenIncomingOnlineTimeoutSignalOnHost_ThenDoesNotSubmitTimeoutCommandToMatchStateProvider()
        {
            _ecsLifecycle.IsActive.Returns(true);

            var timeoutSignals = new Subject<OnlineTimeoutSignal>();
            var onlineSessionStore = new OnlineGameplaySessionContextStore();
            onlineSessionStore.SetDirectInviteSession("ABCDEF", "host-user", isHost: true);

            var matchStateProvider = Substitute.For<IMatchStateProvider>();
            var networkBridge = Substitute.For<IGameplayNetworkBridge>();
            networkBridge.Snapshot.Returns(new ReactiveProperty<GameplayNetworkSnapshot?>(null));
            networkBridge.IncomingMoves.Returns(new Subject<MoveCommand>());
            networkBridge.IncomingRoundReadySignals.Returns(new Subject<RoundReadySignal>());
            networkBridge.IncomingTimeoutSignals.Returns(timeoutSignals);
            networkBridge.BindAsync(Arg.Any<string>(), Arg.Any<bool>()).Returns(UniTask.CompletedTask);

            var sut = new GameplayStartup(
                _configStore,
                _gameService,
                _fieldPresenter,
                _fieldUiAdapter,
                _ecsLifecycle,
                _eventStream,
                _commandSink,
                _movesBinder,
                _winLineRenderer,
                _seriesService,
                _backHandler,
                _stateMachine,
                _botDriver,
                _ultimateBotOrchestrator,
                Substitute.For<IMatchFailSafeGateway>(),
                ultimateSnapshotProvider: null,
                networkBridge: networkBridge,
                onlineSessionContextStore: onlineSessionStore,
                matchStateProvider: matchStateProvider,
                onlineSessionFlow: new TestOnlineSessionFlow(),
                statisticsReporter: CreateStatisticsReporter(_configStore, _eventStream));

            await sut.StartAsync(CancellationToken.None);

            timeoutSignals.OnNext(new OnlineTimeoutSignal("host-user", loserSlot: 1, clientTick: 123));
            await UniTask.DelayFrame(1);

            matchStateProvider.DidNotReceive().SubmitCommand(Arg.Any<IGameplayCommand>());

            sut.Dispose();
            timeoutSignals.Dispose();
        }

        private static async Task RunAllowingFailingLogsAsync(Func<Task> action, params Regex[] expectedFailingLogs)
        {
            var captured = new List<(LogType type, string condition)>();

            void Handler(string condition, string stackTrace, LogType type)
            {
                if (type is LogType.Error or LogType.Exception or LogType.Assert)
                    captured.Add((type, condition));
            }

            var previousIgnore = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;
            Application.logMessageReceived += Handler;

            try
            {
                await action();
            }
            finally
            {
                Application.logMessageReceived -= Handler;
                LogAssert.ignoreFailingMessages = previousIgnore;
            }

            var messages = captured.Select(x => x.condition).ToList();
            messages.Count.Should().Be(expectedFailingLogs.Length,
                "����� ������ Error/Exception/Assert ��� ������ ������ ����");

            for (var i = 0; i < expectedFailingLogs.Length; i++)
            {
                var regex = expectedFailingLogs[i];
                regex.IsMatch(messages[i]).Should().BeTrue(
                    $"expected failing log #{i + 1} to match regex '{regex}', but was: {messages[i]}");
            }
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

        private sealed class TestOnlineSessionFlow : IOnlineSessionFlowService
        {
            private readonly ReactiveProperty<OnlineFlowSnapshot> _snapshot = new(
                new OnlineFlowSnapshot(
                    OnlineFlowState.Idle,
                    previousStableState: null,
                    candidateSessionId: string.Empty,
                    activeSessionId: null,
                    flowEpoch: 1,
                    region: "eu",
                    canStart: false,
                    isBusy: false,
                    errorCode: OnlineErrorCode.None,
                    errorLocalizationKey: null,
                    statusLocalizationKey: null,
                    countdownRemainingSeconds: null,
                    graceDeadlineUtc: null));

            public ReadOnlyReactiveProperty<OnlineFlowSnapshot> Snapshot => _snapshot;

            public void Emit(OnlineFlowState state, OnlineErrorCode errorCode) =>
                _snapshot.Value = new OnlineFlowSnapshot(
                    state,
                    previousStableState: null,
                    candidateSessionId: string.Empty,
                    activeSessionId: "ABCDEF",
                    flowEpoch: _snapshot.Value.FlowEpoch + 1,
                    region: "eu",
                    canStart: false,
                    isBusy: false,
                    errorCode: errorCode,
                    errorLocalizationKey: OnlineLocalizationKeys.ErrorKey(errorCode),
                    statusLocalizationKey: null,
                    countdownRemainingSeconds: null,
                    graceDeadlineUtc: null);

            public UniTask EnterHumanSetupAsync(string region, string currentUserId) => UniTask.CompletedTask;
            public UniTask ConfirmHostIntentAsync() => UniTask.CompletedTask;
            public UniTask StartHostSessionAsync(OnlineSessionConfig hostConfig) => UniTask.CompletedTask;
            public UniTask JoinBySessionIdAsync(string rawSessionIdInput, string region, string currentUserId) => UniTask.CompletedTask;
            public UniTask CopyVisibleSessionIdAsync() => UniTask.CompletedTask;
            public UniTask BackAsync() => UniTask.CompletedTask;
            public UniTask ExitAsync() => UniTask.CompletedTask;
            public UniTask SetReadyForNextMatchAsync(bool isReady) => UniTask.CompletedTask;
            public UniTask OnOpponentReadyForNextMatchAsync(bool isReady) => UniTask.CompletedTask;
            public UniTask OnHostCreatedAsync() => UniTask.CompletedTask;
            public UniTask OnJoinSucceededAsync() => UniTask.CompletedTask;
            public UniTask OnJoinFailedAsync(OnlineErrorCode errorCode) => UniTask.CompletedTask;
            public UniTask OnGuestJoinedAsync() => UniTask.CompletedTask;
            public UniTask OnCountdownTickAsync(int remainingSeconds) => UniTask.CompletedTask;
            public UniTask OnGameplayEnteredAsync() => UniTask.CompletedTask;
            public UniTask OnRoundCompletedAsync() => UniTask.CompletedTask;
            public UniTask OnDisconnectDetectedAsync() => UniTask.CompletedTask;
            public UniTask OnReconnectSucceededAsync() => UniTask.CompletedTask;
            public UniTask OnGraceTimeoutAsync(int eventEpoch) => UniTask.CompletedTask;
            public UniTask OnOpponentLeftAsync() => UniTask.CompletedTask;

            public void Dispose() => _snapshot.Dispose();
        }

    }
}
