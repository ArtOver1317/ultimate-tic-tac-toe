using System;
using System.Collections.Generic;
using System.Text;
using SimpleJSON;

namespace Runtime.Localization
{
    public sealed class JsonLocalizationParser : ILocalizationParser
    {
        public LocalizationTable ParseTable(ReadOnlySpan<byte> payload, LocaleId locale, TextTableId table)
        {
            if (payload.IsEmpty)
                throw new ArgumentException("Payload is empty.", nameof(payload));

            try
            {
                var rootObject = ParseRootObject(payload);
                ValidateLocale(rootObject, locale);
                ValidateTable(rootObject, table);

                var entries = ReadEntries(rootObject);
                return new LocalizationTable(locale, table, entries);
            }
            catch (Exception ex) when (ex is not FormatException)
            {
                throw new FormatException("Failed to parse localization JSON.", ex);
            }
        }

        private static JSONObject ParseRootObject(ReadOnlySpan<byte> payload)
        {
            var json = Encoding.UTF8.GetString(payload);
            var root = JSON.Parse(json);
            var rootObject = root != null && root.IsObject ? root.AsObject : null;

            return rootObject == null 
                ? throw new FormatException("Localization JSON root must be an object.") 
                : rootObject;
        }

        private static void ValidateLocale(JSONObject rootObject, LocaleId locale)
        {
            var fileLocaleCode = ReadOptionalString(rootObject, "locale");

            if (string.IsNullOrWhiteSpace(fileLocaleCode))
                return;

            var fileLocale = new LocaleId(fileLocaleCode);

            if (fileLocale != locale)
                throw new FormatException($"Locale mismatch. Requested '{locale.Code}', file '{fileLocale.Code}'.");
        }

        private static void ValidateTable(JSONObject rootObject, TextTableId table)
        {
            var fileTableName = ReadOptionalString(rootObject, "table");

            if (string.IsNullOrWhiteSpace(fileTableName))
                return;

            var fileTable = new TextTableId(fileTableName);

            if (fileTable != table)
                throw new FormatException($"Table mismatch. Requested '{table.Name}', file '{fileTable.Name}'.");
        }

        private static Dictionary<string, string> ReadEntries(JSONObject rootObject)
        {
            if (!rootObject.HasKey("entries"))
                throw new FormatException("Localization JSON missing 'entries'.");

            var entriesNode = rootObject["entries"];

            if (entriesNode == null || !entriesNode.IsObject)
                throw new FormatException("Localization JSON 'entries' must be an object.");

            var entriesObject = entriesNode.AsObject;
            var entries = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var (key, valueNode) in entriesObject.Linq)
            {
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                entries[key.Trim()] = ReadValueAsText(valueNode);
            }

            return entries;
        }

        private static string ReadOptionalString(JSONObject obj, string key)
        {
            if (!obj.HasKey(key))
                return null;

            var node = obj[key];
            
            if (node == null || node.IsNull)
                return null;

            return !node.IsString ? throw new FormatException($"Localization JSON '{key}' must be a string.") : node.Value;
        }

        private static string ReadValueAsText(JSONNode node)
        {
            if (node == null || node.IsNull)
                return string.Empty;

            if (node.IsString || node.IsNumber || node.IsBoolean)
                return node.Value;

            return node.ToString();
        }
    }
}