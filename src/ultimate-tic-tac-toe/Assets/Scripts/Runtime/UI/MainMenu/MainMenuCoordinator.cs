using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using Runtime.Infrastructure.Logging;
using Runtime.Infrastructure.GameStateMachine;
using Runtime.Infrastructure.GameStateMachine.States;
using StripLog;

namespace Runtime.UI.MainMenu
{
    public class MainMenuCoordinator : IMainMenuCoordinator
    {
        private MainMenuViewModel _viewModel;
        private readonly IGameStateMachine _stateMachine;
        private CompositeDisposable _disposables = new();
        private CancellationTokenSource _lifecycleCts = new();
        private bool _isDisposed;

        public MainMenuCoordinator(IGameStateMachine stateMachine) =>
            _stateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));

        public void Initialize(MainMenuViewModel viewModel)
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(MainMenuCoordinator));

            if (viewModel == null)
                throw new ArgumentNullException(nameof(viewModel));
            
            Cleanup();
            _viewModel = viewModel;
            
            _viewModel.StartGameRequested
                .Subscribe(_ => OnStartGameAsync(_lifecycleCts.Token).Forget())
                .AddTo(_disposables);

            _viewModel.ExitRequested
                .Subscribe(_ => OnExit())
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