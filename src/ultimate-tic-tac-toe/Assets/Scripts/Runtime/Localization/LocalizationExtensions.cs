using System.Collections.Generic;
using R3;

namespace Runtime.Localization
{
    public static class LocalizationExtensions
    {
        public static string Resolve(
            this ILocalizationService service,
            string table,
            string key,
            IReadOnlyDictionary<string, object> args = null) =>
            service.Resolve(new TextTableId(table), new TextKey(key), args);

        public static Observable<string> Observe(
            this ILocalizationService service,
            string table,
            string key,
            IReadOnlyDictionary<string, object> args = null) =>
            service.Observe(new TextTableId(table), new TextKey(key), args);
    }
}
