#nullable enable

using R3;
using Runtime.Extensions;
using Runtime.GameModes.Wizard;
using Runtime.Infrastructure.Logging;
using Runtime.UI.Components;
using UnityEngine.UIElements;

namespace Runtime.UI.GameModes.Wizard
{
    public sealed class MoveTimerSettingsBinder
    {
        public void Bind(VisualElement root, MoveTimerSettingsViewModel viewModel, CompositeDisposable disposables)
        {
            if (root == null || viewModel == null)
                return;

            var title = root.Q<Label>("MoveTimerTitle");
            var chips = root.Q<DifficultyChips>("MoveTimerChips");

            if (title == null || chips == null)
            {
                GameLog.Error("[MoveTimerSettingsBinder] Settings UXML is missing move timer elements.");
                return;
            }

            viewModel.TitleText
                .Subscribe(text => title.text = text)
                .AddTo(disposables);

            viewModel.PresetItems
                .Subscribe(items =>
                {
                    chips.SetItems(items);
                    chips.SetSelectedIdWithoutNotify(viewModel.SelectedPresetId.CurrentValue);
                })
                .AddTo(disposables);

            viewModel.SelectedPresetId
                .Subscribe(id => chips.SetSelectedIdWithoutNotify(id))
                .AddTo(disposables);

            void OnSelected(string id) => viewModel.SetSelectedPresetId(id);

            chips.SelectedIdChanged += OnSelected;
            Disposable.Create(() => chips.SelectedIdChanged -= OnSelected)
                .AddTo(disposables);
        }
    }
}