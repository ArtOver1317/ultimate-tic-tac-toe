using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using Runtime.Infrastructure.Logging;
using Runtime.Localization.Contracts;
using Runtime.Localization.Types;
using Runtime.Services.UI;
using Runtime.UI.Settings;
using StripLog;

namespace Runtime.UI.MainMenu
{
    internal sealed class MainMenuOverlayNavigator : IDisposable
    {
        private const string _playerStatisticsTableName = "PlayerStatistics";
        private const string _gameTableName = "Game";
        private const string _gameWizardTableName = "GameWizard";
        private const string _settingsTableName = "Settings";
        private const string _commonTableName = "Common";

        private readonly IUIService _uiService;
        private readonly ILocalizationService _localization;
        private CompositeDisposable _settingsSubscriptions = new();
        private int _settingsOpenInProgress;

        public MainMenuOverlayNavigator(IUIService uiService, ILocalizationService localization)
        {
            _uiService = uiService ?? throw new ArgumentNullException(nameof(uiService));
            _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        }

        public void CloseTransientOverlays()
        {
            _uiService.Close<PlayerNameEditView>();
            _uiService.Close<LanguageSelectionView>();
            _uiService.Close<SettingsView>();
            _uiService.Close<PlayerStatisticsView>();
        }

        public void ShowMainMenu(MainMenuViewModel viewModel)
        {
            _uiService.Get<MainMenuView>()?.Show();
            viewModel.SetInteractable(true);
        }

        public void HideMainMenu() => _uiService.Hide<MainMenuView>();

        public void Reset()
        {
            Interlocked.Exchange(ref _settingsOpenInProgress, 0);
            ResetSettingsSubscriptions();
        }

        public void Dispose()
        {
            Reset();
        }

        private void ResetSettingsSubscriptions()
        {
            _settingsSubscriptions.Dispose();
            _settingsSubscriptions = new CompositeDisposable();
        }

        public async UniTask OpenSettingsAsync(CancellationToken lifecycleToken)
        {
            lifecycleToken.ThrowIfCancellationRequested();

            if (Interlocked.Exchange(ref _settingsOpenInProgress, 1) != 0)
                return;

            try
            {
                var settingsView = await _uiService.OpenWithLocalizationPreloadAsync<SettingsView, SettingsViewModel>(
                    _localization,
                    lifecycleToken,
                    TextTableId.Settings);

                ResetSettingsSubscriptions();
                SubscribeToSettingsActions(settingsView.GetViewModel(), lifecycleToken);
            }
            catch (InvalidOperationException exception)
            {
                Log.Error(LogTags.UI, $"Failed to open SettingsView. {exception.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref _settingsOpenInProgress, 0);
            }
        }

        public async UniTask OpenStatisticsAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await _uiService.OpenWithLocalizationPreloadAsync<PlayerStatisticsView, PlayerStatisticsViewModel>(
                    _localization,
                    cancellationToken,
                    new TextTableId(_playerStatisticsTableName),
                    new TextTableId(_gameTableName),
                    new TextTableId(_gameWizardTableName));
            }
            catch (InvalidOperationException exception)
            {
                Log.Error(LogTags.UI, $"Failed to open PlayerStatisticsView. {exception.Message}");
            }
        }

        private void SubscribeToSettingsActions(
            SettingsViewModel viewModel,
            CancellationToken lifecycleToken)
        {
            viewModel.LanguageRequest
                .TakeUntil(viewModel.OnCloseRequested)
                .Subscribe(_ => OpenLanguageSelection())
                .AddTo(_settingsSubscriptions);

            viewModel.PlayerNameEditRequest
                .TakeUntil(viewModel.OnCloseRequested)
                .Subscribe(_ => OpenPlayerNameEditAsync(lifecycleToken).Forget(MainMenuAsyncExceptionHandler.HandleFireAndForgetException))
                .AddTo(_settingsSubscriptions);
        }

        private void OpenLanguageSelection()
        {
            try
            {
                _uiService.Open<LanguageSelectionView, LanguageSelectionViewModel>();
            }
            catch (InvalidOperationException exception)
            {
                Log.Error(LogTags.UI, $"Failed to open LanguageSelectionView. {exception.Message}");
            }
        }

        private async UniTask OpenPlayerNameEditAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await _uiService.OpenWithLocalizationPreloadAsync<PlayerNameEditView, PlayerNameEditViewModel>(
                    _localization,
                    cancellationToken,
                    new TextTableId(_settingsTableName),
                    new TextTableId(_commonTableName),
                    TextTableId.Errors);
            }
            catch (InvalidOperationException exception)
            {
                Log.Error(LogTags.UI, $"Failed to open PlayerNameEditView. {exception.Message}");
            }
        }
    }
}