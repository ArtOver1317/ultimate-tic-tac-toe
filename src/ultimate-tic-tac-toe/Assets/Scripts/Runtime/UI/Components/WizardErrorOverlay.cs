#nullable enable

using System;
using UnityEngine.UIElements;

namespace Runtime.UI.Components
{
    public static class WizardErrorOverlayDefaults
    {
        public static readonly TimeSpan ToastDuration = TimeSpan.FromSeconds(3);
    }

    [UxmlElement]
    public sealed partial class WizardErrorOverlay : VisualElement
    {
        private readonly WizardToast _toast;
        private readonly WizardModal _modal;

        public event Action? ModalDismissed;

        public bool IsBlocking { get; private set; }

        public WizardErrorOverlay()
        {
            AddToClassList("gmw-error-overlay");

            _toast = new WizardToast { name = "WizardToast" };
            _modal = new WizardModal { name = "WizardModal" };

            _modal.Dismissed += OnModalDismissed;

            Add(_toast);
            Add(_modal);

            ResetState();
        }

        public void SetModalButtonText(string? text) => _modal.SetButtonText(text);

        public void Present(UIErrorPresentation? presentation)
        {
            if (presentation == null || presentation.DisplayType == UIErrorDisplayType.Inline)
            {
                ResetState();
                return;
            }

            style.display = DisplayStyle.Flex;
            IsBlocking = presentation.IsBlocking;

            switch (presentation.DisplayType)
            {
                case UIErrorDisplayType.Toast:
                    pickingMode = PickingMode.Ignore;
                    _modal.Hide();
                    _toast.Show(presentation.Message, WizardErrorOverlayDefaults.ToastDuration);
                    break;

                case UIErrorDisplayType.Modal:
                    pickingMode = PickingMode.Position;
                    _toast.Hide();
                    _modal.Show(presentation.Message);
                    break;

                default:
                    ResetState();
                    break;
            }
        }

        public void ResetState()
        {
            IsBlocking = false;
            _toast.Hide();
            _modal.Hide();
            style.display = DisplayStyle.None;
            pickingMode = PickingMode.Ignore;
        }

        private void OnModalDismissed()
        {
            ResetState();
            ModalDismissed?.Invoke();
        }
    }
}