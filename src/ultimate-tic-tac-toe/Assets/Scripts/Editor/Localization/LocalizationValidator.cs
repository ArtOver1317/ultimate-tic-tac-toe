using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Editor.Localization.Parsing;
using UnityEditor;
using UnityEngine;
using MissingKeyInfo = Editor.Localization.Parsing.LocalizationConsistencyValidator.MissingKeyInfo;

namespace Editor.Localization
{
    public sealed class LocalizationValidator : EditorWindow
    {
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
            _report = CreateValidationReport();
            Repaint();
        }

        private ValidationReport CreateValidationReport()
        {
            var report = new ValidationReport();

            if (!Directory.Exists(LocalizationEditorConventions.LocalizationRootPath))
            {
                report.Errors.Add(
                    $"Localization root directory not found: {LocalizationEditorConventions.LocalizationRootPath}");

                return report;
            }

            var localeDirectories = LocalizationEditorConventions.GetLocaleDirectories(
                LocalizationEditorConventions.LocalizationRootPath);

            if (localeDirectories.Length == 0)
            {
                report.Errors.Add(
                    $"No locale directories found in {LocalizationEditorConventions.LocalizationRootPath}");

                return report;
            }

            report.FoundLocales.AddRange(localeDirectories.Select(Path.GetFileName));
            var allTables = CollectLocalizationTables(report, localeDirectories);
            ApplyConsistencyValidation(report, allTables);

            // Option A contract: missing keys = error (blocks release)
            report.IsValid = report.Errors.Count == 0 && report.MissingKeys.Count == 0;
            return report;
        }

        private Dictionary<string, Dictionary<string, HashSet<string>>> CollectLocalizationTables(
            ValidationReport report,
            IReadOnlyList<string> localeDirectories)
        {
            var allTables = new Dictionary<string, Dictionary<string, HashSet<string>>>(StringComparer.Ordinal);

            foreach (var localeDirectory in localeDirectories)
            {
                CollectLocaleTables(report, allTables, localeDirectory);
            }

            return allTables;
        }

        private void CollectLocaleTables(
            ValidationReport report,
            Dictionary<string, Dictionary<string, HashSet<string>>> allTables,
            string localeDirectory)
        {
            var locale = Path.GetFileName(localeDirectory);
            var jsonFiles = LocalizationEditorConventions.GetLocalizationJsonFiles(localeDirectory);

            if (jsonFiles.Length == 0)
            {
                report.Warnings.Add($"Locale '{locale}' has no JSON files");
                return;
            }

            foreach (var jsonFile in jsonFiles)
            {
                CollectTableKeys(report, allTables, locale, jsonFile);
            }
        }

        private void CollectTableKeys(
            ValidationReport report,
            Dictionary<string, Dictionary<string, HashSet<string>>> allTables,
            string locale,
            string jsonFile)
        {
            var tableName = Path.GetFileNameWithoutExtension(jsonFile);

            try
            {
                var json = File.ReadAllText(jsonFile, Encoding.UTF8);
                var keys = _keyParser.ParseKeys(json);

                if (keys == null)
                {
                    report.Errors.Add($"Invalid JSON format in {jsonFile}");
                    return;
                }

                var localeKeys = GetOrCreateLocaleKeys(allTables, tableName, locale);

                foreach (var key in keys)
                {
                    localeKeys.Add(key);
                }

                report.ProcessedFiles++;
            }
            catch (Exception ex)
            {
                report.Errors.Add($"Failed to parse {jsonFile}: {ex.Message}");
            }
        }

        private HashSet<string> GetOrCreateLocaleKeys(
            Dictionary<string, Dictionary<string, HashSet<string>>> allTables,
            string tableName,
            string locale)
        {
            if (!allTables.TryGetValue(tableName, out var tableLocales))
            {
                tableLocales = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
                allTables[tableName] = tableLocales;
            }

            if (!tableLocales.TryGetValue(locale, out var localeKeys))
            {
                localeKeys = new HashSet<string>(StringComparer.Ordinal);
                tableLocales[locale] = localeKeys;
            }

            return localeKeys;
        }

        private void ApplyConsistencyValidation(
            ValidationReport report,
            Dictionary<string, Dictionary<string, HashSet<string>>> allTables)
        {
            var validationResult = _consistencyValidator.Validate(allTables, report.FoundLocales);
            report.TotalKeyCount = validationResult.TotalKeyCount;
            report.Warnings.AddRange(validationResult.Warnings);
            report.MissingKeys.AddRange(validationResult.MissingKeys);
        }

        private void DrawReport()
        {
            EditorGUILayout.LabelField("Validation Report", EditorStyles.boldLabel);

            if (_report.IsValid)
                EditorGUILayout.HelpBox("✓ All validations passed!", MessageType.Info);
            else
                EditorGUILayout.HelpBox(BuildFailureSummaryMessage(), MessageType.Error);

            EditorGUILayout.Space();

            EditorGUILayout.LabelField($"Locales found: {string.Join(", ", _report.FoundLocales)}");
            EditorGUILayout.LabelField($"Files processed: {_report.ProcessedFiles}");
            EditorGUILayout.LabelField($"Total unique keys: {_report.TotalKeyCount}");

            EditorGUILayout.Space();

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition, GUILayout.ExpandHeight(true));

            DrawErrors();
            DrawMissingKeys();
            DrawWarnings();

            EditorGUILayout.EndScrollView();
        }

        private void DrawErrors()
        {
            if (_report.Errors.Count == 0)
                return;

            EditorGUILayout.LabelField("Errors:", EditorStyles.boldLabel);

            foreach (var error in _report.Errors)
            {
                EditorGUILayout.HelpBox(error, MessageType.Error);
            }

            EditorGUILayout.Space();
        }

        private void DrawMissingKeys()
        {
            if (_report.MissingKeys.Count == 0)
                return;

            EditorGUILayout.LabelField("Missing Keys:", EditorStyles.boldLabel);

            foreach (var missing in _report.MissingKeys)
            {
                EditorGUILayout.HelpBox(BuildMissingKeysMessage(missing), MessageType.Warning);
            }

            EditorGUILayout.Space();
        }

        private void DrawWarnings()
        {
            if (_report.Warnings.Count == 0)
                return;

            EditorGUILayout.LabelField("Warnings:", EditorStyles.boldLabel);

            foreach (var warning in _report.Warnings)
            {
                EditorGUILayout.HelpBox(warning, MessageType.Warning);
            }
        }

        private static string BuildMissingKeysMessage(MissingKeyInfo missingKeys) =>
            $"{missingKeys.Locale}/{missingKeys.Table}: {missingKeys.Keys.Count} missing keys\n" +
            string.Join("\n", missingKeys.Keys.Select(key => $"  - {key}"));

        private string BuildFailureSummaryMessage()
        {
            if (_report.Errors.Count > 0 && _report.MissingKeys.Count > 0) 
                return $"✗ Validation failed with {_report.Errors.Count} errors and {_report.MissingKeys.Count} missing key groups";

            return _report.Errors.Count > 0 
                ? $"✗ Validation failed with {_report.Errors.Count} errors" 
                : $"✗ Validation failed with {_report.MissingKeys.Count} missing key groups";
        }

        private class ValidationReport
        {
            public bool IsValid { get; set; }
            public List<string> FoundLocales { get; } = new();
            public int ProcessedFiles { get; set; }
            public int TotalKeyCount { get; set; }
            public List<string> Errors { get; } = new();
            public List<string> Warnings { get; } = new();
            public List<MissingKeyInfo> MissingKeys { get; } = new();
        }
    }
}