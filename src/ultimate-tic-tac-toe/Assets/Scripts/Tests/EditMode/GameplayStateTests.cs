using System;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using Runtime.Infrastructure.GameStateMachine;
using Runtime.Infrastructure.GameStateMachine.States;

namespace Tests.EditMode
{
    [TestFixture]
    public class GameplayStateTests
    {
        private IGameStateMachine _stateMachineMock;
        private GameplayState _sut;
        private CancellationToken _cancellationToken;

        [SetUp]
        public void SetUp()
        {
            _stateMachineMock = Substitute.For<IGameStateMachine>();
            _sut = new GameplayState(_stateMachineMock);
            _cancellationToken = CancellationToken.None;
        }

        [Test]
        public async Task WhenReturnToMainMenu_ThenTransitionsToLoadMainMenuState()
        {
            // Arrange
            _stateMachineMock.EnterAsync<LoadMainMenuState>(Arg.Any<CancellationToken>()).Returns(UniTask.CompletedTask);

            // Act
            await _sut.ReturnToMainMenuAsync(_cancellationToken);

            // Assert
            await _stateMachineMock.Received(1).EnterAsync<LoadMainMenuState>(Arg.Any<CancellationToken>());
        }

        [Test]
        public async Task WhenEnter_ThenCompletesWithoutError()
        {
            // Arrange
            Func<Task> act = () => _sut.EnterAsync(_cancellationToken).AsTask();

            // Assert
            await act.Should().NotThrowAsync();
        }

        [Test]
        public void WhenExit_ThenCompletesWithoutError()
        {
            // Arrange
            Action act = () => _sut.Exit();

            // Assert
            act.Should().NotThrow();
        }
    }
}