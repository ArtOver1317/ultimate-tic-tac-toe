#nullable enable

using System;
using System.Collections.Generic;
using Runtime.Games.TicTacToe.Moves;

namespace Runtime.Games.Battleship
{
    public sealed class BattleshipPlacementValidator : IBattleshipPlacementValidator
    {
        private const int BoardSize = 10;

        public bool TryValidate(in FleetLayout layout, out string? errorLocalizationKey)
        {
            errorLocalizationKey = null;

            if (!layout.IsInitialized || layout.Ships == null)
            {
                errorLocalizationKey = "Errors.Battleship.Layout.Invalid";
                return false;
            }

            if (layout.Ships.Count != FleetLayout.ExpectedShipCount)
            {
                errorLocalizationKey = "Errors.Battleship.Layout.Invalid";
                return false;
            }

            var occupancy = new bool[BoardSize * BoardSize];
            var one = 0;
            var two = 0;
            var three = 0;
            var four = 0;

            for (var i = 0; i < layout.Ships.Count; i++)
            {
                var ship = layout.Ships[i];
                switch (ship.Size)
                {
                    case ShipSize.One:
                        one++;
                        break;
                    case ShipSize.Two:
                        two++;
                        break;
                    case ShipSize.Three:
                        three++;
                        break;
                    case ShipSize.Four:
                        four++;
                        break;
                    default:
                        errorLocalizationKey = "Errors.Battleship.Layout.Invalid";
                        return false;
                }

                if (!TryPlaceShip(occupancy, ship))
                {
                    errorLocalizationKey = "Errors.Battleship.Layout.Invalid";
                    return false;
                }
            }

            if (one != 4 || two != 3 || three != 2 || four != 1)
            {
                errorLocalizationKey = "Errors.Battleship.Layout.InvalidFleet";
                return false;
            }

            return true;
        }

        private static bool TryPlaceShip(bool[] occupancy, ShipPlacement ship)
        {
            var size = (int)ship.Size;

            if (size <= 0)
                return false;

            for (var i = 0; i < size; i++)
            {
                var x = ship.StartCell.Minor + (ship.Orientation == ShipOrientation.Horizontal ? i : 0);
                var y = ship.StartCell.Major + (ship.Orientation == ShipOrientation.Vertical ? i : 0);

                if (x < 0 || x >= BoardSize || y < 0 || y >= BoardSize)
                    return false;

                var index = y * BoardSize + x;
                if (occupancy[index])
                    return false;

                for (var ny = y - 1; ny <= y + 1; ny++)
                {
                    if (ny < 0 || ny >= BoardSize)
                        continue;

                    for (var nx = x - 1; nx <= x + 1; nx++)
                    {
                        if (nx < 0 || nx >= BoardSize)
                            continue;

                        var nIndex = ny * BoardSize + nx;
                        if (occupancy[nIndex])
                            return false;
                    }
                }
            }

            for (var i = 0; i < size; i++)
            {
                var x = ship.StartCell.Minor + (ship.Orientation == ShipOrientation.Horizontal ? i : 0);
                var y = ship.StartCell.Major + (ship.Orientation == ShipOrientation.Vertical ? i : 0);
                occupancy[y * BoardSize + x] = true;
            }

            return true;
        }
    }

    public sealed class BattleshipAutoPlacer : IBattleshipAutoPlacer
    {
        private static readonly ShipSize[] FleetOrder =
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

        private readonly IBattleshipPlacementValidator _validator;

        public BattleshipAutoPlacer(IBattleshipPlacementValidator validator) =>
            _validator = validator ?? throw new ArgumentNullException(nameof(validator));

        public FleetLayout Generate(int seed)
        {
            var random = new Random(seed);

            for (var attempt = 0; attempt < 500; attempt++)
            {
                var placements = new ShipPlacement[FleetOrder.Length];
                var occupancy = new bool[100];
                var success = true;

                for (var i = 0; i < FleetOrder.Length; i++)
                {
                    if (!TryGenerateShip(random, FleetOrder[i], occupancy, out var placement))
                    {
                        success = false;
                        break;
                    }

                    placements[i] = placement;
                }

                if (!success)
                    continue;

                var layout = new FleetLayout(Array.AsReadOnly(placements));
                if (_validator.TryValidate(layout, out _))
                    return layout;
            }

            throw new InvalidOperationException("Failed to generate valid battleship layout.");
        }

        private static bool TryGenerateShip(Random random, ShipSize size, bool[] occupancy, out ShipPlacement placement)
        {
            var length = (int)size;

            for (var attempt = 0; attempt < 200; attempt++)
            {
                var orientation = random.Next(0, 2) == 0 ? ShipOrientation.Horizontal : ShipOrientation.Vertical;
                var startX = orientation == ShipOrientation.Horizontal
                    ? random.Next(0, 10 - length + 1)
                    : random.Next(0, 10);
                var startY = orientation == ShipOrientation.Vertical
                    ? random.Next(0, 10 - length + 1)
                    : random.Next(0, 10);

                var candidate = new ShipPlacement(size, orientation, new CellId(startY, startX));
                if (!CanPlace(candidate, occupancy))
                    continue;

                Apply(candidate, occupancy);
                placement = candidate;
                return true;
            }

            placement = default;
            return false;
        }

        private static bool CanPlace(ShipPlacement ship, bool[] occupancy)
        {
            var size = (int)ship.Size;
            for (var i = 0; i < size; i++)
            {
                var x = ship.StartCell.Minor + (ship.Orientation == ShipOrientation.Horizontal ? i : 0);
                var y = ship.StartCell.Major + (ship.Orientation == ShipOrientation.Vertical ? i : 0);

                for (var ny = y - 1; ny <= y + 1; ny++)
                {
                    if (ny < 0 || ny >= 10)
                        continue;

                    for (var nx = x - 1; nx <= x + 1; nx++)
                    {
                        if (nx < 0 || nx >= 10)
                            continue;

                        if (occupancy[ny * 10 + nx])
                            return false;
                    }
                }
            }

            return true;
        }

        private static void Apply(ShipPlacement ship, bool[] occupancy)
        {
            var size = (int)ship.Size;
            for (var i = 0; i < size; i++)
            {
                var x = ship.StartCell.Minor + (ship.Orientation == ShipOrientation.Horizontal ? i : 0);
                var y = ship.StartCell.Major + (ship.Orientation == ShipOrientation.Vertical ? i : 0);
                occupancy[y * 10 + x] = true;
            }
        }
    }
}
