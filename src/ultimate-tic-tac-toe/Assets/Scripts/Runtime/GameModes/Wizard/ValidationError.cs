namespace Runtime.GameModes.Wizard
{
    /// <summary>
    /// Validation error for a specific field/path.
    /// MessageKey is expected to be a localization key.
    /// </summary>
    public sealed class ValidationError
    {
        public string Field { get; }
        public string MessageKey { get; }

        public ValidationError(string field, string messageKey)
        {
            if (string.IsNullOrWhiteSpace(field))
                throw new System.ArgumentException("Value cannot be null or whitespace.", nameof(field));
            if (string.IsNullOrWhiteSpace(messageKey))
                throw new System.ArgumentException("Value cannot be null or whitespace.", nameof(messageKey));

            Field = field;
            MessageKey = messageKey;
        }
    }
}
