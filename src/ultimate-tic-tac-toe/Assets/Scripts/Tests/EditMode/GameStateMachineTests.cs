using System;
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

        [Test]
        public void WhenEnterStateWithPayload_ThenCallsExitThenEnter()
        {
            // Arrange
            var state1 = Substitute.For<IState>();
            var payloadState = Substitute.For<IPayloadedState<int>>();
            _stateFactory.CreateState<IState>().Returns(state1);
            _stateFactory.CreateState<IPayloadedState<int>>().Returns(payloadState);
            var stateMachine = new GameStateMachine(_stateFactory);
            stateMachine.Enter<IState>();

            state1.ClearReceivedCalls();
            _stateFactory.ClearReceivedCalls();

            // Act
            stateMachine.Enter<IPayloadedState<int>, int>(42);

            // Assert
            Received.InOrder(() =>
            {
                state1.Exit();
                payloadState.Enter(42);
            });
            
            payloadState.Received(1).Enter(42);
        }

        [Test]
        public void WhenEnterStateWithPayloadWithNull_ThenHandlesCorrectly()
        {
            // Arrange
            var payloadState = Substitute.For<IPayloadedState<string>>();
            _stateFactory.CreateState<IPayloadedState<string>>().Returns(payloadState);
            var stateMachine = new GameStateMachine(_stateFactory);

            // Act
            Action act = () => stateMachine.Enter<IPayloadedState<string>, string>(null);

            // Assert
            act.Should().NotThrow();
            payloadState.Received(1).Enter(null);
        }

        [Test]
        public void WhenEnterMultipleStatesWithPayload_ThenIsolatesData()
        {
            // Arrange
            const string payload1 = "payload1";
            const int payload2 = 42;
            const string payload3 = "payload3";
            var state1 = Substitute.For<IPayloadedState<string>>();
            var state2 = Substitute.For<IPayloadedState<int>>();
            var state3 = Substitute.For<IPayloadedState<string>>();
            _stateFactory.CreateState<IPayloadedState<string>>().Returns(state1, state3);
            _stateFactory.CreateState<IPayloadedState<int>>().Returns(state2);
            var stateMachine = new GameStateMachine(_stateFactory);

            // Act

            stateMachine.Enter<IPayloadedState<string>, string>(payload1);
            stateMachine.Enter<IPayloadedState<int>, int>(payload2);
            stateMachine.Enter<IPayloadedState<string>, string>(payload3);

            // Assert
            state1.Received(1).Enter(payload1);
            state1.DidNotReceive().Enter(payload3);
            state1.DidNotReceive().Enter(Arg.Is<string>(p => p != payload1));
            
            state2.Received(1).Enter(payload2);
            state2.DidNotReceive().Enter(Arg.Is<int>(p => p != payload2));
            
            state3.Received(1).Enter(payload3);
            state3.DidNotReceive().Enter(payload1);
            state3.DidNotReceive().Enter(Arg.Is<string>(p => p != payload3));
        }

        [Test]
        public void WhenFactoryReturnsNull_ThenThrowsInvalidOperationException()
        {
            // Arrange
            _stateFactory.CreateState<IState>().Returns((IState)null);
            var stateMachine = new GameStateMachine(_stateFactory);

            // Act
            Action act = () => stateMachine.Enter<IState>();

            // Assert
            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*StateFactory returned null*");
        }

        [Test]
        public void WhenFactoryReturnsNullForPayloadState_ThenThrowsInvalidOperationException()
        {
            // Arrange
            _stateFactory.CreateState<IPayloadedState<int>>().Returns((IPayloadedState<int>)null);
            var stateMachine = new GameStateMachine(_stateFactory);

            // Act
            Action act = () => stateMachine.Enter<IPayloadedState<int>, int>(42);

            // Assert
            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*StateFactory returned null*");
        }
    }
}