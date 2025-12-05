using Runtime.Infrastructure.GameStateMachine.States;

namespace Runtime.Infrastructure.GameStateMachine
{
    public interface IGameStateMachine
    {
        IExitableState CurrentState { get; }
        
        void Enter<TState>() where TState : class, IState;
        void Enter<TState, TPayload>(TPayload payload) where TState : class, IPayloadedState<TPayload>;
    }
}

