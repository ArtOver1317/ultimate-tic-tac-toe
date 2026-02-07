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
using Runtime.Gameplay.Moves;
using Runtime.GameModes.Wizard;
using Runtime.Infrastructure.GameStateMachine;
using Runtime.Infrastructure.GameStateMachine.States;
using UnityEngine;
using UnityEngine.TestTools;
using R3;
using UnityEngine.UIElements;

namespace Tests.EditMode.Gameplay
{
    [TestFixture]
    [Category("Unit")]
    public class GameplayStartupTests
    {
        private IGameLaunchConfigStore _configStore;
        private IGameService _gameService;
        private IGameplayFieldPresenter _fieldPresenter;
        private IGameStateMachine _stateMachine;
        private ILocalMovesService _localMoves;
        private GameplayMovesBinder _movesBinder;
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

            _localMoves = Substitute.For<ILocalMovesService>();

            var isStarted = new ReactiveProperty<bool>(false);
            var currentPlayer = new ReactiveProperty<PlayerMark>(PlayerMark.None);

            _localMoves.IsStarted.Returns(isStarted);
            _localMoves.CurrentPlayer.Returns(currentPlayer);
            _localMoves.CellChanged.Returns(new Subject<CellChangedEvent>());
            _localMoves.LastMoveChanged.Returns(new Subject<LastMoveChangedEvent>());
            _localMoves.ClickRejected.Returns(new Subject<ClickRejectedEvent>());
            _localMoves.GetAllCells().Returns(new List<CellValue>());

            _localMoves
                .When(x => x.Start(Arg.Any<LocalMovesConfig>()))
                .Do(callInfo =>
                {
                    var cfg = callInfo.ArgAt<LocalMovesConfig>(0);
                    isStarted.Value = true;
                    currentPlayer.Value = cfg.StartingPlayer == PlayerMark.X || cfg.StartingPlayer == PlayerMark.O
                        ? cfg.StartingPlayer
                        : PlayerMark.X;
                });

            _localMoves
                .When(x => x.Stop())
                .Do(_ =>
                {
                    isStarted.Value = false;
                    currentPlayer.Value = PlayerMark.None;
                });

            var ui = Substitute.For<IGameplayFieldUiAdapter>();
            ui.CellClicks.Returns(new Subject<CellId>());
            ui.CurrentPlayerLabel.Returns(new Label());
            _movesBinder = new GameplayMovesBinder(ui, _localMoves);

            _config = new GameLaunchConfig("classic", new ClassicModeConfig(3), new LocalHumanConfig());
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

            _sut = new GameplayStartup(_configStore, _gameService, _fieldPresenter, _localMoves, _movesBinder, _stateMachine);
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
            _localMoves.Received(1).Stop();
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
            _localMoves.Received(1).Stop();
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
            _localMoves.Received(1).Start(Arg.Any<LocalMovesConfig>());

            _fieldPresenter.DidNotReceive().Unbind();
            _localMoves.DidNotReceive().Stop();
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
            _localMoves.Received(1).Stop();
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
            _localMoves.Received(1).Stop();
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
            _localMoves.Received(1).Stop();
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
            _localMoves.Received(1).Stop();
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
            _localMoves.Received(1).Stop();
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
            _localMoves.Received(1).Stop();
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
                "любой лишний Error/Exception/Assert лог должен валить тест");

            for (var i = 0; i < expectedFailingLogs.Length; i++)
            {
                var regex = expectedFailingLogs[i];
                regex.IsMatch(messages[i]).Should().BeTrue(
                    $"expected failing log #{i + 1} to match regex '{regex}', but was: {messages[i]}");
            }
        }

    }
}
