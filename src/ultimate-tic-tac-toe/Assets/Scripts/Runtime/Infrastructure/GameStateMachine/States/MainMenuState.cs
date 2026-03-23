using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Runtime.GameModes.Wizard.Coordinator;
using Runtime.Infrastructure.Logging;
using Runtime.Localization;
using Runtime.Localization.Contracts;
using Runtime.Localization.Types;
using Runtime.Services.Assets;
using Runtime.Services.UI;
using Runtime.UI.Common;
using Runtime.UI.MainMenu;
using Runtime.UI.GameModes.Wizard;
using StripLog;
using UnityEngine.AddressableAssets;

namespace Runtime.Infrastructure.GameStateMachine.States
{
    public class MainMenuState : IState
    {
        private readonly IUIService _uiService;
        private readonly IMainMenuCoordinator _coordinator;
        private readonly IGameWizardCoordinator _wizardCoordinator;
        private readonly IMainMenuEntryModeStore _entryModeStore;
        private readonly IAssetProvider _assets;
        private readonly AssetLibrary _assetLibrary;
        private readonly ILocalizationService _localization;
        private bool _isExited;
        private MainMenuViewModel _headlessViewModel;

        public MainMenuState(
            IUIService uiService, 
            IMainMenuCoordinator coordinator,
            IGameWizardCoordinator wizardCoordinator,
            IMainMenuEntryModeStore entryModeStore,
            IAssetProvider assets,
            AssetLibrary assetLibrary,
            ILocalizationService localization)
        {
            _uiService = uiService;
            _coordinator = coordinator;
            _wizardCoordinator = wizardCoordinator;
            _entryModeStore = entryModeStore;
            _assets = assets;
            _assetLibrary = assetLibrary;
            _localization = localization;
        }

        public async UniTask EnterAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _isExited = false;
            Log.Debug(LogTags.Scenes, "[MainMenuState] Entered MainMenu");

            await PrepareUiAsync(cancellationToken);

            if (await TryEnterWizardModeAsync(cancellationToken))
                return;

            await OpenMainMenuAsync(cancellationToken);
        }

        private async UniTask PrepareUiAsync(CancellationToken cancellationToken)
        {
            await TryRegisterAndOpenBackgroundAsync(cancellationToken);
            await RegisterMainMenuUiPrefabsAsync(cancellationToken);
        }

        private async UniTask<bool> TryEnterWizardModeAsync(CancellationToken cancellationToken)
        {
            if (!_entryModeStore.TryConsume(out var entryMode) || entryMode != MainMenuEntryMode.OpenWizard)
                return false;

            try
            {
                var menuView = await OpenMainMenuViewAsync(cancellationToken);
                InitializeWizardEntry(menuView);
            }
            catch (InvalidOperationException ex)
            {
                Log.Error(LogTags.UI, $"[MainMenuState] Failed to open MainMenuView for wizard entry. {ex.Message}");
                InitializeHeadlessWizardEntry();
            }

            await StartWizardAsync(cancellationToken);
            return true;
        }

        private async UniTask OpenMainMenuAsync(CancellationToken cancellationToken)
        {
            try
            {
                var view = await OpenMainMenuViewAsync(cancellationToken);
                _coordinator.Initialize(view.GetViewModel());
            }
            catch (InvalidOperationException ex)
            {
                Log.Error(LogTags.UI, $"[MainMenuState] Failed to open MainMenuView! {ex.Message}");
            }
        }

        private async UniTask<MainMenuView> OpenMainMenuViewAsync(CancellationToken cancellationToken) =>
            await _uiService.OpenWithLocalizationPreloadAsync<MainMenuView, MainMenuViewModel>(
                _localization,
                cancellationToken,
                TextTableId.MainMenu);

        private void InitializeWizardEntry(MainMenuView menuView)
        {
            _uiService.Hide<MainMenuView>();
            _coordinator.Initialize(menuView.GetViewModel());
        }

        private void InitializeHeadlessWizardEntry()
        {
            _headlessViewModel?.Dispose();
            _headlessViewModel = new MainMenuViewModel(_localization);
            _headlessViewModel.Initialize();
            _coordinator.Initialize(_headlessViewModel);
        }

        private async UniTask StartWizardAsync(CancellationToken cancellationToken)
        {
            try
            {
                await _wizardCoordinator.StartWizardAsync(cancellationToken);
            }
            catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(ex.Message, ex, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                _uiService.Get<MainMenuView>().Show();
            }
            catch (Exception ex)
            {
                Log.Error(LogTags.UI, $"[MainMenuState] Wizard entry failed. Falling back to MainMenuView. {ex}");
                _uiService.Get<MainMenuView>().Show();
            }
        }

        private async UniTask TryRegisterAndOpenBackgroundAsync(CancellationToken cancellationToken)
        {
            if (_uiService.IsOpen<UIBackgroundView>())
                return;

            if (!await TryRegisterWindowPrefabAsync<UIBackgroundView>(
                    _assetLibrary.BackgroundPrefab,
                    "[MainMenuState] BackgroundPrefab is missing or invalid. UI background will be disabled.",
                    cancellationToken))
                return;

            _uiService.Open<UIBackgroundView, UIBackgroundViewModel>();
        }

        private async UniTask RegisterMainMenuUiPrefabsAsync(CancellationToken cancellationToken)
        {
            await RegisterMainMenuWindowAsync(cancellationToken);
            await RegisterSettingsWindowsAsync(cancellationToken);
            await RegisterWizardWindowsAsync(cancellationToken);
        }

        private async UniTask RegisterMainMenuWindowAsync(CancellationToken cancellationToken)
        {
            var mainMenuPrefab = await _assets.LoadAsync<UnityEngine.GameObject>(_assetLibrary.MainMenuPrefab, cancellationToken);
            _uiService.RegisterWindowPrefab<MainMenuView>(mainMenuPrefab);
        }

        private async UniTask RegisterSettingsWindowsAsync(CancellationToken cancellationToken)
        {
            await TryRegisterWindowPrefabAsync<UI.Settings.SettingsView>(
                _assetLibrary.SettingsPrefab,
                "[MainMenuState] SettingsPrefab is missing or invalid in AssetLibrary. Settings feature will be disabled.",
                cancellationToken);

            await RegisterPlayerStatisticsWindowAsync(cancellationToken);

            await TryRegisterWindowPrefabAsync<UI.Settings.LanguageSelectionView>(
                _assetLibrary.LanguageSelectionPrefab,
                "[MainMenuState] LanguageSelectionPrefab is missing or invalid. Language selection will be disabled.",
                cancellationToken);

            await TryRegisterWindowPrefabAsync<UI.Settings.PlayerNameEditView>(
                _assetLibrary.PlayerNameEditPrefab,
                "[MainMenuState] PlayerNameEditPrefab is missing or invalid. Player name edit will be disabled.",
                cancellationToken);
        }

        private async UniTask RegisterPlayerStatisticsWindowAsync(CancellationToken cancellationToken)
        {
            if (_assetLibrary.PlayerStatisticsPrefab == null || !_assetLibrary.PlayerStatisticsPrefab.RuntimeKeyIsValid())
            {
                Log.Error(LogTags.Scenes, "[MainMenuState] PlayerStatisticsPrefab is missing or invalid in AssetLibrary. Statistics feature will be disabled.");
                return;
            }

            await TryRegisterWindowPrefabAsync<PlayerStatisticsView>(
                _assetLibrary.PlayerStatisticsPrefab,
                "[MainMenuState] PlayerStatisticsPrefab is missing or invalid in AssetLibrary. Statistics feature will be disabled.",
                cancellationToken);
        }

        private async UniTask RegisterWizardWindowsAsync(CancellationToken cancellationToken)
        {
            await TryRegisterWindowPrefabAsync<GameSelectionView>(
                _assetLibrary.ModeSelectionPrefab,
                "[MainMenuState] ModeSelectionPrefab is missing or invalid. Game mode wizard will be disabled.",
                cancellationToken);

            await TryRegisterWindowPrefabAsync<MatchSetupView>(
                _assetLibrary.MatchSetupPrefab,
                "[MainMenuState] MatchSetupPrefab is missing or invalid. Game mode wizard will be disabled.",
                cancellationToken);

            await TryRegisterWindowPrefabAsync<MatchmakingView>(
                _assetLibrary.MatchmakingPrefab,
                "[MainMenuState] MatchmakingPrefab is missing or invalid. Game mode wizard will be disabled.",
                cancellationToken);
        }

        private async UniTask<bool> TryRegisterWindowPrefabAsync<TView>(
            AssetReferenceGameObject prefabReference,
            string invalidReferenceLogMessage,
            CancellationToken cancellationToken)
            where TView : class, UI.Core.IUIView
        {
            if (prefabReference == null || !prefabReference.RuntimeKeyIsValid())
            {
                Log.Error(LogTags.Scenes, invalidReferenceLogMessage);
                return false;
            }

            var prefab = await _assets.LoadAsync<UnityEngine.GameObject>(prefabReference, cancellationToken);

            if (prefab == null)
            {
                Log.Error(LogTags.Scenes, $"[MainMenuState] Failed to load prefab for window {typeof(TView).Name}. Registration skipped.");
                return false;
            }

            _uiService.RegisterWindowPrefab<TView>(prefab);
            return true;
        }

        public void Exit()
        {
            if (_isExited)
                return;
            
            _isExited = true;
            Log.Debug(LogTags.Scenes, "[MainMenuState] Exiting MainMenu");
            
            // Close all potential sub-windows to prevent UI leaks
            _uiService.Close<UI.Settings.LanguageSelectionView>();
            _uiService.Close<UI.Settings.PlayerNameEditView>();
            _uiService.Close<UI.Settings.SettingsView>();
            _uiService.Close<PlayerStatisticsView>();
            _uiService.Close<MainMenuView>();
            
            _coordinator.Dispose();
            _headlessViewModel?.Dispose();
            _headlessViewModel = null;
        }
    }
}