#nullable enable
using System.Collections.Generic;
using Runtime.Games.Battleship.Core;

namespace Runtime.Games.Battleship.UI.Board
{
    internal readonly struct BoardRenderState
    {
        public BoardRenderState(
            IReadOnlyList<BattleshipCellMark> opponentMarks,
            IReadOnlyList<BattleshipCellMark> ownMarks,
            bool[] shipOccupancy,
            int cellCount,
            int boardSize)
        {
            OpponentMarks = opponentMarks;
            OwnMarks = ownMarks;
            ShipOccupancy = shipOccupancy;
            CellCount = cellCount;
            BoardSize = boardSize;
        }

        public IReadOnlyList<BattleshipCellMark> OpponentMarks { get; }

        public IReadOnlyList<BattleshipCellMark> OwnMarks { get; }

        public bool[] ShipOccupancy { get; }

        public int CellCount { get; }

        public int BoardSize { get; }
    }
}