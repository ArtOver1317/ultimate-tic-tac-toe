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
                var json = Encoding.UTF8.GetString(payload);
                var root = JSON.Parse(json);
                
                if (root == null || !root.IsObject)
                    throw new FormatException("Localization JSON root must be an object.");

                var obj = root.AsObject;
                
                if (obj == null)
                    throw new FormatException("Localization JSON root must be an object.");

                var fileLocaleCode = ReadOptionalString(obj, "locale");
                
                if (!string.IsNullOrWhiteSpace(fileLocaleCode))
                {
                    var fileLocale = new LocaleId(fileLocaleCode);
                    
                    if (fileLocale != locale)
                        throw new FormatException($"Locale mismatch. Requested '{locale.Code}', file '{fileLocale.Code}'.");
                }

                var fileTableName = ReadOptionalString(obj, "table");
                
                if (!string.IsNullOrWhiteSpace(fileTableName))
                {
                    var fileTable = new TextTableId(fileTableName);
                    
                    if (fileTable != table)
                        throw new FormatException($"Table mismatch. Requested '{table.Name}', file '{fileTable.Name}'.");
                }

                if (!obj.HasKey("entries"))
                    throw new FormatException("Localization JSON missing 'entries'.");

                var entriesNode = obj["entries"];
                
                if (entriesNode == null || !entriesNode.IsObject)
                    throw new FormatException("Localization JSON 'entries' must be an object.");

                var entriesObject = entriesNode.AsObject;
                var entries = new Dictionary<string, string>(StringComparer.Ordinal);

                foreach (var (key, valueNode) in entriesObject.Linq)
                {
                    if (string.IsNullOrWhiteSpace(key))
                        continue;

                    var value = ReadValueAsText(valueNode);

                    entries[key.Trim()] = value ?? string.Empty;
                }

                return new LocalizationTable(locale, table, entries);
            }
            catch (Exception ex) when (ex is not FormatException)
            {
                throw new FormatException("Failed to parse localization JSON.", ex);
            }
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