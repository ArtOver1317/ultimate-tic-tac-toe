#nullable enable

using System;
using UnityEngine.UIElements;

namespace Runtime.UI.Components
{
    [UxmlElement]
    public sealed partial class WizardModal : VisualElement
    {
        private readonly VisualElement _backdrop;
        private readonly VisualElement _panel;
        private readonly Label _messageLabel;
        private readonly Button _okButton;

        public event Action? Dismissed;

        public bool IsVisible { get; private set; }

        public WizardModal()
        {
            AddToClassList("wizard-modal");

            _backdrop = new VisualElement { name = "Backdrop" };
            _backdrop.AddToClassList("wizard-modal__backdrop");

            _panel = new VisualElement { name = "Panel" };
            _panel.AddToClassList("wizard-modal__panel");

            _messageLabel = new Label { name = "ModalMessage" };
            _messageLabel.AddToClassList("wizard-modal__message");

            _okButton = new Button(OnDismissed) { name = "OkButton", text = "OK" };
            _okButton.AddToClassList("wizard-modal__button");

            _panel.Add(_messageLabel);
            _panel.Add(_okButton);

            Add(_backdrop);
            Add(_panel);

            Hide();
        }

        public void SetButtonText(string? text) => _okButton.text = text ?? string.Empty;

        public void Show(string message)
        {
            _messageLabel.text = message;
            style.display = DisplayStyle.Flex;
            IsVisible = true;
            pickingMode = PickingMode.Position;
        }

        public void Hide()
        {
            style.display = DisplayStyle.None;
            IsVisible = false;
            pickingMode = PickingMode.Ignore;
        }

        private void OnDismissed()
        {
            Hide();
            Dismissed?.Invoke();
        }
    }
}