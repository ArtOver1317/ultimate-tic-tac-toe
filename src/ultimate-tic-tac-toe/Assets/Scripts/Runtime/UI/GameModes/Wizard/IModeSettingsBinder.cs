using R3;
using Runtime.GameModes.Wizard;
using UnityEngine.UIElements;

namespace Runtime.UI.GameModes.Wizard
{
    public interface IModeSettingsBinder
    {
        bool CanBind(ISpecificModeSettingsViewModel viewModel);

        void Bind(VisualElement root, ISpecificModeSettingsViewModel viewModel, CompositeDisposable disposables);
    }
}
