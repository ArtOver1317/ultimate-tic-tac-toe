using UnityEngine;
using UnityEngine.AddressableAssets;

namespace Runtime.Services.Assets
{
    [CreateAssetMenu(fileName = "AssetLibrary", menuName = "Game/AssetLibrary")]
    public class AssetLibrary : ScriptableObject
    {
        [Header("UI")] public AssetReferenceGameObject MainMenuPrefab;
        public AssetReferenceGameObject BackgroundPrefab;
        public AssetReferenceGameObject PlayerStatisticsPrefab;
        public AssetReferenceGameObject SettingsPrefab;
        public AssetReferenceGameObject LanguageSelectionPrefab;
        public AssetReferenceGameObject PlayerNameEditPrefab;
        
        [Header("Wizard")] public AssetReferenceGameObject ModeSelectionPrefab;
        
        public AssetReferenceGameObject MatchSetupPrefab;
        public AssetReferenceGameObject MatchmakingPrefab;
    }
}