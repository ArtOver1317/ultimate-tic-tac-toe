using R3;
using Runtime.Extensions;
using Runtime.GameModes.Wizard;
using Runtime.Infrastructure.Logging;
using Runtime.Localization;
using UnityEngine.UIElements;

namespace Runtime.UI.GameModes.Wizard
{
    public sealed class TicTacToeSettingsBinder : IGameSettingsBinder
    {
        private readonly ILocalizationService _localization;

        public TicTacToeSettingsBinder(ILocalizationService localization) =>
            _localization = localization ?? throw new System.ArgumentNullException(nameof(localization));

        public bool CanBind(IGameSettingsViewModel viewModel) => viewModel is TicTacToeSettingsViewModel;

        public void Bind(VisualElement root, IGameSettingsViewModel viewModel, CompositeDisposable disposables)
        {
            if (root == null || viewModel == null) return;
            if (viewModel is not TicTacToeSettingsViewModel ttt) return;

            BindBoardSizeControls(root, ttt, disposables);
        }

        private void BindBoardSizeControls(VisualElement root, TicTacToeSettingsViewModel vm, CompositeDisposable disposables)
        {
            var decrementButton = root.Q<Button>("DecrementButton");
            var incrementButton = root.Q<Button>("IncrementButton");
            var boardSizeValue = root.Q<Label>("BoardSizeValue");
            var boardSizeTitle = root.Q<Label>("BoardSizeTitle");

            if (decrementButton == null || incrementButton == null || boardSizeValue == null || boardSizeTitle == null)
            {
                GameLog.Error("[TicTacToeSettingsBinder] Settings UXML is missing board size elements.");
                return;
            }

            _localization
                .Observe(new TextTableId("GameWizard"), new TextKey("GameWizard.MatchSetup.TicTacToe.BoardSize"))
                .Subscribe(text => boardSizeTitle.text = text)
                .AddTo(disposables);

            vm.BoardSize
                .Subscribe(size => boardSizeValue.text = size.ToString())
                .AddTo(disposables);

            decrementButton.OnClickAsObservable()
                .Subscribe(_ => vm.DecrementBoardSize())
                .AddTo(disposables);

            incrementButton.OnClickAsObservable()
                .Subscribe(_ => vm.IncrementBoardSize())
                .AddTo(disposables);
        }
    }
}
