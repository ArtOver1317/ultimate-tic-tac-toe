using Runtime.Infrastructure.GameStateMachine.States;
using VContainer;

namespace Runtime.Infrastructure.GameStateMachine
{
    public class StateFactory : IStateFactory
    {
        private readonly IObjectResolver _container;

        public StateFactory(IObjectResolver container) => _container = container;

        public TState CreateState<TState>() where TState : IExitableState => _container.Resolve<TState>();
    }
}

