using System.Threading;
using Cysharp.Threading.Tasks;
using Runtime.Infrastructure.Logging;
using Runtime.Localization;
using Runtime.Services.Assets;
using Runtime.Services.UI;
using Runtime.UI.Common;
using Runtime.UI.MainMenu;
using Runtime.UI.GameModes.Wizard;
using StripLog;

namespace Runtime.Infrastructure.GameStateMachine.States
{
    public class MainMenuState : IState
    {
        private readonly IUIService _uiService;
        private readonly IMainMenuCoordinator _coordinator;
        private readonly IAssetProvider _assets;
        private readonly AssetLibrary _assetLibrary;
        private readonly ILocalizationService _localization;
        private bool _isExited;

        public MainMenuState(
            IUIService uiService, 
            IMainMenuCoordinator coordinator,
            IAssetProvider assets,
            AssetLibrary assetLibrary,
            ILocalizationService localization)
        {
            _uiService = uiService;
            _coordinator = coordinator;
            _assets = assets;
            _assetLibrary = assetLibrary;
            _localization = localization;
        }

        public async UniTask EnterAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _isExited = false;
            Log.Debug(LogTags.Scenes, "[MainMenuState] Entered MainMenu");
            
            // Load and register UI prefabs
            if (_assetLibrary.BackgroundPrefab != null && _assetLibrary.BackgroundPrefab.RuntimeKeyIsValid())
            {
                var backgroundPrefab = await _assets.LoadAsync<UnityEngine.GameObject>(_assetLibrary.BackgroundPrefab, cancellationToken);
                _uiService.RegisterWindowPrefab<UIBackgroundView>(backgroundPrefab);
                _uiService.Open<UIBackgroundView, UIBackgroundViewModel>();
            }
            else
            {
                Log.Error(LogTags.Scenes, "[MainMenuState] BackgroundPrefab is missing or invalid. UI background will be disabled.");
            }

            var mainMenuPrefab = await _assets.LoadAsync<UnityEngine.GameObject>(_assetLibrary.MainMenuPrefab, cancellationToken);
            _uiService.RegisterWindowPrefab<MainMenuView>(mainMenuPrefab);

            if (_assetLibrary.SettingsPrefab != null && _assetLibrary.SettingsPrefab.RuntimeKeyIsValid())
            {
                var settingsPrefab = await _assets.LoadAsync<UnityEngine.GameObject>(_assetLibrary.SettingsPrefab, cancellationToken);
                _uiService.RegisterWindowPrefab<Runtime.UI.Settings.SettingsView>(settingsPrefab);
            }
            else
            {
                 Log.Error(LogTags.Scenes, "[MainMenuState] SettingsPrefab is missing or invalid in AssetLibrary. Settings feature will be disabled.");
            }

            if (_assetLibrary.LanguageSelectionPrefab != null && _assetLibrary.LanguageSelectionPrefab.RuntimeKeyIsValid())
            {
                var languagePrefab = await _assets.LoadAsync<UnityEngine.GameObject>(_assetLibrary.LanguageSelectionPrefab, cancellationToken);
                _uiService.RegisterWindowPrefab<Runtime.UI.Settings.LanguageSelectionView>(languagePrefab);
            }
            else
            {
                 Log.Error(LogTags.Scenes, "[MainMenuState] LanguageSelectionPrefab is missing or invalid. Language selection will be disabled.");
            }

            if (_assetLibrary.ModeSelectionPrefab != null && _assetLibrary.ModeSelectionPrefab.RuntimeKeyIsValid())
            {
                var modeSelectionPrefab = await _assets.LoadAsync<UnityEngine.GameObject>(_assetLibrary.ModeSelectionPrefab, cancellationToken);
                _uiService.RegisterWindowPrefab<ModeSelectionView>(modeSelectionPrefab);
            }
            else
            {
                Log.Error(LogTags.Scenes, "[MainMenuState] ModeSelectionPrefab is missing or invalid. Game mode wizard will be disabled.");
            }

            if (_assetLibrary.MatchSetupPrefab != null && _assetLibrary.MatchSetupPrefab.RuntimeKeyIsValid())
            {
                var matchSetupPrefab = await _assets.LoadAsync<UnityEngine.GameObject>(_assetLibrary.MatchSetupPrefab, cancellationToken);
                _uiService.RegisterWindowPrefab<MatchSetupView>(matchSetupPrefab);
            }
            else
            {
                Log.Error(LogTags.Scenes, "[MainMenuState] MatchSetupPrefab is missing or invalid. Game mode wizard will be disabled.");
            }

            if (_assetLibrary.MatchmakingPrefab != null && _assetLibrary.MatchmakingPrefab.RuntimeKeyIsValid())
            {
                var matchmakingPrefab = await _assets.LoadAsync<UnityEngine.GameObject>(_assetLibrary.MatchmakingPrefab, cancellationToken);
                _uiService.RegisterWindowPrefab<MatchmakingView>(matchmakingPrefab);
            }
            else
            {
                Log.Error(LogTags.Scenes, "[MainMenuState] MatchmakingPrefab is missing or invalid. Game mode wizard will be disabled.");
            }

            var view = await _uiService.OpenWithLocalizationPreloadAsync<MainMenuView, MainMenuViewModel>(
                _localization,
                cancellationToken,
                TextTableId.MainMenu);
            
            if (view == null)
            {
                Log.Error(LogTags.UI, "[MainMenuState] Failed to open MainMenuView!");
                return;
            }
            
            var viewModel = view.GetViewModel();
            _coordinator.Initialize(viewModel);
        }

        public void Exit()
        {
            if (_isExited)
                return;
            
            _isExited = true;
            Log.Debug(LogTags.Scenes, "[MainMenuState] Exiting MainMenu");
            
            // Close all potential sub-windows to prevent UI leaks
            _uiService.Close<Runtime.UI.Settings.LanguageSelectionView>();
            _uiService.Close<Runtime.UI.Settings.SettingsView>();
            _uiService.Close<MainMenuView>();
            _uiService.Close<UIBackgroundView>();
            
            _coordinator.Dispose();
        }
    }
}

