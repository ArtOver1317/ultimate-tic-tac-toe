#nullable enable

using System;
using UnityEngine.UIElements;

namespace Runtime.UI.Components
{
    [UxmlElement]
    public sealed partial class WizardToast : VisualElement
    {
        private readonly Label _messageLabel;
        private IVisualElementScheduledItem? _autoHideItem;
        private int _autoHideToken;

        public bool IsVisible { get; private set; }

        public WizardToast()
        {
            AddToClassList("wizard-toast");

            _messageLabel = new Label { name = "ToastMessage" };
            _messageLabel.AddToClassList("wizard-toast__label");

            Add(_messageLabel);

            Hide();
        }

        public void Show(string message, TimeSpan? autoHide = null)
        {
            _messageLabel.text = message ?? string.Empty;
            style.display = DisplayStyle.Flex;
            IsVisible = true;

            CancelAutoHide();

            if (autoHide.HasValue && autoHide.Value > TimeSpan.Zero)
            {
                var token = ++_autoHideToken;
                _autoHideItem = schedule.Execute(() =>
                {
                    if (_autoHideToken != token)
                        return;

                    Hide();
                }).StartingIn((long)autoHide.Value.TotalMilliseconds);
            }
        }

        public void Hide()
        {
            CancelAutoHide();
            style.display = DisplayStyle.None;
            IsVisible = false;
        }

        private void CancelAutoHide()
        {
            _autoHideToken++;
            if (_autoHideItem != null)
            {
                _autoHideItem.Pause();
                _autoHideItem = null;
            }
        }
    }
}
