using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Runtime.Localization.Contracts;
using Runtime.Localization.Types;

namespace Runtime.Localization.Services
{
    internal sealed class LocalizationTablePreloader
    {
        private readonly ILocalizationLoader _loader;
        private readonly ILocalizationParser _parser;
        private readonly ILocalizationStore _store;
        private readonly ILocalizationCatalog _catalog;
        private readonly ILocalizationPolicy _policy;
        private readonly Action<LocalizationError> _reportError;

        public LocalizationTablePreloader(
            ILocalizationLoader loader,
            ILocalizationParser parser,
            ILocalizationStore store,
            ILocalizationCatalog catalog,
            ILocalizationPolicy policy,
            Action<LocalizationError> reportError)
        {
            _loader = loader ?? throw new ArgumentNullException(nameof(loader));
            _parser = parser ?? throw new ArgumentNullException(nameof(parser));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _policy = policy ?? throw new ArgumentNullException(nameof(policy));
            _reportError = reportError ?? throw new ArgumentNullException(nameof(reportError));
        }

        public async UniTask PreloadAsync(LocaleId locale, IReadOnlyList<TextTableId> tables, CancellationToken cancellationToken)
        {
            if (tables == null)
                throw new ArgumentNullException(nameof(tables));

            var requiredTables = _catalog.GetRequiredTables();

            for (var i = 0; i < tables.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var table = tables[i];
                var preloadResult = await PreloadTableAsync(locale, table, cancellationToken);

                if (preloadResult.LastException == null)
                    continue;

                var errorCode = preloadResult.LastException is FormatException
                    ? LocalizationErrorCode.ParseFailed
                    : LocalizationErrorCode.AddressablesLoadFailed;

                _reportError(new LocalizationError(
                    errorCode,
                    $"Failed to preload table '{table.Name}' for locale '{locale.Code}'. Tried: {preloadResult.TriedKeys}",
                    preloadResult.LastException,
                    locale,
                    table));

                if (IsRequiredTable(table, requiredTables))
                {
                    throw new InvalidOperationException(
                        $"Required localization table '{table.Name}' could not be loaded for locale '{locale.Code}'.");
                }
            }
        }

        private async UniTask<(string TriedKeys, Exception LastException)> PreloadTableAsync(
            LocaleId locale,
            TextTableId table,
            CancellationToken cancellationToken)
        {
            var chain = _policy.GetFallbackChain(locale);
            var triedKeys = new HashSet<string>(StringComparer.Ordinal);
            Exception lastException = null;

            for (var i = 0; i < chain.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var candidateLocale = chain[i];
                var assetKey = _catalog.GetAssetKey(candidateLocale, table);

                if (!triedKeys.Add(assetKey))
                    continue;

                try
                {
                    var bytes = await _loader.LoadBytesAsync(assetKey, cancellationToken);
                    var parsedTable = _parser.ParseTable(bytes.Span, candidateLocale, table);
                    _store.Put(parsedTable);
                    return (string.Join(", ", triedKeys), null);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    lastException = ex;
                }
                finally
                {
                    _loader.Release(assetKey);
                }
            }

            return (string.Join(", ", triedKeys), lastException);
        }

        private static bool IsRequiredTable(TextTableId table, IReadOnlyList<TextTableId> requiredTables)
        {
            if (requiredTables == null)
                return false;

            for (var i = 0; i < requiredTables.Count; i++)
            {
                if (requiredTables[i] == table)
                    return true;
            }

            return false;
        }
    }
}