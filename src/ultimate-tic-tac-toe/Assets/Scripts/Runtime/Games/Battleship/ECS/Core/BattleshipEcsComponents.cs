#nullable enable

using Runtime.Gameplay.ECS.Components;
using Runtime.Games.Battleship.Core;
using Runtime.Games.Battleship.ECS.Placement;
using Runtime.Games.TicTacToe.Moves;
using Scellecs.Morpeh;

namespace Runtime.Games.Battleship.ECS.Core
{
    public struct BattleshipStateComponent : IComponent
    {
        public int BoardSize;
        public BattleshipPhase Phase;
        public bool Player0Placed;
        public bool Player1Placed;
        public ShipPlacement[]? Player0Fleet;
        public ShipPlacement[]? Player1Fleet;
        public bool[]? Player0Ships;
        public bool[]? Player1Ships;
        public bool[]? Player0Shots;
        public bool[]? Player1Shots;
        public int StartingPlayerSlot;
        public int Player0RemainingDecks;
        public int Player1RemainingDecks;
        public int Player0ConsecutiveTimeouts;
        public int Player1ConsecutiveTimeouts;
    }

    public struct BoardDirtyComponent : IComponent { }

    /// <summary>
    /// One-shot request: created by <see cref="BattleshipProcessCommandsSystem"/>, consumed by <see cref="BattleshipPlacementSystem"/>.
    /// </summary>
    public struct SubmitPlacementRequest : IComponent
    {
        public int PlayerSlot;
        public FleetLayout Layout;
    }

    /// <summary>
    /// One-shot request: created by <see cref="BattleshipProcessCommandsSystem"/>, consumed by <see cref="BattleshipPlacementSystem"/>.
    /// </summary>
    public struct PlacementTimeoutRequest : IComponent
    {
        public int PlayerSlot;
        public int AutoPlaceSeed;
    }

    public struct BattleshipPhaseChangedOneShot : IComponent
    {
        public BattleshipPhase Phase;
    }

    public struct BattleshipMarksChangedOneShot : IComponent
    {
        public int ViewerSlot;
        public int SecondaryViewerSlot;
        public bool HasSecondaryViewer;
    }

    public static class BattleshipEcsBoard
    {
        public const int DefaultBoardSize = 10;

        public static bool IsInBounds(int boardSize, in CellId cellId) =>
            cellId.Major >= 0 && cellId.Major < boardSize && cellId.Minor >= 0 && cellId.Minor < boardSize;

        public static int ToIndex(int boardSize, in CellId cellId) => cellId.Major * boardSize + cellId.Minor;

        public static bool TryResolvePlayerIndex(in PlayersComponent players, int slot, out int index)
        {
            index = -1;

            for (var i = 0; i < players.PlayerSlots.Length; i++)
            {
                if (players.PlayerSlots[i] != slot)
                    continue;

                index = i;
                return true;
            }

            return false;
        }
    }
}
