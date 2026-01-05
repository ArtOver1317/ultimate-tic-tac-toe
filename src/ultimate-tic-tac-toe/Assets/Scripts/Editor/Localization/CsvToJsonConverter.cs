using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Editor.Localization.Parsing;
using UnityEditor;
using UnityEngine;

namespace Editor.Localization
{
    public sealed class CsvToJsonConverter : EditorWindow
    {
        private const string _defaultCsvPath = "Assets/Content/Localization/Import.csv";
        private const string _defaultOutputPath = "Assets/Content/Localization/";

        private string _csvPath = _defaultCsvPath;
        private string _outputPath = _defaultOutputPath;
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
                var path = EditorUtility.OpenFilePanel("Select CSV File", "Assets/Content/Localization", "csv");
                
                if (!string.IsNullOrEmpty(path)) 
                    _csvPath = MakeRelativePath(path);
            }

            EditorGUILayout.Space();

            _outputPath = EditorGUILayout.TextField("Output Folder:", _outputPath);
            
            if (GUILayout.Button("Browse Folder...", GUILayout.Width(120)))
            {
                var path = EditorUtility.OpenFolderPanel("Select Output Folder", "Assets/Content/Localization", "");
                
                if (!string.IsNullOrEmpty(path)) 
                    _outputPath = MakeRelativePath(path) + "/";
            }

            EditorGUILayout.Space();

            GUI.enabled = !string.IsNullOrEmpty(_csvPath) && File.Exists(_csvPath);
            
            if (GUILayout.Button("Convert", GUILayout.Height(30))) 
                Convert();

            GUI.enabled = true;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Log:", EditorStyles.boldLabel);

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition, GUILayout.ExpandHeight(true));
            EditorGUILayout.TextArea(_logText, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        private void Convert()
        {
            _logText = "";
            Log($"Starting conversion: {_csvPath}");

            try
            {
                if (!File.Exists(_csvPath))
                {
                    LogError($"CSV file not found: {_csvPath}");
                    return;
                }

                var lines = File.ReadAllLines(_csvPath, Encoding.UTF8);
                
                if (lines.Length < 2)
                {
                    LogError("CSV file is empty or has no data rows");
                    return;
                }

                var header = _csvParser.Parse(lines[0]);
                
                if (header.Length < 2)
                {
                    LogError("Invalid CSV header. Expected at least: Key,Locale1,Locale2,...");
                    return;
                }

                var locales = new List<string>();
                
                for (var i = 1; i < header.Length; i++)
                {
                    var locale = header[i].Trim();
                    
                    if (locale.Equals("Context", StringComparison.OrdinalIgnoreCase)) 
                        break;

                    locales.Add(locale);
                }

                Log($"Found {locales.Count} locales: {string.Join(", ", locales)}");

                var tables = new Dictionary<string, Dictionary<string, Dictionary<string, string>>>();

                for (var lineIndex = 1; lineIndex < lines.Length; lineIndex++)
                {
                    var line = lines[lineIndex].Trim();
                    
                    if (string.IsNullOrEmpty(line)) 
                        continue;

                    var values = _csvParser.Parse(line);
                    
                    if (values.Length < 2)
                    {
                        LogWarning($"Line {lineIndex + 1}: Invalid format, skipping");
                        continue;
                    }

                    var key = values[0].Trim();
                    
                    if (string.IsNullOrEmpty(key)) 
                        continue;

                    var tableName = _tableExtractor.Extract(key);

                    for (var localeIndex = 0; localeIndex < locales.Count && localeIndex + 1 < values.Length; localeIndex++)
                    {
                        var locale = locales[localeIndex];
                        var value = values[localeIndex + 1].Trim();

                        if (string.IsNullOrEmpty(value))
                        {
                            LogWarning($"Line {lineIndex + 1}: Empty value for key '{key}' in locale '{locale}'");
                            continue;
                        }

                        if (!tables.ContainsKey(locale)) 
                            tables[locale] = new Dictionary<string, Dictionary<string, string>>();

                        if (!tables[locale].ContainsKey(tableName)) 
                            tables[locale][tableName] = new Dictionary<string, string>();

                        tables[locale][tableName][key] = value;
                    }
                }

                var filesWritten = 0;
                
                // Sort locales for deterministic output
                var sortedLocales = tables.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
                
                foreach (var locale in sortedLocales)
                {
                    var localeTables = tables[locale];

                    var localeCode = ConvertLocaleFormat(locale);
                    var localeFolder = Path.Combine(_outputPath, localeCode.Split('-')[0]);

                    if (!Directory.Exists(localeFolder))
                    {
                        Directory.CreateDirectory(localeFolder);
                        Log($"Created directory: {localeFolder}");
                    }

                    // Sort table names for deterministic output
                    var sortedTables = localeTables.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
                    
                    foreach (var tableName in sortedTables)
                    {
                        var entries = localeTables[tableName];

                        var json = CreateJsonContent(locale, tableName, entries);
                        var outputFile = Path.Combine(localeFolder, $"{tableName}.json");

                        File.WriteAllText(outputFile, json, Encoding.UTF8);
                        filesWritten++;
                        Log($"Written: {outputFile} ({entries.Count} entries)");
                    }
                }

                AssetDatabase.Refresh();
                Log($"Conversion complete! Written {filesWritten} files.");
                EditorUtility.DisplayDialog("Success", $"Conversion complete!\n{filesWritten} files written.", "OK");
            }
            catch (Exception ex)
            {
                LogError($"Conversion failed: {ex.Message}");
                EditorUtility.DisplayDialog("Error", $"Conversion failed:\n{ex.Message}", "OK");
            }
        }

        private string CreateJsonContent(string locale, string tableName, Dictionary<string, string> entries)
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"version\": \"1.0\",");
            sb.AppendLine($"  \"locale\": \"{locale}\",");
            sb.AppendLine($"  \"table\": \"{tableName}\",");
            sb.AppendLine("  \"entries\": {");

            // Sort keys for deterministic output
            var keys = new List<string>(entries.Keys);
            keys.Sort(StringComparer.Ordinal);

            for (var i = 0; i < keys.Count; i++)
            {
                var key = keys[i];
                var value = entries[key];
                var escapedValue = _jsonEscaper.Escape(value);
                var comma = i < keys.Count - 1 ? "," : "";
                sb.AppendLine($"    \"{key}\": \"{escapedValue}\"{comma}");
            }

            sb.AppendLine("  }");
            sb.AppendLine("}");

            return sb.ToString();
        }

        private string ConvertLocaleFormat(string locale) => locale.Replace("_", "-");

        private string MakeRelativePath(string absolutePath)
        {
            var projectPath = Application.dataPath.Replace("/Assets", "").Replace("\\Assets", "");
            return absolutePath.StartsWith(projectPath) ? absolutePath[(projectPath.Length + 1)..].Replace("\\", "/") : absolutePath;
        }

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
    }
}
