#nullable enable

using System;
using R3;
using Runtime.Extensions;
using Runtime.GameModes.Wizard;
using Runtime.UI.Components;
using Runtime.UI.Core;
using UnityEngine.UIElements;

namespace Runtime.UI.GameModes.Wizard
{
    public sealed class MatchmakingView : UIView<MatchmakingViewModel>
    {
        [Runtime.UI.Core.UxmlElementAttribute("TitleLabel")]
        private Label? _titleLabel;

        [Runtime.UI.Core.UxmlElementAttribute("SearchingState")]
        private VisualElement? _searchingState;

        [Runtime.UI.Core.UxmlElementAttribute("Spinner")]
        private MatchmakingSpinner? _spinner;

        [Runtime.UI.Core.UxmlElementAttribute("Timer")]
        private MatchmakingTimer? _timer;

        [Runtime.UI.Core.UxmlElementAttribute("HintLabel", isOptional: true)]
        private Label? _hintLabel;

        [Runtime.UI.Core.UxmlElementAttribute("FoundState")]
        private VisualElement? _foundState;

        [Runtime.UI.Core.UxmlElementAttribute("FoundLabel")]
        private Label? _foundLabel;

        [Runtime.UI.Core.UxmlElementAttribute("FailedState")]
        private VisualElement? _failedState;

        [Runtime.UI.Core.UxmlElementAttribute("FailedLabel")]
        private Label? _failedLabel;

        [Runtime.UI.Core.UxmlElementAttribute("ErrorLabel")]
        private Label? _errorLabel;

        [Runtime.UI.Core.UxmlElementAttribute("CancelledState")]
        private VisualElement? _cancelledState;

        [Runtime.UI.Core.UxmlElementAttribute("CancelledLabel")]
        private Label? _cancelledLabel;

        [Runtime.UI.Core.UxmlElementAttribute("CancelButton")]
        private Button? _cancelButton;

        [Runtime.UI.Core.UxmlElementAttribute("RetryButton")]
        private Button? _retryButton;

        private string _cancelLabel = string.Empty;
        private string _backLabel = string.Empty;
        private int _hintCount;

        protected override void BindViewModel()
        {
            var titleLabel = _titleLabel ?? throw new InvalidOperationException("TitleLabel element is missing in UXML.");
            var searchingState = _searchingState ?? throw new InvalidOperationException("SearchingState element is missing in UXML.");
            var spinner = _spinner ?? throw new InvalidOperationException("Spinner element is missing in UXML.");
            var timer = _timer ?? throw new InvalidOperationException("Timer element is missing in UXML.");
            var foundState = _foundState ?? throw new InvalidOperationException("FoundState element is missing in UXML.");
            var foundLabel = _foundLabel ?? throw new InvalidOperationException("FoundLabel element is missing in UXML.");
            var failedState = _failedState ?? throw new InvalidOperationException("FailedState element is missing in UXML.");
            var failedLabel = _failedLabel ?? throw new InvalidOperationException("FailedLabel element is missing in UXML.");
            var errorLabel = _errorLabel ?? throw new InvalidOperationException("ErrorLabel element is missing in UXML.");
            var cancelledState = _cancelledState ?? throw new InvalidOperationException("CancelledState element is missing in UXML.");
            var cancelledLabel = _cancelledLabel ?? throw new InvalidOperationException("CancelledLabel element is missing in UXML.");
            var cancelButton = _cancelButton ?? throw new InvalidOperationException("CancelButton element is missing in UXML.");
            var retryButton = _retryButton ?? throw new InvalidOperationException("RetryButton element is missing in UXML.");

            BindText(ViewModel.TitleText, titleLabel);
            BindText(ViewModel.FoundText, foundLabel);
            BindText(ViewModel.FailedText, failedLabel);
            BindText(ViewModel.CancelledText, cancelledLabel);
            BindText(ViewModel.RetryButtonText, retryButton);

            AddDisposable(ViewModel.SearchingPrefixText.Subscribe(text => timer.SetPrefix(text)));
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

            if (_hintLabel != null)
            {
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

            AddDisposable(ViewModel.ErrorMessage.Subscribe(text =>
            {
                errorLabel.text = text ?? string.Empty;
                errorLabel.style.display = string.IsNullOrWhiteSpace(text) ? DisplayStyle.None : DisplayStyle.Flex;
            }));

            AddDisposable(ViewModel.State.Subscribe(state =>
            {
                UpdateState(searchingState, foundState, failedState, cancelledState, cancelButton, retryButton, spinner, state);
                UpdateCancelButtonLabel(state);
            }));

            AddDisposable(cancelButton.OnClickAsObservable().Subscribe(_ =>
            {
                if (ViewModel.State.CurrentValue == MatchmakingState.Searching)
                    ViewModel.RequestCancel();
                else
                    ViewModel.RequestBack();
            }));
            AddDisposable(retryButton.OnClickAsObservable().Subscribe(_ => ViewModel.RequestRetry()));
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
            SetVisible(searchingState, state == MatchmakingState.Searching);
            SetVisible(foundState, state == MatchmakingState.Found);
            SetVisible(failedState, state == MatchmakingState.Failed);
            SetVisible(cancelledState, state == MatchmakingState.Cancelled);

            var isSearching = state == MatchmakingState.Searching;
            var isFailed = state == MatchmakingState.Failed;
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

            button.text = state == MatchmakingState.Searching
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
    }
}

#nullable restore
