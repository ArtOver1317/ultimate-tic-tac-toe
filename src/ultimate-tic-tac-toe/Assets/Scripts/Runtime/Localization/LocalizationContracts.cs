using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;

namespace Runtime.Localization
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

    public interface ILocalizationCatalog
    {
        IReadOnlyList<LocaleId> GetSupportedLocales();
        IReadOnlyList<TextTableId> GetStartupTables();
        string GetAssetKey(LocaleId locale, TextTableId table);
    }

    public interface ILocalizationLoader
    {
        UniTask<ReadOnlyMemory<byte>> LoadBytesAsync(string assetKey, CancellationToken cancellationToken);
        UniTask PreDownloadAsync(string assetKey, CancellationToken cancellationToken);
        void Release(string assetKey);
    }

    public interface ILocalizationParser
    {
        LocalizationTable ParseTable(ReadOnlySpan<byte> payload, LocaleId locale, TextTableId table);
    }

    public interface ILocalizationStore
    {
        Observable<LocalizationStoreEvent> Events { get; }
        void SetActiveLocale(LocaleId locale);
        LocaleId GetActiveLocale();
        void Put(LocalizationTable table);
        bool TryResolveTemplate(TextTableId table, TextKey key, out string template);
        void Remove(LocaleId locale, TextTableId table);
    }

    public interface ITextFormatter
    {
        string Format(string template, LocaleId locale, IReadOnlyDictionary<string, object> args);
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
        Observable<string> Observe(TextTableId table, TextKey key, Observable<IReadOnlyDictionary<string, object>> args);
        Observable<string> Observe(TextTableId table, TextKey key, IReadOnlyDictionary<string, object> args = null);

        IReadOnlyList<LocaleId> GetSupportedLocales();
    }

    public interface ITextKeys
    {
        IReadOnlyList<(TextTableId Table, TextKey Key)> GetAll();
    }
}