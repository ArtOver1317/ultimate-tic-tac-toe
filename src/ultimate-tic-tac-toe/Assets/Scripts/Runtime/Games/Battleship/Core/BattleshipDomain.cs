#nullable enable

using System;
using System.Collections.Generic;
using Runtime.Games.TicTacToe.Moves;

namespace Runtime.Games.Battleship.Core
{
    public enum ShipSize
    {
        One = 1,
        Two = 2,
        Three = 3,
        Four = 4,
    }

    public enum ShipOrientation
    {
        Horizontal,
        Vertical,
    }

    public enum BattleshipPhase
    {
        Placement,
        Waiting,
        Battle,
        Finished,
    }

    public enum BattleshipCellMark
    {
        Unknown,
        Miss,
        Hit,
        Sunk,
    }

    public static class BattleshipFleetConfig
    {
        public static readonly ShipSize[] StandardFleetOrder =
        {
            ShipSize.Four,
            ShipSize.Three,
            ShipSize.Three,
            ShipSize.Two,
            ShipSize.Two,
            ShipSize.Two,
            ShipSize.One,
            ShipSize.One,
            ShipSize.One,
            ShipSize.One,
        };
    }

    public readonly struct ShipPlacement
    {
        public ShipSize Size { get; }
        public ShipOrientation Orientation { get; }
        public CellId StartCell { get; }

        public ShipPlacement(ShipSize size, ShipOrientation orientation, CellId startCell)
        {
            Size = size;
            Orientation = orientation;
            StartCell = startCell;
        }
    }

    public readonly struct FleetLayout
    {
        public const int ExpectedShipCount = 10;
        public IReadOnlyList<ShipPlacement>? Ships { get; }

        public FleetLayout(IReadOnlyList<ShipPlacement> ships)
        {
            if (ships == null)
                throw new ArgumentNullException(nameof(ships));

            if (ships.Count != ExpectedShipCount)
                throw new ArgumentException($"Fleet must contain exactly {ExpectedShipCount} ships.", nameof(ships));

            Ships = ships;
        }

        public bool IsInitialized => Ships != null;
    }

    public interface IBattleshipPlacementValidator
    {
        bool TryValidate(in FleetLayout layout, out string? errorLocalizationKey);
    }

    public interface IBattleshipAutoPlacer
    {
        FleetLayout Generate(int seed);
    }
}