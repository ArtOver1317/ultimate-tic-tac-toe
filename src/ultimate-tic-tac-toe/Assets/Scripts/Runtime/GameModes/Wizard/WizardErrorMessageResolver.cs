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

            var dotIndex = messageKey.IndexOf('.', StringComparison.Ordinal);
            
            if (dotIndex <= 0 || localization == null)
                return messageKey;

            var tableName = messageKey[..dotIndex];
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

            var dotIndex = messageKey.IndexOf('.', StringComparison.Ordinal);
            
            if (dotIndex <= 0 || localization == null)
                return messageKey;

            var tableName = messageKey[..dotIndex];
            var resolved = localization.Resolve(new TextTableId(tableName), new TextKey(messageKey), args);
            return string.IsNullOrWhiteSpace(resolved) ? messageKey : resolved;
        }
    }
}
