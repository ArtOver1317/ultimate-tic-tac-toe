using Services.Scenes;
using UnityEngine;

namespace Infrastructure.States
{
    public class LoadMainMenuState : IState
    {
        private readonly IGameStateMachine _stateMachine;
        private readonly ISceneLoaderService _sceneLoader;

        public LoadMainMenuState(IGameStateMachine stateMachine, ISceneLoaderService sceneLoader)
        {
            _stateMachine = stateMachine;
            _sceneLoader = sceneLoader;
        }

        public void Enter()
        {
            Debug.Log("[LoadMainMenuState] Loading MainMenu scene...");
            _sceneLoader.LoadScene(SceneNames.MainMenu, OnSceneLoaded);
        }

        private void OnSceneLoaded()
        {
            Debug.Log("[LoadMainMenuState] MainMenu scene loaded");
            _stateMachine.Enter<MainMenuState>();
        }

        public void Exit() => Debug.Log("[LoadMainMenuState] Exiting...");
    }
}

