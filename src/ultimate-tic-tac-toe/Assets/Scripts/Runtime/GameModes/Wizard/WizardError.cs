#nullable enable

using System;

namespace Runtime.GameModes.Wizard
{
    public enum ErrorDisplayType
    {
        Inline = 0,
        Toast = 1,
        Modal = 2,
    }

    /// <summary>
    /// User-facing wizard error representation.
    /// Kept minimal in Phase 3; can be extended in later phases.
    /// </summary>
    public sealed class WizardError
    {
        public string Code { get; }
        public string MessageKey { get; }
        public bool IsBlocking { get; }
        public ErrorDisplayType DisplayType { get; }

        public WizardError(string code, string messageKey, bool isBlocking, ErrorDisplayType displayType)
        {
            if (string.IsNullOrWhiteSpace(code))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(code));
            
            if (string.IsNullOrWhiteSpace(messageKey))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(messageKey));

            Code = code;
            MessageKey = messageKey;
            IsBlocking = isBlocking;
            DisplayType = displayType;
        }

        public static WizardError FromException(Exception ex)
        {
            if (ex == null)
                throw new ArgumentNullException(nameof(ex));

            return new WizardError(
                code: "wizard.unhandled_exception",
                messageKey: "Errors.GameModeWizard.UnhandledException",
                isBlocking: true,
                displayType: ErrorDisplayType.Modal);
        }
    }
}
