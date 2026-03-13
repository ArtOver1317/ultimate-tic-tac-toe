#nullable enable

using Runtime.Games.Battleship.Core;

namespace Runtime.Games.Battleship.ECS.Battle
{
    internal readonly struct BattleshipBattleShotContext
    {
        public BattleshipBattleShotContext(int activeSlot, int attackerIndex, int defenderIndex, bool[] shots, bool[] defenderShips, ShipPlacement[]? defenderFleet)
        {
            ActiveSlot = activeSlot;
            AttackerIndex = attackerIndex;
            DefenderIndex = defenderIndex;
            Shots = shots;
            DefenderShips = defenderShips;
            DefenderFleet = defenderFleet;
        }

        public int ActiveSlot { get; }

        public int AttackerIndex { get; }

        public int DefenderIndex { get; }

        public bool[] Shots { get; }

        public bool[] DefenderShips { get; }

        public ShipPlacement[]? DefenderFleet { get; }
    }
}