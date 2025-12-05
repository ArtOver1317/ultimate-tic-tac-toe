using Runtime.Services.Scenes;
using Runtime.Services.UI;
using UnityEngine;

namespace Runtime.Infrastructure.GameStateMachine.States
{
    public class LoadGameplayState : IState
    {
        private readonly IGameStateMachine _stateMachine;
        private readonly ISceneLoaderService _sceneLoader;
        private readonly UIService _uiService;

        public LoadGameplayState(IGameStateMachine stateMachine, ISceneLoaderService sceneLoader, UIService uiService)
        {
            _stateMachine = stateMachine;
            _sceneLoader = sceneLoader;
            _uiService = uiService;
        }

        public void Enter()
        {
            Debug.Log("[LoadGameplayState] Loading Gameplay scene...");
            _uiService.ClearViewModelPools();
            _sceneLoader.LoadSceneAsync(SceneNames.Gameplay, OnSceneLoaded);
        }

        private void OnSceneLoaded()
        {
            Debug.Log("[LoadGameplayState] Gameplay scene loaded");
            _stateMachine.Enter<GameplayState>();
        }

        public void Exit() => Debug.Log("[LoadGameplayState] Exiting...");
    }
}

