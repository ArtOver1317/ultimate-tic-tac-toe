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

        private IReadOnlyList<GameModeMetadata> _modes = Array.Empty<GameModeMetadata>();
        private bool _isSyncingSelection;

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

            _modeList.bindItem = BindModeCard;

            SetModes(ViewModel.AvailableModes.CurrentValue);
            AddDisposable(ViewModel.AvailableModes.Subscribe(SetModes));
            AddDisposable(ViewModel.SelectedModeId.Subscribe(_ => SyncSelectionFromViewModel()));

            // List selection -> VM
            void OnSelectionChanged(IEnumerable<object> items)
            {
                if (_isSyncingSelection || items == null)
                    return;

                foreach (var it in items)
                {
                    ViewModel.SelectMode((it as GameModeMetadata)?.Id);
                    break;
                }
            }

            _modeList.selectionChanged += OnSelectionChanged;
            AddDisposable(Disposable.Create(() => _modeList.selectionChanged -= OnSelectionChanged));

            BindEnabled(ViewModel.CanContinue, _continueButton);

            AddDisposable(_cancelButton.OnClickAsObservable().Subscribe(_ => ViewModel.RequestCancel()));
            AddDisposable(_continueButton.OnClickAsObservable().Subscribe(_ => ViewModel.RequestContinue()));
        }

        private void BindModeCard(VisualElement element, int index)
        {
            if (element is not ModeCardElement card)
                return;

            if (index < 0 || index >= _modes.Count)
                return;

            var meta = _modes[index];
            var isSelected = string.Equals(ViewModel.SelectedModeId.Value, meta.Id, StringComparison.Ordinal);

            // Phase 5: show localization keys directly.
            // Proper localization binding is added in a later phase.
            card.Bind(meta, isSelected);
        }

        private void SetModes(IReadOnlyList<GameModeMetadata> modes)
        {
            _modes = modes ?? Array.Empty<GameModeMetadata>();

            _modeList.itemsSource = _modes as System.Collections.IList ?? new List<GameModeMetadata>(_modes);
            _modeList.Rebuild();

            SyncSelectionFromViewModel();
        }

        private void SyncSelectionFromViewModel()
        {
            var selectedId = ViewModel.SelectedModeId.Value;

            _isSyncingSelection = true;

            try
            {
                if (string.IsNullOrWhiteSpace(selectedId) || _modes.Count == 0)
                {
                    _modeList.ClearSelection();
                    _modeList.RefreshItems();
                    return;
                }

                var selectedIndex = -1;
                for (var i = 0; i < _modes.Count; i++)
                {
                    if (string.Equals(_modes[i].Id, selectedId, StringComparison.Ordinal))
                    {
                        selectedIndex = i;
                        break;
                    }
                }

                if (selectedIndex < 0)
                {
                    _modeList.ClearSelection();
                    _modeList.RefreshItems();
                    return;
                }

                _modeList.SetSelection(selectedIndex);
                _modeList.RefreshItems();
            }
            finally
            {
                _isSyncingSelection = false;
            }
        }
    }
}
