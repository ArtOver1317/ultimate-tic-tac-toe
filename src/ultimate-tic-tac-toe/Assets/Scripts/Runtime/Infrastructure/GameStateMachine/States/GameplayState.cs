using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Runtime.Gameplay;
using Runtime.Infrastructure.Logging;
using Runtime.Services.Assets;
using Runtime.Services.UI;
using Runtime.UI.Common;
using StripLog;
using VContainer;

namespace Runtime.Infrastructure.GameStateMachine.States
{
    public class GameplayState : IState
    {
        private readonly IGameStateMachine _stateMachine;
        private readonly IGameplayScopeAccessor _scopeAccessor;
        private readonly IUIService _uiService;
        private readonly IAssetProvider _assets;
        private readonly AssetLibrary _assetLibrary;

        public GameplayState(
            IGameStateMachine stateMachine,
            IGameplayScopeAccessor scopeAccessor,
            IUIService uiService,
            IAssetProvider assets,
            AssetLibrary assetLibrary)
        {
            _stateMachine = stateMachine;
            _scopeAccessor = scopeAccessor ?? throw new ArgumentNullException(nameof(scopeAccessor));
            _uiService = uiService ?? throw new ArgumentNullException(nameof(uiService));
            _assets = assets ?? throw new ArgumentNullException(nameof(assets));
            _assetLibrary = assetLibrary != null ? assetLibrary : throw new ArgumentNullException(nameof(assetLibrary));
        }

        public async UniTask EnterAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Log.Debug(LogTags.Infrastructure, "[GameplayState] Game started");

            await TryOpenBackgroundAsync(cancellationToken);

            var startup = await TryResolveGameplayStartupAsync(cancellationToken);
            
            if (startup == null)
                return;

            await StartGameplayAsync(startup, cancellationToken);
        }

        private async UniTask TryOpenBackgroundAsync(CancellationToken cancellationToken)
        {
            if (_uiService.IsOpen<UIBackgroundView>())
                return;

            if (_assetLibrary.BackgroundPrefab == null || !_assetLibrary.BackgroundPrefab.RuntimeKeyIsValid())
            {
                Log.Error(LogTags.Scenes, "[GameplayState] BackgroundPrefab is missing or invalid. UI background will be disabled.");
                return;
            }

            var backgroundPrefab = await _assets.LoadAsync<UnityEngine.GameObject>(_assetLibrary.BackgroundPrefab, cancellationToken);
            _uiService.RegisterWindowPrefab<UIBackgroundView>(backgroundPrefab);
            _uiService.Open<UIBackgroundView, UIBackgroundViewModel>();
        }

        private async UniTask<IGameplayStartup> TryResolveGameplayStartupAsync(CancellationToken cancellationToken)
        {
            var scope = _scopeAccessor.Current;
            
            if (scope == null)
            {
                Log.Error(LogTags.Infrastructure, "[GameplayState] Gameplay scope is not available.");
                await ReturnToMainMenuAsync(cancellationToken);
                return null;
            }

            try
            {
                return scope.Resolve<IGameplayStartup>();
            }
            catch (Exception ex)
            {
                Log.Error(LogTags.Infrastructure, $"[GameplayState] Failed to resolve GameplayStartup: {ex}");
                await ReturnToMainMenuAsync(cancellationToken);
                return null;
            }
        }

        private async UniTask StartGameplayAsync(IGameplayStartup startup, CancellationToken cancellationToken)
        {
            try
            {
                await startup.StartAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log.Error(LogTags.Infrastructure, $"[GameplayState] Startup failed: {ex}");
                await ReturnToMainMenuAsync(cancellationToken);
            }
        }

        public void Exit() => Log.Debug(LogTags.Infrastructure, "[GameplayState] Game ended");

        public UniTask ReturnToMainMenuAsync(CancellationToken cancellationToken = default) =>
            _stateMachine.EnterAsync<LoadMainMenuState>(cancellationToken);
    }
}