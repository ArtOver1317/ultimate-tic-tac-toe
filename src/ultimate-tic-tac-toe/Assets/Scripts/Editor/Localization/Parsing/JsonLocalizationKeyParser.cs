using System;
using System.Collections.Generic;
using SimpleJSON;

namespace Editor.Localization.Parsing
{
    /// <summary>
    /// Parses localization keys from JSON localization files.
    /// </summary>
    public sealed class JsonLocalizationKeyParser
    {
        /// <summary>
        /// Parses keys from a JSON localization file containing an "entries" object.
        /// </summary>
        /// <param name="json">JSON string in format: {"entries": {"key1": "value1", "key2": "value2"}}</param>
        /// <returns>HashSet of keys, or null if JSON is invalid or has no entries</returns>
        public HashSet<string> ParseKeys(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                var root = JSON.Parse(json);

                if (root == null || !root.IsObject)
                    return null;

                var obj = root.AsObject;

                if (obj == null || !obj.HasKey("entries"))
                    return null;

                var entriesNode = obj["entries"];

                if (entriesNode == null || !entriesNode.IsObject)
                    return null;

                var entriesObject = entriesNode.AsObject;
                var keys = new HashSet<string>(StringComparer.Ordinal);

                foreach (var (key, _) in entriesObject.Linq)
                {
                    if (string.IsNullOrWhiteSpace(key))
                        continue;

                    keys.Add(key.Trim());
                }

                return keys.Count > 0 ? keys : null;
            }
            catch
            {
                return null;
            }
        }
    }
}
