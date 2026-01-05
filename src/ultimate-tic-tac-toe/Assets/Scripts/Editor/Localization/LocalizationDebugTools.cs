using Runtime.Localization;
using UnityEditor;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Editor.Localization
{
    public static class LocalizationDebugTools
    {
        [MenuItem("Tools/Localization/Debug/Switch to English")]
        private static void SwitchToEnglish() => SwitchLocale(LocaleId.EnglishUs, "English");

        [MenuItem("Tools/Localization/Debug/Switch to Russian")]
        private static void SwitchToRussian() => SwitchLocale(LocaleId.Russian, "Russian");

        [MenuItem("Tools/Localization/Debug/Switch to Japanese")]
        private static void SwitchToJapanese() => SwitchLocale(LocaleId.Japanese, "Japanese");

        [MenuItem("Tools/Localization/Debug/Show Current Locale")]
        private static void ShowCurrentLocale()
        {
            if (!Application.isPlaying)
            {
                EditorUtility.DisplayDialog("Error", "This tool only works in Play Mode", "OK");
                return;
            }

            var service = GetLocalizationService();
            
            if (service != null)
            {
                var currentLocale = service.CurrentLocale.CurrentValue;
                EditorUtility.DisplayDialog("Current Locale", $"Current locale: {currentLocale.Code}", "OK");
            }
        }

        private static void SwitchLocale(LocaleId locale, string localeName)
        {
            if (!Application.isPlaying)
            {
                EditorUtility.DisplayDialog("Error", "This tool only works in Play Mode.\n\nStart the game first!", "OK");
                return;
            }

            var service = GetLocalizationService();
            
            if (service == null)
            {
                EditorUtility.DisplayDialog("Error", "Localization service not found.\n\nMake sure the game is initialized.", "OK");
                return;
            }

            Debug.Log($"[LocalizationDebugTools] Switching to {localeName} ({locale.Code})...");
            
            SwitchLocaleAsync(service, locale, localeName).Forget();
        }

        private static async Cysharp.Threading.Tasks.UniTaskVoid SwitchLocaleAsync(ILocalizationService service, LocaleId locale, string localeName)
        {
            try
            {
                await service.SetLocaleAsync(locale, System.Threading.CancellationToken.None);
                Debug.Log($"[LocalizationDebugTools] Successfully switched to {localeName}!");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[LocalizationDebugTools] Failed to switch to {localeName}: {ex.Message}");
            }
        }

        private static ILocalizationService GetLocalizationService()
        {
            var lifetimeScope = Object.FindAnyObjectByType<LifetimeScope>();
            
            if (lifetimeScope == null)
            {
                Debug.LogError("[LocalizationDebugTools] No LifetimeScope found in scene");
                return null;
            }

            try
            {
                return lifetimeScope.Container.Resolve<ILocalizationService>();
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[LocalizationDebugTools] Failed to resolve ILocalizationService: {ex.Message}");
                return null;
            }
        }
    }
}