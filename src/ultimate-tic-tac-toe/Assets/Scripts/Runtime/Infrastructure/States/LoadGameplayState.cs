using Runtime.Services.Scenes;
using UnityEngine;

namespace Runtime.Infrastructure.States
{
    public class LoadGameplayState : IState
    {
        private readonly IGameStateMachine _stateMachine;
        private readonly ISceneLoaderService _sceneLoader;

        public LoadGameplayState(IGameStateMachine stateMachine, ISceneLoaderService sceneLoader)
        {
            _stateMachine = stateMachine;
            _sceneLoader = sceneLoader;
        }

        public void Enter()
        {
            Debug.Log("[LoadGameplayState] Loading Gameplay scene...");
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

