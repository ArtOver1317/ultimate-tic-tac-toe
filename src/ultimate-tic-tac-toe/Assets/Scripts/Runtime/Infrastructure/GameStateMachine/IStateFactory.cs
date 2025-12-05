using Runtime.Infrastructure.GameStateMachine.States;

namespace Runtime.Infrastructure.GameStateMachine
{
    public interface IStateFactory
    {
        TState CreateState<TState>() where TState : IExitableState;
    }
}

