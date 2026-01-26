using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Runtime.GameModes.Wizard;
using Runtime.Services.UI;

namespace Runtime.UI.GameModes.Wizard
{
    public sealed class GameModeWizardNavigator : IGameModeWizardNavigator
    {
        private readonly IUIService _uiService;

        public GameModeWizardNavigator(IUIService uiService) =>
            _uiService = uiService ?? throw new ArgumentNullException(nameof(uiService));

        public UniTask OpenModeSelectionAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            _uiService.Open<ModeSelectionView, ModeSelectionViewModel>();
            return UniTask.CompletedTask;
        }

        public UniTask CloseModeSelectionAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            _uiService.Close<ModeSelectionView>();
            return UniTask.CompletedTask;
        }

        public UniTask OpenMatchSetupAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            _uiService.Open<MatchSetupView, MatchSetupViewModel>();
            return UniTask.CompletedTask;
        }

        public UniTask CloseMatchSetupAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            _uiService.Close<MatchSetupView>();
            return UniTask.CompletedTask;
        }

        public UniTask<MatchmakingViewModel> OpenMatchmakingAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var view = _uiService.Open<MatchmakingView, MatchmakingViewModel>();
            return UniTask.FromResult(view?.GetViewModel());
        }

        public UniTask CloseMatchmakingAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            _uiService.Close<MatchmakingView>();
            return UniTask.CompletedTask;
        }

        public UniTask CloseAllWizardWindowsAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            _uiService.Close<ModeSelectionView>();
            _uiService.Close<MatchSetupView>();
            _uiService.Close<MatchmakingView>();
            return UniTask.CompletedTask;
        }
    }
}
