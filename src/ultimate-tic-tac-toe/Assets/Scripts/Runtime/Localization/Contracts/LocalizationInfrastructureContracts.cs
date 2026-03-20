using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using Runtime.Localization.Types;

namespace Runtime.Localization.Contracts
{
    public interface ILocalizationCatalog
    {
        IReadOnlyList<LocaleId> GetSupportedLocales();
        IReadOnlyList<TextTableId> GetStartupTables();
        IReadOnlyList<TextTableId> GetRequiredTables();
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
}