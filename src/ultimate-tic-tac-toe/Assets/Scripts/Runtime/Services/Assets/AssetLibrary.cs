using UnityEngine;
using UnityEngine.AddressableAssets;

namespace Runtime.Services.Assets
{
    [CreateAssetMenu(fileName = "AssetLibrary", menuName = "Game/AssetLibrary")]
    public class AssetLibrary : ScriptableObject
    {
        [Header("UI")] public AssetReferenceGameObject MainMenuPrefab;
        public AssetReferenceGameObject SettingsPrefab;
        public AssetReferenceGameObject LanguageSelectionPrefab;
        public AssetReferenceGameObject GameBoardPrefab;

        [Header("Gameplay")] public AssetReferenceGameObject X_MarkPrefab;
        public AssetReferenceGameObject O_MarkPrefab;
    }
}
