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

        [Test]
        public void WhenEnterFirstState_ThenUpdatesCurrentState()
        {
            // Arrange
            var mockState = Substitute.For<IState>();
            _stateFactory.CreateState<IState>().Returns(mockState);
            var stateMachine = new GameStateMachine(_stateFactory);

            // Act
            stateMachine.Enter<IState>();

            // Assert
            stateMachine.CurrentState.Should().NotBeNull();
            stateMachine.CurrentState.Should().Be(mockState);
        }

        [Test]
        public void WhenEnterNewState_ThenExitsOldState()
        {
            // Arrange
            var state1 = Substitute.For<IState>();
            var state2 = Substitute.For<IState>();
            _stateFactory.CreateState<IState>().Returns(state1, state2);
            var stateMachine = new GameStateMachine(_stateFactory);
            stateMachine.Enter<IState>();

            // Act
            stateMachine.Enter<IState>();

            // Assert
            state1.Received(1).Exit();
        }

        [Test]
        public void WhenEnterNewState_ThenCallsExitBeforeEnter()
        {
            // Arrange
            var state1 = Substitute.For<IState>();
            var state2 = Substitute.For<IState>();
            _stateFactory.CreateState<IState>().Returns(state1, state2);
            var stateMachine = new GameStateMachine(_stateFactory);
            stateMachine.Enter<IState>();
            
            state1.ClearReceivedCalls();
            state2.ClearReceivedCalls();
            _stateFactory.ClearReceivedCalls();

            // Act
            stateMachine.Enter<IState>();

            // Assert
            Received.InOrder(() =>
            {
                state1.Exit();
                _stateFactory.CreateState<IState>();
                state2.Enter();
            });
        }

        [Test]
        public void WhenEnterSameStateType_ThenCreatesNewInstance()
        {
            // Arrange
            var firstStateInstance = Substitute.For<IState>();
            var secondStateInstance = Substitute.For<IState>();
            _stateFactory.CreateState<IState>().Returns(firstStateInstance, secondStateInstance);
            var stateMachine = new GameStateMachine(_stateFactory);
            stateMachine.Enter<IState>();

            // Act
            stateMachine.Enter<IState>();

            // Assert
            firstStateInstance.Received(1).Exit();
            _stateFactory.Received(2).CreateState<IState>();
            stateMachine.CurrentState.Should().NotBe(firstStateInstance);
            stateMachine.CurrentState.Should().Be(secondStateInstance);
        }

        [Test]
        public void WhenEnterMultipleStates_ThenEachTransitionWorksCorrectly()
        {
            // Arrange
            var state1 = Substitute.For<IState>();
            var state2 = Substitute.For<IState>();
            var state3 = Substitute.For<IState>();
            _stateFactory.CreateState<IState>().Returns(state1, state2, state3);
            var stateMachine = new GameStateMachine(_stateFactory);

            // Act
            stateMachine.Enter<IState>();
            stateMachine.Enter<IState>(); 
            stateMachine.Enter<IState>();

            // Assert
            state1.Received(1).Exit();
            state2.Received(1).Exit();
            state3.DidNotReceive().Exit();
            stateMachine.CurrentState.Should().Be(state3);
        }

        [Test]
        public void WhenCurrentState_ThenReflectsLastEnteredState()
        {
            // Arrange
            var state1 = Substitute.For<IState>();
            var state2 = Substitute.For<IState>();
            var state3 = Substitute.For<IState>();
            _stateFactory.CreateState<IState>().Returns(state1, state2, state3);
            var stateMachine = new GameStateMachine(_stateFactory);

            // Act & Assert
            stateMachine.Enter<IState>();
            stateMachine.CurrentState.Should().Be(state1);

            stateMachine.Enter<IState>();
            stateMachine.CurrentState.Should().Be(state2);

            stateMachine.Enter<IState>();
            stateMachine.CurrentState.Should().Be(state3);
        }

        [Test]
        public void WhenEnterStateWithPayload_ThenPassesPayloadToEnter()
        {
            // Arrange
            var payloadState = Substitute.For<IPayloadedState<string>>();
            _stateFactory.CreateState<IPayloadedState<string>>().Returns(payloadState);
            var stateMachine = new GameStateMachine(_stateFactory);
            const string testPayload = "TestData";

            // Act
            stateMachine.Enter<IPayloadedState<string>, string>(testPayload);

            // Assert
            payloadState.Received(1).Enter(testPayload);
            payloadState.Received(1).Enter(Arg.Is<string>(p => p == testPayload));
        }
    }
}