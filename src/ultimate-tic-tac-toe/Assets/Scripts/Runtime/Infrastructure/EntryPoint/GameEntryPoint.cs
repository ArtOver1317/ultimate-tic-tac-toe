using System.Threading;
using Cysharp.Threading.Tasks;
using Runtime.Infrastructure.GameStateMachine;
using Runtime.Infrastructure.GameStateMachine.States;
using UnityEngine;
using VContainer.Unity;

namespace Runtime.Infrastructure.EntryPoint
{
    public class GameEntryPoint : IAsyncStartable
    {
        private readonly IGameStateMachine _stateMachine;

        public GameEntryPoint(IGameStateMachine stateMachine) => _stateMachine = stateMachine;

        public async UniTask StartAsync(CancellationToken cancellationToken)
        {
            Debug.Log("[GameEntryPoint] Starting game...");
            await _stateMachine.EnterAsync<BootstrapState>(cancellationToken);
        }
    }
}

