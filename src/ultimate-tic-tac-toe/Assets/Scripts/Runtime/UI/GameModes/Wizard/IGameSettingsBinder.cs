using R3;
using Runtime.GameModes.Wizard;
using UnityEngine.UIElements;

namespace Runtime.UI.GameModes.Wizard
{
    public interface IGameSettingsBinder
    {
        bool CanBind(IGameSettingsViewModel viewModel);

        void Bind(VisualElement root, IGameSettingsViewModel viewModel, CompositeDisposable disposables);
    }
}
