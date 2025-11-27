using Infrastructure.States;
using R3;
using UnityEngine;

namespace UI.MainMenu
{
    public class MainMenuCoordinator
    {
        private MainMenuViewModel _viewModel;
        private readonly IGameStateMachine _stateMachine;
        private readonly CompositeDisposable _disposables = new();

        public MainMenuCoordinator(IGameStateMachine stateMachine) => 
            _stateMachine = stateMachine;

        public void Initialize(MainMenuViewModel viewModel)
        {
            _viewModel = viewModel;
            
            _viewModel.OnStartGameClicked
                .Subscribe(_ => OnStartGame())
                .AddTo(_disposables);

            _viewModel.OnExitClicked
                .Subscribe(_ => OnExit())
                .AddTo(_disposables);
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

