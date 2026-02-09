using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using Runtime.GameModes.Wizard;
using Runtime.Infrastructure.Logging;
using Runtime.Infrastructure.GameStateMachine;
using Runtime.Infrastructure.GameStateMachine.States;
using Runtime.Localization;
using Runtime.Services.UI;
using Runtime.UI.Settings;
using StripLog;
using UnityEngine;

namespace Runtime.UI.MainMenu
{
    public class MainMenuCoordinator : IMainMenuCoordinator
    {
        private MainMenuViewModel _viewModel;
        private readonly IGameStateMachine _stateMachine;
        private readonly IUIService _uiService;
        private readonly ILocalizationService _localization;
        private readonly IGameWizardCoordinator _wizardCoordinator;
        private CompositeDisposable _disposables = new();
        private CompositeDisposable _wizardDisposables = new();
        private CancellationTokenSource _lifecycleCts = new();
        // Transition token: intentionally not linked to lifecycle.
        // MainMenuCoordinator.Dispose() is called during normal scene exit, so linking would cancel valid transitions.
        private CancellationTokenSource _launchCts;
        private bool _isDisposed;
        private int _startInProgress;
        private int _wizardStartInProgress;

        public MainMenuCoordinator(
            IGameStateMachine stateMachine,
            IUIService uiService,
            ILocalizationService localization,
            IGameWizardCoordinator wizardCoordinator)
        {
            _stateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
            _uiService = uiService ?? throw new ArgumentNullException(nameof(uiService));
            _localization = localization ?? throw new ArgumentNullException(nameof(localization));
            _wizardCoordinator = wizardCoordinator ?? throw new ArgumentNullException(nameof(wizardCoordinator));
        }

        public void Initialize(MainMenuViewModel viewModel)
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(MainMenuCoordinator));

            if (viewModel == null)
                throw new ArgumentNullException(nameof(viewModel));
            
            Cleanup();
            _viewModel = viewModel;
            
            _viewModel.StartGameRequested
                .Subscribe(_ => OnStartGameAsync(_lifecycleCts.Token).Forget(ex =>
                {
                    if (ex is OperationCanceledException)
                        return;

                    Log.Exception(ex, LogTags.UI);
                }))
                .AddTo(_disposables);

            _viewModel.ExitRequested
                .Subscribe(_ => OnExit())
                .AddTo(_disposables);

            _viewModel.SettingsRequested
                .Subscribe(_ => OpenSettingsAsync(_lifecycleCts.Token).Forget(ex =>
                {
                    if (ex is OperationCanceledException)
                        return;

                    Log.Exception(ex, LogTags.UI);
                }))
                .AddTo(_disposables);

            WireWizardEvents();
        }

        private void Cleanup()
        {
            _lifecycleCts.Cancel();
            _lifecycleCts.Dispose();
            _lifecycleCts = new CancellationTokenSource();
            _disposables?.Dispose();
            _disposables = new CompositeDisposable();
            _wizardDisposables?.Dispose();
            _wizardDisposables = new CompositeDisposable();
            _startInProgress = 0;
            _wizardStartInProgress = 0;
            _launchCts?.Cancel();
            _launchCts?.Dispose();
            _launchCts = null;
        }

        private async UniTask OnStartGameAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Log.Debug(LogTags.UI, "[MainMenuCoordinator] Starting game...");

            if (Interlocked.Exchange(ref _wizardStartInProgress, 1) != 0)
                return;
            
            // Close overlays before starting game
            _uiService.Close<LanguageSelectionView>();
            _uiService.Close<SettingsView>();
            
            _viewModel.SetInteractable(false);

            try
            {
                await _wizardCoordinator.StartWizardAsync(cancellationToken);
                _uiService.Hide<MainMenuView>();
            }
            catch (OperationCanceledException)
            {
                _uiService.Get<MainMenuView>()?.Show();
                _viewModel.SetInteractable(true);
                throw;
            }
            catch (Exception ex)
            {
                _uiService.Get<MainMenuView>()?.Show();
                _viewModel.SetInteractable(true);
                Log.Exception(ex, LogTags.UI);
            }
            finally
            {
                Interlocked.Exchange(ref _wizardStartInProgress, 0);
            }
        }

        private void OnExit()
        {
            Log.Debug(LogTags.UI, "[MainMenuCoordinator] Exiting game...");
            
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private async UniTask OpenSettingsAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // SettingsView and LanguageSelectionView are transient, opened on top of MainMenu
            var settingsView = await _uiService.OpenWithLocalizationPreloadAsync<SettingsView, SettingsViewModel>(
                _localization,
                cancellationToken,
                TextTableId.Settings);
            
            if (settingsView == null)
            {
                Log.Error(LogTags.UI, "Failed to open SettingsView");
                return;
            }

            var vm = settingsView.GetViewModel();

            // Note: Back navigation is handled by BaseViewModel.RequestClose triggering UIService.Close
            // We only need to handle forward navigation.
            // Using TakeUntil(vm.OnCloseRequested) ensures we unsubscribe when the window closes
            // (even if View is pooled and ViewModel is reset/pooled, OnCloseRequested completes the session)

            vm.LanguageRequest
                .TakeUntil(vm.OnCloseRequested)
                .Subscribe(_ => OpenLanguageSelection())
                .AddTo(_disposables);
        }

        private void OpenLanguageSelection()
        {
            var langView = _uiService.Open<LanguageSelectionView, LanguageSelectionViewModel>();

            if (langView == null) 
                Log.Error(LogTags.UI, "Failed to open LanguageSelectionView");

            // Back navigation handled by RequestClose -> UIService auto-close
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            _lifecycleCts.Cancel();
            _lifecycleCts.Dispose();
            
            if (_startInProgress == 0)
            {
                _launchCts?.Cancel();
                _launchCts?.Dispose();
                _launchCts = null;
            }
            
            _disposables.Dispose();
            _wizardDisposables.Dispose();
            
            _wizardCoordinator.AbortWizardAsync(AbortReason.SceneChange).Forget(ex =>
            {
                if (ex is OperationCanceledException || ex is ObjectDisposedException)
                    return;

                Log.Exception(ex, LogTags.UI);
            });
        }

        private void WireWizardEvents()
        {
            _wizardCoordinator.GameLaunchRequested
                .Subscribe(config => OnLaunchRequestedAsync(config, _lifecycleCts.Token).Forget(ex =>
                {
                    if (ex is OperationCanceledException)
                        return;

                    Log.Exception(ex, LogTags.UI);
                }))
                .AddTo(_wizardDisposables);

            _wizardCoordinator.WizardAborted
                .Subscribe(HandleWizardAborted)
                .AddTo(_wizardDisposables);
        }

        private async UniTask OnLaunchRequestedAsync(GameLaunchConfig config, CancellationToken cancellationToken)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            if (Interlocked.Exchange(ref _startInProgress, 1) != 0)
                return;

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                _viewModel.SetInteractable(false);

                _launchCts?.Dispose();
                _launchCts = new CancellationTokenSource();

                await _stateMachine.EnterAsync<LoadGameplayState, GameLaunchConfig>(config, _launchCts.Token);
            }
            catch (OperationCanceledException)
            {
                _viewModel.SetInteractable(true);
                throw;
            }
            catch (Exception ex)
            {
                _viewModel.SetInteractable(true);
                Log.Exception(ex, LogTags.UI);
            }
            finally
            {
                _launchCts?.Dispose();
                _launchCts = null;
                Interlocked.Exchange(ref _startInProgress, 0);
            }
        }

        private void HandleWizardAborted(AbortReason reason)
        {
            if (_viewModel == null)
                return;

            switch (reason)
            {
                case AbortReason.UserCancel:
                case AbortReason.Error:
                case AbortReason.StartCancelled:
                case AbortReason.Disconnect:
                    if (_startInProgress != 0)
                        _launchCts?.Cancel();

                    _uiService.Get<MainMenuView>()?.Show();
                    _viewModel.SetInteractable(true);
                    break;
                case AbortReason.GameStarted:
                    break;
            }
        }
    }
}