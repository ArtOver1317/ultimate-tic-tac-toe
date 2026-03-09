using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace Editor.Localization
{
    public static class AddressablesSetupTool
    {
        [MenuItem("Tools/Localization/Addressables/Setup Addressables")]
        private static void SetupAddressables()
        {
            if (!EditorUtility.DisplayDialog(
                    "Setup Addressables",
                    "This will create Addressables groups for all locales and assign addresses to JSON files.\n\n" +
                    "Continue?",
                    "Yes",
                    "Cancel"))
                return;

            var settings = AddressableAssetSettingsDefaultObject.Settings;
            
            if (settings == null)
            {
                EditorUtility.DisplayDialog("Error", "Addressables is not initialized. Please create Addressables settings first.", "OK");
                return;
            }

            if (!TryGetLocaleDirectories(out var localeDirectories))
                return;

            var groupsCreated = 0;
            var assetsAdded = 0;

            foreach (var localeDir in localeDirectories)
            {
                var locale = LocalizationEditorConventions.GetLanguageOnlyLocaleToken(Path.GetFileName(localeDir));
                var group = GetOrCreateLocaleGroup(settings, locale, ref groupsCreated);

                foreach (var jsonAsset in GetLocaleJsonAssets(localeDir))
                {
                    AddLocaleAsset(settings, group, jsonAsset, ref assetsAdded);
                }
            }

            settings.SetDirty(AddressableAssetSettings.ModificationEvent.BatchModification, null, true, true);
            AssetDatabase.SaveAssets();

            EditorUtility.DisplayDialog(
                "Success",
                $"Addressables setup complete!\n\n" +
                $"Groups created: {groupsCreated}\n" +
                $"Assets added: {assetsAdded}\n\n" +
                "Don't forget to build Addressables:\n" +
                "Window → Asset Management → Addressables → Groups → Build → New Build",
                "OK");
        }

        [MenuItem("Tools/Localization/Addressables/Validate Setup")]
        private static void ValidateAddressablesSetup()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            
            if (settings == null)
            {
                EditorUtility.DisplayDialog("Error", "Addressables is not initialized.", "OK");
                return;
            }

            var issues = new StringBuilder();
            var issueCount = 0;

            if (!TryGetLocaleDirectories(out var localeDirectories))
                return;

            foreach (var localeDir in localeDirectories)
            {
                var locale = LocalizationEditorConventions.GetLanguageOnlyLocaleToken(Path.GetFileName(localeDir));
                var groupName = LocalizationEditorConventions.BuildAddressablesGroupName(locale);

                var group = settings.FindGroup(groupName);
                
                if (group == null)
                {
                    issues.AppendLine($"✗ Missing group: {groupName}");
                    issueCount++;
                    continue;
                }

                foreach (var jsonAsset in GetLocaleJsonAssets(localeDir))
                {
                    ValidateLocaleAsset(settings, jsonAsset, issues, ref issueCount);
                }
            }

            if (issueCount == 0)
                EditorUtility.DisplayDialog("Validation Success", "✓ All Addressables are set up correctly!", "OK");
            else
            {
                EditorUtility.DisplayDialog(
                    "Validation Failed",
                    $"Found {issueCount} issues:\n\n{issues}",
                    "OK");
            }
        }

        private static bool TryGetLocaleDirectories(out string[] localeDirectories)
        {
            localeDirectories = Array.Empty<string>();

            if (!Directory.Exists(LocalizationEditorConventions.LocalizationRootPath))
            {
                EditorUtility.DisplayDialog(
                    "Error",
                    $"Localization directory not found: {LocalizationEditorConventions.LocalizationRootPath}",
                    "OK");

                return false;
            }

            localeDirectories = LocalizationEditorConventions.GetLocaleDirectories(
                LocalizationEditorConventions.LocalizationRootPath);

            if (localeDirectories.Length > 0)
                return true;

            EditorUtility.DisplayDialog(
                "Error",
                $"No locale directories found in {LocalizationEditorConventions.LocalizationRootPath}",
                "OK");

            return false;
        }

        private static AddressableAssetGroup GetOrCreateLocaleGroup(
            AddressableAssetSettings settings,
            string locale,
            ref int groupsCreated)
        {
            var groupName = LocalizationEditorConventions.BuildAddressablesGroupName(locale);
            var group = settings.FindGroup(groupName);

            if (group != null)
                return group;

            group = settings.CreateGroup(groupName, false, false, true, null);
            CopySchemas(settings, group);
            groupsCreated++;
            Debug.Log($"Created Addressables group: {groupName} with {group.Schemas.Count} schemas");
            return group;
        }

        private static void CopySchemas(AddressableAssetSettings settings, AddressableAssetGroup group)
        {
            var defaultGroup = settings.DefaultGroup;

            if (defaultGroup != null && defaultGroup.Schemas.Count > 0)
            {
                foreach (var schema in defaultGroup.Schemas)
                {
                    group.AddSchema(schema.GetType());
                }

                return;
            }

            group.AddSchema<UnityEditor.AddressableAssets.Settings.GroupSchemas.BundledAssetGroupSchema>();
            group.AddSchema<UnityEditor.AddressableAssets.Settings.GroupSchemas.ContentUpdateGroupSchema>();
        }

        private static LocalizationJsonAsset[] GetLocaleJsonAssets(string localeDirectory)
        {
            var locale = LocalizationEditorConventions.GetLanguageOnlyLocaleToken(Path.GetFileName(localeDirectory));

            return LocalizationEditorConventions.GetLocalizationJsonFiles(localeDirectory)
                .Select(jsonFile => new LocalizationJsonAsset(locale, jsonFile))
                .ToArray();
        }

        private static void AddLocaleAsset(
            AddressableAssetSettings settings,
            AddressableAssetGroup group,
            LocalizationJsonAsset jsonAsset,
            ref int assetsAdded)
        {
            var guid = AssetDatabase.AssetPathToGUID(jsonAsset.AssetPath);

            if (string.IsNullOrEmpty(guid))
            {
                Debug.LogWarning($"Could not find GUID for: {jsonAsset.AssetPath}");
                return;
            }

            var entry = settings.CreateOrMoveEntry(guid, group, false, false);

            if (entry == null)
                return;

            entry.address = jsonAsset.Address;
            assetsAdded++;
            Debug.Log($"Added: {jsonAsset.AssetPath} → {jsonAsset.Address}");
        }

        private static void ValidateLocaleAsset(
            AddressableAssetSettings settings,
            LocalizationJsonAsset jsonAsset,
            StringBuilder issues,
            ref int issueCount)
        {
            var guid = AssetDatabase.AssetPathToGUID(jsonAsset.AssetPath);

            if (string.IsNullOrEmpty(guid))
            {
                issues.AppendLine($"✗ Asset not found: {jsonAsset.AssetPath}");
                issueCount++;
                return;
            }

            var entry = settings.FindAssetEntry(guid);

            if (entry == null)
            {
                issues.AppendLine($"✗ Not in Addressables: {jsonAsset.AssetPath}");
                issueCount++;
                return;
            }

            if (entry.address == jsonAsset.Address)
                return;

            issues.AppendLine($"✗ Wrong address: {jsonAsset.AssetPath}");
            issues.AppendLine($"  Expected: {jsonAsset.Address}");
            issues.AppendLine($"  Actual: {entry.address}");
            issueCount++;
        }

        private readonly struct LocalizationJsonAsset
        {
            public LocalizationJsonAsset(string locale, string jsonFile)
            {
                AssetPath = LocalizationEditorConventions.MakeProjectRelativePath(jsonFile);

                Address = LocalizationEditorConventions.BuildAddressablesAddress(
                    locale,
                    Path.GetFileNameWithoutExtension(jsonFile));
            }

            public string AssetPath { get; }
            public string Address { get; }
        }
    }
}