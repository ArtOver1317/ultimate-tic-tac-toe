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

            if (TryGetExact(difficultyId, out profile))
                return true;

            var alias = ResolveDifficultyAlias(difficultyId);
            if (!string.IsNullOrEmpty(alias) && TryGetExact(alias, out profile))
                return true;

            profile = null;
            return false;
        }

        private bool TryGetExact(string difficultyId, out BotProfile? profile)
        {
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

        private static string? ResolveDifficultyAlias(string difficultyId)
        {
            var normalized = difficultyId.Trim();

            if (string.Equals(normalized, "normal", StringComparison.OrdinalIgnoreCase))
                return "medium";

            if (string.Equals(normalized, "medium", StringComparison.OrdinalIgnoreCase))
                return "normal";

            return null;
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
