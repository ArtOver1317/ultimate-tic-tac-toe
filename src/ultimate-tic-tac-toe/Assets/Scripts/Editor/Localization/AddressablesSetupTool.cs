using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace Editor.Localization
{
    public static class AddressablesSetupTool
    {
        private const string _localizationRootPath = "Assets/Content/Localization";

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

            if (!Directory.Exists(_localizationRootPath))
            {
                EditorUtility.DisplayDialog("Error", $"Localization directory not found: {_localizationRootPath}", "OK");
                return;
            }

            var localeDirectories = Directory.GetDirectories(_localizationRootPath)
                .Where(d => !Path.GetFileName(d).StartsWith("."))
                .ToArray();

            if (localeDirectories.Length == 0)
            {
                EditorUtility.DisplayDialog("Error", $"No locale directories found in {_localizationRootPath}", "OK");
                return;
            }

            var groupsCreated = 0;
            var assetsAdded = 0;

            foreach (var localeDir in localeDirectories)
            {
                var locale = Path.GetFileName(localeDir).ToLowerInvariant();
                var groupName = $"Localization_{locale}";

                var group = settings.FindGroup(groupName);
                
                if (group == null)
                {
                    // Create group first, then add schemas
                    group = settings.CreateGroup(groupName, false, false, true, null);
                    
                    // Copy schemas from default group or add standard ones
                    var defaultGroup = settings.DefaultGroup;
                    
                    if (defaultGroup != null && defaultGroup.Schemas.Count > 0)
                    {
                        foreach (var schema in defaultGroup.Schemas)
                        {
                            var schemaType = schema.GetType();
                            group.AddSchema(schemaType);
                        }
                    }
                    else
                    {
                        // Add standard schemas
                        group.AddSchema<UnityEditor.AddressableAssets.Settings.GroupSchemas.BundledAssetGroupSchema>();
                        group.AddSchema<UnityEditor.AddressableAssets.Settings.GroupSchemas.ContentUpdateGroupSchema>();
                    }
                    
                    groupsCreated++;
                    Debug.Log($"Created Addressables group: {groupName} with {group.Schemas.Count} schemas");
                }

                var jsonFiles = Directory.GetFiles(localeDir, "*.json");
                
                foreach (var jsonFile in jsonFiles)
                {
                    var assetPath = jsonFile.Replace("\\", "/");
                    var guid = AssetDatabase.AssetPathToGUID(assetPath);

                    if (string.IsNullOrEmpty(guid))
                    {
                        Debug.LogWarning($"Could not find GUID for: {assetPath}");
                        continue;
                    }

                    var tableName = Path.GetFileNameWithoutExtension(jsonFile).ToLowerInvariant();
                    var address = $"loc_{locale}_{tableName}";

                    var entry = settings.CreateOrMoveEntry(guid, group, false, false);
                    
                    if (entry != null)
                    {
                        entry.address = address;
                        assetsAdded++;
                        Debug.Log($"Added: {assetPath} → {address}");
                    }
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

            if (!Directory.Exists(_localizationRootPath))
            {
                EditorUtility.DisplayDialog("Error", $"Localization directory not found: {_localizationRootPath}", "OK");
                return;
            }

            var issues = new System.Text.StringBuilder();
            var issueCount = 0;

            var localeDirectories = Directory.GetDirectories(_localizationRootPath)
                .Where(d => !Path.GetFileName(d).StartsWith("."))
                .ToArray();

            foreach (var localeDir in localeDirectories)
            {
                var locale = Path.GetFileName(localeDir).ToLowerInvariant();
                var groupName = $"Localization_{locale}";

                var group = settings.FindGroup(groupName);
                
                if (group == null)
                {
                    issues.AppendLine($"✗ Missing group: {groupName}");
                    issueCount++;
                    continue;
                }

                var jsonFiles = Directory.GetFiles(localeDir, "*.json");
                
                foreach (var jsonFile in jsonFiles)
                {
                    var assetPath = jsonFile.Replace("\\", "/");
                    var guid = AssetDatabase.AssetPathToGUID(assetPath);

                    if (string.IsNullOrEmpty(guid))
                    {
                        issues.AppendLine($"✗ Asset not found: {assetPath}");
                        issueCount++;
                        continue;
                    }

                    var entry = settings.FindAssetEntry(guid);
                    
                    if (entry == null)
                    {
                        issues.AppendLine($"✗ Not in Addressables: {assetPath}");
                        issueCount++;
                        continue;
                    }

                    var tableName = Path.GetFileNameWithoutExtension(jsonFile).ToLowerInvariant();
                    var expectedAddress = $"loc_{locale}_{tableName}";

                    if (entry.address != expectedAddress)
                    {
                        issues.AppendLine($"✗ Wrong address: {assetPath}");
                        issues.AppendLine($"  Expected: {expectedAddress}");
                        issues.AppendLine($"  Actual: {entry.address}");
                        issueCount++;
                    }
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
    }
}