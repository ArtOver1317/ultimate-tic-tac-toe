using System;
using System.Collections.Generic;

namespace Runtime.Localization
{
    public sealed class AddressablesLocalizationCatalog : ILocalizationCatalog
    {
        private static readonly LocaleId[] _supportedLocales =
        {
            LocaleId.EnglishUs,
            LocaleId.Russian,
            LocaleId.Japanese,
        };

        private static readonly TextTableId[] _startupTables =
        {
            TextTableId.UI,
            TextTableId.Errors,
        };

        public IReadOnlyList<LocaleId> GetSupportedLocales() => _supportedLocales;
        public IReadOnlyList<TextTableId> GetStartupTables() => _startupTables;

        public string GetAssetKey(LocaleId locale, TextTableId table)
        {
            // Convention-based, deterministic mapping.
            // Matches doc examples like: "loc_en_ui".
            var language = ExtractLanguage(locale);
            var tableName = table.Name.Trim().ToLowerInvariant();

            if (string.IsNullOrEmpty(language))
                throw new ArgumentException($"Invalid locale: '{locale.Code}'", nameof(locale));

            if (string.IsNullOrEmpty(tableName))
                throw new ArgumentException("Invalid table.", nameof(table));

            return $"loc_{language}_{tableName}";
        }

        private static string ExtractLanguage(LocaleId locale)
        {
            var code = locale.Code;
            
            if (string.IsNullOrWhiteSpace(code))
                return string.Empty;

            var dashIndex = code.IndexOf('-');
            
            if (dashIndex <= 0)
                return code.Trim().ToLowerInvariant();

            return code[..dashIndex].Trim().ToLowerInvariant();
        }
    }
}