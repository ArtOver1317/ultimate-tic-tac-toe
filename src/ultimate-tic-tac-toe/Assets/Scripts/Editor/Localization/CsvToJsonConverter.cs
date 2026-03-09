using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Editor.Localization.Parsing;
using UnityEditor;
using UnityEngine;

using LocalizationTables = System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, string>>>;

namespace Editor.Localization
{
    public sealed class CsvToJsonConverter : EditorWindow
    {
        private string _csvPath = LocalizationEditorConventions.SourceOfTruthCsvPath;
        private string _outputPath = LocalizationEditorConventions.LocalizationRootPathWithTrailingSlash;
        private Vector2 _scrollPosition;
        private string _logText = "";

        // Extracted core logic classes
        private readonly CsvLineParser _csvParser = new();
        private readonly JsonStringEscaper _jsonEscaper = new();
        private readonly TableNameExtractor _tableExtractor = new();

        [MenuItem("Tools/Localization/Content Management/CSV → JSON Converter")]
        private static void ShowWindow()
        {
            var window = GetWindow<CsvToJsonConverter>("CSV → JSON Converter");
            window.minSize = new Vector2(500, 400);
            window.Show();
        }

        [MenuItem("Tools/Localization/Content Management/Convert CSV To JSON (Headless)")]
        private static void ConvertHeadless()
        {
            var converter = CreateInstance<CsvToJsonConverter>();
            converter.Convert(showDialogs: false);
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("CSV → JSON Converter", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            EditorGUILayout.HelpBox(
                "Expected CSV format:\n" +
                "Key,en-US,ru-RU,ja-JP,Context\n" +
                "MainMenu.Title,Ultimate Tic-Tac-Toe,Ультимативные крестики-нолики,究極の○×ゲーム,Main menu header",
                MessageType.Info);

            EditorGUILayout.Space();

            _csvPath = EditorGUILayout.TextField("CSV File Path:", _csvPath);
            
            if (GUILayout.Button("Browse CSV...", GUILayout.Width(120)))
            {
                var path = EditorUtility.OpenFilePanel("Select CSV File", LocalizationEditorConventions.LocalizationRootPath, "csv");
                
                if (!string.IsNullOrEmpty(path)) 
                    _csvPath = LocalizationEditorConventions.MakeProjectRelativePath(path);
            }

            EditorGUILayout.Space();

            _outputPath = EditorGUILayout.TextField("Output Folder:", _outputPath);
            
            if (GUILayout.Button("Browse Folder...", GUILayout.Width(120)))
            {
                var path = EditorUtility.OpenFolderPanel("Select Output Folder", LocalizationEditorConventions.LocalizationRootPath, "");
                
                if (!string.IsNullOrEmpty(path)) 
                    _outputPath = LocalizationEditorConventions.EnsureTrailingSlash(LocalizationEditorConventions.MakeProjectRelativePath(path));
            }

            EditorGUILayout.Space();

            GUI.enabled = !string.IsNullOrEmpty(_csvPath) &&
                          File.Exists(LocalizationEditorConventions.GetAbsolutePath(_csvPath));
            
            if (GUILayout.Button("Convert", GUILayout.Height(30))) 
                Convert();

            GUI.enabled = true;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Log:", EditorStyles.boldLabel);

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition, GUILayout.ExpandHeight(true));
            EditorGUILayout.TextArea(_logText, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        private void Convert() => Convert(showDialogs: true);

        private void Convert(bool showDialogs)
        {
            _logText = "";
            Log($"Starting conversion: {_csvPath}");

            try
            {
                var csvPath = LocalizationEditorConventions.GetAbsolutePath(_csvPath);
                var outputPath = LocalizationEditorConventions.GetAbsolutePath(_outputPath);
                var conversionPipeline = CreateConversionPipeline();

                if (!conversionPipeline.TryConvert(csvPath, outputPath, out var filesWritten))
                    return;

                CompleteConversion(filesWritten, showDialogs);
            }
            catch (Exception ex)
            {
                HandleConversionFailure(ex, showDialogs);
            }
        }

        private void CompleteConversion(int filesWritten, bool showDialogs)
        {
            AssetDatabase.Refresh();
            Log($"Conversion complete! Written {filesWritten} files.");

            if (showDialogs)
                EditorUtility.DisplayDialog("Success", $"Conversion complete!\n{filesWritten} files written.", "OK");
        }

        private void HandleConversionFailure(Exception ex, bool showDialogs)
        {
            LogError($"Conversion failed: {ex.Message}");

            if (showDialogs)
                EditorUtility.DisplayDialog("Error", $"Conversion failed:\n{ex.Message}", "OK");
        }

        private ConversionPipeline CreateConversionPipeline() =>
            new(_csvParser, _jsonEscaper, _tableExtractor, Log, LogWarning, LogError);

        private void Log(string message)
        {
            _logText += $"[INFO] {message}\n";
            Debug.Log($"[CsvToJsonConverter] {message}");
        }

        private void LogWarning(string message)
        {
            _logText += $"[WARNING] {message}\n";
            Debug.LogWarning($"[CsvToJsonConverter] {message}");
        }

        private void LogError(string message)
        {
            _logText += $"[ERROR] {message}\n";
            Debug.LogError($"[CsvToJsonConverter] {message}");
        }

        private sealed class ConversionPipeline
        {
            private readonly CsvLineParser _csvParser;
            private readonly JsonStringEscaper _jsonEscaper;
            private readonly TableNameExtractor _tableExtractor;
            private readonly Action<string> _logInfo;
            private readonly Action<string> _logWarning;
            private readonly Action<string> _logError;

            public ConversionPipeline(
                CsvLineParser csvParser,
                JsonStringEscaper jsonEscaper,
                TableNameExtractor tableExtractor,
                Action<string> logInfo,
                Action<string> logWarning,
                Action<string> logError)
            {
                _csvParser = csvParser ?? throw new ArgumentNullException(nameof(csvParser));
                _jsonEscaper = jsonEscaper ?? throw new ArgumentNullException(nameof(jsonEscaper));
                _tableExtractor = tableExtractor ?? throw new ArgumentNullException(nameof(tableExtractor));
                _logInfo = logInfo ?? throw new ArgumentNullException(nameof(logInfo));
                _logWarning = logWarning ?? throw new ArgumentNullException(nameof(logWarning));
                _logError = logError ?? throw new ArgumentNullException(nameof(logError));
            }

            public bool TryConvert(string csvPath, string outputPath, out int filesWritten)
            {
                filesWritten = 0;

                if (!TryReadCsvLines(csvPath, out var lines) ||
                    !TryParseLocales(lines[0], out var locales))
                    return false;

                var tables = BuildLocalizationTables(lines, locales);
                filesWritten = WriteJsonFiles(outputPath, tables);
                return true;
            }

            private bool TryReadCsvLines(string csvPath, out string[] lines)
            {
                lines = Array.Empty<string>();

                if (!File.Exists(csvPath))
                {
                    _logError($"CSV file not found: {csvPath}");
                    return false;
                }

                lines = File.ReadAllLines(csvPath, Encoding.UTF8);

                if (lines.Length >= 2)
                    return true;

                _logError("CSV file is empty or has no data rows");
                return false;
            }

            private bool TryParseLocales(string headerLine, out List<string> locales)
            {
                locales = new List<string>();
                var header = _csvParser.Parse(headerLine);

                if (header.Length < 2)
                {
                    _logError("Invalid CSV header. Expected at least: Key,Locale1,Locale2,...");
                    return false;
                }

                for (var index = 1; index < header.Length; index++)
                {
                    var locale = header[index].Trim();

                    if (locale.Equals("Context", StringComparison.OrdinalIgnoreCase))
                        break;

                    locales.Add(locale);
                }

                _logInfo($"Found {locales.Count} locales: {string.Join(", ", locales)}");
                return true;
            }

            private LocalizationTables BuildLocalizationTables(string[] lines, IReadOnlyList<string> locales)
            {
                var tables = new LocalizationTables(StringComparer.Ordinal);

                for (var lineIndex = 1; lineIndex < lines.Length; lineIndex++)
                {
                    var line = lines[lineIndex].Trim();

                    if (string.IsNullOrEmpty(line))
                        continue;

                    var values = _csvParser.Parse(line);

                    if (values.Length < 2)
                    {
                        _logWarning($"Line {lineIndex + 1}: Invalid format, skipping");
                        continue;
                    }

                    var key = values[0].Trim();

                    if (string.IsNullOrEmpty(key))
                        continue;

                    AddLocalizedEntries(tables, key, _tableExtractor.Extract(key), values, locales, lineIndex + 1);
                }

                return tables;
            }

            private void AddLocalizedEntries(
                LocalizationTables tables,
                string key,
                string tableName,
                string[] values,
                IReadOnlyList<string> locales,
                int sourceLineNumber)
            {
                for (var localeIndex = 0; localeIndex < locales.Count && localeIndex + 1 < values.Length; localeIndex++)
                {
                    var locale = locales[localeIndex];
                    var value = values[localeIndex + 1].Trim();

                    if (string.IsNullOrEmpty(value))
                    {
                        _logWarning($"Line {sourceLineNumber}: Empty value for key '{key}' in locale '{locale}'");
                        continue;
                    }

                    var tableEntries = GetOrCreateTableEntries(tables, locale, tableName);
                    tableEntries[key] = value;
                }
            }

            private static Dictionary<string, string> GetOrCreateTableEntries(
                LocalizationTables tables,
                string locale,
                string tableName)
            {
                if (!tables.TryGetValue(locale, out var localeTables))
                {
                    localeTables = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
                    tables[locale] = localeTables;
                }

                if (!localeTables.TryGetValue(tableName, out var tableEntries))
                {
                    tableEntries = new Dictionary<string, string>(StringComparer.Ordinal);
                    localeTables[tableName] = tableEntries;
                }

                return tableEntries;
            }

            private int WriteJsonFiles(string outputPath, LocalizationTables tables)
            {
                var filesWritten = 0;

                foreach (var locale in tables.Keys.OrderBy(locale => locale, StringComparer.Ordinal))
                {
                    var localeFolder = EnsureLocaleOutputDirectory(outputPath, locale);
                    var localeTables = tables[locale];

                    foreach (var tableName in localeTables.Keys.OrderBy(name => name, StringComparer.Ordinal))
                    {
                        var entries = localeTables[tableName];
                        var outputFile = Path.Combine(localeFolder, $"{tableName}.json");
                        var json = CreateJsonContent(locale, tableName, entries);

                        File.WriteAllText(outputFile, json, Encoding.UTF8);
                        filesWritten++;
                        _logInfo($"Written: {outputFile} ({entries.Count} entries)");
                    }
                }

                return filesWritten;
            }

            private string EnsureLocaleOutputDirectory(string outputPath, string locale)
            {
                var localeFolder = Path.Combine(
                    outputPath,
                    LocalizationEditorConventions.GetLanguageOnlyLocaleSegment(locale));

                if (Directory.Exists(localeFolder))
                    return localeFolder;

                Directory.CreateDirectory(localeFolder);
                _logInfo($"Created directory: {localeFolder}");
                return localeFolder;
            }

            private string CreateJsonContent(string locale, string tableName, Dictionary<string, string> entries)
            {
                var sb = new StringBuilder();
                sb.AppendLine("{");
                sb.AppendLine("  \"version\": \"1.0\",");
                sb.AppendLine($"  \"locale\": \"{locale}\",");
                sb.AppendLine($"  \"table\": \"{tableName}\",");
                sb.AppendLine("  \"entries\": {");

                var keys = new List<string>(entries.Keys);
                keys.Sort(StringComparer.Ordinal);

                for (var i = 0; i < keys.Count; i++)
                {
                    var key = keys[i];
                    var escapedValue = _jsonEscaper.Escape(entries[key]);
                    var comma = i < keys.Count - 1 ? "," : string.Empty;
                    sb.AppendLine($"    \"{key}\": \"{escapedValue}\"{comma}");
                }

                sb.AppendLine("  }");
                sb.AppendLine("}");

                return sb.ToString();
            }
        }
    }
}