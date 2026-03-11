#nullable enable

using System;
using System.Collections.Generic;
using R3;
using Runtime.Extensions;
using Runtime.GameModes.Wizard;
using Runtime.GameModes.Wizard.Modes;
using Runtime.GameModes.Wizard.ViewModels;
using Runtime.Localization;
using Runtime.UI.Components;
using Runtime.UI.Core;
using UnityEngine.UIElements;
using VContainer;

namespace Runtime.UI.GameModes.Wizard
{
    public sealed class GameSelectionView : UIView<GameSelectionViewModel>
    {
        private ILocalizationService? _localization;

        [Runtime.UI.Core.UxmlElementAttribute("Title")]
        private Label? _titleLabel;
        [Runtime.UI.Core.UxmlElementAttribute("ModeList")]
        private ListView? _modeList;

        [Runtime.UI.Core.UxmlElementAttribute("CancelButton")]
        private Button? _cancelButton;

        [Runtime.UI.Core.UxmlElementAttribute("ContinueButton")]
        private Button? _continueButton;

        [Runtime.UI.Core.UxmlElementAttribute("ErrorOverlay", isOptional: true)]
        private WizardErrorOverlay? _errorOverlay;

        private IReadOnlyList<GameMetadata> _modes = Array.Empty<GameMetadata>();
        private bool _isSyncingSelection;

        internal Action<string?> OnSelectModeInvokedForTests { get; set; } = static _ => { };

        [Inject]
        public void Construct(ILocalizationService localization) =>
            _localization = localization ?? throw new ArgumentNullException(nameof(localization));

        protected override void BindViewModel()
        {
            if (_modeList == null)
                throw new InvalidOperationException("ModeList element is missing in UXML.");
            if (_titleLabel == null)
                throw new InvalidOperationException("Title element is missing in UXML.");
            if (_cancelButton == null)
                throw new InvalidOperationException("CancelButton element is missing in UXML.");
            if (_continueButton == null)
                throw new InvalidOperationException("ContinueButton element is missing in UXML.");

            _modeList.selectionType = SelectionType.Single;
            _modeList.fixedItemHeight = 130;
            _modeList.makeItem = static () => new GameCardElement();

            _modeList.bindItem = BindModeCard;

            BindText(ViewModel.TitleText, _titleLabel);
            BindText(ViewModel.CancelButtonText, _cancelButton);
            BindText(ViewModel.ContinueButtonText, _continueButton);

            SetModes(ViewModel.AvailableModes.CurrentValue);
            AddDisposable(ViewModel.AvailableModes.Subscribe(SetModes));
            AddDisposable(ViewModel.SelectedGameId.Subscribe(_ => SyncSelectionFromViewModel()));


            // List selection -> VM
            void OnSelectionChanged(IEnumerable<object> items)
            {
                if (_isSyncingSelection || items == null)
                    return;

                foreach (var it in items)
                {
                    var gameId = (it as GameMetadata)?.Id;
                    ViewModel.SelectMode(gameId);
                    OnSelectModeInvokedForTests?.Invoke(gameId);
                    break;
                }
            }

            _modeList.selectionChanged += OnSelectionChanged;
            AddDisposable(Disposable.Create(() => _modeList.selectionChanged -= OnSelectionChanged));

            var isBlocking = ViewModel.Error.Select(static error => error != null && error.IsBlocking);
            var canContinue = Observable.CombineLatest(
                ViewModel.CanContinue,
                isBlocking,
                static (can, blocked) => can && !blocked);

            BindEnabled(canContinue, _continueButton);
            BindEnabled(ViewModel.IsBusy.Select(static isBusy => !isBusy), Root);

            AddDisposable(_cancelButton.OnClickAsObservable().Subscribe(_ => ViewModel.RequestCancel()));
            AddDisposable(_continueButton.OnClickAsObservable().Subscribe(_ => ViewModel.RequestContinue()));

            BindErrorOverlay();
        }

        private void BindErrorOverlay()
        {
            var overlay = _errorOverlay;
            if (overlay == null)
                return;

            if (_localization == null)
                throw new InvalidOperationException("Localization service is not available for error overlay binding.");

            AddDisposable(WizardErrorOverlayBinder.Bind(overlay, _localization, ViewModel.Error, ViewModel.AcknowledgeError));
        }

        private void BindModeCard(VisualElement element, int index)
        {
            if (element is not GameCardElement card)
                return;

            if (index < 0 || index >= _modes.Count)
                return;

            var meta = _modes[index];
            var isSelected = string.Equals(ViewModel.SelectedGameId.Value, meta.Id, StringComparison.Ordinal);

            var title = ResolveModeText(meta.DisplayNameKey);
            var description = ResolveModeText(meta.DescriptionKey);
            card.Bind(title, description, meta.IconAssetKey, isSelected);
        }

        private string ResolveModeText(string key)
        {
            if (_localization == null || string.IsNullOrWhiteSpace(key))
                return key ?? string.Empty;

            // Keys are stored in localization tables as fully-qualified strings, e.g. "Game.TicTacToe".
            // Resolve against the table inferred from the prefix ("Game" in this example).
            var dotIndex = key.IndexOf('.');
            if (dotIndex <= 0)
                return key;

            var tableName = key.Substring(0, dotIndex);
            return _localization.Resolve(new TextTableId(tableName), new TextKey(key));
        }

        private void SetModes(IReadOnlyList<GameMetadata> modes)
        {
            _modes = modes ?? Array.Empty<GameMetadata>();

            var modeList = _modeList ?? throw new InvalidOperationException("ModeList element is missing in UXML.");
            modeList.itemsSource = _modes as System.Collections.IList ?? new List<GameMetadata>(_modes);
            modeList.Rebuild();

            SyncSelectionFromViewModel();
        }

        private void SyncSelectionFromViewModel()
        {
            var selectedId = ViewModel.SelectedGameId.Value;
            var modeList = _modeList ?? throw new InvalidOperationException("ModeList element is missing in UXML.");

            _isSyncingSelection = true;

            try
            {
                if (string.IsNullOrWhiteSpace(selectedId) || _modes.Count == 0)
                {
                    modeList.ClearSelection();
                    modeList.RefreshItems();
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
                    modeList.ClearSelection();
                    modeList.RefreshItems();
                    return;
                }

                modeList.SetSelection(selectedIndex);
                modeList.RefreshItems();
            }
            finally
            {
                _isSyncingSelection = false;
            }
        }
    }
}

#nullable restore
