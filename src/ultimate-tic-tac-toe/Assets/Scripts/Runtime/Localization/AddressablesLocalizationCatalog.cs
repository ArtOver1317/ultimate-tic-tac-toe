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
            new TextTableId("Common"),
            new TextTableId("MainMenu"),
            new TextTableId("Settings"),
            TextTableId.Errors,
        };

        public IReadOnlyList<LocaleId> GetSupportedLocales() => _supportedLocales;
        public IReadOnlyList<TextTableId> GetStartupTables() => _startupTables;

        public string GetAssetKey(LocaleId locale, TextTableId table)
        {
            // Convention-based, deterministic mapping.
            // Uses short locale code (language only, without region) for simplicity.
            // Example: "loc_en_ui" (not "loc_en_us_ui").
            
            if (string.IsNullOrWhiteSpace(locale.Code))
                throw new ArgumentException("Invalid locale: Code is null or empty.", nameof(locale));
            
            if (string.IsNullOrWhiteSpace(table.Name))
                throw new ArgumentException("Invalid table: Name is null or empty.", nameof(table));
            
            if (!locale.TryGetLanguageOnly(out var languageOnly))
                languageOnly = locale;
            
            var localeToken = languageOnly.Code.ToLowerInvariant();
            var tableName = table.Name.Trim().ToLowerInvariant();

            return string.IsNullOrEmpty(tableName) 
                ? throw new ArgumentException("Invalid table.", nameof(table)) 
                : $"loc_{localeToken}_{tableName}";
        }
    }
}