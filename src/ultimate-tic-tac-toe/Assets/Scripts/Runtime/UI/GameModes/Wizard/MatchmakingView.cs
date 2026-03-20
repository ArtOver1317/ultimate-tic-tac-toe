#nullable enable

using System;
using R3;
using Runtime.Extensions;
using Runtime.GameModes.Wizard.Matchmaking.Runtime;
using Runtime.GameModes.Wizard.Session;
using Runtime.Localization;
using Runtime.UI.Components;
using Runtime.UI.Core;
using UnityEngine.UIElements;
using VContainer;

namespace Runtime.UI.GameModes.Wizard
{
    public sealed class MatchmakingView : UIView<MatchmakingViewModel>
    {
        [Core.UxmlElementAttribute("TitleLabel")]
        private Label? _titleLabel;

        [Core.UxmlElementAttribute("SearchingState")]
        private VisualElement? _searchingState;

        [Core.UxmlElementAttribute("Spinner")]
        private MatchmakingSpinner? _spinner;

        [Core.UxmlElementAttribute("Timer")]
        private MatchmakingTimer? _timer;

        [Core.UxmlElementAttribute("HintLabel", isOptional: true)]
        private Label? _hintLabel;

        [Core.UxmlElementAttribute("FoundState")]
        private VisualElement? _foundState;

        [Core.UxmlElementAttribute("FoundLabel")]
        private Label? _foundLabel;

        [Core.UxmlElementAttribute("FailedState")]
        private VisualElement? _failedState;

        [Core.UxmlElementAttribute("FailedLabel")]
        private Label? _failedLabel;

        [Core.UxmlElementAttribute("ErrorLabel")]
        private Label? _errorLabel;

        [Core.UxmlElementAttribute("CancelledState")]
        private VisualElement? _cancelledState;

        [Core.UxmlElementAttribute("CancelledLabel")]
        private Label? _cancelledLabel;

        [Core.UxmlElementAttribute("CancelButton")]
        private Button? _cancelButton;

        [Core.UxmlElementAttribute("RetryButton")]
        private Button? _retryButton;

        [Core.UxmlElementAttribute("ErrorOverlay", isOptional: true)]
        private WizardErrorOverlay? _errorOverlay;

        private ILocalizationService? _localization;

        private string _cancelLabel = string.Empty;
        private string _backLabel = string.Empty;
        private int _hintCount;

        [Inject]
        public void Construct(ILocalizationService localization) => 
            _localization = localization ?? throw new ArgumentNullException(nameof(localization));

        protected override void BindViewModel()
        {
            var titleLabel = GetRequired(_titleLabel, "TitleLabel");
            var searchingState = GetRequired(_searchingState, "SearchingState");
            var spinner = GetRequired(_spinner, "Spinner");
            var timer = GetRequired(_timer, "Timer");
            var foundState = GetRequired(_foundState, "FoundState");
            var foundLabel = GetRequired(_foundLabel, "FoundLabel");
            var failedState = GetRequired(_failedState, "FailedState");
            var failedLabel = GetRequired(_failedLabel, "FailedLabel");
            var errorLabel = GetRequired(_errorLabel, "ErrorLabel");
            var cancelledState = GetRequired(_cancelledState, "CancelledState");
            var cancelledLabel = GetRequired(_cancelledLabel, "CancelledLabel");
            var cancelButton = GetRequired(_cancelButton, "CancelButton");
            var retryButton = GetRequired(_retryButton, "RetryButton");

            BindStaticTexts(titleLabel, foundLabel, failedLabel, cancelledLabel, retryButton);
            BindSearchingSection(timer);
            BindHintSection();
            BindStateSection(searchingState, foundState, failedState, cancelledState, cancelButton, retryButton, spinner);
            BindBusyState();
            BindErrorMessage(errorLabel);
            BindErrorOverlay();
            BindButtons(cancelButton, retryButton);
        }

        private void BindErrorOverlay()
        {
            var overlay = _errorOverlay;
            
            if (overlay == null)
                return;

            var localization = _localization ?? throw new InvalidOperationException("Localization service is not available for error overlay binding.");

            AddDisposable(WizardErrorOverlayBinder.Bind(overlay, localization, ViewModel.Error, ViewModel.AcknowledgeError));
        }

        private void BindStaticTexts(Label titleLabel, Label foundLabel, Label failedLabel, Label cancelledLabel, Button retryButton)
        {
            BindText(ViewModel.TitleText, titleLabel);
            BindText(ViewModel.FoundText, foundLabel);
            BindText(ViewModel.FailedText, failedLabel);
            BindText(ViewModel.CancelledText, cancelledLabel);
            BindText(ViewModel.RetryButtonText, retryButton);
        }

        private void BindSearchingSection(MatchmakingTimer timer)
        {
            AddDisposable(ViewModel.SearchingPrefixText.Subscribe(timer.SetPrefix));
            AddDisposable(ViewModel.ElapsedTime.Subscribe(timer.SetTime));

            AddDisposable(ViewModel.CancelButtonText.Subscribe(text =>
            {
                _cancelLabel = text ?? string.Empty;
                UpdateCancelButtonLabel(ViewModel.State.CurrentValue);
            }));
            
            AddDisposable(ViewModel.BackButtonText.Subscribe(text =>
            {
                _backLabel = text ?? string.Empty;
                UpdateCancelButtonLabel(ViewModel.State.CurrentValue);
            }));
        }

        private void BindHintSection()
        {
            if (_hintLabel == null)
                return;

            AddDisposable(ViewModel.HintText.Subscribe(text =>
            {
                _hintLabel.text = text ?? string.Empty;
                UpdateHintVisibility();
            }));
            
            AddDisposable(ViewModel.PlayersWithDifferentParams.Subscribe(count =>
            {
                _hintCount = count;
                UpdateHintVisibility();
            }));
        }

        private void BindStateSection(
            VisualElement searchingState,
            VisualElement foundState,
            VisualElement failedState,
            VisualElement cancelledState,
            Button cancelButton,
            Button retryButton,
            MatchmakingSpinner spinner) =>
            AddDisposable(ViewModel.State.Subscribe(state =>
            {
                UpdateState(searchingState, foundState, failedState, cancelledState, cancelButton, retryButton, spinner, state);
                UpdateCancelButtonLabel(state);
            }));

        private void BindBusyState() =>
            BindEnabled(ViewModel.IsBusy.Select(static isBusy => !isBusy), Root);

        private void BindErrorMessage(Label errorLabel) =>
            BindTextWithAutoVisibility(ViewModel.ErrorMessage, errorLabel);

        private void BindButtons(Button cancelButton, Button retryButton)
        {
            AddDisposable(cancelButton.OnClickAsObservable().Subscribe(_ => HandleCancelButtonClicked()));
            AddDisposable(retryButton.OnClickAsObservable().Subscribe(_ => ViewModel.RequestRetry()));
        }

        private void HandleCancelButtonClicked()
        {
            var viewModel = ViewModel;
            
            if (viewModel == null)
                return;

            if (viewModel.State.CurrentValue is MatchmakingState.Searching or MatchmakingState.CancelPending)
                viewModel.RequestCancel();
            else
                viewModel.RequestBack();
        }

        protected override void OnResetForPool()
        {
            _spinner?.Stop();
            base.OnResetForPool();
        }

        protected override void OnDestroy()
        {
            _spinner?.Stop();
            base.OnDestroy();
        }

        private void UpdateState(
            VisualElement searchingState,
            VisualElement foundState,
            VisualElement failedState,
            VisualElement cancelledState,
            Button cancelButton,
            Button retryButton,
            MatchmakingSpinner spinner,
            MatchmakingState state)
        {
            SetVisible(searchingState, state is MatchmakingState.Searching or MatchmakingState.CancelPending);
            SetVisible(foundState, state == MatchmakingState.Found);
            SetVisible(failedState, state is MatchmakingState.Failed or MatchmakingState.TerminalModal);
            SetVisible(cancelledState, state == MatchmakingState.Cancelled);

            var isSearching = state is MatchmakingState.Searching or MatchmakingState.CancelPending;
            var isFailed = state is MatchmakingState.Failed or MatchmakingState.TerminalModal;
            var isCancelled = state == MatchmakingState.Cancelled;

            SetVisible(cancelButton, isSearching || isFailed || isCancelled);
            SetVisible(retryButton, isFailed);

            if (isSearching)
                spinner.Start();
            else
                spinner.Stop();
        }

        private void UpdateCancelButtonLabel(MatchmakingState state)
        {
            var button = _cancelButton;
            
            if (button == null)
                return;

            button.text = state is MatchmakingState.Searching or MatchmakingState.CancelPending
                ? _cancelLabel
                : _backLabel;
        }

        private void UpdateHintVisibility()
        {
            var hintLabel = _hintLabel;
            
            if (hintLabel == null)
                return;

            var hasText = !string.IsNullOrWhiteSpace(hintLabel.text);
            hintLabel.style.display = _hintCount > 0 && hasText ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private static void SetVisible(VisualElement element, bool isVisible) =>
            element.style.display = isVisible ? DisplayStyle.Flex : DisplayStyle.None;

        private void BindTextWithAutoVisibility(Observable<string?> source, Label label) =>
            AddDisposable(source.Subscribe(text =>
            {
                label.text = text ?? string.Empty;
                label.style.display = string.IsNullOrWhiteSpace(text) ? DisplayStyle.None : DisplayStyle.Flex;
            }));

        private static T GetRequired<T>(T? element, string elementName)
            where T : class =>
            element ?? throw new InvalidOperationException($"{elementName} element is missing in UXML.");
    }
}