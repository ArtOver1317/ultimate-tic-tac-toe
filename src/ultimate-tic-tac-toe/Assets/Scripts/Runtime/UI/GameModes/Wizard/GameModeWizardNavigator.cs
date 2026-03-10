using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Runtime.Localization;
using Runtime.GameModes.Wizard;
using Runtime.GameModes.Wizard.Matchmaking;
using Runtime.GameModes.Wizard.Matchmaking.Runtime;
using Runtime.Services.UI;

namespace Runtime.UI.GameModes.Wizard
{
    public sealed class GameWizardNavigator : IGameWizardNavigator
    {
        private readonly IUIService _uiService;
        private readonly ILocalizationService _localization;

        private static readonly TextTableId[] ModeSelectionTables =
        {
            new("GameWizard"),
            new("Mode"),
            new("Game"),
        };

        private static readonly TextTableId[] WizardTables =
        {
            new("GameWizard"),
            new("Game"),
        };

        public GameWizardNavigator(IUIService uiService, ILocalizationService localization)
        {
            _uiService = uiService ?? throw new ArgumentNullException(nameof(uiService));
            _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        }

        public async UniTask OpenModeSelectionAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            await _uiService.OpenWithLocalizationPreloadAsync<GameSelectionView, GameSelectionViewModel>(
                _localization,
                ModeSelectionTables,
                ct);
        }

        public UniTask CloseModeSelectionAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            _uiService.Close<GameSelectionView>();
            return UniTask.CompletedTask;
        }

        public async UniTask OpenMatchSetupAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            await _uiService.OpenWithLocalizationPreloadAsync<MatchSetupView, MatchSetupViewModel>(
                _localization,
                WizardTables,
                ct);
        }

        public UniTask CloseMatchSetupAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            _uiService.Close<MatchSetupView>();
            return UniTask.CompletedTask;
        }

        public async UniTask<MatchmakingViewModel> OpenMatchmakingAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var view = await _uiService.OpenWithLocalizationPreloadAsync<MatchmakingView, MatchmakingViewModel>(
                _localization,
                WizardTables,
                ct);
            return view?.GetViewModel();
        }

        public UniTask CloseMatchmakingAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            _uiService.Close<MatchmakingView>();
            return UniTask.CompletedTask;
        }

        public UniTask ReplaceModeSelectionWithMatchSetupAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return _uiService.ReplaceWithLocalizationPreloadAsync<GameSelectionView, MatchSetupView, MatchSetupViewModel>(
                _localization,
                WizardTables,
                ct);
        }

        public UniTask ReplaceMatchSetupWithModeSelectionAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return _uiService.ReplaceWithLocalizationPreloadAsync<MatchSetupView, GameSelectionView, GameSelectionViewModel>(
                _localization,
                ModeSelectionTables,
                ct);
        }

        public async UniTask<MatchmakingViewModel> ReplaceMatchSetupWithMatchmakingAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var view = await _uiService.ReplaceWithLocalizationPreloadAsync<MatchSetupView, MatchmakingView, MatchmakingViewModel>(
                _localization,
                WizardTables,
                ct);
            return view?.GetViewModel();
        }

        public UniTask ReplaceMatchmakingWithMatchSetupAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return _uiService.ReplaceWithLocalizationPreloadAsync<MatchmakingView, MatchSetupView, MatchSetupViewModel>(
                _localization,
                WizardTables,
                ct);
        }

        public UniTask CloseAllWizardWindowsAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            _uiService.Close<GameSelectionView>();
            _uiService.Close<MatchSetupView>();
            _uiService.Close<MatchmakingView>();
            return UniTask.CompletedTask;
        }
    }
}
