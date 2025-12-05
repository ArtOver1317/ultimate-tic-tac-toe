using Runtime.Services.UI;
using Runtime.UI.MainMenu;
using UnityEngine;

namespace Runtime.Infrastructure.GameStateMachine.States
{
    public class MainMenuState : IState
    {
        private readonly UIService _uiService;
        private readonly MainMenuCoordinator _coordinator;

        public MainMenuState(
            UIService uiService, 
            MainMenuCoordinator coordinator)
        {
            _uiService = uiService;
            _coordinator = coordinator;
        }

        public void Enter()
        {
            Debug.Log("[MainMenuState] Entered MainMenu");
            var mainMenuPrefab = Resources.Load<GameObject>("MainMenu");
            _uiService.RegisterWindowPrefab<MainMenuView>(mainMenuPrefab);
            var view = _uiService.Open<MainMenuView, MainMenuViewModel>();
            var viewModel = view.GetViewModel();
            _coordinator.Initialize(viewModel);
        }

        public void Exit()
        {
            Debug.Log("[MainMenuState] Exiting MainMenu");
            _uiService.Close<MainMenuView>();
            _coordinator?.Dispose();
        }
    }
}

