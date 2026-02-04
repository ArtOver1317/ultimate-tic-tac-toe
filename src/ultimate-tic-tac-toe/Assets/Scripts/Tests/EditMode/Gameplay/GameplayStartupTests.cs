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
using Runtime.GameModes.Wizard;
using Runtime.Infrastructure.GameStateMachine;
using Runtime.Infrastructure.GameStateMachine.States;
using UnityEngine;
using UnityEngine.TestTools;

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

            _sut = new GameplayStartup(_configStore, _gameService, _fieldPresenter, _stateMachine);
        }

        [TearDown]
        public void TearDown() => _sut = null;

        [Test]
        public async Task WhenLaunchConfigMissing_ThenUnbindDisposesAndReturnsToMainMenu()
        {
            // Arrange
            _configStore.TryConsume(out Arg.Any<GameLaunchConfig>()).Returns(false);

            // Act
            Func<Task> act = () => _sut.StartAsync(CancellationToken.None).AsTask();

            // Assert
            await RunAllowingFailingLogsAsync(act,
                new Regex(
                    @"(\[Error\]\s*)?\[Infrastructure\] \[GameplayStartup\] INVALID_CONFIG: Launch config not found\.\s*$",
                    RegexOptions.CultureInvariant));
            _fieldPresenter.Received(1).Unbind();
            _gameService.Received(1).Dispose();
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
            Func<Task> act = () => _sut.StartAsync(CancellationToken.None).AsTask();

            // Assert
            await RunAllowingFailingLogsAsync(act,
                new Regex(
                    @"(\[Error\]\s*)?\[Infrastructure\] \[GameplayStartup\] INVALID_CONFIG: Launch config not found\.\s*$",
                    RegexOptions.CultureInvariant));
            _fieldPresenter.Received(1).Unbind();
            _gameService.Received(1).Dispose();
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
            Func<Task> act = () => _sut.StartAsync(CancellationToken.None).AsTask();

            // Assert
            await act.Should().NotThrowAsync();
            await _gameService.Received(1).StartMatchAsync(Arg.Any<GameLaunchConfig>(), Arg.Any<CancellationToken>());
            await _fieldPresenter.Received(1).BindAsync(Arg.Any<FieldRenderSpec>(), Arg.Any<CancellationToken>());

            _fieldPresenter.DidNotReceive().Unbind();
            _gameService.DidNotReceive().Dispose();
            await _stateMachine.DidNotReceive().EnterAsync<LoadMainMenuState>(Arg.Any<CancellationToken>());
        }

        [Test]
        public async Task WhenStartMatchThrowsInvalidOperationException_ThenUnbindDisposesAndReturnsToMainMenu()
        {
            // Arrange
            _gameService.StartMatchAsync(Arg.Any<GameLaunchConfig>(), Arg.Any<CancellationToken>())
                .Returns(UniTask.FromException<IGameplaySession>(new InvalidOperationException("invalid")));

            // Act
            Func<Task> act = () => _sut.StartAsync(CancellationToken.None).AsTask();

            // Assert
            await RunAllowingFailingLogsAsync(act,
                new Regex(
                    @"(\[Error\]\s*)?\[Infrastructure\] \[GameplayStartup\] Failed to start gameplay:.*",
                    RegexOptions.CultureInvariant | RegexOptions.Singleline),
                new Regex(
                    @"(\[Error\]\s*)?\[Infrastructure\] \[GameplayStartup\] INVALID_CONFIG: invalid\s*$",
                    RegexOptions.CultureInvariant));
            _fieldPresenter.Received(1).Unbind();
            _gameService.Received(1).Dispose();
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
            Func<Task> act = () => _sut.StartAsync(CancellationToken.None).AsTask();

            // Assert
            await RunAllowingFailingLogsAsync(act,
                new Regex(
                    @"(\[Error\]\s*)?\[Infrastructure\] \[GameplayStartup\] Failed to start gameplay:.*",
                    RegexOptions.CultureInvariant | RegexOptions.Singleline),
                new Regex(
                    @"(\[Error\]\s*)?\[Infrastructure\] \[GameplayStartup\] INVALID_CONFIG:[\s\S]*boardSize\s*$",
                    RegexOptions.CultureInvariant));
            _fieldPresenter.Received(1).Unbind();
            _gameService.Received(1).Dispose();
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
            Func<Task> act = () => _sut.StartAsync(CancellationToken.None).AsTask();

            // Assert
            await RunAllowingFailingLogsAsync(act,
                new Regex(
                    @"(\[Error\]\s*)?\[Infrastructure\] \[GameplayStartup\] Failed to start gameplay:.*",
                    RegexOptions.CultureInvariant | RegexOptions.Singleline),
                new Regex(
                    @"(\[Error\]\s*)?\[Infrastructure\] \[GameplayStartup\] BUILD_FAILED: boom\s*$",
                    RegexOptions.CultureInvariant));
            _fieldPresenter.Received(1).Unbind();
            _gameService.Received(1).Dispose();
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
            Func<Task> act = () => _sut.StartAsync(CancellationToken.None).AsTask();

            // Assert
            await RunAllowingFailingLogsAsync(act,
                new Regex(
                    @"(\[Error\]\s*)?\[Infrastructure\] \[GameplayStartup\] Failed to start gameplay:.*",
                    RegexOptions.CultureInvariant | RegexOptions.Singleline),
                new Regex(
                    @"(\[Error\]\s*)?\[Infrastructure\] \[GameplayStartup\] BUILD_FAILED: bind failed\s*$",
                    RegexOptions.CultureInvariant));
            _fieldPresenter.Received(1).Unbind();
            _gameService.Received(1).Dispose();
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
            Func<Task> act = () => _sut.StartAsync(cts.Token).AsTask();

            // Assert
            await act.Should().ThrowAsync<OperationCanceledException>();
            _fieldPresenter.Received(1).Unbind();
            _gameService.Received(1).Dispose();
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
            Func<Task> act = () => _sut.StartAsync(cts.Token).AsTask();

            // Assert
            await act.Should().ThrowAsync<OperationCanceledException>();
            _fieldPresenter.Received(1).Unbind();
            _gameService.Received(1).Dispose();
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
