using System;
using System.Collections.Generic;

namespace Runtime.Infrastructure.States
{
    public class GameStateMachine : IGameStateMachine
    {
        private readonly IStateFactory _stateFactory;
        private readonly Dictionary<Type, IExitableState> _states = new();

        public IExitableState CurrentState { get; private set; }

        public GameStateMachine(IStateFactory stateFactory) => _stateFactory = stateFactory;

        public void Enter<TState>() where TState : class, IState
        {
            var newState = ChangeState<TState>();
            newState.Enter();
        }

        public void Enter<TState, TPayload>(TPayload payload) where TState : class, IPayloadedState<TPayload>
        {
            var newState = ChangeState<TState>();
            newState.Enter(payload);
        }

        private TState ChangeState<TState>() where TState : class, IExitableState
        {
            CurrentState?.Exit();
            var state = GetState<TState>();
            CurrentState = state;
            return state;
        }

        private TState GetState<TState>() where TState : class, IExitableState
        {
            var stateType = typeof(TState);
            
            if (!_states.ContainsKey(stateType)) 
                _states[stateType] = _stateFactory.CreateState<TState>();

            return _states[stateType] as TState;
        }
    }
}

