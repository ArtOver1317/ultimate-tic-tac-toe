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
    public sealed class LocalizationValidator : EditorWindow
    {
        private const string _localizationRootPath = "Assets/Content/Localization";

        private Vector2 _scrollPosition;
        private ValidationReport _report;

        // Extracted core logic classes
        private readonly JsonLocalizationKeyParser _keyParser = new();
        private readonly LocalizationConsistencyValidator _consistencyValidator = new();

        [MenuItem("Tools/Localization/Content Management/Validate Keys")]
        private static void ShowWindow()
        {
            var window = GetWindow<LocalizationValidator>("Localization Validator");
            window.minSize = new Vector2(600, 500);
            window.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Localization Validator", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            EditorGUILayout.HelpBox(
                "Validates that all locales have the same keys and no missing translations.",
                MessageType.Info);

            EditorGUILayout.Space();

            if (GUILayout.Button("Validate", GUILayout.Height(30))) 
                Validate();

            if (_report != null)
            {
                EditorGUILayout.Space();
                DrawReport();
            }
        }

        private void Validate()
        {
            _report = new ValidationReport();

            if (!Directory.Exists(_localizationRootPath))
            {
                _report.Errors.Add($"Localization root directory not found: {_localizationRootPath}");
                return;
            }

            var localeDirectories = Directory.GetDirectories(_localizationRootPath)
                .Where(d => !Path.GetFileName(d).StartsWith("."))
                .ToArray();

            if (localeDirectories.Length == 0)
            {
                _report.Errors.Add($"No locale directories found in {_localizationRootPath}");
                return;
            }

            _report.FoundLocales = localeDirectories.Select(Path.GetFileName).ToList();

            var allTables = new Dictionary<string, Dictionary<string, HashSet<string>>>();

            foreach (var localeDir in localeDirectories)
            {
                var locale = Path.GetFileName(localeDir);
                var jsonFiles = Directory.GetFiles(localeDir, "*.json");

                if (jsonFiles.Length == 0)
                {
                    _report.Warnings.Add($"Locale '{locale}' has no JSON files");
                    continue;
                }

                foreach (var jsonFile in jsonFiles)
                {
                    var tableName = Path.GetFileNameWithoutExtension(jsonFile);

                    try
                    {
                        var json = File.ReadAllText(jsonFile, Encoding.UTF8);
                        var keys = _keyParser.ParseKeys(json);

                        if (keys == null)
                        {
                            _report.Errors.Add($"Invalid JSON format in {jsonFile}");
                            continue;
                        }

                        if (!allTables.ContainsKey(tableName)) 
                            allTables[tableName] = new Dictionary<string, HashSet<string>>();

                        if (!allTables[tableName].ContainsKey(locale)) 
                            allTables[tableName][locale] = new HashSet<string>();

                        foreach (var key in keys)
                        {
                            allTables[tableName][locale].Add(key);
                        }

                        _report.ProcessedFiles++;
                    }
                    catch (Exception ex)
                    {
                        _report.Errors.Add($"Failed to parse {jsonFile}: {ex.Message}");
                    }
                }
            }

            var validationResult = _consistencyValidator.Validate(allTables, _report.FoundLocales);
            _report.TotalKeyCount = validationResult.TotalKeyCount;
            _report.Warnings.AddRange(validationResult.Warnings);
            
            _report.MissingKeys.AddRange(validationResult.MissingKeys.Select(mk => new MissingKeyInfo
            {
                Locale = mk.Locale,
                Table = mk.Table,
                Keys = mk.Keys,
            }));

            // Option A contract: missing keys = error (blocks release)
            _report.IsValid = _report.Errors.Count == 0 && _report.MissingKeys.Count == 0;
            Repaint();
        }

        private void DrawReport()
        {
            EditorGUILayout.LabelField("Validation Report", EditorStyles.boldLabel);

            if (_report.IsValid)
                EditorGUILayout.HelpBox("✓ All validations passed!", MessageType.Info);
            else
                EditorGUILayout.HelpBox($"✗ Validation failed with {_report.Errors.Count} errors", MessageType.Error);

            EditorGUILayout.Space();

            EditorGUILayout.LabelField($"Locales found: {string.Join(", ", _report.FoundLocales)}");
            EditorGUILayout.LabelField($"Files processed: {_report.ProcessedFiles}");
            EditorGUILayout.LabelField($"Total unique keys: {_report.TotalKeyCount}");

            EditorGUILayout.Space();

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition, GUILayout.ExpandHeight(true));

            if (_report.Errors.Count > 0)
            {
                EditorGUILayout.LabelField("Errors:", EditorStyles.boldLabel);
                
                foreach (var error in _report.Errors)
                {
                    EditorGUILayout.HelpBox(error, MessageType.Error);
                }
                
                EditorGUILayout.Space();
            }

            if (_report.MissingKeys.Count > 0)
            {
                EditorGUILayout.LabelField("Missing Keys:", EditorStyles.boldLabel);
                
                foreach (var missing in _report.MissingKeys)
                {
                    var message = $"{missing.Locale}/{missing.Table}: {missing.Keys.Count} missing keys\n" +
                                  string.Join("\n", missing.Keys.Select(k => $"  - {k}"));
                    
                    EditorGUILayout.HelpBox(message, MessageType.Warning);
                }
                
                EditorGUILayout.Space();
            }

            if (_report.Warnings.Count > 0)
            {
                EditorGUILayout.LabelField("Warnings:", EditorStyles.boldLabel);
                
                foreach (var warning in _report.Warnings)
                {
                    EditorGUILayout.HelpBox(warning, MessageType.Warning);
                }
            }

            EditorGUILayout.EndScrollView();
        }

        private class ValidationReport
        {
            public bool IsValid;
            public List<string> FoundLocales = new();
            public int ProcessedFiles;
            public int TotalKeyCount;
            public readonly List<string> Errors = new();
            public readonly List<string> Warnings = new();
            public readonly List<MissingKeyInfo> MissingKeys = new();
        }

        private class MissingKeyInfo
        {
            public string Locale;
            public string Table;
            public List<string> Keys;
        }
    }
}