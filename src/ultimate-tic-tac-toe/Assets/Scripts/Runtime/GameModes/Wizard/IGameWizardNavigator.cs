using System.Threading;
using Cysharp.Threading.Tasks;
using Runtime.GameModes.Wizard.Matchmaking;

namespace Runtime.GameModes.Wizard
{
    /// <summary>
    /// Navigation adapter used by <see cref="GameWizardCoordinator"/>.
    /// Abstracts away concrete UI windows and allows Phase 3 to stay UI-agnostic.
    /// 
    /// Contract: during transitions the coordinator closes the previous window
    /// before opening the next one. All wizard windows must disable input
    /// while the coordinator is transitioning/submitting.
    /// </summary>
    public interface IGameWizardNavigator
    {
        UniTask OpenModeSelectionAsync(CancellationToken ct);
        UniTask CloseModeSelectionAsync(CancellationToken ct);

        UniTask OpenMatchSetupAsync(CancellationToken ct);
        UniTask CloseMatchSetupAsync(CancellationToken ct);

        UniTask<MatchmakingViewModel> OpenMatchmakingAsync(CancellationToken ct);
        UniTask CloseMatchmakingAsync(CancellationToken ct);

        UniTask ReplaceModeSelectionWithMatchSetupAsync(CancellationToken ct);
        UniTask ReplaceMatchSetupWithModeSelectionAsync(CancellationToken ct);
        UniTask<MatchmakingViewModel> ReplaceMatchSetupWithMatchmakingAsync(CancellationToken ct);
        UniTask ReplaceMatchmakingWithMatchSetupAsync(CancellationToken ct);

        UniTask CloseAllWizardWindowsAsync(CancellationToken ct);
    }
}
