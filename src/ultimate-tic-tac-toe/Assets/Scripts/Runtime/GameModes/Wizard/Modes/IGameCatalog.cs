#nullable enable

using System.Collections.Generic;

namespace Runtime.GameModes.Wizard.Modes
{
    /// <summary>
    /// Catalog of available game modes.
    /// </summary>
    public interface IGameCatalog
    {
        /// <summary>Available mode strategies (sorted by metadata SortOrder).</summary>
        IReadOnlyList<IGameStrategy> Strategies { get; }

        /// <summary>Available mode metadata (sorted by SortOrder).</summary>
        IReadOnlyList<GameMetadata> Metadata { get; }

        bool TryGetStrategy(string gameId, out IGameStrategy? strategy);
    }
}