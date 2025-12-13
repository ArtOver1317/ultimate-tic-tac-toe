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
    public class BootstrapStateTests
    {
        private IGameStateMachine _stateMachineMock;
        private BootstrapState _sut;
        private CancellationToken _cancellationToken;

        [SetUp]
        public void SetUp()
        {
            _stateMachineMock = Substitute.For<IGameStateMachine>();
            _sut = new BootstrapState(_stateMachineMock);
            _cancellationToken = CancellationToken.None;
        }

        [Test]
        public async Task WhenEnter_ThenTransitionsToLoadMainMenuState()
        {
            // Arrange
            _stateMachineMock.EnterAsync<LoadMainMenuState>(Arg.Any<CancellationToken>()).Returns(UniTask.CompletedTask);

            // Act
            await _sut.EnterAsync(_cancellationToken);

            // Assert
            await _stateMachineMock.Received(1).EnterAsync<LoadMainMenuState>(Arg.Any<CancellationToken>());
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