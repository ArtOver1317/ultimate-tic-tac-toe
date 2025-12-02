using Runtime.Services.Scenes;
using Runtime.Services.UI;
using UnityEngine;

namespace Runtime.Infrastructure.States
{
    public class LoadMainMenuState : IState
    {
        private readonly IGameStateMachine _stateMachine;
        private readonly ISceneLoaderService _sceneLoader;
        private readonly UIService _uiService;

        public LoadMainMenuState(IGameStateMachine stateMachine, ISceneLoaderService sceneLoader, UIService uiService)
        {
            _stateMachine = stateMachine;
            _sceneLoader = sceneLoader;
            _uiService = uiService;
        }

        public void Enter()
        {
            Debug.Log("[LoadMainMenuState] Loading MainMenu scene...");
            _uiService.ClearViewModelPools();
            _sceneLoader.LoadSceneAsync(SceneNames.MainMenu, OnSceneLoaded);
        }

        private void OnSceneLoaded()
        {
            Debug.Log("[LoadMainMenuState] MainMenu scene loaded");
            _stateMachine.Enter<MainMenuState>();
        }

        public void Exit() => Debug.Log("[LoadMainMenuState] Exiting...");
    }
}

