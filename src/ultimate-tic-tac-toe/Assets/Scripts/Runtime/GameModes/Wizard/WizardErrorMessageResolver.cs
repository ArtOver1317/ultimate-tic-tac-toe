#nullable enable

using System;
using System.Collections.Generic;
using Runtime.Localization;

namespace Runtime.GameModes.Wizard
{
    /// <summary>
    /// Resolves localized wizard error messages from localization keys.
    /// Returns the key itself when localization is unavailable.
    /// </summary>
    public static class WizardErrorMessageResolver
    {
        public static string Resolve(ILocalizationService? localization, string messageKey)
        {
            if (string.IsNullOrWhiteSpace(messageKey))
                return string.Empty;

            if (localization == null || !TryGetTableName(messageKey, out var tableName))
                return messageKey;

            var resolved = localization.Resolve(new TextTableId(tableName), new TextKey(messageKey));
            return string.IsNullOrWhiteSpace(resolved) ? messageKey : resolved;
        }

        public static string Resolve(
            ILocalizationService? localization,
            string messageKey,
            IReadOnlyDictionary<string, object>? args)
        {
            if (string.IsNullOrWhiteSpace(messageKey))
                return string.Empty;

            if (localization == null || !TryGetTableName(messageKey, out var tableName))
                return messageKey;

            var resolved = localization.Resolve(new TextTableId(tableName), new TextKey(messageKey), args);
            return string.IsNullOrWhiteSpace(resolved) ? messageKey : resolved;
        }

        private static bool TryGetTableName(string messageKey, out string tableName)
        {
            tableName = string.Empty;

            var dotIndex = messageKey.IndexOf('.', StringComparison.Ordinal);
            
            if (dotIndex <= 0)
                return false;

            tableName = messageKey[..dotIndex];
            return true;
        }
    }
}