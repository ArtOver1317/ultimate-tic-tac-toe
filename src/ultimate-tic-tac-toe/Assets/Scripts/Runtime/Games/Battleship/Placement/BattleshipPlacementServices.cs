#nullable enable

using System;
using System.Collections.Generic;
using Runtime.Games.Battleship.Core;
using Runtime.Games.Battleship.ECS.Core;
using Runtime.Games.TicTacToe.Moves;

namespace Runtime.Games.Battleship.Placement
{
    public sealed class BattleshipPlacementValidator : IBattleshipPlacementValidator
    {
        private const string _invalidLayoutErrorKey = "Errors.Battleship.Layout.Invalid";
        private const string _invalidFleetErrorKey = "Errors.Battleship.Layout.InvalidFleet";
        private const int _shipCountBucketCount = (int)ShipSize.Four + 1;

        public bool TryValidate(in FleetLayout layout, out string? errorLocalizationKey)
        {
            errorLocalizationKey = ValidateLayoutHeader(layout);
            
            if (errorLocalizationKey != null)
                return false;

            const int boardSize = BattleshipEcsBoard.DefaultBoardSize;
            var occupancy = new bool[boardSize * boardSize];
            var shipCounts = new int[_shipCountBucketCount];
            
            if (!TryCountAndPlaceShips(layout.Ships!, occupancy, shipCounts))
            {
                errorLocalizationKey = _invalidLayoutErrorKey;
                return false;
            }

            errorLocalizationKey = HasExpectedFleetComposition(shipCounts)
                ? null
                : _invalidFleetErrorKey;
            
            return errorLocalizationKey == null;
        }

        private static string? ValidateLayoutHeader(in FleetLayout layout)
        {
            if (!layout.IsInitialized || layout.Ships == null)
                return _invalidLayoutErrorKey;

            return layout.Ships.Count == FleetLayout.ExpectedShipCount
                ? null
                : _invalidLayoutErrorKey;
        }

        private static bool TryCountAndPlaceShips(IReadOnlyList<ShipPlacement> ships, bool[] occupancy, int[] shipCounts)
        {
            for (var i = 0; i < ships.Count; i++)
            {
                var ship = ships[i];
                
                if (!TryIncrementShipCount(ship.Size, shipCounts) || !TryPlaceShip(occupancy, ship))
                    return false;
            }

            return true;
        }

        private static bool TryIncrementShipCount(ShipSize size, int[] shipCounts)
        {
            var index = (int)size;
            
            if (index is < (int)ShipSize.One or > (int)ShipSize.Four)
                return false;

            shipCounts[index]++;
            return true;
        }

        private static bool HasExpectedFleetComposition(int[] shipCounts) =>
            shipCounts[(int)ShipSize.One] == 4
            && shipCounts[(int)ShipSize.Two] == 3
            && shipCounts[(int)ShipSize.Three] == 2
            && shipCounts[(int)ShipSize.Four] == 1;

        private static bool TryPlaceShip(bool[] occupancy, ShipPlacement ship)
        {
            const int boardSize = BattleshipEcsBoard.DefaultBoardSize;
            var size = (int)ship.Size;
            
            if (size <= 0)
                return false;

            return TryValidateShipCells(occupancy, ship, boardSize, size)
                   && TryMarkShipCells(occupancy, ship, boardSize, size);
        }

        private static bool TryValidateShipCells(bool[] occupancy, ShipPlacement ship, int boardSize, int size)
        {
            for (var i = 0; i < size; i++)
            {
                var x = ship.StartCell.Minor + (ship.Orientation == ShipOrientation.Horizontal ? i : 0);
                var y = ship.StartCell.Major + (ship.Orientation == ShipOrientation.Vertical ? i : 0);
                
                if (!TryValidateShipSegment(occupancy, boardSize, x, y))
                    return false;
            }

            return true;
        }

        private static bool TryValidateShipSegment(bool[] occupancy, int boardSize, int x, int y)
        {
            if (x < 0 || x >= boardSize || y < 0 || y >= boardSize)
                return false;

            if (occupancy[y * boardSize + x])
                return false;

            return !HasOccupiedNeighbor(occupancy, boardSize, x, y);
        }

        private static bool HasOccupiedNeighbor(bool[] occupancy, int boardSize, int x, int y)
        {
            for (var ny = y - 1; ny <= y + 1; ny++)
            {
                if (ny < 0 || ny >= boardSize)
                    continue;

                for (var nx = x - 1; nx <= x + 1; nx++)
                {
                    if (nx < 0 || nx >= boardSize)
                        continue;

                    if (occupancy[ny * boardSize + nx])
                        return true;
                }
            }

            return false;
        }

        private static bool TryMarkShipCells(bool[] occupancy, ShipPlacement ship, int boardSize, int size)
        {
            for (var i = 0; i < size; i++)
            {
                var x = ship.StartCell.Minor + (ship.Orientation == ShipOrientation.Horizontal ? i : 0);
                var y = ship.StartCell.Major + (ship.Orientation == ShipOrientation.Vertical ? i : 0);
                occupancy[y * boardSize + x] = true;
            }

            return true;
        }
    }

    public sealed class BattleshipAutoPlacer : IBattleshipAutoPlacer
    {
        private const int _layoutGenerationAttemptLimit = 500;
        private const int _shipGenerationAttemptLimit = 200;
        private const int _orientationVariantCount = 2;
        private const int _boardSize = BattleshipEcsBoard.DefaultBoardSize;
        private const int _boardCellCount = _boardSize * _boardSize;

        private readonly IBattleshipPlacementValidator _validator;

        public BattleshipAutoPlacer(IBattleshipPlacementValidator validator) =>
            _validator = validator ?? throw new ArgumentNullException(nameof(validator));

        public FleetLayout Generate(int seed)
        {
            var random = new Random(seed);

            for (var attempt = 0; attempt < _layoutGenerationAttemptLimit; attempt++)
            {
                if (TryGenerateLayout(random, out var layout) && _validator.TryValidate(layout, out _))
                    return layout;
            }

            throw new InvalidOperationException("Failed to generate valid battleship layout.");
        }

        private static bool TryGenerateLayout(Random random, out FleetLayout layout)
        {
            layout = default;
            var placements = new ShipPlacement[BattleshipFleetConfig.StandardFleetOrder.Length];
            var occupancy = new bool[_boardCellCount];

            for (var i = 0; i < BattleshipFleetConfig.StandardFleetOrder.Length; i++)
            {
                if (!TryGenerateShip(random, BattleshipFleetConfig.StandardFleetOrder[i], occupancy, out placements[i]))
                    return false;
            }

            layout = new FleetLayout(Array.AsReadOnly(placements));
            return true;
        }

        private static bool TryGenerateShip(Random random, ShipSize size, bool[] occupancy, out ShipPlacement placement)
        {
            for (var attempt = 0; attempt < _shipGenerationAttemptLimit; attempt++)
            {
                var candidate = CreateRandomCandidate(random, size);
                
                if (!CanPlace(candidate, occupancy))
                    continue;

                Apply(candidate, occupancy);
                placement = candidate;
                return true;
            }

            placement = default;
            return false;
        }

        private static ShipPlacement CreateRandomCandidate(Random random, ShipSize size)
        {
            var length = (int)size;
            var orientation = random.Next(0, _orientationVariantCount) == 0 ? ShipOrientation.Horizontal : ShipOrientation.Vertical;
            var startX = orientation == ShipOrientation.Horizontal ? random.Next(0, _boardSize - length + 1) : random.Next(0, _boardSize);
            var startY = orientation == ShipOrientation.Vertical ? random.Next(0, _boardSize - length + 1) : random.Next(0, _boardSize);
            return new ShipPlacement(size, orientation, new CellId(startY, startX));
        }

        private static bool CanPlace(ShipPlacement ship, bool[] occupancy)
        {
            var size = (int)ship.Size;
            
            for (var i = 0; i < size; i++)
            {
                var x = ship.StartCell.Minor + (ship.Orientation == ShipOrientation.Horizontal ? i : 0);
                var y = ship.StartCell.Major + (ship.Orientation == ShipOrientation.Vertical ? i : 0);

                if (!IsSegmentAreaClear(x, y, occupancy))
                    return false;
            }

            return true;
        }

        private static bool IsSegmentAreaClear(int x, int y, bool[] occupancy)
            => !HasOccupiedAreaCell(x, y, occupancy);

        private static bool HasOccupiedAreaCell(int x, int y, bool[] occupancy)
        {
            for (var ny = y - 1; ny <= y + 1; ny++)
            {
                if (ny is < 0 or >= _boardSize)
                    continue;

                for (var nx = x - 1; nx <= x + 1; nx++)
                {
                    if (nx is < 0 or >= _boardSize)
                        continue;

                    if (occupancy[ny * _boardSize + nx])
                        return true;
                }
            }

            return false;
        }

        private static void Apply(ShipPlacement ship, bool[] occupancy)
        {
            var size = (int)ship.Size;
            
            for (var i = 0; i < size; i++)
            {
                var x = ship.StartCell.Minor + (ship.Orientation == ShipOrientation.Horizontal ? i : 0);
                var y = ship.StartCell.Major + (ship.Orientation == ShipOrientation.Vertical ? i : 0);
                occupancy[y * _boardSize + x] = true;
            }
        }
    }
}