using System.Threading;
using Cysharp.Threading.Tasks;
using Runtime.GameModes.Wizard;
using Runtime.Infrastructure.Logging;
using Runtime.Services.Assets;
using Runtime.Services.Scenes;
using Runtime.Services.UI;
using StripLog;

namespace Runtime.Infrastructure.GameStateMachine.States
{
    public class LoadGameplayState : IState, IPayloadedState<GameLaunchConfig>
    {
        private readonly IGameStateMachine _stateMachine;
        private readonly ISceneLoaderService _sceneLoader;
        private readonly IUIService _uiService;
        private readonly IAssetProvider _assets;
        private readonly IGameLaunchConfigStore _launchConfigStore;

        public LoadGameplayState(
            IGameStateMachine stateMachine,
            ISceneLoaderService sceneLoader,
            IUIService uiService,
            IAssetProvider assets,
            IGameLaunchConfigStore launchConfigStore)
        {
            _stateMachine = stateMachine;
            _sceneLoader = sceneLoader;
            _uiService = uiService;
            _assets = assets;
            _launchConfigStore = launchConfigStore ?? throw new System.ArgumentNullException(nameof(launchConfigStore));
        }

        public async UniTask EnterAsync(CancellationToken cancellationToken = default)
        {
            _launchConfigStore.Clear();
            await EnterInternalAsync(cancellationToken);
        }

        public async UniTask EnterAsync(GameLaunchConfig payload, CancellationToken cancellationToken = default)
        {
            if (payload == null)
                throw new System.ArgumentNullException(nameof(payload));

            _launchConfigStore.Set(payload);
            await EnterInternalAsync(cancellationToken);
        }

        private async UniTask EnterInternalAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Log.Debug(LogTags.Scenes, "[LoadGameplayState] Loading Gameplay scene...");
            _uiService.ClearViewModelPools();
            _assets.Cleanup();
            await _sceneLoader.LoadSceneAsync(SceneNames.Gameplay, cancellationToken);
            Log.Debug(LogTags.Scenes, "[LoadGameplayState] Gameplay scene loaded");
            await _stateMachine.EnterAsync<GameplayState>(cancellationToken);
        }

        public void Exit() => Log.Debug(LogTags.Scenes, "[LoadGameplayState] Exiting...");
    }
}