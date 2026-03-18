using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Runtime.Infrastructure.GameStateMachine.States;

namespace Runtime.Infrastructure.GameStateMachine
{
    public class GameStateMachine : IGameStateMachine
    {
        private readonly IStateFactory _stateFactory;

        public IExitableState CurrentState { get; private set; }

        public GameStateMachine(IStateFactory stateFactory) => _stateFactory = stateFactory;

        public async UniTask EnterAsync<TState>(CancellationToken cancellationToken = default) where TState : class, IState
        {
            cancellationToken.ThrowIfCancellationRequested();
            var newState = ChangeState<TState>(cancellationToken);
            await newState.EnterAsync(cancellationToken);
        }

        public async UniTask EnterAsync<TState, TPayload>(TPayload payload, CancellationToken cancellationToken = default) where TState : class, IPayloadedState<TPayload>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var newState = ChangeState<TState>(cancellationToken);
            await newState.EnterAsync(payload, cancellationToken);
        }

        private TState ChangeState<TState>(CancellationToken cancellationToken) where TState : class, IExitableState
        {
            cancellationToken.ThrowIfCancellationRequested();
            CurrentState?.Exit();
            var state = _stateFactory.CreateState<TState>();

            CurrentState = state ?? throw new InvalidOperationException($"StateFactory returned null for state type {typeof(TState).Name}");
            return state;
        }
    }
}