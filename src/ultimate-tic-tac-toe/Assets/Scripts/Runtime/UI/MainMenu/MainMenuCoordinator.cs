using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using Runtime.Infrastructure.Logging;
using Runtime.Infrastructure.GameStateMachine;
using Runtime.Infrastructure.GameStateMachine.States;
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
        private CompositeDisposable _disposables = new();
        private CancellationTokenSource _lifecycleCts = new();
        private bool _isDisposed;

        public MainMenuCoordinator(IGameStateMachine stateMachine, IUIService uiService)
        {
            _stateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
            _uiService = uiService ?? throw new ArgumentNullException(nameof(uiService));
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
                .Subscribe(_ => OpenSettings())
                .AddTo(_disposables);
        }

        private void Cleanup()
        {
            _lifecycleCts.Cancel();
            _lifecycleCts.Dispose();
            _lifecycleCts = new CancellationTokenSource();
            _disposables?.Dispose();
            _disposables = new CompositeDisposable();
        }

        private async UniTask OnStartGameAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Log.Debug(LogTags.UI, "[MainMenuCoordinator] Starting game...");
            
            // Close overlays before starting game
            _uiService.Close<LanguageSelectionView>();
            _uiService.Close<SettingsView>();
            
            _viewModel.SetInteractable(false);
            await _stateMachine.EnterAsync<LoadGameplayState>(cancellationToken);
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

        private void OpenSettings()
        {
            // SettingsView and LanguageSelectionView are transient, opened on top of MainMenu
            var settingsView = _uiService.Open<SettingsView, SettingsViewModel>();
            
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
            _disposables.Dispose();
        }
    }
}