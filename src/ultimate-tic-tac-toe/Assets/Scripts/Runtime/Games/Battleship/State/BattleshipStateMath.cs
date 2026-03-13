#nullable enable

using System;
using System.Collections.Generic;
using Runtime.Games.Battleship.Core;
using Runtime.Games.Battleship.ECS.Core;
using CellId = Runtime.Games.TicTacToe.Moves.CellId;

namespace Runtime.Games.Battleship.State
{
    internal static class BattleshipStateMath
    {
        internal static BattleshipCellMark[] CreateUnknownMarks(int boardSize)
        {
            var size = boardSize <= 0 ? BattleshipEcsBoard.DefaultBoardSize : boardSize;
            return new BattleshipCellMark[size * size];
        }

        internal static BattleshipCellMark[] BuildMarks(
            int boardSize,
            bool[]? shots,
            bool[]? ships,
            ShipPlacement[]? fleet)
        {
            var cellCount = boardSize * boardSize;
            var result = new BattleshipCellMark[cellCount];

            if (shots == null || ships == null || shots.Length < cellCount || ships.Length < cellCount)
                return result;

            ApplyShotMarks(shots, ships, result);

            if (fleet == null)
                return result;

            ApplySunkMarks(boardSize, shots, ships, fleet, result);

            return result;
        }

        internal static bool TryCreateFleetLayout(ShipPlacement[]? fleet, out FleetLayout layout)
        {
            layout = default;

            if (fleet == null || fleet.Length == 0)
                return false;

            var copy = new ShipPlacement[fleet.Length];
            Array.Copy(fleet, copy, fleet.Length);
            layout = new FleetLayout(Array.AsReadOnly(copy));
            return true;
        }

        internal static bool[] BuildShotsFromMarks(IReadOnlyList<BattleshipCellMark>? marks, int cellCount)
        {
            var shots = new bool[cellCount];
            
            if (marks == null)
                return shots;

            var count = marks.Count < cellCount ? marks.Count : cellCount;
            
            for (var i = 0; i < count; i++)
            {
                if (marks[i] != BattleshipCellMark.Unknown)
                    shots[i] = true;
            }

            return shots;
        }

        internal static bool AreMarksEqual(IReadOnlyList<BattleshipCellMark>? left, IReadOnlyList<BattleshipCellMark>? right)
        {
            if (ReferenceEquals(left, right))
                return true;

            if (left == null || right == null || left.Count != right.Count)
                return false;

            for (var i = 0; i < left.Count; i++)
            {
                if (left[i] != right[i])
                    return false;
            }

            return true;
        }

        internal static bool AreShipPlacementsEqual(ShipPlacement[]? left, ShipPlacement[]? right)
        {
            if (ReferenceEquals(left, right))
                return true;

            if (left == null || right == null || left.Length != right.Length)
                return false;

            for (var i = 0; i < left.Length; i++)
            {
                if (left[i].Size != right[i].Size
                    || left[i].Orientation != right[i].Orientation
                    || !left[i].StartCell.Equals(right[i].StartCell))
                    return false;
            }

            return true;
        }

        internal static void ApplyFleetState(
            int boardSize,
            in FleetLayout layout,
            bool[]? shotsReceived,
            out ShipPlacement[] fleet,
            out bool[] ships,
            out int remainingDecks)
        {
            fleet = ToFleetArray(layout);
            ships = new bool[boardSize * boardSize];

            remainingDecks = 0;
            
            for (var i = 0; i < fleet.Length; i++)
            {
                var ship = fleet[i];
                var deckCount = (int)ship.Size;
                
                for (var deck = 0; deck < deckCount; deck++)
                {
                    if (!TryGetShipDeckIndex(boardSize, ship, deck, out var index))
                        continue;

                    ships[index] = true;
                    var hit = shotsReceived != null && index < shotsReceived.Length && shotsReceived[index];
                    
                    if (!hit)
                        remainingDecks++;
                }
            }
        }

        private static void ApplyShotMarks(bool[] shots, bool[] ships, BattleshipCellMark[] result)
        {
            for (var index = 0; index < result.Length; index++)
            {
                if (!shots[index])
                    continue;

                result[index] = ships[index]
                    ? BattleshipCellMark.Hit
                    : BattleshipCellMark.Miss;
            }
        }

        private static void ApplySunkMarks(
            int boardSize,
            bool[] shots,
            bool[] ships,
            ShipPlacement[] fleet,
            BattleshipCellMark[] result)
        {
            for (var shipIndex = 0; shipIndex < fleet.Length; shipIndex++)
            {
                var ship = fleet[shipIndex];
                
                if (!IsShipSunk(boardSize, shots, ship))
                    continue;

                MarkShipAsSunk(boardSize, shots, ships, ship, result);
            }
        }

        private static bool IsShipSunk(int boardSize, bool[] shots, in ShipPlacement ship)
        {
            var deckCount = (int)ship.Size;
            
            if (deckCount <= 0)
                return false;

            for (var deck = 0; deck < deckCount; deck++)
            {
                if (!TryGetShipDeckIndex(boardSize, ship, deck, out var index) || !shots[index])
                    return false;
            }

            return true;
        }

        private static void MarkShipAsSunk(
            int boardSize,
            bool[] shots,
            bool[] ships,
            in ShipPlacement ship,
            BattleshipCellMark[] result)
        {
            var deckCount = (int)ship.Size;
            
            if (deckCount <= 0)
                return;

            for (var deck = 0; deck < deckCount; deck++)
            {
                if (!TryGetShipDeckIndex(boardSize, ship, deck, out var index))
                    continue;

                if (shots[index] && ships[index])
                    result[index] = BattleshipCellMark.Sunk;
            }
        }

        private static bool TryGetShipDeckIndex(int boardSize, in ShipPlacement ship, int deck, out int index)
        {
            var major = ship.StartCell.Major + (ship.Orientation == ShipOrientation.Vertical ? deck : 0);
            var minor = ship.StartCell.Minor + (ship.Orientation == ShipOrientation.Horizontal ? deck : 0);
            var cellId = new CellId(major, minor);

            if (!BattleshipEcsBoard.IsInBounds(boardSize, cellId))
            {
                index = -1;
                return false;
            }

            index = BattleshipEcsBoard.ToIndex(boardSize, cellId);
            return true;
        }

        private static ShipPlacement[] ToFleetArray(in FleetLayout layout)
        {
            if (!layout.IsInitialized || layout.Ships == null || layout.Ships.Count == 0)
                return Array.Empty<ShipPlacement>();

            var fleet = new ShipPlacement[layout.Ships.Count];
            
            for (var i = 0; i < layout.Ships.Count; i++)
            {
                fleet[i] = layout.Ships[i];
            }

            return fleet;
        }
    }
}