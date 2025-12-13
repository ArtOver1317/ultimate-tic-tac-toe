using System.Threading;
using Cysharp.Threading.Tasks;
using Runtime.Services.Scenes;
using Runtime.Services.UI;
using UnityEngine;

namespace Runtime.Infrastructure.GameStateMachine.States
{
    public class LoadGameplayState : IState
    {
        private readonly IGameStateMachine _stateMachine;
        private readonly ISceneLoaderService _sceneLoader;
        private readonly IUIService _uiService;

        public LoadGameplayState(IGameStateMachine stateMachine, ISceneLoaderService sceneLoader, IUIService uiService)
        {
            _stateMachine = stateMachine;
            _sceneLoader = sceneLoader;
            _uiService = uiService;
        }

        public async UniTask EnterAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Debug.Log("[LoadGameplayState] Loading Gameplay scene...");
            _uiService.ClearViewModelPools();
            await _sceneLoader.LoadSceneAsync(SceneNames.Gameplay, cancellationToken);
            Debug.Log("[LoadGameplayState] Gameplay scene loaded");
            await _stateMachine.EnterAsync<GameplayState>(cancellationToken);
        }

        public void Exit() => Debug.Log("[LoadGameplayState] Exiting...");
    }
}