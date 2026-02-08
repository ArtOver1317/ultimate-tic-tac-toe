#nullable enable

using System;

namespace Runtime.UI.Components
{
    public enum UIErrorDisplayType
    {
        Inline = 0,
        Toast = 1,
        Modal = 2,
    }

    /// <summary>
    /// UI-ready error payload (display-only).
    /// </summary>
    public sealed class UIErrorPresentation
    {
        public string Code { get; }
        public string Message { get; }
        public bool IsBlocking { get; }
        public UIErrorDisplayType DisplayType { get; }

        public UIErrorPresentation(string code, string? message, bool isBlocking, UIErrorDisplayType displayType)
        {
            if (string.IsNullOrWhiteSpace(code))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(code));

            Code = code;
            Message = message ?? string.Empty;
            IsBlocking = isBlocking;
            DisplayType = displayType;
        }
    }
}
