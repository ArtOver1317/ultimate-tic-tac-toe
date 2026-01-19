using System;
using System.Collections.Generic;
using R3;
using Runtime.Extensions;
using Runtime.GameModes.Wizard;
using Runtime.UI.Core;
using UnityEngine.UIElements;

namespace Runtime.UI.GameModes.Wizard
{
    public sealed class ModeSelectionView : UIView<ModeSelectionViewModel>
    {
        [Runtime.UI.Core.UxmlElementAttribute("ModeList")]
        private ListView _modeList;

        [Runtime.UI.Core.UxmlElementAttribute("CancelButton")]
        private Button _cancelButton;

        [Runtime.UI.Core.UxmlElementAttribute("ContinueButton")]
        private Button _continueButton;

        protected override void BindViewModel()
        {
            if (_modeList == null)
                throw new InvalidOperationException("ModeList element is missing in UXML.");
            if (_cancelButton == null)
                throw new InvalidOperationException("CancelButton element is missing in UXML.");
            if (_continueButton == null)
                throw new InvalidOperationException("ContinueButton element is missing in UXML.");

            _modeList.selectionType = SelectionType.Single;
            _modeList.fixedItemHeight = 130;
            _modeList.makeItem = static () => new ModeCardElement();

            _modeList.bindItem = (element, index) =>
            {
                var card = element as ModeCardElement;
                if (card == null)
                    return;

                var modes = ViewModel.AvailableModes.CurrentValue;
                if (modes == null || index < 0 || index >= modes.Count)
                    return;

                var meta = modes[index];
                var selected = ViewModel.SelectedModeId.Value;
                var isSelected = string.Equals(selected, meta.Id, StringComparison.Ordinal);

                // Phase 5: show localization keys directly.
                // Proper localization binding is added in a later phase.
                card.Bind(
                    title: meta.DisplayNameKey ?? string.Empty,
                    description: meta.DescriptionKey ?? string.Empty,
                    iconKey: meta.IconAssetKey,
                    isSelected: isSelected);
            };

            BindModes(ViewModel.AvailableModes.CurrentValue);

            // If modes ever change (unlikely for now), rebind.
            AddDisposable(ViewModel.AvailableModes.Subscribe(BindModes));

            // Keep selection highlight in sync.
            AddDisposable(ViewModel.SelectedModeId.Subscribe(_ => _modeList.RefreshItems()));

            // List selection -> VM
            void OnSelectionChanged(IEnumerable<object> items)
            {
                if (items == null)
                    return;

                foreach (var it in items)
                {
                    if (it is GameModeMetadata meta)
                        ViewModel.SelectMode(meta.Id);
                    break;
                }
            }

            _modeList.selectionChanged += OnSelectionChanged;
            AddDisposable(Disposable.Create(() => _modeList.selectionChanged -= OnSelectionChanged));

            BindEnabled(ViewModel.CanContinue, _continueButton);

            AddDisposable(_cancelButton.OnClickAsObservable().Subscribe(_ => ViewModel.RequestCancel()));
            AddDisposable(_continueButton.OnClickAsObservable().Subscribe(_ => ViewModel.RequestContinue()));
        }

        private void BindModes(IReadOnlyList<GameModeMetadata> modes)
        {
            if (_modeList == null)
                return;

            _modeList.itemsSource = modes == null
                ? null
                : (modes as System.Collections.IList) ?? new List<GameModeMetadata>(modes);
            _modeList.Rebuild();

            // Restore selection from VM/session.
            var selectedId = ViewModel.SelectedModeId.Value;
            if (string.IsNullOrWhiteSpace(selectedId) || modes == null)
                return;

            for (var i = 0; i < modes.Count; i++)
            {
                if (string.Equals(modes[i].Id, selectedId, StringComparison.Ordinal))
                {
                    _modeList.SetSelection(i);
                    break;
                }
            }
        }
    }
}
