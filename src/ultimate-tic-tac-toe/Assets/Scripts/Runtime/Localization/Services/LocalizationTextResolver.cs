using System;
using System.Collections.Generic;
using System.Threading;
using R3;

namespace Runtime.Localization
{
    internal sealed class LocalizationTextResolver
    {
        private const int _maxReportedMissingKeys = 4096;

        private readonly ILocalizationStore _store;
        private readonly ITextFormatter _formatter;
        private readonly ILocalizationPolicy _policy;
        private readonly Action<LocalizationError> _reportError;

        private readonly HashSet<MissingKeyReportKey> _reportedMissingKeys = new();
        
        private readonly object _trackedTablesLock = new();
        
        // Tables touched by Resolve/Observe so locale switches can reload them for the new locale.
        private readonly HashSet<TextTableId> _trackedTables = new();

        public LocalizationTextResolver(
            ILocalizationStore store,
            ITextFormatter formatter,
            ILocalizationPolicy policy,
            Action<LocalizationError> reportError)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _formatter = formatter ?? throw new ArgumentNullException(nameof(formatter));
            _policy = policy ?? throw new ArgumentNullException(nameof(policy));
            _reportError = reportError ?? throw new ArgumentNullException(nameof(reportError));
        }

        public void ResetReportedMissingKeys() => _reportedMissingKeys.Clear();

        public IReadOnlyList<TextTableId> BuildLocaleSwitchPreloadList(
            IReadOnlyList<TextTableId> requiredTables,
            IReadOnlyList<TextTableId> startupTables)
        {
            TextTableId[] trackedSnapshot;

            lock (_trackedTablesLock)
            {
                if (_trackedTables.Count == 0)
                    return MergeTables(requiredTables, startupTables);

                trackedSnapshot = new TextTableId[_trackedTables.Count];
                _trackedTables.CopyTo(trackedSnapshot);
            }

            // Locale switch reloads required/startup tables plus tables already used in this session.
            return MergeTables(requiredTables, startupTables, trackedSnapshot);
        }

        public string Resolve(TextTableId table, TextKey key, IReadOnlyDictionary<string, object> args = null) =>
            TryResolve(table, key, out var result, args)
                ? result
                : (_policy.UseMissingKeyPlaceholders ? $"⟦Missing: {table.Name}.{key.Value}⟧" : string.Empty);

        public bool TryResolve(
            TextTableId table,
            TextKey key,
            out string result,
            IReadOnlyDictionary<string, object> args = null)
        {
            TrackTable(table);

            if (_store.TryResolveTemplate(table, key, out var template))
            {
                var activeLocale = _store.GetActiveLocale();
                result = _formatter.Format(template, activeLocale, args);
                return true;
            }

            ReportMissingKey(table, key);
            result = string.Empty;
            return false;
        }

        public Observable<string> Observe(
            TextTableId table,
            TextKey key,
            Observable<IReadOnlyDictionary<string, object>> args,
            ReadOnlyReactiveProperty<LocaleId> currentLocale)
        {
            if (args == null)
                throw new ArgumentNullException(nameof(args));

            TrackTable(table);

            return Observable.Create<string>(observer =>
            {
                IReadOnlyDictionary<string, object> latestArgs = null;
                string lastEmitted = null;
                var isDisposed = 0;

                void Emit()
                {
                    if (Volatile.Read(ref isDisposed) != 0)
                        return;

                    var text = Resolve(table, key, latestArgs);

                    if (string.Equals(text, lastEmitted, StringComparison.Ordinal))
                        return;

                    lastEmitted = text;
                    observer.OnNext(text);
                }

                var argsSub = args.Subscribe(value =>
                {
                    latestArgs = value;
                    Emit();
                });

                var localeSub = currentLocale.Subscribe(_ => Emit());
                var storeSub = SubscribeToStoreEvents(table, Emit);

                Emit();

                return Disposable.Create(() =>
                {
                    Interlocked.Exchange(ref isDisposed, 1);
                    argsSub.Dispose();
                    localeSub.Dispose();
                    storeSub.Dispose();
                });
            });
        }

        public Observable<string> Observe(
            TextTableId table,
            TextKey key,
            IReadOnlyDictionary<string, object> args,
            ReadOnlyReactiveProperty<LocaleId> currentLocale)
        {
            TrackTable(table);

            return Observable.Create<string>(observer =>
            {
                string lastEmitted = null;
                var isDisposed = 0;

                void Emit()
                {
                    if (Volatile.Read(ref isDisposed) != 0)
                        return;

                    var text = Resolve(table, key, args);

                    if (string.Equals(text, lastEmitted, StringComparison.Ordinal))
                        return;

                    lastEmitted = text;
                    observer.OnNext(text);
                }

                var localeSub = currentLocale.Subscribe(_ => Emit());
                var storeSub = SubscribeToStoreEvents(table, Emit);

                Emit();

                return Disposable.Create(() =>
                {
                    Interlocked.Exchange(ref isDisposed, 1);
                    localeSub.Dispose();
                    storeSub.Dispose();
                });
            });
        }

        private IDisposable SubscribeToStoreEvents(TextTableId table, Action onRelevantChange)
        {
            var storeEvents = _store.Events;

            if (storeEvents == null)
                return Disposable.Empty;

            return storeEvents.Subscribe(e =>
            {
                if (e.Type != LocalizationStoreEventType.TableLoaded && e.Type != LocalizationStoreEventType.TableUnloaded)
                    return;

                if (e.TableId != table)
                    return;

                var activeLocale = _store.GetActiveLocale();
                var chain = _policy.GetFallbackChain(activeLocale);

                for (var i = 0; i < chain.Count; i++)
                {
                    if (chain[i] == e.Locale)
                    {
                        onRelevantChange();
                        return;
                    }
                }
            });
        }

        private void ReportMissingKey(TextTableId table, TextKey key)
        {
            var locale = _store.GetActiveLocale();

            if (_reportedMissingKeys.Count >= _maxReportedMissingKeys)
                _reportedMissingKeys.Clear();

            if (_reportedMissingKeys.Add(new MissingKeyReportKey(locale, table, key)))
            {
                _reportError(new LocalizationError(
                    LocalizationErrorCode.MissingKey,
                    $"Missing key '{key.Value}' in table '{table.Name}'.",
                    locale: locale,
                    tableId: table,
                    key: key));
            }
        }

        private void TrackTable(TextTableId table)
        {
            if (string.IsNullOrWhiteSpace(table.Name))
                return;

            lock (_trackedTablesLock)
            {
                _trackedTables.Add(table);
            }
        }

        private static IReadOnlyList<TextTableId> MergeTables(params IReadOnlyList<TextTableId>[] tableGroups)
        {
            if (tableGroups == null || tableGroups.Length == 0)
                return Array.Empty<TextTableId>();

            var seen = new HashSet<TextTableId>();
            List<TextTableId> merged = null;

            for (var i = 0; i < tableGroups.Length; i++)
            {
                var tables = tableGroups[i];

                if (tables == null || tables.Count == 0)
                    continue;

                merged ??= new List<TextTableId>(tables.Count);

                for (var j = 0; j < tables.Count; j++)
                {
                    var table = tables[j];

                    if (seen.Add(table))
                        merged.Add(table);
                }
            }

            return merged ?? (IReadOnlyList<TextTableId>)Array.Empty<TextTableId>();
        }

        private readonly struct MissingKeyReportKey : IEquatable<MissingKeyReportKey>
        {
            private readonly LocaleId _locale;
            private readonly TextTableId _table;
            private readonly TextKey _key;

            public MissingKeyReportKey(LocaleId locale, TextTableId table, TextKey key)
            {
                _locale = locale;
                _table = table;
                _key = key;
            }

            public bool Equals(MissingKeyReportKey other)
                => _locale == other._locale && _table == other._table && _key == other._key;

            public override bool Equals(object obj) => obj is MissingKeyReportKey other && Equals(other);

            public override int GetHashCode() => HashCode.Combine(_locale, _table, _key);
        }
    }
}