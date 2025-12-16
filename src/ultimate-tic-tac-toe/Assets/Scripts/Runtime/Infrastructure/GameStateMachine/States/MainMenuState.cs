using System.Threading;
using Cysharp.Threading.Tasks;
using Runtime.Infrastructure.Logging;
using Runtime.Services.UI;
using Runtime.UI.MainMenu;
using StripLog;
using UnityEngine;

namespace Runtime.Infrastructure.GameStateMachine.States
{
    public class MainMenuState : IState
    {
        private readonly IUIService _uiService;
        private readonly IMainMenuCoordinator _coordinator;
        private bool _isExited;

        public MainMenuState(
            IUIService uiService, 
            IMainMenuCoordinator coordinator)
        {
            _uiService = uiService;
            _coordinator = coordinator;
        }

        public UniTask EnterAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _isExited = false;
            Log.Debug(LogTags.Scenes, "[MainMenuState] Entered MainMenu");
            var mainMenuPrefab = Resources.Load<GameObject>("MainMenu");
            _uiService.RegisterWindowPrefab<MainMenuView>(mainMenuPrefab);
            var view = _uiService.Open<MainMenuView, MainMenuViewModel>();
            
            if (view == null)
            {
                Log.Error(LogTags.UI, "[MainMenuState] Failed to open MainMenuView!");
                return UniTask.CompletedTask;
            }
            
            var viewModel = view.GetViewModel();
            _coordinator.Initialize(viewModel);

            return UniTask.CompletedTask;
        }

        public void Exit()
        {
            if (_isExited)
                return;
            
            _isExited = true;
            Log.Debug(LogTags.Scenes, "[MainMenuState] Exiting MainMenu");
            _uiService.Close<MainMenuView>();
            _coordinator.Dispose();
        }
    }
}

