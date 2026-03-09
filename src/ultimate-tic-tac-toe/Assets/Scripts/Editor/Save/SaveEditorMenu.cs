using System.IO;
using UnityEditor;
using UnityEngine;

namespace Editor.Save
{
    internal static class SaveEditorMenu
    {
        private const string _menuPath = "Tools/Save/Open Save File Location";
        private const string _webGlSaveKey = "SaveSystem.Data";
        private const string _saveFileName = "save.dat";

        [MenuItem(_menuPath)]
        private static void OpenSaveFileLocation()
        {
            if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.WebGL)
            {
                EditorUtility.DisplayDialog(
                    "Save System",
                    $"WebGL uses PlayerPrefs key: {_webGlSaveKey}",
                    "OK");
                
                return;
            }

            var savePath = Path.Combine(Application.persistentDataPath, _saveFileName);
            EditorUtility.RevealInFinder(savePath);
        }
    }
}