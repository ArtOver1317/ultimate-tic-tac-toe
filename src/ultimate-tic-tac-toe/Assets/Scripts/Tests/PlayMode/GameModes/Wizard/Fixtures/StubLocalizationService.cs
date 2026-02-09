using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using Runtime.Localization;

namespace Tests.PlayMode.GameModes.Wizard
{
    /// <summary>
    /// Stub <see cref="ILocalizationService"/> that returns key values as-is.
    /// </summary>
    internal sealed class StubLocalizationService : ILocalizationService, IDisposable
    {
        private readonly ReactiveProperty<LocaleId> _currentLocale = new(LocaleId.EnglishUs);
        private readonly ReactiveProperty<bool> _isBusy = new(false);
        private readonly Subject<LocalizationError> _errors = new();

        public ReadOnlyReactiveProperty<LocaleId> CurrentLocale => _currentLocale;
        public ReadOnlyReactiveProperty<bool> IsBusy => _isBusy;
        public Observable<LocalizationError> Errors => _errors;

        public UniTask InitializeAsync(CancellationToken cancellationToken) => UniTask.CompletedTask;

        public UniTask SetLocaleAsync(LocaleId locale, CancellationToken cancellationToken)
        {
            _currentLocale.Value = locale;
            return UniTask.CompletedTask;
        }

        public UniTask PreloadAsync(LocaleId locale, IReadOnlyList<TextTableId> tables, CancellationToken cancellationToken) =>
            UniTask.CompletedTask;

        public string Resolve(TextTableId table, TextKey key, IReadOnlyDictionary<string, object> args = null) =>
            key.Value ?? string.Empty;

        public Observable<string> Observe(TextTableId table, TextKey key, Observable<IReadOnlyDictionary<string, object>> args) =>
            Observable.Return(key.Value ?? string.Empty);

        public Observable<string> Observe(TextTableId table, TextKey key, IReadOnlyDictionary<string, object> args = null) =>
            Observable.Return(key.Value ?? string.Empty);

        public IReadOnlyList<LocaleId> GetSupportedLocales() => new[] { LocaleId.EnglishUs };

        public void Dispose()
        {
            _errors.Dispose();
            _isBusy.Dispose();
            _currentLocale.Dispose();
        }
    }
}
