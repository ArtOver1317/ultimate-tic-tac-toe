using UnityEngine;

namespace Runtime.Infrastructure.GameStateMachine.States
{
    public class GameplayState : IState
    {
        private readonly IGameStateMachine _stateMachine;

        public GameplayState(IGameStateMachine stateMachine) => _stateMachine = stateMachine;

        public void Enter() => Debug.Log("[GameplayState] Game started");

        public void Exit() => Debug.Log("[GameplayState] Game ended");

        public void ReturnToMainMenu() => _stateMachine.Enter<LoadMainMenuState>();
    }
}

