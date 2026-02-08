using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;

namespace Runtime.Localization
{
    public sealed partial class LocalizationService : ILocalizationService, IDisposable
    {
        private const int _maxReportedMissingKeys = 4096;

        private readonly ILocalizationStore _store;
        private readonly ILocalizationLoader _loader;
        private readonly ILocalizationParser _parser;
        private readonly ILocalizationCatalog _catalog;
        private readonly ILocalizationPolicy _policy;
        private readonly ITextFormatter _formatter;
        private readonly ILocaleStorage _localeStorage;

        private readonly ReactiveProperty<LocaleId> _currentLocale;
        private readonly ReactiveProperty<bool> _isBusy;
        private readonly Subject<LocalizationError> _errors = new();

        private readonly HashSet<MissingKeyReportKey> _reportedMissingKeys = new();

        private readonly object _trackedTablesLock = new();
        private readonly HashSet<TextTableId> _trackedTables = new();

        private bool _isInitialized;

        private readonly SemaphoreSlim _initializeGate = new(1, 1);

        private readonly object _switchLock = new();
        private CancellationTokenSource _switchCts;
        private int _switchVersion;
        private readonly SemaphoreSlim _saveGate = new(1, 1);
        private int _busyCount;

        public ReadOnlyReactiveProperty<LocaleId> CurrentLocale => _currentLocale;
        public ReadOnlyReactiveProperty<bool> IsBusy => _isBusy;
        public Observable<LocalizationError> Errors => _errors;

        public LocalizationService(
            ILocalizationStore store,
            ILocalizationLoader loader,
            ILocalizationParser parser,
            ILocalizationCatalog catalog,
            ILocalizationPolicy policy,
            ITextFormatter formatter,
            ILocaleStorage localeStorage)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _loader = loader ?? throw new ArgumentNullException(nameof(loader));
            _parser = parser ?? throw new ArgumentNullException(nameof(parser));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _policy = policy ?? throw new ArgumentNullException(nameof(policy));
            _formatter = formatter ?? throw new ArgumentNullException(nameof(formatter));
            _localeStorage = localeStorage ?? throw new ArgumentNullException(nameof(localeStorage));

            _currentLocale = new ReactiveProperty<LocaleId>(_policy.DefaultLocale);
            _isBusy = new ReactiveProperty<bool>(false);
        }

        public IReadOnlyList<LocaleId> GetSupportedLocales() => _catalog.GetSupportedLocales();

        public async UniTask InitializeAsync(CancellationToken cancellationToken)
        {
            await _initializeGate.WaitAsync(cancellationToken);
            var enteredBusy = false;
            
            try
            {
                if (_isInitialized)
                    return;

                EnterBusy();
                enteredBusy = true;

                var supported = _catalog.GetSupportedLocales();
                var locale = await ResolveStartupLocaleAsync(supported);

                var startupTables = _catalog.GetStartupTables();
                var requiredTables = _catalog.GetRequiredTables();
                await PreloadAsync(locale, MergeTables(requiredTables, startupTables), cancellationToken);

                _store.SetActiveLocale(locale);
                _currentLocale.Value = locale;
                _reportedMissingKeys.Clear();

                _isInitialized = true;
            }
            finally
            {
                if (enteredBusy)
                    ExitBusy();
                
                _initializeGate.Release();
            }
        }

        private async UniTask<LocaleId> ResolveStartupLocaleAsync(IReadOnlyList<LocaleId> supported)
        {
            var locale = _policy.DefaultLocale;
            
            try
            {
                var saved = await _localeStorage.LoadAsync();
                
                if (saved.HasValue)
                {
                    if (IsSupported(supported, saved.Value))
                        locale = saved.Value;
                    else
                    {
                        _errors.OnNext(new LocalizationError(
                            LocalizationErrorCode.UnsupportedLocale,
                            $"Unsupported saved locale '{saved.Value.Code}'.",
                            locale: saved.Value));
                    }
                }
            }
            catch (Exception ex)
            {
                _errors.OnNext(new LocalizationError(LocalizationErrorCode.Unknown, "Failed to load saved locale.", ex));
            }

            return locale;
        }

        public async UniTask SetLocaleAsync(LocaleId locale, CancellationToken cancellationToken)
        {
            EnsureInitialized();

            var supported = _catalog.GetSupportedLocales();
            
            if (!IsSupported(supported, locale))
            {
                _errors.OnNext(new LocalizationError(
                    LocalizationErrorCode.UnsupportedLocale,
                    $"Unsupported locale '{locale.Code}'.",
                    locale: locale));
                
                return;
            }

            CancellationTokenSource linkedCts;
            int myVersion;
            
            lock (_switchLock)
            {
                _switchCts?.Cancel();
                _switchCts?.Dispose();
                _switchCts = new CancellationTokenSource();

                _switchVersion++;
                myVersion = _switchVersion;

                linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _switchCts.Token);
            }

            EnterBusy();
            
            try
            {
                var tablesToPreload = BuildLocaleSwitchPreloadList();
                await PreloadAsync(locale, tablesToPreload, linkedCts.Token);

                lock (_switchLock)
                {
                    if (myVersion != _switchVersion)
                        return;
                }

                _store.SetActiveLocale(locale);
                _currentLocale.Value = locale;
                _reportedMissingKeys.Clear();

                await TrySaveLocaleAsync(locale, myVersion, cancellationToken);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Expected during rapid switching: the previous request is canceled by a newer SetLocaleAsync.
            }
            finally
            {
                linkedCts.Dispose();
                ExitBusy();
            }
        }

        private async UniTask TrySaveLocaleAsync(LocaleId locale, int myVersion, CancellationToken cancellationToken)
        {
            try
            {
                await _saveGate.WaitAsync(cancellationToken);
                
                try
                {
                    lock (_switchLock)
                    {
                        if (myVersion != _switchVersion)
                            return;
                    }

                    await _localeStorage.SaveAsync(locale);
                }
                finally
                {
                    _saveGate.Release();
                }
            }
            catch (Exception ex)
            {
                _errors.OnNext(new LocalizationError(LocalizationErrorCode.Unknown, "Failed to save locale.", ex, locale: locale));
            }
        }

        public async UniTask PreloadAsync(LocaleId locale, IReadOnlyList<TextTableId> tables, CancellationToken cancellationToken)
        {
            if (tables == null)
                throw new ArgumentNullException(nameof(tables));

            for (var i = 0; i < tables.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var table = tables[i];

                var chain = _policy.GetFallbackChain(locale);
                var triedKeys = new HashSet<string>(StringComparer.Ordinal);
                Exception lastException = null;

                for (var j = 0; j < chain.Count; j++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var candidateLocale = chain[j];
                    var assetKey = _catalog.GetAssetKey(candidateLocale, table);

                    if (!triedKeys.Add(assetKey))
                        continue;

                    try
                    {
                        var bytes = await _loader.LoadBytesAsync(assetKey, cancellationToken);
                        var parsedTable = _parser.ParseTable(bytes.Span, candidateLocale, table);
                        _store.Put(parsedTable);
                        lastException = null;
                        break;
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

                if (lastException != null)
                {
                    _errors.OnNext(new LocalizationError(
                        lastException is FormatException ? LocalizationErrorCode.ParseFailed : LocalizationErrorCode.AddressablesLoadFailed,
                        $"Failed to preload table '{table.Name}' for locale '{locale.Code}'. Tried: {string.Join(", ", triedKeys)}",
                        lastException,
                        locale,
                        table));

                    var requiredTables = _catalog.GetRequiredTables() ?? Array.Empty<TextTableId>();
                    var isRequired = false;

                    for (var k = 0; k < requiredTables.Count; k++)
                    {
                        if (requiredTables[k] == table)
                        {
                            isRequired = true;
                            break;
                        }
                    }

                    if (isRequired)
                    {
                        throw new InvalidOperationException(
                            $"Required localization table '{table.Name}' could not be loaded for locale '{locale.Code}'.");
                    }
                }
            }
        }

        private static IReadOnlyList<TextTableId> MergeTables(IReadOnlyList<TextTableId> a, IReadOnlyList<TextTableId> b)
        {
            if (a == null || a.Count == 0)
                return b ?? Array.Empty<TextTableId>();

            if (b == null || b.Count == 0)
                return a;

            var merged = new List<TextTableId>(a.Count + b.Count);
            
            for (var i = 0; i < a.Count; i++)
            {
                merged.Add(a[i]);
            }

            for (var i = 0; i < b.Count; i++)
            {
                var table = b[i];
                var alreadyAdded = false;
                
                for (var j = 0; j < merged.Count; j++)
                {
                    if (merged[j] == table)
                    {
                        alreadyAdded = true;
                        break;
                    }
                }

                if (!alreadyAdded)
                    merged.Add(table);
            }

            return merged;
        }

        public string Resolve(TextTableId table, TextKey key, IReadOnlyDictionary<string, object> args = null)
        {
            EnsureInitialized();

            TrackTable(table);

            if (_store.TryResolveTemplate(table, key, out var template))
            {
                var activeLocale = _store.GetActiveLocale();
                return _formatter.Format(template, activeLocale, args);
            }

            var locale = _store.GetActiveLocale();

            if (_reportedMissingKeys.Count >= _maxReportedMissingKeys)
                _reportedMissingKeys.Clear();

            if (_reportedMissingKeys.Add(new MissingKeyReportKey(locale, table, key)))
            {
                _errors.OnNext(new LocalizationError(
                    LocalizationErrorCode.MissingKey,
                    $"Missing key '{key.Value}' in table '{table.Name}'.",
                    locale: locale,
                    tableId: table,
                    key: key));
            }

            return _policy.UseMissingKeyPlaceholders ? $"⟦Missing: {table.Name}.{key.Value}⟧" : string.Empty;
        }

        public void Dispose()
        {
            lock (_switchLock)
            {
                _switchCts?.Cancel();
                _switchCts?.Dispose();
                _switchCts = null;
            }

            _errors.Dispose();
            _currentLocale.Dispose();
            _isBusy.Dispose();
            _initializeGate.Dispose();
            _saveGate.Dispose();
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

            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = _locale.GetHashCode();
                    hash = (hash * 397) ^ _table.GetHashCode();
                    hash = (hash * 397) ^ _key.GetHashCode();
                    return hash;
                }
            }
        }

        private static bool IsSupported(IReadOnlyList<LocaleId> supported, LocaleId locale)
        {
            foreach (var supportedLocale in supported)
            {
                if (supportedLocale == locale)
                    return true;
            }

            return false;
        }

        private void EnsureInitialized()
        {
            if (!_isInitialized)
                throw new InvalidOperationException("LocalizationService is not initialized. Call InitializeAsync first.");
        }

        private void EnterBusy()
        {
            var count = Interlocked.Increment(ref _busyCount);
            
            if (count == 1)
                _isBusy.Value = true;
        }

        private void ExitBusy()
        {
            var count = Interlocked.Decrement(ref _busyCount);
            
            if (count <= 0)
            {
                _busyCount = 0;
                _isBusy.Value = false;
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

        private IReadOnlyList<TextTableId> BuildLocaleSwitchPreloadList()
        {
            var startupTables = _catalog.GetStartupTables();
            var requiredTables = _catalog.GetRequiredTables();

            TextTableId[] trackedSnapshot;
            
            lock (_trackedTablesLock)
            {
                if (_trackedTables.Count == 0)
                    return MergeTables(requiredTables, startupTables);

                trackedSnapshot = new TextTableId[_trackedTables.Count];
                _trackedTables.CopyTo(trackedSnapshot);
            }

            // Merge startup + tracked (deduplicated).
            var merged = new List<TextTableId>();
            var requiredAndStartup = MergeTables(requiredTables, startupTables);
            
            for (var i = 0; i < requiredAndStartup.Count; i++)
            {
                merged.Add(requiredAndStartup[i]);
            }

            for (var i = 0; i < trackedSnapshot.Length; i++)
            {
                var table = trackedSnapshot[i];

                var alreadyAdded = false;
                
                for (var j = 0; j < merged.Count; j++)
                {
                    if (merged[j] == table)
                    {
                        alreadyAdded = true;
                        break;
                    }
                }

                if (!alreadyAdded)
                    merged.Add(table);
            }

            return merged;
        }
    }
}