using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using Runtime.Infrastructure.States;

namespace Tests.EditMode
{
    [TestFixture]
    public class GameStateMachineTests
    {
        private IStateFactory _stateFactory;

        [SetUp]
        public void SetUp() => _stateFactory = Substitute.For<IStateFactory>();

        [Test]
        public void WhenConstructor_ThenSetsCurrentStateToNull()
        {
            // Arrange & Act
            var stateMachine = new GameStateMachine(_stateFactory);

            // Assert
            stateMachine.CurrentState.Should().BeNull();
        }

        [Test]
        public void WhenEnterFirstState_ThenCreatesStateAndCallsEnter()
        {
            // Arrange
            var mockState = Substitute.For<IState>();
            _stateFactory.CreateState<IState>().Returns(mockState);
            var stateMachine = new GameStateMachine(_stateFactory);

            // Act
            stateMachine.Enter<IState>();

            // Assert
            _stateFactory.Received(1).CreateState<IState>();
            mockState.Received(1).Enter();
            stateMachine.CurrentState.Should().Be(mockState);
        }
    }
}