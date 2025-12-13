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
    public class GameStateMachineTests
    {
        private IStateFactory _stateFactory;
        private CancellationToken _cancellationToken;

        [SetUp]
        public void SetUp()
        {
            _stateFactory = Substitute.For<IStateFactory>();
            _cancellationToken = CancellationToken.None;
        }

        #region Initialization Tests

        [Test]
        public void WhenConstructor_ThenSetsCurrentStateToNull()
        {
            var stateMachine = new GameStateMachine(_stateFactory);

            stateMachine.CurrentState.Should().BeNull();
        }

        #endregion

        #region Basic State Transitions Tests

        [Test]
        public async Task WhenEnterFirstState_ThenCreatesStateAndCallsEnter()
        {
            var mockState = Substitute.For<IState>();
            mockState.EnterAsync(Arg.Any<CancellationToken>()).Returns(UniTask.CompletedTask);
            _stateFactory.CreateState<IState>().Returns(mockState);
            var stateMachine = new GameStateMachine(_stateFactory);

            await stateMachine.EnterAsync<IState>(_cancellationToken);

            _stateFactory.Received(1).CreateState<IState>();
            await mockState.Received(1).EnterAsync(_cancellationToken);
            stateMachine.CurrentState.Should().Be(mockState);
        }

        [Test]
        public async Task WhenEnterFirstState_ThenUpdatesCurrentState()
        {
            var mockState = Substitute.For<IState>();
            mockState.EnterAsync(Arg.Any<CancellationToken>()).Returns(UniTask.CompletedTask);
            _stateFactory.CreateState<IState>().Returns(mockState);
            var stateMachine = new GameStateMachine(_stateFactory);

            await stateMachine.EnterAsync<IState>(_cancellationToken);

            stateMachine.CurrentState.Should().NotBeNull();
            stateMachine.CurrentState.Should().Be(mockState);
        }

        [Test]
        public async Task WhenEnterNewState_ThenExitsOldState()
        {
            var state1 = Substitute.For<IState>();
            var state2 = Substitute.For<IState>();
            state1.EnterAsync(Arg.Any<CancellationToken>()).Returns(UniTask.CompletedTask);
            state2.EnterAsync(Arg.Any<CancellationToken>()).Returns(UniTask.CompletedTask);
            _stateFactory.CreateState<IState>().Returns(state1, state2);
            var stateMachine = new GameStateMachine(_stateFactory);
            await stateMachine.EnterAsync<IState>(_cancellationToken);

            await stateMachine.EnterAsync<IState>(_cancellationToken);

            state1.Received(1).Exit();
        }

        #endregion

        #region State Lifecycle & Order Tests

        [Test]
        public async Task WhenEnterNewState_ThenCallsExitBeforeEnter()
        {
            var state1 = Substitute.For<IState>();
            var state2 = Substitute.For<IState>();
            state1.EnterAsync(Arg.Any<CancellationToken>()).Returns(UniTask.CompletedTask);
            state2.EnterAsync(Arg.Any<CancellationToken>()).Returns(UniTask.CompletedTask);
            _stateFactory.CreateState<IState>().Returns(state1, state2);
            var stateMachine = new GameStateMachine(_stateFactory);
            await stateMachine.EnterAsync<IState>(_cancellationToken);
            
            state1.ClearReceivedCalls();
            state2.ClearReceivedCalls();
            _stateFactory.ClearReceivedCalls();

            await stateMachine.EnterAsync<IState>(_cancellationToken);

            Received.InOrder(() =>
            {
                state1.Exit();
                _stateFactory.CreateState<IState>();
                state2.EnterAsync(_cancellationToken);
            });
        }

        [Test]
        public async Task WhenEnterSameStateType_ThenCreatesNewInstance()
        {
            var firstStateInstance = Substitute.For<IState>();
            var secondStateInstance = Substitute.For<IState>();
            firstStateInstance.EnterAsync(Arg.Any<CancellationToken>()).Returns(UniTask.CompletedTask);
            secondStateInstance.EnterAsync(Arg.Any<CancellationToken>()).Returns(UniTask.CompletedTask);
            _stateFactory.CreateState<IState>().Returns(firstStateInstance, secondStateInstance);
            var stateMachine = new GameStateMachine(_stateFactory);
            await stateMachine.EnterAsync<IState>(_cancellationToken);

            await stateMachine.EnterAsync<IState>(_cancellationToken);

            firstStateInstance.Received(1).Exit();
            _stateFactory.Received(2).CreateState<IState>();
            stateMachine.CurrentState.Should().NotBe(firstStateInstance);
            stateMachine.CurrentState.Should().Be(secondStateInstance);
        }

        [Test]
        public async Task WhenEnterMultipleStates_ThenEachTransitionWorksCorrectly()
        {
            var state1 = Substitute.For<IState>();
            var state2 = Substitute.For<IState>();
            var state3 = Substitute.For<IState>();
            state1.EnterAsync(Arg.Any<CancellationToken>()).Returns(UniTask.CompletedTask);
            state2.EnterAsync(Arg.Any<CancellationToken>()).Returns(UniTask.CompletedTask);
            state3.EnterAsync(Arg.Any<CancellationToken>()).Returns(UniTask.CompletedTask);
            _stateFactory.CreateState<IState>().Returns(state1, state2, state3);
            var stateMachine = new GameStateMachine(_stateFactory);

            await stateMachine.EnterAsync<IState>(_cancellationToken);
            await stateMachine.EnterAsync<IState>(_cancellationToken); 
            await stateMachine.EnterAsync<IState>(_cancellationToken);

            state1.Received(1).Exit();
            state2.Received(1).Exit();
            state3.DidNotReceive().Exit();
            stateMachine.CurrentState.Should().Be(state3);
        }

        #endregion

        #region CurrentState Management Tests

        [Test]
        public async Task WhenCurrentState_ThenReflectsLastEnteredState()
        {
            var state1 = Substitute.For<IState>();
            var state2 = Substitute.For<IState>();
            var state3 = Substitute.For<IState>();
            state1.EnterAsync(Arg.Any<CancellationToken>()).Returns(UniTask.CompletedTask);
            state2.EnterAsync(Arg.Any<CancellationToken>()).Returns(UniTask.CompletedTask);
            state3.EnterAsync(Arg.Any<CancellationToken>()).Returns(UniTask.CompletedTask);
            _stateFactory.CreateState<IState>().Returns(state1, state2, state3);
            var stateMachine = new GameStateMachine(_stateFactory);

            await stateMachine.EnterAsync<IState>(_cancellationToken);
            stateMachine.CurrentState.Should().Be(state1);

            await stateMachine.EnterAsync<IState>(_cancellationToken);
            stateMachine.CurrentState.Should().Be(state2);

            await stateMachine.EnterAsync<IState>(_cancellationToken);
            stateMachine.CurrentState.Should().Be(state3);
        }

        #endregion

        #region Payload State Tests

        [Test]
        public async Task WhenEnterStateWithPayload_ThenPassesPayloadToEnter()
        {
            const string testPayload = "TestData";
            var payloadState = Substitute.For<IPayloadedState<string>>();
            payloadState.EnterAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(UniTask.CompletedTask);
            _stateFactory.CreateState<IPayloadedState<string>>().Returns(payloadState);
            var stateMachine = new GameStateMachine(_stateFactory);

            await stateMachine.EnterAsync<IPayloadedState<string>, string>(testPayload, _cancellationToken);

            await payloadState.Received(1).EnterAsync(testPayload, _cancellationToken);
        }

        [Test]
        public async Task WhenEnterStateWithPayload_ThenCallsExitThenEnter()
        {
            var state1 = Substitute.For<IState>();
            var payloadState = Substitute.For<IPayloadedState<int>>();
            state1.EnterAsync(Arg.Any<CancellationToken>()).Returns(UniTask.CompletedTask);
            payloadState.EnterAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(UniTask.CompletedTask);
            _stateFactory.CreateState<IState>().Returns(state1);
            _stateFactory.CreateState<IPayloadedState<int>>().Returns(payloadState);
            var stateMachine = new GameStateMachine(_stateFactory);
            await stateMachine.EnterAsync<IState>(_cancellationToken);

            state1.ClearReceivedCalls();
            _stateFactory.ClearReceivedCalls();

            await stateMachine.EnterAsync<IPayloadedState<int>, int>(42, _cancellationToken);

            Received.InOrder(() =>
            {
                state1.Exit();
                payloadState.EnterAsync(42, _cancellationToken);
            });
            
            await payloadState.Received(1).EnterAsync(42, _cancellationToken);
        }

        [Test]
        public async Task WhenEnterStateWithPayloadWithNull_ThenHandlesCorrectly()
        {
            var payloadState = Substitute.For<IPayloadedState<string>>();
            payloadState.EnterAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(UniTask.CompletedTask);
            _stateFactory.CreateState<IPayloadedState<string>>().Returns(payloadState);
            var stateMachine = new GameStateMachine(_stateFactory);

            Func<Task> act = () => stateMachine.EnterAsync<IPayloadedState<string>, string>(null, _cancellationToken).AsTask();

            await act.Should().NotThrowAsync();
            await payloadState.Received(1).EnterAsync(null, _cancellationToken);
        }

        [Test]
        public async Task WhenEnterMultipleStatesWithPayload_ThenIsolatesData()
        {
            const string payload1 = "payload1";
            const int payload2 = 42;
            const string payload3 = "payload3";
            var state1 = Substitute.For<IPayloadedState<string>>();
            var state2 = Substitute.For<IPayloadedState<int>>();
            var state3 = Substitute.For<IPayloadedState<string>>();
            state1.EnterAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(UniTask.CompletedTask);
            state2.EnterAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(UniTask.CompletedTask);
            state3.EnterAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(UniTask.CompletedTask);
            _stateFactory.CreateState<IPayloadedState<string>>().Returns(state1, state3);
            _stateFactory.CreateState<IPayloadedState<int>>().Returns(state2);
            var stateMachine = new GameStateMachine(_stateFactory);

            await stateMachine.EnterAsync<IPayloadedState<string>, string>(payload1, _cancellationToken);
            await stateMachine.EnterAsync<IPayloadedState<int>, int>(payload2, _cancellationToken);
            await stateMachine.EnterAsync<IPayloadedState<string>, string>(payload3, _cancellationToken);

            await state1.Received(1).EnterAsync(payload1, _cancellationToken);
            await state1.DidNotReceive().EnterAsync(payload3, _cancellationToken);
            await state1.DidNotReceive().EnterAsync(Arg.Is<string>(p => p != payload1), _cancellationToken);
            
            await state2.Received(1).EnterAsync(payload2, _cancellationToken);
            await state2.DidNotReceive().EnterAsync(Arg.Is<int>(p => p != payload2), _cancellationToken);
            
            await state3.Received(1).EnterAsync(payload3, _cancellationToken);
            await state3.DidNotReceive().EnterAsync(payload1, _cancellationToken);
            await state3.DidNotReceive().EnterAsync(Arg.Is<string>(p => p != payload3), _cancellationToken);
        }

        #endregion

        #region Error Handling Tests

        [Test]
        public async Task WhenFactoryReturnsNull_ThenThrowsInvalidOperationException()
        {
            _stateFactory.CreateState<IState>().Returns((IState)null);
            var stateMachine = new GameStateMachine(_stateFactory);

            Func<Task> act = () => stateMachine.EnterAsync<IState>(_cancellationToken).AsTask();

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*StateFactory returned null*");
        }

        [Test]
        public async Task WhenFactoryReturnsNullForPayloadState_ThenThrowsInvalidOperationException()
        {
            _stateFactory.CreateState<IPayloadedState<int>>().Returns((IPayloadedState<int>)null);
            var stateMachine = new GameStateMachine(_stateFactory);

            Func<Task> act = () => stateMachine.EnterAsync<IPayloadedState<int>, int>(42, _cancellationToken).AsTask();

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*StateFactory returned null*");
        }

        [Test]
        public async Task WhenPreviousStateExitThrows_ThenPropagatesException()
        {
            const string expectedWildcardPattern = "Exit failed";
            var state1 = Substitute.For<IState>();
            var state2 = Substitute.For<IState>();
            var expectedException = new InvalidOperationException(expectedWildcardPattern);
            
            state1.EnterAsync(Arg.Any<CancellationToken>()).Returns(UniTask.CompletedTask);
            state2.EnterAsync(Arg.Any<CancellationToken>()).Returns(UniTask.CompletedTask);
            state1.When(x => x.Exit()).Do(_ => throw expectedException);
            
            _stateFactory.CreateState<IState>().Returns(state1, state2);
            var stateMachine = new GameStateMachine(_stateFactory);
            
            await stateMachine.EnterAsync<IState>(_cancellationToken);
            var previousState = stateMachine.CurrentState;
            
            Func<Task> act = () => stateMachine.EnterAsync<IState>(_cancellationToken).AsTask();
            
            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage(expectedWildcardPattern);

            stateMachine.CurrentState.Should().BeSameAs(previousState);
            await state2.DidNotReceive().EnterAsync(Arg.Any<CancellationToken>());
        }

        [Test]
        public async Task WhenNewStateEnterThrows_ThenPropagatesException()
        {
            const string expectedExceptionMessage = "Enter failed";
            var state1 = Substitute.For<IState>();
            var state2 = Substitute.For<IState>();
            var expectedException = new InvalidOperationException(expectedExceptionMessage);
            
            state1.EnterAsync(Arg.Any<CancellationToken>()).Returns(UniTask.CompletedTask);
            state2.EnterAsync(Arg.Any<CancellationToken>()).Returns(UniTask.FromException(expectedException));
            
            _stateFactory.CreateState<IState>().Returns(state1, state2);
            var stateMachine = new GameStateMachine(_stateFactory);
            
            await stateMachine.EnterAsync<IState>(_cancellationToken);
            
            Func<Task> act = () => stateMachine.EnterAsync<IState>(_cancellationToken).AsTask();
            
            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage(expectedExceptionMessage);

            stateMachine.CurrentState.Should().BeSameAs(state2);
            state1.Received(1).Exit();
        }

        #endregion
    }
}