using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using Runtime.Localization.Contracts;
using Runtime.Localization.Types;

namespace Tests.PlayMode.GameModes.Wizard.Fixtures
{
    internal sealed class TestLocalizationService : ILocalizationService, IDisposable
    {
        private readonly ReactiveProperty<LocaleId> _currentLocale = new(LocaleId.EnglishUs);
        private readonly ReactiveProperty<bool> _isBusy = new(false);
        private readonly Subject<LocalizationError> _errors = new();
        private readonly Dictionary<(LocaleId Locale, string Key), ReactiveProperty<string>> _texts = new();

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
            GetOrCreate(_currentLocale.CurrentValue, key.Value).Value;

        public bool TryResolve(TextTableId table, TextKey key, out string result, IReadOnlyDictionary<string, object> args = null)
        {
            result = GetOrCreate(_currentLocale.CurrentValue, key.Value).Value;
            return true;
        }

        public Observable<string> Observe(TextTableId table, TextKey key, Observable<IReadOnlyDictionary<string, object>> args) =>
            GetOrCreate(_currentLocale.CurrentValue, key.Value);

        public Observable<string> Observe(TextTableId table, TextKey key, IReadOnlyDictionary<string, object> args = null) =>
            GetOrCreate(_currentLocale.CurrentValue, key.Value);

        public IReadOnlyList<LocaleId> GetSupportedLocales() => new[] { LocaleId.EnglishUs, LocaleId.Russian };

        public void SetText(LocaleId locale, string key, string value) => GetOrCreate(locale, key).Value = value ?? string.Empty;

        public void SetText(string key, string value) => SetText(_currentLocale.CurrentValue, key, value);

        public void Dispose()
        {
            foreach (var entry in _texts.Values)
            {
                entry.Dispose();
            }

            _errors.Dispose();
            _isBusy.Dispose();
            _currentLocale.Dispose();
            _texts.Clear();
        }

        private ReactiveProperty<string> GetOrCreate(LocaleId locale, string key)
        {
            var id = (locale, key ?? string.Empty);
           
            if (!_texts.TryGetValue(id, out var value))
            {
                value = new ReactiveProperty<string>(key ?? string.Empty);
                _texts.Add(id, value);
            }

            return value;
        }
    }
}