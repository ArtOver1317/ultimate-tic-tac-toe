#nullable enable

using System;
using System.Collections.Generic;
using R3;
using Runtime.Extensions;
using Runtime.GameModes.Wizard.Modes;
using Runtime.GameModes.Wizard.ViewModels;
using Runtime.Localization;
using Runtime.Localization.Contracts;
using Runtime.Localization.Types;
using Runtime.UI.Components;
using Runtime.UI.Core;
using UnityEngine.UIElements;
using VContainer;

namespace Runtime.UI.GameModes.Wizard
{
    public sealed class GameSelectionView : UIView<GameSelectionViewModel>
    {
        private const float _modeListFixedItemHeight = 130;
        private ILocalizationService? _localization;

        [Core.UxmlElementAttribute("Title")] private Label? _titleLabel;
        
        [Core.UxmlElementAttribute("ModeList")]
        private ListView? _modeList;

        [Core.UxmlElementAttribute("CancelButton")]
        private Button? _cancelButton;

        [Core.UxmlElementAttribute("ContinueButton")]
        private Button? _continueButton;

        [Core.UxmlElementAttribute("ErrorOverlay", isOptional: true)]
        private WizardErrorOverlay? _errorOverlay;

        private IReadOnlyList<GameMetadata> _modes = Array.Empty<GameMetadata>();
        private bool _isSyncingSelection;

        [Inject]
        public void Construct(ILocalizationService localization) =>
            _localization = localization ?? throw new ArgumentNullException(nameof(localization));

        protected override void BindViewModel()
        {
            var modeList = GetRequired(_modeList, "ModeList");
            var titleLabel = GetRequired(_titleLabel, "Title");
            var cancelButton = GetRequired(_cancelButton, "CancelButton");
            var continueButton = GetRequired(_continueButton, "ContinueButton");

            ConfigureModeList(modeList);
            BindTexts(titleLabel, cancelButton, continueButton);
            BindModes();
            BindSelection(modeList);
            BindButtons(cancelButton, continueButton);
            BindButtonStates(continueButton);

            BindErrorOverlay();
        }

        private void ConfigureModeList(ListView modeList)
        {
            modeList.selectionType = SelectionType.Single;
            modeList.fixedItemHeight = _modeListFixedItemHeight;
            modeList.makeItem = static () => new GameCardElement();
            modeList.bindItem = BindModeCard;
        }

        private void BindTexts(Label titleLabel, Button cancelButton, Button continueButton)
        {
            BindText(ViewModel.TitleText, titleLabel);
            BindText(ViewModel.CancelButtonText, cancelButton);
            BindText(ViewModel.ContinueButtonText, continueButton);
        }

        private void BindModes()
        {
            SetModes(ViewModel.AvailableModes.CurrentValue);
            AddDisposable(ViewModel.AvailableModes.Subscribe(SetModes));
            AddDisposable(ViewModel.SelectedGameId.Subscribe(_ => SyncSelectionFromViewModel()));
        }

        private void BindSelection(ListView modeList)
        {
            void OnSelectionChanged(IEnumerable<object> items)
            {
                if (_isSyncingSelection)
                    return;

                foreach (var item in items)
                {
                    var gameId = (item as GameMetadata)?.Id;
                    ViewModel.SelectMode(gameId);
                    break;
                }
            }

            modeList.selectionChanged += OnSelectionChanged;
            AddDisposable(Disposable.Create(() => modeList.selectionChanged -= OnSelectionChanged));
        }

        private void BindButtons(Button cancelButton, Button continueButton)
        {
            AddDisposable(cancelButton.OnClickAsObservable().Subscribe(_ => ViewModel.RequestCancel()));
            AddDisposable(continueButton.OnClickAsObservable().Subscribe(_ => ViewModel.RequestContinue()));
        }

        private void BindButtonStates(Button continueButton)
        {
            var isBlocking = ViewModel.Error.Select(static error => error is { IsBlocking: true });
            var canContinue = ViewModel.CanContinue.CombineLatest(isBlocking,
                static (can, blocked) => can && !blocked);

            BindEnabled(canContinue, continueButton);
            BindEnabled(ViewModel.IsBusy.Select(static isBusy => !isBusy), Root);
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
                return key;

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
            var safeModes = modes ?? Array.Empty<GameMetadata>();
            _modes = safeModes;

            var modeList = _modeList ?? throw new InvalidOperationException("ModeList element is missing in UXML.");
            modeList.itemsSource = safeModes as System.Collections.IList ?? new List<GameMetadata>(safeModes);
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
                if (string.IsNullOrWhiteSpace(selectedId) || !TryGetSelectedIndex(selectedId, out var selectedIndex))
                {
                    ClearSelection(modeList);
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

        private bool TryGetSelectedIndex(string selectedId, out int selectedIndex)
        {
            for (var i = 0; i < _modes.Count; i++)
            {
                if (string.Equals(_modes[i].Id, selectedId, StringComparison.Ordinal))
                {
                    selectedIndex = i;
                    return true;
                }
            }

            selectedIndex = -1;
            return false;
        }

        private static void ClearSelection(ListView modeList)
        {
            modeList.ClearSelection();
            modeList.RefreshItems();
        }

        private static T GetRequired<T>(T? element, string elementName)
            where T : class =>
            element ?? throw new InvalidOperationException($"{elementName} element is missing in UXML.");
    }
}