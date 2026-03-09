#nullable enable

using System;
using UnityEngine;

namespace Runtime.GameModes.Wizard.Matchmaking
{
    [CreateAssetMenu(
        fileName = "MatchmakingConfig",
        menuName = "Game/Wizard/Matchmaking Config",
        order = 200)]
    public sealed class MatchmakingConfigAsset : ScriptableObject, IMatchmakingConfig
    {
        [SerializeField] private float SearchTimeoutSeconds = 60f;
        [SerializeField] private float CancelAckTimeoutSeconds = 15f;

        public TimeSpan SearchTimeout => TimeSpan.FromSeconds(Mathf.Max(1f, SearchTimeoutSeconds));
        public TimeSpan CancelAckTimeout => TimeSpan.FromSeconds(Mathf.Max(1f, CancelAckTimeoutSeconds));

        public static MatchmakingConfigAsset CreateRuntimeDefault()
        {
            var asset = CreateInstance<MatchmakingConfigAsset>();
            asset.SearchTimeoutSeconds = 60f;
            asset.CancelAckTimeoutSeconds = 15f;
            return asset;
        }
    }
}
