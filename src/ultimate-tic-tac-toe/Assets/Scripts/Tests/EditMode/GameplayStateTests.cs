using System;
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

        [SetUp]
        public void SetUp()
        {
            _stateMachineMock = Substitute.For<IGameStateMachine>();
            _sut = new GameplayState(_stateMachineMock);
        }

        [Test]
        public void WhenReturnToMainMenu_ThenTransitionsToLoadMainMenuState()
        {
            _sut.ReturnToMainMenu();

            _stateMachineMock.Received(1).Enter<LoadMainMenuState>();
        }

        [Test]
        public void WhenEnter_ThenCompletesWithoutError()
        {
            Action act = () => _sut.Enter();

            act.Should().NotThrow();
        }

        [Test]
        public void WhenExit_ThenCompletesWithoutError()
        {
            Action act = () => _sut.Exit();

            act.Should().NotThrow();
        }
    }
}