using System.Collections.Generic;

#nullable enable

namespace Runtime.GameModes.Wizard
{
    /// <summary>
    /// Catalog of available game modes.
    /// </summary>
    public interface IGameModeCatalog
    {
        /// <summary>Available mode strategies (sorted by metadata SortOrder).</summary>
        IReadOnlyList<IGameModeStrategy> Strategies { get; }

        /// <summary>Available mode metadata (sorted by SortOrder).</summary>
        IReadOnlyList<GameModeMetadata> Metadata { get; }

        bool TryGetStrategy(string modeId, out IGameModeStrategy? strategy);
    }
}