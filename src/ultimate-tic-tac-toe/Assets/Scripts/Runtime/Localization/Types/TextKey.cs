using System;

namespace Runtime.Localization
{
    /// <summary>
    /// Entry identifier within a table.
    /// Recommended format: "Screen.Button.Play".
    /// </summary>
    public readonly struct TextKey : IEquatable<TextKey>
    {
        public string Value { get; }

        public TextKey(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) 
                throw new ArgumentException("Key must be non-empty.", nameof(value));

            Value = value.Trim();
        }

        public static implicit operator TextKey(string value) => new(value);

        public bool Equals(TextKey other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is TextKey other && Equals(other);
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value;

        public static bool operator ==(TextKey left, TextKey right) => left.Equals(right);
        public static bool operator !=(TextKey left, TextKey right) => !left.Equals(right);
    }
}