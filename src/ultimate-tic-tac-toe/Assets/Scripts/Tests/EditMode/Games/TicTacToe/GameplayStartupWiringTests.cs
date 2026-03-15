#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using R3;
using Runtime.GameModes.Wizard;
using Runtime.GameModes.Wizard.Configs;
using Runtime.GameModes.Wizard.Modes;
using Runtime.GameModes.Wizard.Online;
using Runtime.Gameplay;
using Runtime.Gameplay.ECS;
using Runtime.Gameplay.ECS.Lifecycle;
using Runtime.Gameplay.Shared;
using Runtime.Games.TicTacToe;
using Runtime.Games.TicTacToe.ECS;
using Runtime.Games.TicTacToe.Moves;
using Runtime.Games.TicTacToe.Rules;
using Runtime.Games.TicTacToe.AI;
using Runtime.Games.TicTacToe.AI.Core;
using Runtime.Games.TicTacToe.AI.Ultimate;
using Runtime.Games.TicTacToe.AI.Ultimate.Core;
using Runtime.Games.TicTacToe.Series;
using Runtime.Infrastructure.GameStateMachine;
using Runtime.Infrastructure.GameStateMachine.States;
using Runtime.PlayerStatistics;
using UnityEngine.UIElements;
using CellId = Runtime.Games.TicTacToe.Moves.CellId;
using EcsGameStatus = Runtime.Gameplay.Shared.GameStatus;

namespace Tests.EditMode.Games.TicTacToe
{
    /// <summary>
    /// Series wiring tests for <see cref="GameplayStartup"/> (ADR-10).
    /// TC-C1..C2 from MatchEcsLifecycle_TestPlan.md.
    /// </summary>
    [TestFixture]
    [Category("Unit")]
    public class GameplayStartupWiringTests
    {
        private IGameLaunchConfigStore _configStore = null!;
        private IGameService _gameService = null!;
        private IGameplayFieldPresenter _fieldPresenter = null!;
        private IGameplayFieldUiAdapter _fieldUiAdapter = null!;
        private IMatchEcsLifecycle _ecsLifecycle = null!;
        private IGameplayCommandSink _commandSink = null!;
        private IGameplaySnapshotProvider _snapshotProvider = null!;
        private ISeriesService _seriesService = null!;
        private IGameplayBackHandler _backHandler = null!;
        private IGameStateMachine _stateMachine = null!;

        private Subject<RoundFinishedEvent> _roundFinishedSubject = null!;
        private Label _player1ScoreLabel = null!;
        private Label _player2ScoreLabel = null!;
        private GameplayStartup _sut = null!;

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
            _snapshotProvider.GetAllCells().Returns(new System.Collections.Generic.List<CellSnapshot>());
            _seriesService = Substitute.For<ISeriesService>();
            _seriesService.Score.Returns(new ReactiveProperty<SeriesScore>(default));
            _backHandler = Substitute.For<IGameplayBackHandler>();
            _backHandler.HandleBackAsync(Arg.Any<CancellationToken>()).Returns(UniTask.CompletedTask);

            // UI stubs
            _player1ScoreLabel = new Label();
            _player2ScoreLabel = new Label();
            _fieldUiAdapter = Substitute.For<IGameplayFieldUiAdapter>();
            _fieldUiAdapter.CellClicks.Returns(new Subject<CellId>());
            _fieldUiAdapter.CurrentPlayerLabel.Returns(new Label());
            _fieldUiAdapter.FieldContainer.Returns(new VisualElement());
            _fieldUiAdapter.Player1Panel.Returns(new VisualElement());
            _fieldUiAdapter.Player2Panel.Returns(new VisualElement());
            _fieldUiAdapter.Player1ScoreLabel.Returns(_player1ScoreLabel);
            _fieldUiAdapter.Player2ScoreLabel.Returns(_player2ScoreLabel);

            // Event stream with controllable Subject
            var eventStream = Substitute.For<IGameplayEventStream>();
            _roundFinishedSubject = new Subject<RoundFinishedEvent>();
            eventStream.CellChanged.Returns(new Subject<CellChangedEvent>());
            eventStream.LastMoveChanged.Returns(new Subject<LastMoveChangedEvent>());
            eventStream.CurrentPlayerChanged.Returns(new Subject<CurrentPlayerChangedEvent>());
            eventStream.CommandRejected.Returns(new Subject<CommandRejectedEvent>());
            eventStream.RoundFinished.Returns(_roundFinishedSubject);

            var movesBinder = new GameplayMovesBinder(
                _fieldUiAdapter, _commandSink, eventStream, _snapshotProvider);
            var winLineRenderer = new WinLineRenderer(_fieldUiAdapter);

            // Config for StartAsync
            var config = new GameLaunchConfig("classic", new TicTacToeConfig(3), new LocalHumanConfig());
            var session = Substitute.For<IGameplaySession>();
            session.FieldRenderSpec.Returns(FieldRenderSpec.Classic(3));

            _configStore.TryConsume(out Arg.Any<GameLaunchConfig>()).Returns(callInfo =>
            {
                callInfo[0] = config;
                return true;
            });
            _configStore.TryPeek(out Arg.Any<GameLaunchConfig>()).Returns(callInfo =>
            {
                callInfo[0] = config;
                return true;
            });
            _gameService.StartMatchAsync(Arg.Any<GameLaunchConfig>(), Arg.Any<CancellationToken>())
                .Returns(UniTask.FromResult(session));
            _fieldPresenter.BindAsync(Arg.Any<FieldRenderSpec>(), Arg.Any<CancellationToken>(), Arg.Any<string>())
                .Returns(UniTask.CompletedTask);
            _stateMachine.EnterAsync<LoadMainMenuState>(Arg.Any<CancellationToken>())
                .Returns(UniTask.CompletedTask);

            var botDriver = Substitute.For<IBotTurnDriver>();
            botDriver.IsBusy.Returns(new ReactiveProperty<bool>(false));
            botDriver.IsDisabled.Returns(new ReactiveProperty<bool>(false));

            var ultimateBotOrchestrator = Substitute.For<IBotTurnOrchestrator>();
            ultimateBotOrchestrator.IsThinking.Returns(new ReactiveProperty<bool>(false));
            ultimateBotOrchestrator.MoveFailed.Returns(new Subject<BotMoveFailedEvent>());
            var matchFailSafeGateway = Substitute.For<IMatchFailSafeGateway>();
            var outcomeResolver = Substitute.For<IMatchOutcomeResolver>();
            var statisticsService = Substitute.For<IPlayerStatisticsService>();
            var contextStore = Substitute.For<IOnlineGameplaySessionContextStore>();
            contextStore.Snapshot.Returns(OnlineGameplaySessionSnapshot.Empty());
            var statisticsReporter = new PlayerStatisticsMatchReporter(
                _configStore,
                eventStream,
                outcomeResolver,
                statisticsService,
                contextStore,
                new MatchKeyMapper());

            _sut = new GameplayStartup(
                _configStore, _gameService, _fieldPresenter, _fieldUiAdapter,
                _ecsLifecycle, eventStream, _commandSink,
                movesBinder, winLineRenderer, _seriesService, _backHandler, _stateMachine,
                botDriver, ultimateBotOrchestrator, matchFailSafeGateway,
                statisticsReporter: statisticsReporter);
        }

        [TearDown]
        public void TearDown()
        {
            _sut?.Dispose();
        }

        // ── TC-C1: RoundFinished → RecordResult + UpdateScoreLabels ─

        [Test]
        public async Task WhenRoundFinishedEventReceived_ThenRecordsResultAndUpdatesScoreLabels()
        {
            // Arrange — start the gameplay so subscriptions are active
            await _sut.StartAsync(CancellationToken.None);

            // Configure mock so RecordResult updates Score (causal link)
            var scoreProperty = new ReactiveProperty<SeriesScore>(default);
            _seriesService.Score.Returns(scoreProperty);
            _seriesService.When(s => s.RecordResult(Arg.Any<GameResult>()))
                .Do(_ => scoreProperty.Value = new SeriesScore(1, 0, 0, 0));

            // Act — emit RoundFinished through the Subject
            var evt = new RoundFinishedEvent(
                EcsGameStatus.Win,
                TicTacToeEcsRegistrar.SlotX,
                new EcsWinLine(new CellId(0, 0), new CellId(0, 2)));
            _roundFinishedSubject.OnNext(evt);

            // Assert — verify causal chain: RoundFinished → RecordResult → Score update → UI update
            _seriesService.Received(1).RecordResult(
                Arg.Is<GameResult>(r =>
                    r.Status == Runtime.Games.TicTacToe.Rules.GameStatus.Win &&
                    r.Winner == PlayerMark.X &&
                    r.WinLine.HasValue));

            _player1ScoreLabel.text.Should().Be("1",
                "UpdateScoreLabels should reflect Score.Player1Wins after RecordResult");
            _player2ScoreLabel.text.Should().Be("0",
                "UpdateScoreLabels should reflect Score.Player2Wins after RecordResult");
        }

        [Test]
        public async Task WhenResultActionRestartReceived_ThenCallsNextRoundAndSubmitsRestartRoundCommandWithMappedSlot()
        {
            await _sut.StartAsync(CancellationToken.None);

            var submittedCommands = new System.Collections.Generic.List<IGameplayCommand>();
            _commandSink
                .When(sink => sink.SubmitCommand(Arg.Any<IGameplayCommand>()))
                .Do(callInfo => submittedCommands.Add(callInfo.Arg<IGameplayCommand>()));
            _seriesService.NextRound().Returns(PlayerMark.O);

            _sut.HandleResultAction(ResultAction.Restart);
            await UniTask.DelayFrame(1);

            _seriesService.Received(1).NextRound();
            submittedCommands.Should().ContainSingle(command =>
                command is RestartRoundCommand
                && ((RestartRoundCommand)command).StartingPlayerSlot == PlayerSlotMapping.SlotO);
        }
    }
}

