using R3;
using Runtime.Infrastructure.States;
using UnityEngine;

namespace Runtime.UI.MainMenu
{
    public class MainMenuCoordinator
    {
        private MainMenuViewModel _viewModel;
        private readonly IGameStateMachine _stateMachine;
        private CompositeDisposable _disposables = new();

        public MainMenuCoordinator(IGameStateMachine stateMachine) => 
            _stateMachine = stateMachine;

        public void Initialize(MainMenuViewModel viewModel)
        {
            Cleanup();
            _viewModel = viewModel;
            
            _viewModel.OnStartGameClicked
                .Subscribe(_ => OnStartGame())
                .AddTo(_disposables);

            _viewModel.OnExitClicked
                .Subscribe(_ => OnExit())
                .AddTo(_disposables);
        }

        private void Cleanup()
        {
            _disposables?.Dispose();
            _disposables = new CompositeDisposable();
        }

        private void OnStartGame()
        {
            Debug.Log("[MainMenuPresenter] Starting game...");
            _viewModel.SetInteractable(false);
            _stateMachine.Enter<LoadGameplayState>();
        }

        private void OnExit()
        {
            Debug.Log("[MainMenuPresenter] Exiting game...");
            
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        public void Dispose() => _disposables.Dispose();
    }
}
