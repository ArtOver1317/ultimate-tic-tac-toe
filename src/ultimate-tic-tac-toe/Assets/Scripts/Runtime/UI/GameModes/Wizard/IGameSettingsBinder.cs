using R3;
using Runtime.GameModes.Wizard;
using Runtime.GameModes.Wizard.ViewModels;
using UnityEngine.UIElements;

namespace Runtime.UI.GameModes.Wizard
{
    public interface IGameSettingsBinder
    {
        bool CanBind(IGameSettingsViewModel viewModel);

        void Bind(VisualElement root, IGameSettingsViewModel viewModel, CompositeDisposable disposables);
    }
}
