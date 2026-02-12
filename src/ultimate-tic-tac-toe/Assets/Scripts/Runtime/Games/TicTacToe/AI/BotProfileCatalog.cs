#nullable enable

using System;
using UnityEngine;

namespace Runtime.Games.TicTacToe.AI
{
    /// <summary>
    /// ScriptableObject catalog of bot profiles, keyed by DifficultyId.
    /// Registered in DI as <see cref="IBotProfileCatalog"/> via RegisterInstance.
    /// </summary>
    [CreateAssetMenu(fileName = "BotProfileCatalog", menuName = "TicTacToe/AI/Bot Profile Catalog")]
    public sealed class BotProfileCatalog : ScriptableObject, IBotProfileCatalog
    {
        [SerializeField] private BotProfile[] Profiles = Array.Empty<BotProfile>();

        public bool TryGet(string difficultyId, out BotProfile? profile)
        {
            if (string.IsNullOrEmpty(difficultyId))
            {
                profile = null;
                return false;
            }

            for (int i = 0; i < Profiles.Length; i++)
            {
                if (Profiles[i] != null &&
                    string.Equals(Profiles[i].Id, difficultyId, StringComparison.OrdinalIgnoreCase))
                {
                    profile = Profiles[i];
                    return true;
                }
            }

            profile = null;
            return false;
        }
    }

    /// <summary>
    /// NullObject fallback — always returns false. Used when no catalog asset is assigned.
    /// </summary>
    internal sealed class EmptyBotProfileCatalog : IBotProfileCatalog
    {
        public bool TryGet(string difficultyId, out BotProfile? profile)
        {
            profile = null;
            return false;
        }
    }
}
