#nullable enable

using Runtime.Gameplay.ECS.Components;
using Runtime.Gameplay.Shared;
using Runtime.Games.Battleship.Core;
using Runtime.Games.Battleship.ECS.Core;
using Runtime.Games.TicTacToe.Moves;

namespace Runtime.Games.Battleship.ECS.Battle
{
    internal static class BattleshipBattleShotResolver
    {
        public static bool TryBuildContext(
            ref BattleshipStateComponent state,
            in PlayersComponent players,
            in MakeMoveRequest moveRequest,
            out BattleshipBattleShotContext context,
            out GameplayRejectionReason rejectionReason)
        {
            context = default;
            rejectionReason = GameplayRejectionReason.ForbiddenMove;

            var activeSlot = players.ActivePlayerSlot;
            
            if (!BattleshipEcsBoard.TryResolvePlayerIndex(players, activeSlot, out var attackerIndex))
                return false;

            var defenderIndex = attackerIndex == 0 ? 1 : 0;
            var shots = GetShotsArray(ref state, attackerIndex);
            var defenderShips = GetShipsArray(ref state, defenderIndex);
            var defenderFleet = GetFleetArray(ref state, defenderIndex);
            
            if (shots == null || defenderShips == null)
                return false;

            var index = BattleshipEcsBoard.ToIndex(state.BoardSize, moveRequest.CellId);
            
            if (shots[index])
            {
                rejectionReason = GameplayRejectionReason.CellOccupied;
                return false;
            }

            context = new BattleshipBattleShotContext(activeSlot, attackerIndex, defenderIndex, shots, defenderShips, defenderFleet);
            return true;
        }

        public static bool ApplyShot(ref BattleshipStateComponent state, in BattleshipBattleShotContext context, in CellId cellId)
        {
            var index = BattleshipEcsBoard.ToIndex(state.BoardSize, cellId);
            context.Shots[index] = true;

            var isHit = context.DefenderShips[index];
            
            if (!isHit)
                return false;

            if (context.DefenderIndex == 0)
                state.Player0RemainingDecks--;
            else
                state.Player1RemainingDecks--;

            if (context.DefenderFleet != null
                && TryFindShipContainingCell(context.DefenderFleet, state.BoardSize, cellId, out var hitShip)
                && IsShipSunk(context.Shots, state.BoardSize, hitShip))
                MarkWaterAroundSunkShip(context.Shots, context.DefenderShips, state.BoardSize, hitShip);

            return true;
        }

        public static int ResolveOtherPlayerSlot(in PlayersComponent players, int currentSlot)
        {
            if (players.PlayerSlots.Length < 2)
                return currentSlot;

            return players.PlayerSlots[0] == currentSlot
                ? players.PlayerSlots[1]
                : players.PlayerSlots[0];
        }

        private static bool[]? GetShotsArray(ref BattleshipStateComponent state, int playerIndex) =>
            playerIndex == 0
                ? state.Player0Shots
                : playerIndex == 1
                    ? state.Player1Shots
                    : null;

        private static bool[]? GetShipsArray(ref BattleshipStateComponent state, int playerIndex) =>
            playerIndex == 0
                ? state.Player0Ships
                : playerIndex == 1
                    ? state.Player1Ships
                    : null;

        private static ShipPlacement[]? GetFleetArray(ref BattleshipStateComponent state, int playerIndex) =>
            playerIndex == 0
                ? state.Player0Fleet
                : playerIndex == 1
                    ? state.Player1Fleet
                    : null;

        private static bool TryFindShipContainingCell(
            ShipPlacement[] fleet,
            int boardSize,
            in CellId cellId,
            out ShipPlacement ship)
        {
            ship = default;

            for (var shipIndex = 0; shipIndex < fleet.Length; shipIndex++)
            {
                var candidate = fleet[shipIndex];
                var deckCount = (int)candidate.Size;
                
                for (var deck = 0; deck < deckCount; deck++)
                {
                    var major = candidate.StartCell.Major + (candidate.Orientation == ShipOrientation.Vertical ? deck : 0);
                    var minor = candidate.StartCell.Minor + (candidate.Orientation == ShipOrientation.Horizontal ? deck : 0);
                    var candidateCell = new CellId(major, minor);
                    
                    if (!BattleshipEcsBoard.IsInBounds(boardSize, candidateCell))
                        continue;

                    if (!candidateCell.Equals(cellId))
                        continue;

                    ship = candidate;
                    return true;
                }
            }

            return false;
        }

        private static bool IsShipSunk(bool[] shots, int boardSize, in ShipPlacement ship)
        {
            var deckCount = (int)ship.Size;
            
            for (var deck = 0; deck < deckCount; deck++)
            {
                var major = ship.StartCell.Major + (ship.Orientation == ShipOrientation.Vertical ? deck : 0);
                var minor = ship.StartCell.Minor + (ship.Orientation == ShipOrientation.Horizontal ? deck : 0);
                var cellId = new CellId(major, minor);
                
                if (!BattleshipEcsBoard.IsInBounds(boardSize, cellId))
                    return false;

                var index = BattleshipEcsBoard.ToIndex(boardSize, cellId);
                
                if (index < 0 || index >= shots.Length || !shots[index])
                    return false;
            }

            return true;
        }

        private static void MarkWaterAroundSunkShip(
            bool[] shots,
            bool[] defenderShips,
            int boardSize,
            in ShipPlacement ship)
        {
            var deckCount = (int)ship.Size;
            
            for (var deck = 0; deck < deckCount; deck++)
            {
                MarkWaterAroundShipDeck(shots, defenderShips, boardSize, ship, deck);
            }
        }

        private static void MarkWaterAroundShipDeck(
            bool[] shots,
            bool[] defenderShips,
            int boardSize,
            in ShipPlacement ship,
            int deck)
        {
            var major = ship.StartCell.Major + (ship.Orientation == ShipOrientation.Vertical ? deck : 0);
            var minor = ship.StartCell.Minor + (ship.Orientation == ShipOrientation.Horizontal ? deck : 0);

            for (var neighborMajor = major - 1; neighborMajor <= major + 1; neighborMajor++)
            {
                for (var neighborMinor = minor - 1; neighborMinor <= minor + 1; neighborMinor++)
                {
                    TryMarkWaterCell(shots, defenderShips, boardSize, neighborMajor, neighborMinor);
                }
            }
        }

        private static void TryMarkWaterCell(
            bool[] shots,
            bool[] defenderShips,
            int boardSize,
            int major,
            int minor)
        {
            var neighborCell = new CellId(major, minor);
            
            if (!BattleshipEcsBoard.IsInBounds(boardSize, neighborCell))
                return;

            var index = BattleshipEcsBoard.ToIndex(boardSize, neighborCell);
            
            if (index < 0 || index >= shots.Length || index >= defenderShips.Length)
                return;

            if (defenderShips[index])
                return;

            shots[index] = true;
        }
    }
}