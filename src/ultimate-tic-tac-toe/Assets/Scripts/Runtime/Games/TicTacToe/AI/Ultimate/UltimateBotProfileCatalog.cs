#nullable enable

using System;
using UnityEngine;

namespace Runtime.Games.TicTacToe.AI.Ultimate
{
    [CreateAssetMenu(fileName = "UltimateBotProfileCatalog", menuName = "TicTacToe/AI/Ultimate/Bot Profile Catalog")]
    public sealed class UltimateBotProfileCatalog : ScriptableObject, IUltimateBotProfileCatalog
    {
        [SerializeField] private UltimateBotProfile[] Profiles = Array.Empty<UltimateBotProfile>();

        public bool TryGet(string difficultyId, out UltimateBotDifficultyProfileData profile)
        {
            if (string.IsNullOrWhiteSpace(difficultyId))
            {
                profile = default;
                return false;
            }

            for (var i = 0; i < Profiles.Length; i++)
            {
                var item = Profiles[i];
                if (item == null)
                {
                    continue;
                }

                if (string.Equals(item.Id, difficultyId, StringComparison.OrdinalIgnoreCase))
                {
                    profile = item.ToValidatedData();
                    return true;
                }
            }

            profile = default;
            return false;
        }
    }

    internal sealed class EmptyUltimateBotProfileCatalog : IUltimateBotProfileCatalog
    {
        public bool TryGet(string difficultyId, out UltimateBotDifficultyProfileData profile)
        {
            profile = default;
            return false;
        }
    }
}
