using System.Threading;
using Cysharp.Threading.Tasks;
using Runtime.Infrastructure.Logging;
using Runtime.Services.Scenes;
using Runtime.Services.UI;
using StripLog;

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
            Log.Debug(LogTags.Scenes, "[LoadGameplayState] Loading Gameplay scene...");
            _uiService.ClearViewModelPools();
            await _sceneLoader.LoadSceneAsync(SceneNames.Gameplay, cancellationToken);
            Log.Debug(LogTags.Scenes, "[LoadGameplayState] Gameplay scene loaded");
            await _stateMachine.EnterAsync<GameplayState>(cancellationToken);
        }

        public void Exit() => Log.Debug(LogTags.Scenes, "[LoadGameplayState] Exiting...");
    }
}