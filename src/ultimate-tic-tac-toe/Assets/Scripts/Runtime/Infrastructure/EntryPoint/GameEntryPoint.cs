using Runtime.Infrastructure.GameStateMachine;
using Runtime.Infrastructure.GameStateMachine.States;
using UnityEngine;
using VContainer.Unity;

namespace Runtime.Infrastructure.EntryPoint
{
    public class GameEntryPoint : IStartable
    {
        private readonly IGameStateMachine _stateMachine;

        public GameEntryPoint(IGameStateMachine stateMachine) => _stateMachine = stateMachine;

        public void Start()
        {
            Debug.Log("[GameEntryPoint] Starting game...");
            _stateMachine.Enter<BootstrapState>();
        }
    }
}

