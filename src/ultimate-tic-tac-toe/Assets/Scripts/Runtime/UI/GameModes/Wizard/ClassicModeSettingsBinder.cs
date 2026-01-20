using R3;
using Runtime.Extensions;
using Runtime.GameModes.Wizard;
using Runtime.Infrastructure.Logging;
using Runtime.Localization;
using UnityEngine.UIElements;

namespace Runtime.UI.GameModes.Wizard
{
    public sealed class ClassicModeSettingsBinder : IModeSettingsBinder
    {
        private readonly ILocalizationService _localization;

        public ClassicModeSettingsBinder(ILocalizationService localization) =>
            _localization = localization ?? throw new System.ArgumentNullException(nameof(localization));

        public bool CanBind(ISpecificModeSettingsViewModel viewModel) => viewModel is ClassicSettingsViewModel;

        public void Bind(VisualElement root, ISpecificModeSettingsViewModel viewModel, CompositeDisposable disposables)
        {
            if (root == null || viewModel == null)
                return;

            if (viewModel is not ClassicSettingsViewModel classic)
                return;

            var decrementButton = root.Q<Button>("DecrementButton");
            var incrementButton = root.Q<Button>("IncrementButton");
            var boardSizeValue = root.Q<Label>("BoardSizeValue");

            var boardSizeTitle = root.Q<Label>("BoardSizeTitle");

            if (decrementButton == null || incrementButton == null || boardSizeValue == null || boardSizeTitle == null)
            {
                GameLog.Error("[ClassicModeSettingsBinder] Classic settings UXML is missing required elements.");
                return;
            }

            _localization
                .Observe(new TextTableId("GameModeWizard"), new TextKey("GameModeWizard.MatchSetup.Classic.BoardSize"))
                .Subscribe(text => boardSizeTitle.text = text)
                .AddTo(disposables);

            classic.BoardSize
                .Subscribe(size => boardSizeValue.text = size.ToString())
                .AddTo(disposables);

            decrementButton.OnClickAsObservable()
                .Subscribe(_ => classic.DecrementBoardSize())
                .AddTo(disposables);

            incrementButton.OnClickAsObservable()
                .Subscribe(_ => classic.IncrementBoardSize())
                .AddTo(disposables);
        }
    }
}
