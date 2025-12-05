using Runtime.Infrastructure.GameStateMachine.States;

namespace Runtime.Infrastructure.GameStateMachine
{
    public class GameStateMachine : IGameStateMachine
    {
        private readonly IStateFactory _stateFactory;

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
            var state = _stateFactory.CreateState<TState>();

            CurrentState = state ?? throw StateExceptions.FactoryReturnedNull(typeof(TState));
            
            return state;
        }
    }
}