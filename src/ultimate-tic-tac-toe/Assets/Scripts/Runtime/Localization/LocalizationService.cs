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
        private readonly ILocalizationLoader _loader;
        private readonly ILocalizationParser _parser;
        private readonly ILocalizationCatalog _catalog;
        private readonly ILocalizationPolicy _policy;
        private readonly ITextFormatter _formatter;
        private readonly ILocaleStorage _localeStorage;

        private readonly ReactiveProperty<LocaleId> _currentLocale;
        private readonly ReactiveProperty<bool> _isBusy;
        private readonly Subject<LocalizationError> _errors = new();

        private bool _isInitialized;

        private readonly object _switchLock = new();
        private CancellationTokenSource _switchCts;
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
            if (_isInitialized)
                return;

            EnterBusy();
            
            try
            {
                var supported = _catalog.GetSupportedLocales();

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

                await PreloadAsync(locale, _catalog.GetStartupTables(), cancellationToken);

                _store.SetActiveLocale(locale);
                _currentLocale.Value = locale;

                _isInitialized = true;
            }
            finally
            {
                ExitBusy();
            }
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
            
            lock (_switchLock)
            {
                _switchCts?.Cancel();
                _switchCts?.Dispose();
                _switchCts = new CancellationTokenSource();

                linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _switchCts.Token);
            }

            EnterBusy();
            
            try
            {
                await PreloadAsync(locale, _catalog.GetStartupTables(), linkedCts.Token);

                _store.SetActiveLocale(locale);
                _currentLocale.Value = locale;

                try
                {
                    await _localeStorage.SaveAsync(locale);
                }
                catch (Exception ex)
                {
                    _errors.OnNext(new LocalizationError(LocalizationErrorCode.Unknown, "Failed to save locale.", ex, locale: locale));
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Expected during rapid switching: the previous request is canceled by a newer SetLocaleAsync.
                // No state change should be applied in this case.
            }
            finally
            {
                linkedCts.Dispose();
                ExitBusy();
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
                var assetKey = _catalog.GetAssetKey(locale, table);

                try
                {
                    var bytes = await _loader.LoadBytesAsync(assetKey, cancellationToken);
                    var parsedTable = _parser.ParseTable(bytes.Span, locale, table);
                    _store.Put(parsedTable);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _errors.OnNext(new LocalizationError(
                        ex is FormatException ? LocalizationErrorCode.ParseFailed : LocalizationErrorCode.AddressablesLoadFailed,
                        $"Failed to preload table '{table.Name}' for locale '{locale.Code}' (assetKey='{assetKey}').",
                        ex,
                        locale,
                        table));
                }
                finally
                {
                    _loader.Release(assetKey);
                }
            }
        }

        public string Resolve(TextTableId table, TextKey key, IReadOnlyDictionary<string, object> args = null)
        {
            EnsureInitialized();

            if (_store.TryResolveTemplate(table, key, out var template))
            {
                var activeLocale = _store.GetActiveLocale();
                return _formatter.Format(template, activeLocale, args);
            }

            _errors.OnNext(new LocalizationError(
                LocalizationErrorCode.MissingKey,
                $"Missing key '{key.Value}' in table '{table.Name}'.",
                locale: _store.GetActiveLocale(),
                tableId: table,
                key: key));

            if (_policy.UseMissingKeyPlaceholders) 
                return $"⟦Missing: {table.Name}.{key.Value}⟧";

            return string.Empty;
        }

        public Observable<string> Observe(TextTableId table, TextKey key, Observable<IReadOnlyDictionary<string, object>> args)
        {
            if (args == null)
                throw new ArgumentNullException(nameof(args));

            EnsureInitialized();

            return Observable.Create<string>(observer =>
            {
                IReadOnlyDictionary<string, object> latestArgs = null;

                var argsSub = args.Subscribe(a =>
                {
                    latestArgs = a;
                    observer.OnNext(Resolve(table, key, latestArgs));
                });

                var localeSub = CurrentLocale.Subscribe(_ =>
                {
                    observer.OnNext(Resolve(table, key, latestArgs));
                });

                return Disposable.Create(() =>
                {
                    argsSub.Dispose();
                    localeSub.Dispose();
                });
            });
        }

        public Observable<string> Observe(TextTableId table, TextKey key, IReadOnlyDictionary<string, object> args = null)
        {
            EnsureInitialized();

            return Observable.Create<string>(observer =>
            {
                var localeSub = CurrentLocale.Subscribe(_ =>
                {
                    observer.OnNext(Resolve(table, key, args));
                });

                return Disposable.Create(() => localeSub.Dispose());
            });
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
        }

        private static bool IsSupported(IReadOnlyList<LocaleId> supported, LocaleId locale)
        {
            for (var i = 0; i < supported.Count; i++)
            {
                if (supported[i] == locale)
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
    }
}