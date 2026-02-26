using System.IO;
using UnityEditor;
using UnityEngine;

namespace Editor.Save
{
    internal static class SaveEditorMenu
    {
        private const string MenuPath = "Tools/Save/Open Save File Location";
        private const string WebGlSaveKey = "SaveSystem.Data";
        private const string SaveFileName = "save.dat";

        [MenuItem(MenuPath)]
        private static void OpenSaveFileLocation()
        {
            if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.WebGL)
            {
                EditorUtility.DisplayDialog(
                    "Save System",
                    $"WebGL uses PlayerPrefs key: {WebGlSaveKey}",
                    "OK");
                return;
            }

            var savePath = Path.Combine(Application.persistentDataPath, SaveFileName);
            EditorUtility.RevealInFinder(savePath);
        }
    }
}