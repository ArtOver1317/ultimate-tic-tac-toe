using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Editor.Localization
{
    internal static class LocalizationEditorConventions
    {
        internal const string LocalizationRootPath = "Assets/Content/Localization";
        internal const string LocalizationRootPathWithTrailingSlash = LocalizationRootPath + "/";
        internal const string SourceOfTruthCsvPath = LocalizationRootPath + "/Localization_SourceOfTruth.csv";
        internal const string PreferredReferenceLocale = "en";

        internal static string EnsureTrailingSlash(string path) =>
            string.IsNullOrEmpty(path) || path.EndsWith("/", StringComparison.Ordinal) ? path : $"{path}/";

        internal static string NormalizeAssetPath(string path) => path.Replace("\\", "/");

        internal static string MakeProjectRelativePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return path;

            var normalizedPath = NormalizeAssetPath(path);
            var projectRoot = GetProjectRootPath();

            return normalizedPath.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase)
                ? normalizedPath[(projectRoot.Length + 1)..]
                : normalizedPath;
        }

        internal static string GetAbsolutePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return path;

            var normalizedPath = NormalizeAssetPath(path);

            return Path.IsPathRooted(normalizedPath)
                ? normalizedPath
                : NormalizeAssetPath(Path.Combine(GetProjectRootPath(), normalizedPath));
        }

        internal static string[] GetLocaleDirectories(string localizationRootPath) =>
            Directory.GetDirectories(localizationRootPath)
                .Where(IsVisibleDirectory)
                .OrderBy(Path.GetFileName, StringComparer.Ordinal)
                .ToArray();

        internal static string[] GetLocalizationJsonFiles(string localeDirectoryPath) =>
            Directory.GetFiles(localeDirectoryPath, "*.json")
                .OrderBy(Path.GetFileName, StringComparer.Ordinal)
                .ToArray();

        internal static string GetLanguageOnlyLocaleSegment(string locale)
        {
            if (string.IsNullOrWhiteSpace(locale))
                return string.Empty;

            var normalizedLocale = locale.Replace("_", "-").Trim();
            var separatorIndex = normalizedLocale.IndexOf('-');

            return separatorIndex >= 0 ? normalizedLocale[..separatorIndex] : normalizedLocale;
        }

        internal static string GetLanguageOnlyLocaleToken(string locale) =>
            GetLanguageOnlyLocaleSegment(locale).ToLowerInvariant();

        internal static string BuildAddressablesGroupName(string locale) =>
            $"Localization_{GetLanguageOnlyLocaleToken(locale)}";

        internal static string BuildAddressablesAddress(string locale, string tableName) =>
            $"loc_{GetLanguageOnlyLocaleToken(locale)}_{tableName.Trim().ToLowerInvariant()}";

        private static bool IsVisibleDirectory(string directoryPath)
        {
            var directoryName = Path.GetFileName(directoryPath);
            return !string.IsNullOrEmpty(directoryName) && !directoryName.StartsWith(".", StringComparison.Ordinal);
        }

        private static string GetProjectRootPath() =>
            NormalizeAssetPath(Application.dataPath.Replace("/Assets", string.Empty).Replace("\\Assets", string.Empty));
    }
}