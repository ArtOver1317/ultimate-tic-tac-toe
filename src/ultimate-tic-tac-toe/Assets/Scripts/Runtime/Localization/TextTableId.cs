using System;

namespace Runtime.Localization
{
    /// <summary>
    /// Logical table identifier (group of localized strings).
    /// Examples: "UI", "Gameplay", "Errors".
    /// </summary>
    public readonly struct TextTableId : IEquatable<TextTableId>
    {
        public string Name { get; }

        public TextTableId(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) 
                throw new ArgumentException("Table name must be non-empty.", nameof(name));

            Name = name.Trim();
        }

        public static TextTableId UI => new("UI");
        public static TextTableId Gameplay => new("Gameplay");
        public static TextTableId Errors => new("Errors");

        public bool Equals(TextTableId other) => string.Equals(Name, other.Name, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is TextTableId other && Equals(other);
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Name);
        public override string ToString() => Name;

        public static bool operator ==(TextTableId left, TextTableId right) => left.Equals(right);
        public static bool operator !=(TextTableId left, TextTableId right) => !left.Equals(right);
    }
}