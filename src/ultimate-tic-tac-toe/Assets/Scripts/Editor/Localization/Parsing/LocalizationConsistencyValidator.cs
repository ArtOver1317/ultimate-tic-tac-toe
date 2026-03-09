using System.Collections.Generic;
using System.Linq;
using Editor.Localization;

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
                AddMissingTableWarning(result, tableName, localeKeys, foundLocales);

                var referenceLocale = GetReferenceLocale(localeKeys);
                var referenceKeys = localeKeys[referenceLocale];
                result.TotalKeyCount += referenceKeys.Count;

                AddMissingKeyFindings(result, tableName, referenceLocale, referenceKeys, localeKeys);
                AddExtraKeyWarnings(result, tableName, referenceLocale, referenceKeys, localeKeys);
            }

            return result;
        }

        private static void AddMissingTableWarning(
            ValidationResult result,
            string tableName,
            Dictionary<string, HashSet<string>> localeKeys,
            List<string> foundLocales)
        {
            if (localeKeys.Count >= foundLocales.Count)
                return;

            var missingLocales = foundLocales.Except(localeKeys.Keys).ToList();
            result.Warnings.Add($"Table '{tableName}' is missing in locales: {string.Join(", ", missingLocales)}");
        }

        private static string GetReferenceLocale(Dictionary<string, HashSet<string>> localeKeys) =>
            localeKeys.ContainsKey(LocalizationEditorConventions.PreferredReferenceLocale)
                ? LocalizationEditorConventions.PreferredReferenceLocale
                : localeKeys.Keys.First();

        private static void AddMissingKeyFindings(
            ValidationResult result,
            string tableName,
            string referenceLocale,
            HashSet<string> referenceKeys,
            Dictionary<string, HashSet<string>> localeKeys)
        {
            foreach (var (locale, keys) in localeKeys)
            {
                var missingKeys = referenceKeys.Except(keys).ToList();

                if (missingKeys.Count == 0)
                    continue;

                result.MissingKeys.Add(new MissingKeyInfo
                {
                    Locale = locale,
                    Table = tableName,
                    Keys = missingKeys,
                });
            }
        }

        private static void AddExtraKeyWarnings(
            ValidationResult result,
            string tableName,
            string referenceLocale,
            HashSet<string> referenceKeys,
            Dictionary<string, HashSet<string>> localeKeys)
        {
            foreach (var (locale, keys) in localeKeys)
            {
                if (locale == referenceLocale)
                    continue;

                var extraKeys = keys.Except(referenceKeys).ToList();

                if (extraKeys.Count > 0)
                    result.Warnings.Add($"Extra keys in {locale}/{tableName}: {string.Join(", ", extraKeys)}");
            }
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