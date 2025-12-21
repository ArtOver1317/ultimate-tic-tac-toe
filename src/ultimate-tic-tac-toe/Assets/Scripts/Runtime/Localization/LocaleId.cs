using System;

namespace Runtime.Localization
{
    /// <summary>
    /// Immutable locale identifier (language + optional region).
    /// Examples: "en", "en-US", "ru-RU".
    /// </summary>
    public readonly struct LocaleId : IEquatable<LocaleId>
    {
        public string Code { get; }

        public LocaleId(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("Locale code must be non-empty.", nameof(code));

            Code = Normalize(code);
        }

        public static LocaleId EnglishUs => new("en-US");
        public static LocaleId Russian => new("ru-RU");
        public static LocaleId Japanese => new("ja-JP");

        public bool Equals(LocaleId other) => string.Equals(Code, other.Code, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is LocaleId other && Equals(other);
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Code);
        public override string ToString() => Code;

        public static bool operator ==(LocaleId left, LocaleId right) => left.Equals(right);
        public static bool operator !=(LocaleId left, LocaleId right) => !left.Equals(right);

        private static string Normalize(string code)
        {
            code = code.Trim();

            var dashIndex = code.IndexOf('-');
            
            if (dashIndex < 0) 
                return code.ToLowerInvariant();

            var language = code[..dashIndex].Trim();
            var rest = code[(dashIndex + 1)..].Trim();

            if (language.Length == 0) 
                return code;

            if (rest.Length == 0) 
                return language.ToLowerInvariant();

            var normalizedLanguage = language.ToLowerInvariant();

            // Most common format: xx-YY
            if (rest.Length == 2) 
                return normalizedLanguage + "-" + rest.ToUpperInvariant();

            return normalizedLanguage + "-" + rest;
        }

        public bool TryGetLanguageOnly(out LocaleId languageOnly)
        {
            var dashIndex = Code.IndexOf('-');
            
            if (dashIndex <= 0)
            {
                languageOnly = this;
                return false;
            }

            languageOnly = new LocaleId(Code.Substring(0, dashIndex));
            return true;
        }
    }
}