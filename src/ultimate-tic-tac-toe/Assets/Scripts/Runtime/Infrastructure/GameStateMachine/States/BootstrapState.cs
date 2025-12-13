using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Runtime.Infrastructure.GameStateMachine.States
{
    public class BootstrapState : IState
    {
        private readonly IGameStateMachine _stateMachine;

        public BootstrapState(IGameStateMachine stateMachine) => _stateMachine = stateMachine;

        public async UniTask EnterAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Debug.Log("[BootstrapState] Initializing...");
            await _stateMachine.EnterAsync<LoadMainMenuState>(cancellationToken);
        }

        public void Exit() => Debug.Log("[BootstrapState] Exiting...");
    }
}

