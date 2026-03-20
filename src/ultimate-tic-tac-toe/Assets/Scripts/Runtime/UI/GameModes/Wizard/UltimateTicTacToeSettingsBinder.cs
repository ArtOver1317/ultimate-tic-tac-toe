using R3;
using Runtime.GameModes.Wizard.ViewModels;
using UnityEngine.UIElements;

namespace Runtime.UI.GameModes.Wizard
{
    public sealed class UltimateTicTacToeSettingsBinder : IGameSettingsBinder
    {
        public bool CanBind(IGameSettingsViewModel viewModel) => viewModel is UltimateTicTacToeSettingsViewModel;

        public void Bind(VisualElement root, IGameSettingsViewModel viewModel, CompositeDisposable disposables)
        {
            // Ultimate mode currently has no interactive settings controls in UXML.
            // Binder is intentionally no-op to mark VM type as supported and avoid runtime warning.
        }
    }
}