using UnityEngine;

namespace Runtime.Infrastructure.GameStateMachine.States
{
    public class BootstrapState : IState
    {
        private readonly IGameStateMachine _stateMachine;

        public BootstrapState(IGameStateMachine stateMachine) => _stateMachine = stateMachine;

        public void Enter()
        {
            Debug.Log("[BootstrapState] Initializing...");
            _stateMachine.Enter<LoadMainMenuState>();
        }

        public void Exit() => Debug.Log("[BootstrapState] Exiting...");
    }
}

