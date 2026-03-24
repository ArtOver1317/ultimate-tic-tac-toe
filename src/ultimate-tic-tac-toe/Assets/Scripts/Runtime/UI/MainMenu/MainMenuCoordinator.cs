using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine;
using Runtime.GameModes.Wizard.Coordinator;
using Runtime.GameModes.Wizard.Online;
using Runtime.Infrastructure.GameStateMachine;
using Runtime.Infrastructure.Logging;
using Runtime.Localization.Contracts;
using Runtime.Services.UI;
using StripLog;

namespace Runtime.UI.MainMenu
{
    public sealed class MainMenuCoordinator : IMainMenuCoordinator
    {
        private MainMenuViewModel _viewModel;
        private readonly IGameWizardCoordinator _wizardCoordinator;
        private readonly MainMenuOverlayNavigator _overlayNavigator;
        private readonly MainMenuMatchStartFlow _matchStartFlow;
        private CompositeDisposable _disposables = new();
        
        private CancellationTokenSource _lifecycleCts = new();
        private bool _isDisposed;

        public MainMenuCoordinator(
            IGameStateMachine stateMachine,
            IUIService uiService,
            ILocalizationService localization,
            IGameWizardCoordinator wizardCoordinator,
            IOnlineSessionLauncher onlineSessionLauncher = null,
            IOnlineSessionFlowService onlineSessionFlow = null)
        {
            var resolvedStateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
            var resolvedUiService = uiService ?? throw new ArgumentNullException(nameof(uiService));
            var resolvedLocalization = localization ?? throw new ArgumentNullException(nameof(localization));
            _wizardCoordinator = wizardCoordinator ?? throw new ArgumentNullException(nameof(wizardCoordinator));

            _overlayNavigator = new MainMenuOverlayNavigator(resolvedUiService, resolvedLocalization);
            
            _matchStartFlow = new MainMenuMatchStartFlow(
                resolvedStateMachine,
                _wizardCoordinator,
                onlineSessionLauncher ?? NoOpOnlineSessionLauncher.Instance,
                onlineSessionFlow ?? NoOpOnlineSessionFlowService.Instance,
                _overlayNavigator);
        }

        public void Initialize(MainMenuViewModel viewModel)
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(MainMenuCoordinator));

            if (viewModel == null)
                throw new ArgumentNullException(nameof(viewModel));
            
            Cleanup();
            _viewModel = viewModel;
            _matchStartFlow.Initialize(_viewModel, _lifecycleCts.Token);
            
            _viewModel.StartGameRequested
                .Subscribe(_ => _matchStartFlow.OnStartGameAsync(_lifecycleCts.Token).Forget(MainMenuAsyncExceptionHandler.HandleFireAndForgetException))
                .AddTo(_disposables);

            _viewModel.StatisticsRequested
                .Subscribe(_ => _overlayNavigator.OpenStatisticsAsync(_lifecycleCts.Token).Forget(MainMenuAsyncExceptionHandler.HandleFireAndForgetException))
                .AddTo(_disposables);

            _viewModel.ExitRequested
                .Subscribe(_ => OnExit())
                .AddTo(_disposables);

            _viewModel.SettingsRequested
                .Subscribe(_ => _overlayNavigator.OpenSettingsAsync(_lifecycleCts.Token).Forget(MainMenuAsyncExceptionHandler.HandleFireAndForgetException))
                .AddTo(_disposables);
        }

        private void Cleanup()
        {
            _lifecycleCts.Cancel();
            _lifecycleCts.Dispose();
            _lifecycleCts = new CancellationTokenSource();
            _disposables?.Dispose();
            _disposables = new CompositeDisposable();
            _overlayNavigator.Reset();
            _matchStartFlow.Reset();
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

        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            _lifecycleCts.Cancel();
            _lifecycleCts.Dispose();

            _disposables.Dispose();
            _overlayNavigator.Dispose();
            _matchStartFlow.Dispose();
            
            _wizardCoordinator.AbortWizardAsync(AbortReason.SceneChange).Forget(MainMenuAsyncExceptionHandler.HandleDisposeException);
        }
    }

    internal static class MainMenuAsyncExceptionHandler
    {
        public static void HandleFireAndForgetException(Exception exception)
        {
            if (exception is OperationCanceledException)
                return;

            Log.Exception(exception, LogTags.UI);
        }

        public static void HandleDisposeException(Exception exception)
        {
            if (exception is OperationCanceledException or ObjectDisposedException)
                return;

            Log.Exception(exception, LogTags.UI);
        }
    }
}