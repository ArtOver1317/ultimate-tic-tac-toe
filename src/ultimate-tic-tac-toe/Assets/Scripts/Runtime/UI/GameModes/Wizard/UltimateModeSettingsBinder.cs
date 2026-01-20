using R3;
using Runtime.GameModes.Wizard;
using Runtime.Localization;
using UnityEngine.UIElements;

namespace Runtime.UI.GameModes.Wizard
{
    public sealed class UltimateModeSettingsBinder : IModeSettingsBinder
    {
        private readonly ILocalizationService _localization;

        public UltimateModeSettingsBinder(ILocalizationService localization)
        {
            _localization = localization ?? throw new System.ArgumentNullException(nameof(localization));
        }

        public bool CanBind(ISpecificModeSettingsViewModel viewModel) => viewModel is UltimateSettingsViewModel;

        public void Bind(VisualElement root, ISpecificModeSettingsViewModel viewModel, CompositeDisposable disposables)
        {
            if (root == null || viewModel == null)
                return;

            var infoLabel = root.Q<Label>("InfoLabel");
            if (infoLabel == null)
                return;

            _localization
                .Observe(new TextTableId("GameModeWizard"), new TextKey("GameModeWizard.MatchSetup.Ultimate.NoSettings"))
                .Subscribe(text => infoLabel.text = text)
                .AddTo(disposables);
        }
    }
}
