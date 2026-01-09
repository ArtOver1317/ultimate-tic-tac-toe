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

        private readonly Dictionary<LocaleId, LocaleId[]> _fallbackChainCache = new();

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
            if (_fallbackChainCache.TryGetValue(requested, out var cached))
                return cached;

            var result = new List<LocaleId>(capacity: 4);
            AppendUnique(result, requested);

            if (requested.TryGetLanguageOnly(out var languageOnly))
            {
                if (languageOnly != requested)
                    AppendUnique(result, languageOnly);
            }

            AppendUnique(result, DefaultLocale);

            foreach (var fallbackLocale in _defaultFallback)
            {
                AppendUnique(result, fallbackLocale);
            }

            var chain = result.ToArray();
            _fallbackChainCache[requested] = chain;
            return chain;
        }

        private static void AppendUnique(List<LocaleId> list, LocaleId locale)
        {
            foreach (var localeId in list)
            {
                if (localeId == locale)
                    return;
            }

            list.Add(locale);
        }
    }
}