using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using Runtime.Localization.Types;
using SimpleJSON;

namespace Tests.EditMode.Localization
{
    [Category("Content")]
    [Category("Localization")]
    public sealed class LocalizationContentConsistencyTests
    {
        private const string _localizationRoot = "Assets/Content/Localization";

        [Test]
        public void WhenValidatingLocalizationContent_ThenAllLocalesHaveSameTablesAndKeys()
        {
            Directory.Exists(_localizationRoot)
                .Should().BeTrue($"Localization root directory not found: {_localizationRoot}");

            var localeDirs = Directory.GetDirectories(_localizationRoot)
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Where(name => !name.StartsWith(".", StringComparison.Ordinal))
                .ToArray();

            localeDirs.Length.Should().BeGreaterThan(0, "at least one locale directory must exist");

            // locale -> table -> keys
            var data = new Dictionary<string, Dictionary<string, HashSet<string>>>(StringComparer.Ordinal);

            foreach (var localeDirName in localeDirs)
            {
                var localeDirPath = Path.Combine(_localizationRoot, localeDirName);
                var jsonFiles = Directory.GetFiles(localeDirPath, "*.json");

                jsonFiles.Length.Should().BeGreaterThan(0, $"Locale '{localeDirName}' has no JSON tables");

                var tables = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

                foreach (var jsonFile in jsonFiles)
                {
                    var tableName = Path.GetFileNameWithoutExtension(jsonFile);
                    var json = File.ReadAllText(jsonFile);

                    var parsed = ParseKeysAndValidateMetadata(json, localeDirName, tableName, jsonFile);
                    tables[tableName] = parsed;
                }

                data[localeDirName] = tables;
            }

            // Use en as reference when available; otherwise first locale.
            var referenceLocale = data.ContainsKey("en") ? "en" : data.Keys.First();
            var referenceTables = data[referenceLocale];

            foreach (var (locale, tables) in data)
            {
                // Tables must match exactly (strict).
                var missingTables = referenceTables.Keys.Except(tables.Keys, StringComparer.Ordinal).ToArray();
                var extraTables = tables.Keys.Except(referenceTables.Keys, StringComparer.Ordinal).ToArray();

                missingTables.Should().BeEmpty($"Locale '{locale}' is missing tables: {string.Join(", ", missingTables)}");
                extraTables.Should().BeEmpty($"Locale '{locale}' has extra tables: {string.Join(", ", extraTables)}");

                foreach (var (tableName, referenceKeys) in referenceTables)
                {
                    var keys = tables[tableName];

                    var missingKeys = referenceKeys.Except(keys, StringComparer.Ordinal).ToArray();
                    var extraKeys = keys.Except(referenceKeys, StringComparer.Ordinal).ToArray();

                    missingKeys.Should().BeEmpty($"Missing keys in {locale}/{tableName}: {string.Join(", ", missingKeys)}");
                    extraKeys.Should().BeEmpty($"Extra keys in {locale}/{tableName}: {string.Join(", ", extraKeys)}");
                }
            }
        }

        private static HashSet<string> ParseKeysAndValidateMetadata(
            string json,
            string localeDirName,
            string expectedTableName,
            string filePath)
        {
            json.Should().NotBeNullOrWhiteSpace($"Empty JSON file: {filePath}");

            JSONNode root;

            try
            {
                root = JSON.Parse(json);
            }
            catch (Exception ex)
            {
                throw new AssertionException($"Invalid JSON in {filePath}: {ex.Message}");
            }

            root.Should().NotBeNull($"Invalid JSON in {filePath}");
            root.IsObject.Should().BeTrue($"Localization JSON root must be an object: {filePath}");

            var obj = root.AsObject;
            obj.Should().NotBeNull($"Localization JSON root must be an object: {filePath}");

            // Validate metadata if present.
            if (obj.HasKey("locale"))
            {
                var localeValue = obj["locale"];
                localeValue.Should().NotBeNull($"'locale' must not be null: {filePath}");
                localeValue.IsString.Should().BeTrue($"'locale' must be a string: {filePath}");

                var fileLocale = new LocaleId(localeValue.Value);
                fileLocale.TryGetLanguageOnly(out var languageOnly);

                languageOnly.Code.Should().Be(localeDirName.ToLowerInvariant(),
                    $"Locale metadata mismatch. Folder='{localeDirName}', file locale='{fileLocale.Code}' ({filePath})");
            }

            if (obj.HasKey("table"))
            {
                var tableValue = obj["table"];
                tableValue.Should().NotBeNull($"'table' must not be null: {filePath}");
                tableValue.IsString.Should().BeTrue($"'table' must be a string: {filePath}");

                tableValue.Value.Should().Be(expectedTableName,
                    $"Table metadata mismatch. File name='{expectedTableName}', file table='{tableValue.Value}' ({filePath})");
            }

            obj.HasKey("entries").Should().BeTrue($"Localization JSON missing 'entries': {filePath}");

            var entries = obj["entries"];
            entries.Should().NotBeNull($"Localization JSON 'entries' is null: {filePath}");
            entries.IsObject.Should().BeTrue($"Localization JSON 'entries' must be an object: {filePath}");

            var entriesObj = entries.AsObject;
            entriesObj.Should().NotBeNull($"Localization JSON 'entries' must be an object: {filePath}");

            var keys = new HashSet<string>(StringComparer.Ordinal);

            foreach (var (key, _) in entriesObj.Linq)
            {
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                keys.Add(key.Trim());
            }

            keys.Count.Should().BeGreaterThan(0, $"Table has no keys: {filePath}");
            return keys;
        }
    }
}
