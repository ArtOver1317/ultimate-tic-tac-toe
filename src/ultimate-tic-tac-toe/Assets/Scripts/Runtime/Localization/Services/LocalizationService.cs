using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;

namespace Runtime.Localization
{
    public sealed class LocalizationService : ILocalizationService, IDisposable
    {
        private readonly ILocalizationStore _store;
        private readonly ILocalizationCatalog _catalog;
        private readonly ILocalizationPolicy _policy;
        private readonly ILocaleStorage _localeStorage;
        private readonly LocalizationTablePreloader _preloader;
        private readonly LocalizationTextResolver _resolver;

        private readonly ReactiveProperty<LocaleId> _currentLocale;
        private readonly ReactiveProperty<bool> _isBusy;
        private readonly Subject<LocalizationError> _errors = new();

        private bool _isInitialized;

        private readonly SemaphoreSlim _initializeGate = new(1, 1);

        private readonly object _switchLock = new();
        private readonly object _busyLock = new();
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
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _policy = policy ?? throw new ArgumentNullException(nameof(policy));
            _localeStorage = localeStorage ?? throw new ArgumentNullException(nameof(localeStorage));
            
            _preloader = new LocalizationTablePreloader(
                loader ?? throw new ArgumentNullException(nameof(loader)),
                parser ?? throw new ArgumentNullException(nameof(parser)),
                store,
                catalog,
                policy,
                ReportError);
            
            _resolver = new LocalizationTextResolver(
                store,
                formatter ?? throw new ArgumentNullException(nameof(formatter)),
                policy,
                ReportError);

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
                _resolver.ResetReportedMissingKeys();

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
                        ReportError(new LocalizationError(
                            LocalizationErrorCode.UnsupportedLocale,
                            $"Unsupported saved locale '{saved.Value.Code}'.",
                            locale: saved.Value));
                    }
                }
            }
            catch (Exception ex)
            {
                ReportError(new LocalizationError(LocalizationErrorCode.Unknown, "Failed to load saved locale.", ex));
            }

            return locale;
        }

        public async UniTask SetLocaleAsync(LocaleId locale, CancellationToken cancellationToken)
        {
            EnsureInitialized();

            var supported = _catalog.GetSupportedLocales();
            
            if (!IsSupported(supported, locale))
            {
                ReportError(new LocalizationError(
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
                var tablesToPreload = _resolver.BuildLocaleSwitchPreloadList(
                    _catalog.GetRequiredTables(),
                    _catalog.GetStartupTables());

                await PreloadAsync(locale, tablesToPreload, linkedCts.Token);

                lock (_switchLock)
                {
                    if (myVersion != _switchVersion)
                        return;
                }

                _store.SetActiveLocale(locale);
                _currentLocale.Value = locale;
                _resolver.ResetReportedMissingKeys();

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
                ReportError(new LocalizationError(LocalizationErrorCode.Unknown, "Failed to save locale.", ex, locale: locale));
            }
        }

        public async UniTask PreloadAsync(LocaleId locale, IReadOnlyList<TextTableId> tables, CancellationToken cancellationToken) => 
            await _preloader.PreloadAsync(locale, tables, cancellationToken);

        public Observable<string> Observe(TextTableId table, TextKey key, Observable<IReadOnlyDictionary<string, object>> args)
        {
            EnsureInitialized();
            return _resolver.Observe(table, key, args, CurrentLocale);
        }

        public Observable<string> Observe(TextTableId table, TextKey key, IReadOnlyDictionary<string, object> args = null)
        {
            EnsureInitialized();
            return _resolver.Observe(table, key, args, CurrentLocale);
        }

        public string Resolve(TextTableId table, TextKey key, IReadOnlyDictionary<string, object> args = null)
        {
            EnsureInitialized();
            return _resolver.Resolve(table, key, args);
        }

        public bool TryResolve(TextTableId table, TextKey key, out string result, IReadOnlyDictionary<string, object> args = null)
        {
            EnsureInitialized();
            return _resolver.TryResolve(table, key, out result, args);
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
            lock (_busyLock)
            {
                _busyCount++;

                if (_busyCount == 1)
                    _isBusy.Value = true;
            }
        }

        private void ExitBusy()
        {
            lock (_busyLock)
            {
                if (_busyCount == 0)
                    return;

                _busyCount--;

                if (_busyCount == 0)
                    _isBusy.Value = false;
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

        private void ReportError(LocalizationError error) => _errors.OnNext(error);
    }
}