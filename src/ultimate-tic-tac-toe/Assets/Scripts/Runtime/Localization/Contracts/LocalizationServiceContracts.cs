using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using Runtime.Localization.Types;

namespace Runtime.Localization.Contracts
{
    public interface ILocalizationPolicy
    {
        IReadOnlyList<LocaleId> GetFallbackChain(LocaleId requested);
        bool UseMissingKeyPlaceholders { get; }
        int MaxCachedTables { get; }
        LocaleId DefaultLocale { get; }
    }

    public interface ILocaleStorage
    {
        UniTask<LocaleId?> LoadAsync();
        UniTask SaveAsync(LocaleId locale);
    }

    public interface ILocalizationService
    {
        ReadOnlyReactiveProperty<LocaleId> CurrentLocale { get; }
        ReadOnlyReactiveProperty<bool> IsBusy { get; }
        Observable<LocalizationError> Errors { get; }

        UniTask InitializeAsync(CancellationToken cancellationToken);
        UniTask SetLocaleAsync(LocaleId locale, CancellationToken cancellationToken);
        UniTask PreloadAsync(LocaleId locale, IReadOnlyList<TextTableId> tables, CancellationToken cancellationToken);

        string Resolve(TextTableId table, TextKey key, IReadOnlyDictionary<string, object> args = null);
        bool TryResolve(TextTableId table, TextKey key, out string result, IReadOnlyDictionary<string, object> args = null);
        Observable<string> Observe(TextTableId table, TextKey key, Observable<IReadOnlyDictionary<string, object>> args);
        Observable<string> Observe(TextTableId table, TextKey key, IReadOnlyDictionary<string, object> args = null);

        IReadOnlyList<LocaleId> GetSupportedLocales();
    }
}