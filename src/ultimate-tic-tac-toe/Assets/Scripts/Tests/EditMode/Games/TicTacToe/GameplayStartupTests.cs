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
using Runtime.Games.TicTacToe.Series;
using Runtime.GameModes.Wizard;
using Runtime.Infrastructure.GameStateMachine;
using Runtime.Infrastructure.GameStateMachine.States;
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

            _fieldUiAdapter = Substitute.For<IGameplayFieldUiAdapter>();
            _fieldUiAdapter.CellClicks.Returns(new Subject<CellId>());
            _fieldUiAdapter.CurrentPlayerLabel.Returns(new Label());
            _fieldUiAdapter.FieldContainer.Returns(new VisualElement());
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

            _gameService.StartMatchAsync(Arg.Any<GameLaunchConfig>(), Arg.Any<CancellationToken>())
                .Returns(UniTask.FromResult(_session));

            _fieldPresenter.BindAsync(Arg.Any<FieldRenderSpec>(), Arg.Any<CancellationToken>())
                .Returns(UniTask.CompletedTask);

            _stateMachine.EnterAsync<LoadMainMenuState>(Arg.Any<CancellationToken>())
                .Returns(UniTask.CompletedTask);

            _sut = new GameplayStartup(_configStore, _gameService, _fieldPresenter, _fieldUiAdapter, _ecsLifecycle, _eventStream, _commandSink, _movesBinder, _winLineRenderer, _seriesService, _backHandler, _stateMachine);
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

    }
}
