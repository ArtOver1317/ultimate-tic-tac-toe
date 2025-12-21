using System;
using System.Collections.Generic;

namespace Runtime.Localization
{
    public sealed class GameLocalizationPolicy : ILocalizationPolicy
    {
        private static readonly LocaleId[] _defaultFallback =
        {
            LocaleId.EnglishUs,
        };

        public bool UseMissingKeyPlaceholders { get; }
        public int MaxCachedTables { get; }
        public LocaleId DefaultLocale { get; }

        public GameLocalizationPolicy(
            bool useMissingKeyPlaceholders = true,
            int maxCachedTables = 32,
            LocaleId? defaultLocale = null)
        {
            if (maxCachedTables <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxCachedTables), maxCachedTables, "MaxCachedTables must be > 0.");

            UseMissingKeyPlaceholders = useMissingKeyPlaceholders;
            MaxCachedTables = maxCachedTables;
            DefaultLocale = defaultLocale ?? LocaleId.EnglishUs;
        }

        public IReadOnlyList<LocaleId> GetFallbackChain(LocaleId requested)
        {
            var result = new List<LocaleId>(capacity: 4);
            AppendUnique(result, requested);

            if (requested.TryGetLanguageOnly(out var languageOnly))
            {
                if (languageOnly != requested) 
                    AppendUnique(result, languageOnly);
            }

            // Ensure default locale is always last fallback.
            AppendUnique(result, DefaultLocale);

            // Safety net in case DefaultLocale is invalid.
            foreach (var fallback in _defaultFallback)
            {
                AppendUnique(result, fallback);
            }

            return result;
        }

        private static void AppendUnique(List<LocaleId> list, LocaleId locale)
        {
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i] == locale)
                    return;
            }

            list.Add(locale);
        }
    }
}
