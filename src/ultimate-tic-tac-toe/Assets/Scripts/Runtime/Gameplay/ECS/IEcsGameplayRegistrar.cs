using System.Collections.Generic;
using Runtime.GameModes.Wizard;
using Runtime.GameModes.Wizard.Configs;
using Scellecs.Morpeh;
using CellId = Runtime.Games.TicTacToe.Moves.CellId;

namespace Runtime.Gameplay.ECS
{
    /// <summary>
    /// Game-specific registrar that adds ECS systems, initializes components for a particular game,
    /// and provides game-specific snapshot reads for the Service Layer (ADR-3/ADR-9).
    /// Resolved from DI by <see cref="GameLaunchConfig.GameId"/>.
    /// </summary>
    public interface IEcsGameplayRegistrar
    {
        string GameId { get; }

        /// <summary>
        /// Register game-specific systems and initialize game-specific components on the match entity.
        /// Called after shared infrastructure systems are already added.
        /// </summary>
        void Register(World world, SystemsGroup systemsGroup, Entity matchEntity, GameLaunchConfig config);

        /// <summary>
        /// Reads a single cell's slot from game-specific board components.
        /// Returns -1 if the cell is empty or invalid.
        /// </summary>
        int GetCellSlot(World world, Entity matchEntity, CellId cellId);

        /// <summary>
        /// Reads all cells from game-specific board components.
        /// </summary>
        IReadOnlyList<CellSnapshot> GetAllCells(World world, Entity matchEntity);
    }
}
