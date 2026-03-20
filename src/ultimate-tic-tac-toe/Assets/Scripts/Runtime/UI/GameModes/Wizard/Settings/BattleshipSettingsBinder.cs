#nullable enable

using R3;
using Runtime.GameModes.Wizard.ViewModels;
using Runtime.Infrastructure.Logging;
using Runtime.Localization;
using Runtime.Localization.Contracts;
using Runtime.UI.Components;
using UnityEngine.UIElements;
using TextKey = Runtime.Localization.Types.TextKey;
using TextTableId = Runtime.Localization.Types.TextTableId;

namespace Runtime.UI.GameModes.Wizard
{
    public sealed class BattleshipSettingsBinder : IGameSettingsBinder
    {
        private readonly ILocalizationService _localization;

        public BattleshipSettingsBinder(ILocalizationService localization) =>
            _localization = localization ?? throw new System.ArgumentNullException(nameof(localization));

        public bool CanBind(IGameSettingsViewModel viewModel) => viewModel is IBattleshipSettingsViewModel;

        public void Bind(VisualElement root, IGameSettingsViewModel viewModel, CompositeDisposable disposables)
        {
            if (viewModel is not IBattleshipSettingsViewModel vm)
                return;

            var title = root.Q<Label>("PlacementTimerTitle");
            var chips = root.Q<DifficultyChips>("PlacementTimerChips");

            if (title == null || chips == null)
            {
                GameLog.Error("[BattleshipSettingsBinder] Settings UXML is missing placement timer elements.");
                return;
            }

            _localization
                .Observe(new TextTableId("GameWizard"), new TextKey("GameWizard.Battleship.PlacementTimer.Label"))
                .Subscribe(text => title.text = text)
                .AddTo(disposables);

            vm.PlacementTimerPresetItems
                .Subscribe(items =>
                {
                    chips.SetItems(items);
                    chips.SetSelectedIdWithoutNotify(vm.SelectedPlacementTimerPresetId.CurrentValue);
                })
                .AddTo(disposables);

            vm.SelectedPlacementTimerPresetId
                .Subscribe(id => chips.SetSelectedIdWithoutNotify(id))
                .AddTo(disposables);

            chips.SelectedIdChanged += OnSelected;
            
            Disposable.Create(() => chips.SelectedIdChanged -= OnSelected)
                .AddTo(disposables);

            return;

            void OnSelected(string id) => vm.SetSelectedPlacementTimerPresetId(id);
        }
    }
}