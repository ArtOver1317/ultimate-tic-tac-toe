using System.Threading;
using Cysharp.Threading.Tasks;

namespace Runtime.GameModes.Wizard
{
    /// <summary>
    /// Navigation adapter used by <see cref="GameModeWizardCoordinator"/>.
    /// Abstracts away concrete UI windows and allows Phase 3 to stay UI-agnostic.
    /// </summary>
    public interface IGameModeWizardNavigator
    {
        UniTask OpenModeSelectionAsync(CancellationToken ct);
        UniTask CloseModeSelectionAsync(CancellationToken ct);

        UniTask OpenMatchSetupAsync(CancellationToken ct);
        UniTask CloseMatchSetupAsync(CancellationToken ct);

        UniTask<MatchmakingViewModel> OpenMatchmakingAsync(CancellationToken ct);
        UniTask CloseMatchmakingAsync(CancellationToken ct);

        UniTask CloseAllWizardWindowsAsync(CancellationToken ct);
    }
}
