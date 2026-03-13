#nullable enable

using System;
using System.Collections.Generic;
using Runtime.Games.Battleship.Core;
using Runtime.Games.Battleship.ECS.Core;
using Runtime.Games.TicTacToe.Moves;

namespace Runtime.Games.Battleship.Placement
{
    internal sealed class BattleshipPlacementDraft
    {
        private readonly BattleshipPlacementShipState[] _ships = new BattleshipPlacementShipState[FleetLayout.ExpectedShipCount];
        private int? _selectedShipId;

        public BattleshipPlacementDraft() => ResetToDock();

        public IReadOnlyList<BattleshipPlacementShipState> Ships => _ships;

        public int? SelectedShipId => _selectedShipId;

        public bool TrySelectShip(int shipId)
        {
            if (!IsValidShipId(shipId))
                return false;

            _selectedShipId = shipId;
            return true;
        }

        public bool ClearSelection()
        {
            if (_selectedShipId == null)
                return false;

            _selectedShipId = null;
            return true;
        }

        public bool TryGetSelectedShip(out BattleshipPlacementShipState ship)
        {
            ship = default;
            
            if (_selectedShipId == null)
                return false;

            ship = _ships[_selectedShipId.Value];
            return true;
        }

        public bool TrySetShipOrientation(int shipId, ShipOrientation orientation)
        {
            if (!IsValidShipId(shipId))
                return false;

            var ship = _ships[shipId];
            _ships[shipId] = new BattleshipPlacementShipState(ship.ShipId, ship.Size, orientation, ship.StartCell);
            return true;
        }

        public bool TryPlaceShip(int shipId, CellId startCell, ShipOrientation orientation)
        {
            if (!IsValidShipId(shipId))
                return false;

            var existing = _ships[shipId];
            var candidate = new BattleshipPlacementShipState(existing.ShipId, existing.Size, orientation, startCell);
            
            if (!CanPlace(candidate, shipId))
                return false;

            _ships[shipId] = candidate;
            _selectedShipId = shipId;
            return true;
        }

        public bool TryRemoveShip(int shipId)
        {
            if (!IsValidShipId(shipId))
                return false;

            ref var ship = ref _ships[shipId];
            
            if (!ship.IsPlaced)
                return false;

            ship = new BattleshipPlacementShipState(ship.ShipId, ship.Size, ship.Orientation, null);
            return true;
        }

        public void ResetToDock()
        {
            for (var i = 0; i < _ships.Length; i++)
            {
                _ships[i] = new BattleshipPlacementShipState(
                    shipId: i,
                    size: BattleshipFleetConfig.StandardFleetOrder[i],
                    orientation: ShipOrientation.Horizontal,
                    startCell: null);
            }

            _selectedShipId = null;
        }

        public void ApplyLayout(in FleetLayout layout)
        {
            if (!layout.IsInitialized || layout.Ships == null || layout.Ships.Count != FleetLayout.ExpectedShipCount)
                return;

            for (var i = 0; i < layout.Ships.Count && i < _ships.Length; i++)
            {
                var placement = layout.Ships[i];
                
                _ships[i] = new BattleshipPlacementShipState(
                    shipId: i,
                    size: placement.Size,
                    orientation: placement.Orientation,
                    startCell: placement.StartCell);
            }

            _selectedShipId = null;
        }

        public bool TryBuildLayout(out FleetLayout layout)
        {
            layout = default;

            for (var i = 0; i < _ships.Length; i++)
            {
                if (!_ships[i].StartCell.HasValue)
                    return false;
            }

            var placements = new ShipPlacement[_ships.Length];
            
            for (var i = 0; i < _ships.Length; i++)
            {
                var ship = _ships[i];
                placements[i] = new ShipPlacement(ship.Size, ship.Orientation, ship.StartCell!.Value);
            }

            layout = new FleetLayout(Array.AsReadOnly(placements));
            return true;
        }

        public bool TryGetShipAt(CellId cellId, out int shipId)
        {
            shipId = -1;

            for (var i = 0; i < _ships.Length; i++)
            {
                var ship = _ships[i];
                
                if (!ship.IsPlaced)
                    continue;

                if (ContainsCell(ship, cellId))
                {
                    shipId = ship.ShipId;
                    return true;
                }
            }

            return false;
        }

        private static bool IsValidShipId(int shipId) => shipId is >= 0 and < FleetLayout.ExpectedShipCount;

        private bool CanPlace(in BattleshipPlacementShipState candidate, int movingShipId) => 
            TryBuildOccupancy(movingShipId, out var occupancy) 
            && TryApplyShipToBoard(candidate, occupancy, validateNeighbors: true);

        private bool TryBuildOccupancy(int movingShipId, out bool[] occupancy)
        {
            const int boardSize = BattleshipEcsBoard.DefaultBoardSize;
            occupancy = new bool[boardSize * boardSize];

            for (var i = 0; i < _ships.Length; i++)
            {
                if (i == movingShipId)
                    continue;

                var ship = _ships[i];
                
                if (!ship.IsPlaced)
                    continue;

                if (!TryApplyShipToBoard(ship, occupancy, validateNeighbors: false))
                    return false;
            }

            return true;
        }

        private static bool TryApplyShipToBoard(
            in BattleshipPlacementShipState ship,
            bool[] occupancy,
            bool validateNeighbors)
        {
            if (!ship.StartCell.HasValue)
                return false;

            var startCell = ship.StartCell.Value;
            var length = (int)ship.Size;
            
            if (length <= 0)
                return false;

            const int boardSize = BattleshipEcsBoard.DefaultBoardSize;
            
            if (!TryValidateShipCells(ship, occupancy, validateNeighbors, boardSize, startCell, length))
                return false;

            MarkShipCells(ship, occupancy, boardSize, startCell, length);
            return true;
        }

        private static bool TryValidateShipCells(
            in BattleshipPlacementShipState ship,
            bool[] occupancy,
            bool validateNeighbors,
            int boardSize,
            in CellId startCell,
            int length)
        {
            for (var segment = 0; segment < length; segment++)
            {
                var row = startCell.Major + (ship.Orientation == ShipOrientation.Vertical ? segment : 0);
                var col = startCell.Minor + (ship.Orientation == ShipOrientation.Horizontal ? segment : 0);

                if (!TryValidateShipSegment(occupancy, validateNeighbors, boardSize, row, col))
                    return false;
            }

            return true;
        }

        private static bool TryValidateShipSegment(
            bool[] occupancy,
            bool validateNeighbors,
            int boardSize,
            int row,
            int col)
        {
            if (row < 0 || row >= boardSize || col < 0 || col >= boardSize)
                return false;

            var index = row * boardSize + col;
            
            if (occupancy[index])
                return false;

            return !validateNeighbors || !HasOccupiedNeighbor(occupancy, boardSize, row, col);
        }

        private static bool HasOccupiedNeighbor(bool[] occupancy, int boardSize, int row, int col)
        {
            for (var neighborRow = row - 1; neighborRow <= row + 1; neighborRow++)
            {
                if (neighborRow < 0 || neighborRow >= boardSize)
                    continue;

                for (var neighborCol = col - 1; neighborCol <= col + 1; neighborCol++)
                {
                    if (neighborCol < 0 || neighborCol >= boardSize)
                        continue;

                    if (occupancy[neighborRow * boardSize + neighborCol])
                        return true;
                }
            }

            return false;
        }

        private static void MarkShipCells(
            in BattleshipPlacementShipState ship,
            bool[] occupancy,
            int boardSize,
            in CellId startCell,
            int length)
        {
            for (var segment = 0; segment < length; segment++)
            {
                var row = startCell.Major + (ship.Orientation == ShipOrientation.Vertical ? segment : 0);
                var col = startCell.Minor + (ship.Orientation == ShipOrientation.Horizontal ? segment : 0);
                occupancy[row * boardSize + col] = true;
            }
        }

        private static bool ContainsCell(in BattleshipPlacementShipState ship, CellId cellId)
        {
            if (!ship.StartCell.HasValue)
                return false;

            var start = ship.StartCell.Value;
            var length = (int)ship.Size;

            for (var segment = 0; segment < length; segment++)
            {
                var row = start.Major + (ship.Orientation == ShipOrientation.Vertical ? segment : 0);
                var col = start.Minor + (ship.Orientation == ShipOrientation.Horizontal ? segment : 0);
                
                if (row == cellId.Major && col == cellId.Minor)
                    return true;
            }

            return false;
        }
    }
}