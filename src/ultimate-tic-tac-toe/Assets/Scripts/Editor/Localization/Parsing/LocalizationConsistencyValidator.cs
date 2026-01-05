using System.Collections.Generic;
using System.Linq;

namespace Editor.Localization.Parsing
{
    /// <summary>
    /// Validates consistency of localization keys across multiple locales and tables.
    /// </summary>
    public sealed class LocalizationConsistencyValidator
    {
        /// <summary>
        /// Validates that all locales have consistent keys for each table.
        /// </summary>
        /// <param name="allTables">Table data: tableName → locale → keys</param>
        /// <param name="foundLocales">All locales that should be validated</param>
        /// <returns>Validation result with warnings and missing key information</returns>
        public ValidationResult Validate(
            Dictionary<string, Dictionary<string, HashSet<string>>> allTables,
            List<string> foundLocales)
        {
            var result = new ValidationResult();

            foreach (var (tableName, localeKeys) in allTables)
            {
                // Check if table exists in all locales
                if (localeKeys.Count < foundLocales.Count)
                {
                    var missingLocales = foundLocales.Except(localeKeys.Keys).ToList();
                    result.Warnings.Add($"Table '{tableName}' is missing in locales: {string.Join(", ", missingLocales)}");
                }

                // Use first locale (or en if available) as reference
                var referenceLocale = localeKeys.ContainsKey("en") ? "en" : localeKeys.Keys.First();
                var referenceKeys = localeKeys[referenceLocale];

                result.TotalKeyCount += referenceKeys.Count;

                foreach (var (locale, keys) in localeKeys)
                {
                    // Missing keys: what's in reference but not in this locale
                    var missingKeys = referenceKeys.Except(keys).ToList();

                    if (missingKeys.Count > 0)
                    {
                        result.MissingKeys.Add(new MissingKeyInfo
                        {
                            Locale = locale,
                            Table = tableName,
                            Keys = missingKeys,
                        });
                    }

                    // Extra keys: what's in this locale but not in reference
                    if (locale != referenceLocale)
                    {
                        var extraKeys = keys.Except(referenceKeys).ToList();

                        if (extraKeys.Count > 0)
                            result.Warnings.Add($"Extra keys in {locale}/{tableName}: {string.Join(", ", extraKeys)}");
                    }
                }
            }

            return result;
        }

        public sealed class ValidationResult
        {
            public int TotalKeyCount { get; set; }
            public List<string> Warnings { get; } = new();
            public List<MissingKeyInfo> MissingKeys { get; } = new();
        }

        public sealed class MissingKeyInfo
        {
            public string Locale { get; set; }
            public string Table { get; set; }
            public List<string> Keys { get; set; }
        }
    }
}
